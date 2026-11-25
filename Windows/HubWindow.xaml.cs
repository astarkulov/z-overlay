using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ApexHUD.Core.Native;
using ApexHUD.Core.Settings;
using ApexHUD.Core.Telemetry;
using Forms = System.Windows.Forms;

namespace ApexHUD.Windows;

public partial class HubWindow : Window
{
    private const int HotkeyLockId = 0xA101;
    private const uint VkL = 0x4C;

    private readonly AppSettings _settings;
    private readonly Action _onSettingsChanged;
    private readonly Action _onResetLayout;
    private readonly Action<string, WidgetAnchor, double> _onSnapWidget;
    private readonly Action<string, double, double> _onNudgeWidget;
    private readonly Action<string> _onSelectWidget;
    private readonly Action _onMinimizeToTray;
    private readonly Action _onExitApp;

    private HwndSource? _hwndSource;
    private bool _syncingUi = true;
    private string _selectedWidgetKey = "Relative";
    private double _nudgeStepPx = 5.0;
    private readonly Dictionary<string, Button> _selectButtons = new();
    private readonly Dictionary<string, (CheckBox Enable, Slider Scale, TextBlock ScaleVal, Slider Opacity, TextBlock OpacityVal)> _widgetRows = new();

    private static readonly (string Key, string Label, string Desc)[] WidgetDefs =
    {
        ("Relative",  "Relative",      "Дистанции до машин, классы, iR/SR, круговые, флаги"),
        ("Standings", "Standings",     "Таблица мультикласса, SOF, прогноз ±iR, пит-стопы"),
        ("Inputs",    "Inputs",        "60Hz график педалей + ABS, руль, передача и RPM"),
        ("Fuel",      "Fuel Strategy", "Расход/круг, запас кругов, точный расчёт долива"),
        ("Radar",     "Radar",         "Боковые дуги приближения, автоскрытие на чистой трассе")
    };

    public HubWindow(
        AppSettings settings,
        Action onSettingsChanged,
        Action onResetLayout,
        Action<string, WidgetAnchor, double> onSnapWidget,
        Action<string, double, double> onNudgeWidget,
        Action<string> onSelectWidget,
        Action onMinimizeToTray,
        Action onExitApp)
    {
        _settings = settings;
        _onSettingsChanged = onSettingsChanged;
        _onResetLayout = onResetLayout;
        _onSnapWidget = onSnapWidget;
        _onNudgeWidget = onNudgeWidget;
        _onSelectWidget = onSelectWidget;
        _onMinimizeToTray = onMinimizeToTray;
        _onExitApp = onExitApp;

        InitializeComponent();

        BuildWidgetControls();
        BuildWidgetSelectorButtons();
        BuildMonitorSelectorCheckboxes();
        PopulateFontCombos();
        SyncUiFromSettings();
        SelectWidget("Relative", notifyHost: false);

        SourceInitialized += OnSourceInitialized;
        Closing += (_, e) =>
        {
            e.Cancel = true;
            _onMinimizeToTray();
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);
        Win32.RegisterHotKey(hwnd, HotkeyLockId, Win32.MOD_CONTROL | Win32.MOD_SHIFT | Win32.MOD_NOREPEAT, VkL);
    }

