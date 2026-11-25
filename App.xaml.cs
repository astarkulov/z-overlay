using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ApexHUD.Core.Native;
using ApexHUD.Core.Settings;
using ApexHUD.Core.Telemetry;
using ApexHUD.Windows;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace ApexHUD;

public partial class App : Application
{
    private AppSettings _settings = null!;
    private TelemetryService _telemetry = null!;
    private HubWindow _hub = null!;

    private RelativeWindow _relativeWin = null!;
    private StandingsWindow _standingsWin = null!;
    private InputsWindow _inputsWin = null!;
    private FuelWindow _fuelWin = null!;
    private RadarWindow _radarWin = null!;
    private List<OverlayWindowBase> _overlays = new();

    private Forms.NotifyIcon? _trayIcon;
    private DispatcherTimer? _topmostTimer;
    private DispatcherTimer? _focusPollTimer;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showHubEvent;
    private EventWaitHandle? _quitEvent;
    private volatile bool _shuttingDown;

    private const string InstanceMutexName = "Local\\ApexHUD_SingleInstance_Mutex_v1";
    private const string ShowHubEventName = "Local\\ApexHUD_ShowHub_Event_v1";
    private const string QuitEventName = "Local\\ApexHUD_Quit_Event_v1";

    private TelemetrySnapshot? _pendingSnapshot;
    private int _uiDispatchScheduled;
    private int _frameCounter;
    private bool _lastConnected;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Always format telemetry numbers with crisp motorsport dots (e.g. 54.4%, 36.7 L, +0.26)
        Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

        DispatcherUnhandledException += (_, exArgs) =>
        {
            LogCrash(exArgs.Exception);
            MessageBox.Show($"ApexHUD error:\n{exArgs.Exception.Message}\n\nLog: %AppData%\\ApexHUD\\crash.log",
                "ApexHUD", MessageBoxButton.OK, MessageBoxImage.Error);
            exArgs.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, exArgs) =>
        {
            if (exArgs.ExceptionObject is Exception ex) LogCrash(ex);
        };

        base.OnStartup(e);

        // Headless high-DPI screenshot capture mode for README / docs
        if (e.Args.Length >= 2 && string.Equals(e.Args[0], "--capture-screenshots", StringComparison.OrdinalIgnoreCase))
        {
            ApexHUD.Core.Diagnostics.ScreenshotExporter.CaptureAll(e.Args[1]);
            Shutdown(0);
            return;
        }

        if (!EnsureSingleInstance())
        {
            Shutdown(0);
            return;
        }

        _settings = AppSettings.Load();

        _relativeWin = new RelativeWindow(_settings);
        _standingsWin = new StandingsWindow(_settings);
        _inputsWin = new InputsWindow(_settings);
        _fuelWin = new FuelWindow(_settings);
        _radarWin = new RadarWindow(_settings);

        _overlays = new List<OverlayWindowBase>
        {
            _relativeWin,
            _standingsWin,
            _inputsWin,
            _fuelWin,
            _radarWin
        };

        foreach (var ov in _overlays)
        {
            ov.LayoutChangedByUser += () =>
            {
                _hub?.SyncUiFromSettings();
                _settings.Save();
            };
            ov.WidgetSelected += key =>
            {
                HighlightSelectedWidget(key);
                _hub?.SelectWidget(key, notifyHost: false);
            };
        }

        _hub = new HubWindow(
            _settings,
            onSettingsChanged: ApplyAllSettingsAndSave,
            onResetLayout: ResetLayoutToDefaults,
            onSnapWidget: (key, anchor, margin) =>
            {
                var target = _overlays.Find(o => o.WidgetKey == key);
                target?.SnapTo(anchor, margin);
            },
            onNudgeWidget: (key, dx, dy) =>
            {
                var target = _overlays.Find(o => o.WidgetKey == key);
                target?.Nudge(dx, dy);
            },
            onSelectWidget: HighlightSelectedWidget,
            onMinimizeToTray: MinimizeHubToTray,
            onExitApp: CleanShutdown);

        MainWindow = _hub;
        SetupTrayIcon();
        _hub.Show();
        _hub.Activate();
        ApplyAllSettingsAndSave();

        // Fast foreground check (150ms) so Alt-Tabbing out of iRacing hides overlays instantaneously
        _focusPollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _focusPollTimer.Tick += (_, _) => UpdateOverlayVisibility(ShouldHideOverlays(_lastConnected));
        _focusPollTimer.Start();

        // Keep overlays reliably above borderless full-screen iRacing
        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _topmostTimer.Tick += (_, _) =>
        {
            foreach (var ov in _overlays)
                ov.EnsureTopmost();
        };
        _topmostTimer.Start();