    public void UnregisterGlobalHotkey()
    {
        if (_hwndSource != null)
        {
            Win32.UnregisterHotKey(_hwndSource.Handle, HotkeyLockId);
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY && wParam.ToInt32() == HotkeyLockId)
        {
            ToggleLock();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void NavTab_Checked(object sender, RoutedEventArgs e)
    {
        if (PageWidgets == null || PagePosition == null || PageAppearance == null || PageHudPrefs == null) return;

        PageWidgets.Visibility = TabWidgets.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PagePosition.Visibility = TabPosition.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageAppearance.Visibility = TabAppearance.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageHudPrefs.Visibility = TabHudPrefs.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        PageTitle.Text = TabWidgets.IsChecked == true
            ? "Виджеты оверлея"
            : TabPosition.IsChecked == true
                ? "Позиция и привязка"
                : TabAppearance.IsChecked == true
                    ? "Внешний вид и масштаб"
                    : "Настройки HUD";
    }

    private void PopulateFontCombos()
    {
        var installed = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string[] priorityUi = { "Segoe UI Variable Text", "Bahnschrift", "Rajdhani", "Exo 2", "Segoe UI", "Calibri", "Arial", "Trebuchet MS", "Verdana" };
        string[] priorityMono = { "Cascadia Mono", "Consolas", "Bahnschrift", "Rajdhani", "Exo 2", "Orbitron", "Cascadia Code", "Lucida Console" };

        var uiList = priorityUi.Where(p => installed.Contains(p, StringComparer.OrdinalIgnoreCase))
            .Concat(installed.Where(f => !priorityUi.Contains(f, StringComparer.OrdinalIgnoreCase)))
            .ToList();
        var monoList = priorityMono.Where(p => installed.Contains(p, StringComparer.OrdinalIgnoreCase))
            .Concat(installed.Where(f => !priorityMono.Contains(f, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        UiFontCombo.ItemsSource = uiList;
        MonoFontCombo.ItemsSource = monoList;
    }

    private void FontCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingUi || _settings == null) return;
        if (UiFontCombo.SelectedItem is string uiFont) _settings.UiFontFamily = uiFont;
        if (MonoFontCombo.SelectedItem is string monoFont) _settings.MonoFontFamily = monoFont;
        _onSettingsChanged();
    }

    private void FontPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            var parts = tag.Split('|');
            if (parts.Length == 2)
            {
                _settings.UiFontFamily = parts[0];
                _settings.MonoFontFamily = parts[1];
                SyncUiFromSettings();
                _onSettingsChanged();
            }
        }
    }

    private void FuelMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string mode)
        {
            _settings.FuelCalcMode = mode;
            UpdateFuelModeButtons();
            _onSettingsChanged();
        }
    }

    private void GraphSeconds_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingUi || _settings == null) return;
        double val = Math.Round(e.NewValue * 2.0) / 2.0; // 0.5s precision
        _settings.InputsGraphSeconds = Math.Clamp(val, 1.0, 12.0);
        GraphSecondsVal.Text = $"{_settings.InputsGraphSeconds:0.0} сек";
        _onSettingsChanged();
    }

    private void GraphSecondsPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string s &&
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
        {
            _settings.InputsGraphSeconds = Math.Clamp(val, 1.0, 12.0);
            SyncUiFromSettings();
            _onSettingsChanged();
        }
    }

    private void GraphWidth_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingUi || _settings == null) return;
        double val = Math.Round(e.NewValue / 10.0) * 10.0;
        _settings.InputsGraphWidth = Math.Clamp(val, 140.0, 460.0);
        GraphWidthVal.Text = $"{(int)_settings.InputsGraphWidth} px";
        _onSettingsChanged();
    }

    private void GraphWidthPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string s &&
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
        {
            _settings.InputsGraphWidth = Math.Clamp(val, 140.0, 460.0);
            SyncUiFromSettings();
            _onSettingsChanged();
        }
    }

    private void HudFontScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingUi || _settings == null) return;
        double val = Math.Round(e.NewValue, 2);
        _settings.HudFontScale = Math.Clamp(val, 0.80, 1.35);
        HudFontScaleVal.Text = $"{(int)Math.Round(_settings.HudFontScale * 100)}%";
        _onSettingsChanged();
    }

    private void UpdateFuelModeButtons()
    {
        var primary = (Style)FindResource("ModernPrimaryButton");
        var normal = (Style)FindResource("ModernButton");
        FuelModeAvgBtn.Style = _settings.FuelCalcMode == "Avg" ? primary : normal;
        FuelModeLastBtn.Style = _settings.FuelCalcMode == "Last" ? primary : normal;
        FuelModeMaxBtn.Style = _settings.FuelCalcMode == "Max" ? primary : normal;
    }

    private void BuildMonitorSelectorCheckboxes()
    {
        MonitorsPanel.Children.Clear();
        var screens = Forms.Screen.AllScreens;
        var toggleStyle = (Style)FindResource("CleanToggle");

        // Ensure at least primary screen is selected if list is empty
        if (_settings.AllowedMonitorDevices.Count == 0 && screens.Length > 0)
        {
            var primary = Forms.Screen.PrimaryScreen ?? screens[0];
            _settings.AllowedMonitorDevices.Add(primary.DeviceName);
        }

        for (int i = 0; i < screens.Length; i++)
        {
            var sc = screens[i];
            string dev = sc.DeviceName;
            string tag = sc.Primary ? " • Основной" : "";
            string label = $"Монитор {i + 1} ({sc.Bounds.Width}×{sc.Bounds.Height}{tag})";

            var chk = new CheckBox
            {
                Style = toggleStyle,
                Content = label,
                IsChecked = _settings.AllowedMonitorDevices.Contains(dev),
                Margin = new Thickness(0, 3, 18, 3)
            };

            chk.Click += (_, _) =>
            {
                if (_syncingUi) return;
                if (chk.IsChecked == true)
                {
                    if (!_settings.AllowedMonitorDevices.Contains(dev))
                        _settings.AllowedMonitorDevices.Add(dev);
                }
                else
                {
                    // Always keep at least one monitor enabled
                    if (_settings.AllowedMonitorDevices.Count <= 1)
                    {
                        chk.IsChecked = true;
                        return;
                    }
                    _settings.AllowedMonitorDevices.Remove(dev);
                }
                _onSettingsChanged();
                UpdateSelectedCoordsDisplay();
            };

            MonitorsPanel.Children.Add(chk);
        }
    }

    private void BuildWidgetSelectorButtons()
    {
        WidgetSelectPanel.Children.Clear();
        _selectButtons.Clear();

        foreach (var (key, label, _) in WidgetDefs)
        {
            string k = key;
            var btn = new Button
            {
                Content = label,
                Style = (Style)FindResource("ModernButton"),
                Margin = new Thickness(0, 0, 8, 6),
                Padding = new Thickness(12, 6, 12, 6)
            };
            btn.Click += (_, _) => SelectWidget(k, notifyHost: true);
            WidgetSelectPanel.Children.Add(btn);
            _selectButtons[key] = btn;
        }
    }

    public void SelectWidget(string key, bool notifyHost = false)
    {
        _selectedWidgetKey = key;
        var primary = (Style)FindResource("ModernPrimaryButton");
        var normal = (Style)FindResource("ModernButton");
        foreach (var kv in _selectButtons)
            kv.Value.Style = kv.Key == key ? primary : normal;

        UpdateSelectedCoordsDisplay();
        if (notifyHost) _onSelectWidget(key);
    }

    private void UpdateSelectedCoordsDisplay()
    {
        if (SelectedCoordsText == null || _settings == null) return;
        var layout = _settings.GetWidget(_selectedWidgetKey);
        SelectedCoordsText.Text = $"X: {(int)Math.Round(layout.Left)} px   •   Y: {(int)Math.Round(layout.Top)} px";
    }

    private void SnapAnchor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && Enum.TryParse<WidgetAnchor>(tag, out var anchor))
        {
            double margin = SnapMarginCheck.IsChecked == true ? 12.0 : 0.0;
            _onSnapWidget(_selectedWidgetKey, anchor, margin);
            UpdateSelectedCoordsDisplay();
        }
    }

    private void NudgeArrow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            var parts = tag.Split(',');
            if (parts.Length == 2 &&
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double dx) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double dy))
            {
                _onNudgeWidget(_selectedWidgetKey, dx * _nudgeStepPx, dy * _nudgeStepPx);
                UpdateSelectedCoordsDisplay();
            }
        }
    }

    private void ToggleNudgeStep_Click(object sender, RoutedEventArgs e)
    {
        _nudgeStepPx = _nudgeStepPx switch
        {
            1.0 => 5.0,
            5.0 => 20.0,
            _ => 1.0
        };
        NudgeStepBtn.Content = $"{(int)_nudgeStepPx} px";
    }

    private void BuildWidgetControls()
    {
        WidgetsPanel.Children.Clear();
        var monoFont = (FontFamily)FindResource("HudMonoFont");
        var uiFont = (FontFamily)FindResource("HudUiFont");
        var toggleStyle = (Style)FindResource("CleanToggle");
        var sliderStyle = (Style)FindResource("CleanSlider");

        foreach (var (key, label, desc) in WidgetDefs)
        {
            var card = new Border
            {
                Background = FormatHelpers.GetFrozenBrush("#FF141923"),
                BorderBrush = FormatHelpers.GetFrozenBrush("#FF242D3D"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(122) });

            var enableChk = new CheckBox
            {
                Style = toggleStyle,
                Content = new TextBlock
                {
                    Text = label,
                    FontFamily = uiFont,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = FormatHelpers.GetFrozenBrush("#FFF8FAFC")
                }
            };
            string k = key;
            enableChk.Click += (_, _) =>
            {
                if (_syncingUi) return;
                _settings.GetWidget(k).Enabled = enableChk.IsChecked == true;
                _onSettingsChanged();
            };
            Grid.SetRow(enableChk, 0);
            Grid.SetColumn(enableChk, 0);
            g.Children.Add(enableChk);

            var descTb = new TextBlock
            {
                Text = desc,
                FontFamily = uiFont,
                FontSize = 11,
                Foreground = FormatHelpers.GetFrozenBrush("#FF94A3B8"),
                Margin = new Thickness(0, 4, 10, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetRow(descTb, 1);
            Grid.SetColumn(descTb, 0);
            g.Children.Add(descTb);

            // Individual Scale slider (70% .. 200%)
            var scalePanel = new StackPanel { Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var scaleHeader = new Grid();
            scaleHeader.Children.Add(new TextBlock { Text = "Масштаб", FontSize = 10, Foreground = FormatHelpers.GetFrozenBrush("#FF94A3B8") });
            var scaleVal = new TextBlock { Text = "100%", FontFamily = monoFont, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = FormatHelpers.GetFrozenBrush("#FF60A5FA"), HorizontalAlignment = HorizontalAlignment.Right };
            scaleHeader.Children.Add(scaleVal);
            var scaleSlider = new Slider { Style = sliderStyle, Minimum = 0.55, Maximum = 2.50, SmallChange = 0.05, LargeChange = 0.15 };
            scaleSlider.ValueChanged += (_, e) =>
            {
                if (_syncingUi) return;
                _settings.GetWidget(k).Scale = Math.Round(e.NewValue, 2);
                scaleVal.Text = $"{(int)Math.Round(e.NewValue * 100)}%";
                _onSettingsChanged();
            };
            scalePanel.Children.Add(scaleHeader);
            scalePanel.Children.Add(scaleSlider);
            Grid.SetRowSpan(scalePanel, 2);
            Grid.SetColumn(scalePanel, 1);
            g.Children.Add(scalePanel);

            // Individual Overall Opacity slider
            var opPanel = new StackPanel { Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var opHeader = new Grid();
            opHeader.Children.Add(new TextBlock { Text = "Яркость", FontSize = 10, Foreground = FormatHelpers.GetFrozenBrush("#FF94A3B8") });
            var opVal = new TextBlock { Text = "100%", FontFamily = monoFont, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = FormatHelpers.GetFrozenBrush("#FF38BDF8"), HorizontalAlignment = HorizontalAlignment.Right };
            opHeader.Children.Add(opVal);
            var opSlider = new Slider { Style = sliderStyle, Minimum = 0.35, Maximum = 1.0, SmallChange = 0.05, LargeChange = 0.10 };
            opSlider.ValueChanged += (_, e) =>
            {
                if (_syncingUi) return;
                _settings.GetWidget(k).Opacity = Math.Round(e.NewValue, 2);
                opVal.Text = $"{(int)Math.Round(e.NewValue * 100)}%";
                _onSettingsChanged();
            };
            opPanel.Children.Add(opHeader);
            opPanel.Children.Add(opSlider);
            Grid.SetRowSpan(opPanel, 2);
            Grid.SetColumn(opPanel, 2);
            g.Children.Add(opPanel);

            card.Child = g;
            WidgetsPanel.Children.Add(card);
            _widgetRows[key] = (enableChk, scaleSlider, scaleVal, opSlider, opVal);
        }
    }

    public void SyncUiFromSettings()
    {
        _syncingUi = true;
        try
        {
            DemoModeCheck.IsChecked = _settings.DemoMode;

            double gs = Math.Clamp(_settings.GlobalScale, 0.70, 2.20);
            QuickScaleSlider.Value = gs;
            AppScaleSlider.Value = gs;
            string gsText = $"{(int)Math.Round(gs * 100)}%";
            QuickScaleVal.Text = gsText;
            AppScaleVal.Text = gsText;

            double bg = Math.Clamp(_settings.BackgroundOpacity, 0.0, 0.95);
            QuickBackdropSlider.Value = bg;
            AppBackdropSlider.Value = bg;
            string bgText = $"{(int)Math.Round(bg * 100)}%";
            QuickBackdropVal.Text = bgText;
            AppBackdropVal.Text = bgText;

            ShowBrandIconsCheck.IsChecked = _settings.ShowCarBrandIcons;
            RelShowHeaderCheck.IsChecked = _settings.ShowRelativeHeader;
            RelShowNumCheck.IsChecked = _settings.ShowRelativeCarNumber;
            RelShowPaceCheck.IsChecked = _settings.ShowRelativePaceDelta;
            RelShowIrCheck.IsChecked = _settings.ShowRelativeIRating;
            RelShowLicCheck.IsChecked = _settings.ShowRelativeLicense;

            StdShowHeaderCheck.IsChecked = _settings.ShowStandingsHeader;
            StdShowNumCheck.IsChecked = _settings.ShowStandingsCarNumber;
            StdShowPosDeltaCheck.IsChecked = _settings.ShowStandingsPosDelta;
            StdShowTireCheck.IsChecked = _settings.ShowStandingsTireCompound;
            StdShowIrCheck.IsChecked = _settings.ShowStandingsIRating;
            StdShowIntervalCheck.IsChecked = _settings.ShowStandingsInterval;
            StdShowDeltaIrCheck.IsChecked = _settings.ShowStandingsDeltaIR;
            StdShowLastLapCheck.IsChecked = _settings.ShowStandingsLastLap;
            StdShowPitsCheck.IsChecked = _settings.ShowStandingsPits;
            StandingsRowsText.Text = _settings.StandingsMaxRows.ToString();

            InpShowGraphCheck.IsChecked = _settings.ShowInputsGraph;
            InpShowPedalBarsCheck.IsChecked = _settings.ShowInputsPedalBars;
            InpShowGearCheck.IsChecked = _settings.ShowInputsGearCluster;
            InpShowShiftCheck.IsChecked = _settings.ShowInputsShiftBar;
            InpShowWheelCheck.IsChecked = _settings.ShowInputsSteering;
            InpShowClutchCheck.IsChecked = _settings.ShowInputsClutch;
            InpShowElecCheck.IsChecked = _settings.ShowInputsElectronics;
            InpShowDeltaCheck.IsChecked = _settings.ShowInputsDeltaBar;
            InpShowCornerSpeedCheck.IsChecked = _settings.ShowInputsCornerSpeed;

            double gW = Math.Clamp(_settings.InputsGraphWidth, 140.0, 460.0);
            GraphWidthSlider.Value = gW;
            GraphWidthVal.Text = $"{(int)Math.Round(gW)} px";

            double gSec = Math.Clamp(_settings.InputsGraphSeconds, 1.0, 12.0);
            GraphSecondsSlider.Value = gSec;
            GraphSecondsVal.Text = $"{gSec:0.0} сек";

            double fs = Math.Clamp(_settings.HudFontScale, 0.80, 1.35);
            HudFontScaleSlider.Value = fs;
            HudFontScaleVal.Text = $"{(int)Math.Round(fs * 100)}%";

            UpdateSkinButtons();

            RadarAutoHideCheck.IsChecked = _settings.RadarAutoHideWhenClear;
            OnlyIRacingFocusCheck.IsChecked = _settings.ShowOnlyWhenIRacingFocused;
            AutoHideOutOfSimCheck.IsChecked = _settings.AutoHideOutOfSim;
            ImperialUnitsCheck.IsChecked = _settings.UseImperialUnits;
            FuelMarginText.Text = $"+{_settings.FuelSafetyMarginLaps:0.0} L";
            UpdateFuelModeButtons();

            UiFontCombo.SelectedItem = _settings.UiFontFamily;
            MonoFontCombo.SelectedItem = _settings.MonoFontFamily;

            UpdateLockButtonVisual();

            foreach (var kv in _widgetRows)
            {
                var layout = _settings.GetWidget(kv.Key);
                kv.Value.Enable.IsChecked = layout.Enabled;
                kv.Value.Scale.Value = Math.Clamp(layout.Scale, 0.55, 2.50);
                kv.Value.ScaleVal.Text = $"{(int)Math.Round(layout.Scale * 100)}%";
                kv.Value.Opacity.Value = Math.Clamp(layout.Opacity, 0.35, 1.0);
                kv.Value.OpacityVal.Text = $"{(int)Math.Round(layout.Opacity * 100)}%";
            }

            UpdateSelectedCoordsDisplay();
        }
        finally
        {
            _syncingUi = false;
        }
    }

    public void UpdateConnectionStatus(TelemetrySnapshot snap)
    {
        if (snap.IsDemo)
        {
            StatusBadge.Background = FormatHelpers.GetFrozenBrush("#1A3B82F6");
            StatusBadge.BorderBrush = FormatHelpers.GetFrozenBrush("#443B82F6");
            StatusDot.Fill = FormatHelpers.GetFrozenBrush("#FF38BDF8");
            StatusText.Foreground = FormatHelpers.GetFrozenBrush("#FFE0F2FE");
            StatusText.Text = "Demo 60 Hz";
        }
        else if (snap.IsConnected)
        {
            StatusBadge.Background = FormatHelpers.GetFrozenBrush("#1A22C55E");
            StatusBadge.BorderBrush = FormatHelpers.GetFrozenBrush("#4422C55E");
            StatusDot.Fill = FormatHelpers.GetFrozenBrush("#FF22C55E");
            StatusText.Foreground = FormatHelpers.GetFrozenBrush("#FFDCFCE7");
            StatusText.Text = "iRacing Live";
        }
        else
        {
            StatusBadge.Background = FormatHelpers.GetFrozenBrush("#1AF59E0B");
            StatusBadge.BorderBrush = FormatHelpers.GetFrozenBrush("#44F59E0B");
            StatusDot.Fill = FormatHelpers.GetFrozenBrush("#FFF59E0B");
            StatusText.Foreground = FormatHelpers.GetFrozenBrush("#FFFEF3C7");
            StatusText.Text = "Ожидание iRacing";
        }
    }

    public void ToggleLock()
    {
        _settings.Locked = !_settings.Locked;
        UpdateLockButtonVisual();
        _onSettingsChanged();
    }

    private void UpdateLockButtonVisual()
    {
        if (_settings.Locked)
        {
            LockButton.Style = (Style)FindResource("ModernButton");
            LockButton.Content = "Разблокировать (Ctrl+Shift+L)";
        }
        else
        {
            LockButton.Style = (Style)FindResource("ModernPrimaryButton");
            LockButton.Content = "Закрепить (Ctrl+Shift+L)";
        }
    }

    private void GlobalScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingUi || _settings == null) return;
        double val = Math.Round(e.NewValue, 2);
        _settings.GlobalScale = val;
        SyncUiFromSettings();
        _onSettingsChanged();
    }

    private void BackdropOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingUi || _settings == null) return;
        double val = Math.Round(e.NewValue, 2);
        _settings.BackgroundOpacity = val;
        SyncUiFromSettings();
        _onSettingsChanged();
    }

    private void QuickScalePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string s &&
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
        {
            _settings.GlobalScale = val;
            SyncUiFromSettings();
            _onSettingsChanged();
        }
    }

    private void UpdateSkinButtons()
    {
        if (SkinIRacingBtn == null || SkinCarbonBtn == null) return;
        var primary = (Style)FindResource("ModernPrimaryButton");
        var normal = (Style)FindResource("ModernButton");
        bool isNative = string.Equals(_settings.UiSkin, "IRacingNative", StringComparison.OrdinalIgnoreCase);
        SkinIRacingBtn.Style = isNative ? primary : normal;
        SkinCarbonBtn.Style = isNative ? normal : primary;
    }

    private void SkinIRacing_Click(object sender, RoutedEventArgs e)
    {
        _settings.UiSkin = "IRacingNative";
        _settings.ThemePreset = "IRacingNative";
        _settings.BackgroundOpacity = 0.82;
        SyncUiFromSettings();
        _onSettingsChanged();
    }

    private void SkinCarbon_Click(object sender, RoutedEventArgs e)
    {
        _settings.UiSkin = "ApexCarbon";
        _settings.ThemePreset = "CarbonGlass";
        _settings.BackgroundOpacity = 0.62;
        SyncUiFromSettings();
        _onSettingsChanged();
    }

    private void PresetCarbon_Click(object sender, RoutedEventArgs e)
    {
        _settings.ThemePreset = "CarbonGlass";
        _settings.BackgroundOpacity = 0.60;
        SyncUiFromSettings();
        _onSettingsChanged();
    }

    private void PresetStealth_Click(object sender, RoutedEventArgs e)
    {
        _settings.ThemePreset = "Stealth";
        _settings.BackgroundOpacity = 0.28;
        SyncUiFromSettings();
        _onSettingsChanged();
    }

    private void PresetBroadcast_Click(object sender, RoutedEventArgs e)
    {
        _settings.ThemePreset = "Broadcast";
        _settings.BackgroundOpacity = 0.82;
        SyncUiFromSettings();
        _onSettingsChanged();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void ToggleLock_Click(object sender, RoutedEventArgs e) => ToggleLock();

    private void ResetLayout_Click(object sender, RoutedEventArgs e)
    {
        _onResetLayout();
        SyncUiFromSettings();
    }

    private void DemoMode_Click(object sender, RoutedEventArgs e)
    {
        _settings.DemoMode = DemoModeCheck.IsChecked == true;
        _onSettingsChanged();
    }

    private void PrefsChanged_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShowCarBrandIcons = ShowBrandIconsCheck.IsChecked == true;
        _settings.ShowRelativeHeader = RelShowHeaderCheck.IsChecked == true;
        _settings.ShowRelativeCarNumber = RelShowNumCheck.IsChecked == true;
        _settings.ShowRelativePaceDelta = RelShowPaceCheck.IsChecked == true;
        _settings.ShowRelativeIRating = RelShowIrCheck.IsChecked == true;
        _settings.ShowRelativeLicense = RelShowLicCheck.IsChecked == true;

        _settings.ShowStandingsHeader = StdShowHeaderCheck.IsChecked == true;
        _settings.ShowStandingsCarNumber = StdShowNumCheck.IsChecked == true;
        _settings.ShowStandingsPosDelta = StdShowPosDeltaCheck.IsChecked == true;
        _settings.ShowStandingsTireCompound = StdShowTireCheck.IsChecked == true;
        _settings.ShowStandingsIRating = StdShowIrCheck.IsChecked == true;
        _settings.ShowStandingsInterval = StdShowIntervalCheck.IsChecked == true;
        _settings.ShowStandingsDeltaIR = StdShowDeltaIrCheck.IsChecked == true;
        _settings.ShowStandingsLastLap = StdShowLastLapCheck.IsChecked == true;
        _settings.ShowStandingsPits = StdShowPitsCheck.IsChecked == true;

        _settings.ShowInputsGraph = InpShowGraphCheck.IsChecked == true;
        _settings.ShowInputsPedalBars = InpShowPedalBarsCheck.IsChecked == true;
        _settings.ShowInputsGearCluster = InpShowGearCheck.IsChecked == true;
        _settings.ShowInputsShiftBar = InpShowShiftCheck.IsChecked == true;
        _settings.ShowInputsSteering = InpShowWheelCheck.IsChecked == true;
        _settings.ShowInputsClutch = InpShowClutchCheck.IsChecked == true;
        _settings.ShowInputsElectronics = InpShowElecCheck.IsChecked == true;
        _settings.ShowInputsDeltaBar = InpShowDeltaCheck.IsChecked == true;
        _settings.ShowInputsCornerSpeed = InpShowCornerSpeedCheck.IsChecked == true;

        _settings.RadarAutoHideWhenClear = RadarAutoHideCheck.IsChecked == true;
        _settings.ShowOnlyWhenIRacingFocused = OnlyIRacingFocusCheck.IsChecked == true;
        _settings.AutoHideOutOfSim = AutoHideOutOfSimCheck.IsChecked == true;
        _settings.UseImperialUnits = ImperialUnitsCheck.IsChecked == true;
        _onSettingsChanged();
    }

    private void StandingsRowsMinus_Click(object sender, RoutedEventArgs e)
    {
        _settings.StandingsMaxRows = Math.Max(4, _settings.StandingsMaxRows - 1);
        StandingsRowsText.Text = _settings.StandingsMaxRows.ToString();
        _onSettingsChanged();
    }

    private void StandingsRowsPlus_Click(object sender, RoutedEventArgs e)
    {
        _settings.StandingsMaxRows = Math.Min(18, _settings.StandingsMaxRows + 1);
        StandingsRowsText.Text = _settings.StandingsMaxRows.ToString();
        _onSettingsChanged();
    }

    private void FuelMarginMinus_Click(object sender, RoutedEventArgs e)
    {
        _settings.FuelSafetyMarginLaps = Math.Max(0.0, Math.Round(_settings.FuelSafetyMarginLaps - 0.5, 1));
        FuelMarginText.Text = $"+{_settings.FuelSafetyMarginLaps:0.0} L";
        _onSettingsChanged();
    }

    private void FuelMarginPlus_Click(object sender, RoutedEventArgs e)
    {
        _settings.FuelSafetyMarginLaps = Math.Min(5.0, Math.Round(_settings.FuelSafetyMarginLaps + 0.5, 1));
        FuelMarginText.Text = $"+{_settings.FuelSafetyMarginLaps:0.0} L";
        _onSettingsChanged();
    }

    private void MinimizeToTray_Click(object sender, RoutedEventArgs e) => _onMinimizeToTray();
    private void ExitApp_Click(object sender, RoutedEventArgs e) => _onExitApp();
}