        _telemetry = new TelemetryService(_settings);
        _telemetry.SnapshotReady += OnTelemetrySnapshot;
        _telemetry.Start();
    }

    private void OnTelemetrySnapshot(TelemetrySnapshot snapshot)
    {
        // Lock-free drop-stale-frame dispatch so UI never lags behind 60Hz telemetry
        Interlocked.Exchange(ref _pendingSnapshot, snapshot);
        if (Interlocked.Exchange(ref _uiDispatchScheduled, 1) == 0)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Render, ProcessLatestSnapshot);
        }
    }

    private bool ShouldHideOverlays(bool simConnected)
    {
        if (_settings.DemoMode) return false;

        if (_settings.AutoHideOutOfSim && !simConnected)
            return true;

        if (_settings.ShowOnlyWhenIRacingFocused)
        {
            var (isIRacing, isSelf) = Win32.GetForegroundAppState();
            if (isIRacing) return false;
            // Allow seeing overlays while user is actively editing/dragging in unlocked mode
            if (!_settings.Locked && isSelf) return false;
            return true;
        }

        return false;
    }

    private void ProcessLatestSnapshot()
    {
        Interlocked.Exchange(ref _uiDispatchScheduled, 0);
        var snap = Interlocked.Exchange(ref _pendingSnapshot, null);
        if (snap == null) return;

        _lastConnected = snap.IsConnected;
        bool hideAll = ShouldHideOverlays(snap.IsConnected);

        UpdateOverlayVisibility(hideAll);

        if (!hideAll)
        {
            // Inputs & Radar run at full 60Hz
            if (_inputsWin.IsVisible) _inputsWin.UpdateData(snap);
            if (_radarWin.IsVisible) _radarWin.UpdateData(snap);

            // Relative, Fuel & Standings update cleanly at ~15Hz (still instant to human eye, tiny CPU)
            _frameCounter++;
            if (_frameCounter % 4 == 0)
            {
                if (_relativeWin.IsVisible) _relativeWin.UpdateData(snap);
                if (_fuelWin.IsVisible) _fuelWin.UpdateData(snap);
                if (_standingsWin.IsVisible) _standingsWin.UpdateData(snap);
                _hub.UpdateConnectionStatus(snap);
            }
        }
        else if (_frameCounter++ % 15 == 0)
        {
            _hub.UpdateConnectionStatus(snap);
        }
    }

    private void UpdateOverlayVisibility(bool hideAll)
    {
        foreach (var ov in _overlays)
        {
            bool shouldShow = !hideAll && _settings.GetWidget(ov.WidgetKey).Enabled;
            if (shouldShow && !ov.IsVisible)
            {
                ov.Show();
                ov.ApplyLayoutFromSettings();
            }
            else if (!shouldShow && ov.IsVisible)
            {
                ov.Hide();
            }
        }
    }

    private void ApplyAllSettingsAndSave()
    {
        bool hideAll = ShouldHideOverlays(_lastConnected);
        foreach (var ov in _overlays)
        {
            var layout = _settings.GetWidget(ov.WidgetKey);
            if (!hideAll && layout.Enabled)
            {
                if (!ov.IsVisible) ov.Show();
                ov.ApplyLayoutFromSettings();
            }
            else if (ov.IsVisible)
            {
                ov.Hide();
            }
        }
        _settings.Save();
    }

    private void ResetLayoutToDefaults()
    {
        var defaults = AppSettings.CreateDefaultWidgets();
        foreach (var kv in defaults)
        {
            var cur = _settings.GetWidget(kv.Key);
            cur.Left = kv.Value.Left;
            cur.Top = kv.Value.Top;
            cur.Scale = kv.Value.Scale;
            cur.Opacity = kv.Value.Opacity;
        }
        ApplyAllSettingsAndSave();
    }

    private void HighlightSelectedWidget(string key)
    {
        foreach (var ov in _overlays)
            ov.SetSelectedVisual(ov.WidgetKey == key);
    }

    private void MinimizeHubToTray()
    {
        _hub.Hide();
    }

    private void ShowHubFromTray()
    {
        _hub.Show();
        _hub.WindowState = WindowState.Normal;
        _hub.Activate();
    }

    private void SetupTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть ApexHUD", null, (_, _) => Dispatcher.Invoke(ShowHubFromTray));
        menu.Items.Add("Lock / Unlock Overlays (Ctrl+Shift+L)", null, (_, _) => Dispatcher.Invoke(() => _hub.ToggleLock()));
        menu.Items.Add("Переключить Demo / Live iRacing", null, (_, _) => Dispatcher.Invoke(() =>
        {
            _settings.DemoMode = !_settings.DemoMode;
            _hub.SyncUiFromSettings();
            ApplyAllSettingsAndSave();
        }));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Dispatcher.Invoke(CleanShutdown));

        _trayIcon = new Forms.NotifyIcon
        {
            Text = "ApexHUD — iRacing Telemetry Overlay",
            Icon = CreateCrispTrayIcon(),
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowHubFromTray);
    }

    private static Drawing.Icon CreateCrispTrayIcon()
    {
        using var bmp = new Drawing.Bitmap(16, 16);
        using var g = Drawing.Graphics.FromImage(bmp);
        g.Clear(Drawing.Color.FromArgb(0x0B, 0x0E, 0x14));
        using var blue = new Drawing.SolidBrush(Drawing.Color.FromArgb(0x3B, 0x82, 0xF6));
        using var white = new Drawing.SolidBrush(Drawing.Color.FromArgb(0xF8, 0xFA, 0xFC));
        g.FillRectangle(blue, 0, 0, 16, 3);
        g.FillPolygon(blue, new[] { new Drawing.Point(8, 4), new Drawing.Point(14, 14), new Drawing.Point(11, 14), new Drawing.Point(8, 8), new Drawing.Point(5, 14), new Drawing.Point(2, 14) });
        g.FillRectangle(white, 6, 11, 4, 2);
        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    private bool EnsureSingleInstance()
    {
        _instanceMutex = new Mutex(true, InstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            // Check if another running ApexHUD is a different build/path (e.g. user launched a newer version)
            bool isDifferentBuild = IsRunningDifferentApexBuild(out var otherProcs);
            if (!isDifferentBuild)
            {
                // Same build already running -> pop its Control Hub to front instead of spawning duplicate overlays
                try
                {
                    using var showEv = EventWaitHandle.OpenExisting(ShowHubEventName);
                    showEv.Set();
                }
                catch { }

                _instanceMutex.Dispose();
                _instanceMutex = null;
                return false;
            }

            // Newer / different build -> ask old instance to exit cleanly, fallback to terminating it
            try
            {
                using var quitEv = EventWaitHandle.OpenExisting(QuitEventName);
                quitEv.Set();
            }
            catch { }

            try
            {
                if (!_instanceMutex.WaitOne(1500))
                {
                    foreach (var proc in otherProcs)
                    {
                        try { if (!proc.HasExited) proc.Kill(); } catch { }
                    }
                    _instanceMutex.WaitOne(1500);
                }
            }
            catch (AbandonedMutexException)
            {
                // Acquired after old process exited
            }
        }

        StartSingleInstanceListener();
        return true;
    }

    private static bool IsRunningDifferentApexBuild(out List<System.Diagnostics.Process> others)
    {
        others = new List<System.Diagnostics.Process>();
        try
        {
            int selfId = Environment.ProcessId;
            string? selfPath = Environment.ProcessPath;
            DateTime selfWrite = !string.IsNullOrEmpty(selfPath) && System.IO.File.Exists(selfPath)
                ? System.IO.File.GetLastWriteTimeUtc(selfPath)
                : DateTime.MinValue;

            foreach (var p in System.Diagnostics.Process.GetProcessesByName("ApexHUD"))
            {
                if (p.Id == selfId) continue;
                others.Add(p);
                try
                {
                    string? otherPath = p.MainModule?.FileName;
                    if (!string.Equals(selfPath, otherPath, StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (otherPath != null && Math.Abs((System.IO.File.GetLastWriteTimeUtc(otherPath) - selfWrite).TotalSeconds) > 1.0)
                        return true;
                }
                catch
                {
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    private void StartSingleInstanceListener()
    {
        try
        {
            _showHubEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowHubEventName);
            _quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
            var handles = new WaitHandle[] { _showHubEvent, _quitEvent };

            var t = new Thread(() =>
            {
                while (!_shuttingDown)
                {
                    try
                    {
                        int idx = WaitHandle.WaitAny(handles);
                        if (_shuttingDown) break;
                        if (idx == 0)
                            Dispatcher.BeginInvoke(ShowHubFromTray);
                        else if (idx == 1)
                            Dispatcher.BeginInvoke(CleanShutdown);
                    }
                    catch
                    {
                        break;
                    }
                }
            })
            {
                IsBackground = true,
                Name = "ApexHUD-SingleInstance"
            };
            t.Start();
        }
        catch { }
    }

    private void CleanShutdown()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;

        _settings?.Save();
        _focusPollTimer?.Stop();
        _topmostTimer?.Stop();
        _telemetry?.Dispose();
        _hub?.UnregisterGlobalHotkey();

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        foreach (var ov in _overlays)
            ov.Close();

        try { _showHubEvent?.Set(); } catch { }
        try { _showHubEvent?.Dispose(); } catch { }
        try { _quitEvent?.Dispose(); } catch { }
        try { _instanceMutex?.ReleaseMutex(); } catch { }
        try { _instanceMutex?.Dispose(); } catch { }
        _showHubEvent = null;
        _quitEvent = null;
        _instanceMutex = null;

        Shutdown();
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ApexHUD");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(dir, "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { }
    }
}
