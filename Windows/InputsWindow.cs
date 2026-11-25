using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ApexHUD.Controls;
using ApexHUD.Core.Settings;
using ApexHUD.Core.Telemetry;

namespace ApexHUD.Windows;

/// <summary>
/// Racelab-grade Input Telemetry HUD:
/// Shift lights + 60Hz rolling graph + pedal bars + Gear/Speed/RPM + GT3 Steering Wheel
/// + live In-Car Electronics strip (Brake Bias, TC, ABS) + live Lap Delta Bar!
/// </summary>
public sealed class InputsWindow : OverlayWindowBase
{
    private readonly Border _outerBorder;
    private readonly Border _shiftRowHost;
    private readonly ShiftLightBar _shiftBar;
    private readonly InputGraphCanvas _graph;
    private readonly Border _absBadge;
    private readonly Border _apexBadge;
    private readonly TextBlock _apexText;

    // Racelab Corner Minimum (Apex) Speed tracker
    private bool _inCorner;
    private float _currentCornerMinMs = float.MaxValue;
    private float _lastApexSpeedMs;
    private long _lastApexLockMs;
    private float _recentMaxSpeedMs;

    private readonly StackPanel _clutchCol;
    private readonly Border _clutchFill;
    private readonly Border _brakeFill;
    private readonly Border _throttleFill;
    private readonly TextBlock _clutchPct;
    private readonly TextBlock _brakePct;
    private readonly TextBlock _throttlePct;

    private readonly TextBlock _gearText;
    private readonly TextBlock _speedText;
    private readonly TextBlock _unitText;
    private readonly TextBlock _rpmText;

    private readonly ColumnDefinition _wheelDividerCol;
    private readonly ColumnDefinition _wheelCol;
    private readonly Border _wheelDivider;
    private readonly StackPanel _wheelStack;
    private readonly SteeringWheelVisual _wheel;
    private readonly TextBlock _steerDegText;

    // Racelab Electronics + Live Delta Footer
    private readonly Border _bottomStripHost;
    private readonly StackPanel _elecPanel;
    private readonly TextBlock _bbText;
    private readonly TextBlock _tcText;
    private readonly TextBlock _absSetText;
    private readonly Grid _deltaPanel;
    private readonly Border _deltaBarLeft;
    private readonly Border _deltaBarRight;
    private readonly TextBlock _deltaText;

    private const double PedalBarHeight = 68.0;

    private static readonly SolidColorBrush Hairline = FormatHelpers.GetFrozenBrush("#1AFFFFFF");
    private static readonly SolidColorBrush TextWhite = FormatHelpers.GetFrozenBrush("#F8FAFC");
    private static readonly SolidColorBrush TextMuted = FormatHelpers.GetFrozenBrush("#94A3B8");
    private static readonly SolidColorBrush ClutchBrush = FormatHelpers.GetFrozenBrush("#38BDF8");
    private static readonly SolidColorBrush BrakeBrush = FormatHelpers.GetFrozenBrush("#EF4444");
    private static readonly SolidColorBrush ThrottleBrush = FormatHelpers.GetFrozenBrush("#22C55E");
    private static readonly SolidColorBrush AbsBrush = FormatHelpers.GetFrozenBrush("#F59E0B");
    private static readonly SolidColorBrush DeltaGreen = FormatHelpers.GetFrozenBrush("#22C55E");
    private static readonly SolidColorBrush DeltaRed = FormatHelpers.GetFrozenBrush("#EF4444");

    public InputsWindow(AppSettings settings) : base("Inputs", settings)
    {
        _outerBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            SnapsToDevicePixels = true
        };

        var rootStack = new StackPanel();

        _shiftBar = new ShiftLightBar
        {
            Height = 6,
            Margin = new Thickness(6, 5, 6, 5)
        };
        _shiftRowHost = new Border
        {
            BorderBrush = Hairline,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = _shiftBar
        };
        rootStack.Children.Add(_shiftRowHost);

        var mainGrid = new Grid { Height = 100 };
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        _wheelDividerCol = new ColumnDefinition { Width = new GridLength(1) };
        _wheelCol = new ColumnDefinition { Width = new GridLength(82) };
        mainGrid.ColumnDefinitions.Add(_wheelDividerCol);
        mainGrid.ColumnDefinitions.Add(_wheelCol);

        // Zone 1: Rolling 60Hz Graph + Apex Min Speed + ABS badge
        var graphHost = new Grid { Margin = new Thickness(6, 5, 6, 5) };
        _graph = new InputGraphCanvas();
        graphHost.Children.Add(_graph);

        _apexText = new TextBlock
        {
            Text = "MIN --",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = TextWhite
        };
        _apexBadge = new Border
        {
            Background = FormatHelpers.GetFrozenBrush("#CC0B0E14"),
            BorderBrush = FormatHelpers.GetFrozenBrush("#6638BDF8"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(5, 1, 5, 1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(4, 4, 0, 0),
            Visibility = Visibility.Collapsed,
            Child = _apexText
        };
        graphHost.Children.Add(_apexBadge);

        _absBadge = new Border
        {
            Background = AbsBrush,
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(5, 1, 5, 1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 4, 0),
            Visibility = Visibility.Collapsed,
            Child = new TextBlock
            {
                Text = "ABS",
                FontSize = 9.5,
                FontWeight = FontWeights.ExtraBold,
                Foreground = FormatHelpers.GetFrozenBrush("#0B0E14")
            }
        };
        graphHost.Children.Add(_absBadge);
        Grid.SetColumn(graphHost, 0);
        mainGrid.Children.Add(graphHost);

        AddDivider(mainGrid, 1);

        // Zone 2: Pedals
        var pedalsStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(7, 6, 7, 5),
            VerticalAlignment = VerticalAlignment.Center
        };
        (_clutchCol, _clutchFill, _clutchPct) = MakePedalColumn(ClutchBrush);
        var (brakeCol, brakeFill, brakePct) = MakePedalColumn(BrakeBrush);
        var (throttleCol, throttleFill, throttlePct) = MakePedalColumn(ThrottleBrush);
        _brakeFill = brakeFill;
        _brakePct = brakePct;
        _throttleFill = throttleFill;
        _throttlePct = throttlePct;

        pedalsStack.Children.Add(_clutchCol);
        pedalsStack.Children.Add(brakeCol);
        pedalsStack.Children.Add(throttleCol);
        Grid.SetColumn(pedalsStack, 2);
        mainGrid.Children.Add(pedalsStack);

        AddDivider(mainGrid, 3);

        // Zone 3: Gear / Speed / RPM
        var cluster = new Grid { Margin = new Thickness(4, 3, 4, 4) };
        cluster.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        cluster.RowDefinitions.Add(new RowDefinition { Height = new GridLength(25) });
        cluster.RowDefinitions.Add(new RowDefinition { Height = new GridLength(17) });

        _gearText = new TextBlock
        {
            Text = "N",
            FontSize = 38,
            FontWeight = FontWeights.Bold,
            Foreground = TextWhite,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(_gearText, 0);
        cluster.Children.Add(_gearText);

        var speedRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _speedText = new TextBlock
        {
            Text = "0",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextWhite,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        _unitText = new TextBlock
        {
            Text = "km/h",
            FontSize = 9.5,
            FontWeight = FontWeights.Medium,
            Foreground = TextMuted,
            Margin = new Thickness(3, 0, 0, 2),
            VerticalAlignment = VerticalAlignment.Bottom
        };
        speedRow.Children.Add(_speedText);
        speedRow.Children.Add(_unitText);
        Grid.SetRow(speedRow, 1);
        cluster.Children.Add(speedRow);

        _rpmText = new TextBlock
        {
            Text = "0 rpm",
            FontSize = 10,
            Foreground = TextMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(_rpmText, 2);
        cluster.Children.Add(_rpmText);

        Grid.SetColumn(cluster, 4);
        mainGrid.Children.Add(cluster);

        _wheelDivider = new Border { Width = 1, Background = Hairline, VerticalAlignment = VerticalAlignment.Stretch };
        Grid.SetColumn(_wheelDivider, 5);
        mainGrid.Children.Add(_wheelDivider);

        // Zone 4: Steering Wheel
        _wheelStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _wheel = new SteeringWheelVisual { Width = 58, Height = 58 };
        _steerDegText = new TextBlock
        {
            Text = "0°",
            FontSize = 10.5,
            Foreground = TextMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0)
        };
        _wheelStack.Children.Add(_wheel);
        _wheelStack.Children.Add(_steerDegText);
        Grid.SetColumn(_wheelStack, 6);
        mainGrid.Children.Add(_wheelStack);

        rootStack.Children.Add(mainGrid);

        // Racelab Bottom Strip: Electronics (BB / TC / ABS) + Live Delta Bar
        var bottomGrid = new Grid { Height = 24, Margin = new Thickness(8, 0, 8, 0) };
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _elecPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _bbText = MakeElecPill("BB --", "#F87171");
        _tcText = MakeElecPill("TC --", "#38BDF8");
        _absSetText = MakeElecPill("ABS --", "#FBBF24");
        _elecPanel.Children.Add(_bbText);
        _elecPanel.Children.Add(_tcText);
        _elecPanel.Children.Add(_absSetText);
        Grid.SetColumn(_elecPanel, 0);
        bottomGrid.Children.Add(_elecPanel);

        // Compact Delta Bar + numeric readout
        _deltaPanel = new Grid { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        _deltaPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        _deltaPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });

        var barBg = new Grid { Height = 6, Background = FormatHelpers.GetFrozenBrush("#20FFFFFF"), VerticalAlignment = VerticalAlignment.Center };
        barBg.ColumnDefinitions.Add(new ColumnDefinition());
        barBg.ColumnDefinitions.Add(new ColumnDefinition());
        _deltaBarLeft = new Border { Background = DeltaGreen, HorizontalAlignment = HorizontalAlignment.Right, Width = 0, CornerRadius = new CornerRadius(1, 0, 0, 1) };
        _deltaBarRight = new Border { Background = DeltaRed, HorizontalAlignment = HorizontalAlignment.Left, Width = 0, CornerRadius = new CornerRadius(0, 1, 1, 0) };
        Grid.SetColumn(_deltaBarLeft, 0);
        Grid.SetColumn(_deltaBarRight, 1);
        barBg.Children.Add(_deltaBarLeft);
        barBg.Children.Add(_deltaBarRight);
        // Center tick
        var centerTick = new Border { Width = 1, Background = FormatHelpers.GetFrozenBrush("#66FFFFFF"), HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetColumnSpan(centerTick, 2);
        barBg.Children.Add(centerTick);
        Grid.SetColumn(barBg, 0);
        _deltaPanel.Children.Add(barBg);

        _deltaText = new TextBlock
        {
            Text = "+0.00",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = TextMuted,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_deltaText, 1);
        _deltaPanel.Children.Add(_deltaText);

        Grid.SetColumn(_deltaPanel, 1);
        bottomGrid.Children.Add(_deltaPanel);

        _bottomStripHost = new Border
        {
            BorderBrush = Hairline,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = bottomGrid
        };
        rootStack.Children.Add(_bottomStripHost);

        _outerBorder.Child = rootStack;
        AttachOverlayRoot(_outerBorder, "Inputs");
    }

    private static TextBlock MakeElecPill(string init, string colorHex) => new()
    {
        Text = init,
        FontSize = 10.5,
        FontWeight = FontWeights.SemiBold,
        Foreground = FormatHelpers.GetFrozenBrush(colorHex),
        Margin = new Thickness(0, 0, 12, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    protected override void OnAppearanceUpdated()
    {
        if (_outerBorder == null) return;
        _outerBorder.Background = MakeBackdropBrush(1.0);
        _outerBorder.BorderBrush = MakeBorderBrush();
        _graph.SetBackdropOpacity(Settings.BackgroundOpacity);
        _graph.ShowClutch = Settings.ShowInputsClutch;
        _graph.WindowSeconds = Settings.InputsGraphSeconds;

        var uiFont = GetUiFont();
        var monoFont = GetMonoFont();

        _clutchPct.FontFamily = monoFont;
        _brakePct.FontFamily = monoFont;
        _throttlePct.FontFamily = monoFont;
        _gearText.FontFamily = monoFont;
        _speedText.FontFamily = monoFont;
        _unitText.FontFamily = uiFont;
        _rpmText.FontFamily = monoFont;
        _steerDegText.FontFamily = monoFont;
        _bbText.FontFamily = monoFont;
        _tcText.FontFamily = monoFont;
        _absSetText.FontFamily = monoFont;
        _deltaText.FontFamily = monoFont;
        _apexText.FontFamily = monoFont;

        if (!Settings.ShowInputsCornerSpeed)
            _apexBadge.Visibility = Visibility.Collapsed;

        _shiftRowHost.Visibility = Settings.ShowInputsShiftBar ? Visibility.Visible : Visibility.Collapsed;
        _clutchCol.Visibility = Settings.ShowInputsClutch ? Visibility.Visible : Visibility.Collapsed;

        bool showWheel = Settings.ShowInputsSteering;
        _wheelDividerCol.Width = new GridLength(showWheel ? 1 : 0);
        _wheelCol.Width = new GridLength(showWheel ? 82 : 0);
        _wheelDivider.Visibility = showWheel ? Visibility.Visible : Visibility.Collapsed;
        _wheelStack.Visibility = showWheel ? Visibility.Visible : Visibility.Collapsed;

        _elecPanel.Visibility = Settings.ShowInputsElectronics ? Visibility.Visible : Visibility.Collapsed;
        _deltaPanel.Visibility = Settings.ShowInputsDeltaBar ? Visibility.Visible : Visibility.Collapsed;
        _bottomStripHost.Visibility = (Settings.ShowInputsElectronics || Settings.ShowInputsDeltaBar)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static void AddDivider(Grid g, int col)
    {
        var d = new Border { Width = 1, Background = Hairline, VerticalAlignment = VerticalAlignment.Stretch };
        Grid.SetColumn(d, col);
        g.Children.Add(d);
    }

    private static (StackPanel Col, Border Fill, TextBlock Label) MakePedalColumn(Brush fillBrush)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(3, 0, 3, 0) };

        var track = new Border
        {
            Width = 14,
            Height = PedalBarHeight,
            Background = FormatHelpers.GetFrozenBrush("#18FFFFFF"),
            BorderBrush = FormatHelpers.GetFrozenBrush("#22FFFFFF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2)
        };

        var fill = new Border
        {
            Background = fillBrush,
            Height = 0,
            CornerRadius = new CornerRadius(1),
            VerticalAlignment = VerticalAlignment.Bottom
        };
        track.Child = fill;

        var pct = new TextBlock
        {
            Text = "0",
            FontSize = 9.5,
            Foreground = TextMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0),
            Width = 22,
            TextAlignment = TextAlignment.Center
        };

        stack.Children.Add(track);
        stack.Children.Add(pct);
        return (stack, fill, pct);
    }

    public void UpdateData(TelemetrySnapshot snap)
    {
        var inp = snap.Inputs;

        _graph.PushSample(inp.Throttle, inp.Brake, inp.Clutch, inp.AbsActive);
        _absBadge.Visibility = inp.AbsActive ? Visibility.Visible : Visibility.Collapsed;

        _clutchFill.Height = Math.Round(inp.Clutch * (PedalBarHeight - 2));
        _brakeFill.Height = Math.Round(inp.Brake * (PedalBarHeight - 2));
        _throttleFill.Height = Math.Round(inp.Throttle * (PedalBarHeight - 2));

        _clutchPct.Text = ((int)Math.Round(inp.Clutch * 100)).ToString();
        _brakePct.Text = ((int)Math.Round(inp.Brake * 100)).ToString();
        _throttlePct.Text = ((int)Math.Round(inp.Throttle * 100)).ToString();

        _gearText.Text = inp.Gear switch
        {
            -1 => "R",
            0 => "N",
            _ => inp.Gear.ToString()
        };

        float speed = Settings.UseImperialUnits ? inp.SpeedMs * 2.236936f : inp.SpeedMs * 3.6f;
        _speedText.Text = ((int)Math.Round(speed)).ToString();
        _unitText.Text = Settings.UseImperialUnits ? "mph" : "km/h";
        _rpmText.Text = $"{(int)Math.Round(inp.Rpm)} rpm";

        if (Settings.ShowInputsShiftBar)
            _shiftBar.UpdateRpm(inp.Rpm, inp.ShiftFirstRpm, inp.ShiftOptimalRpm, inp.ShiftBlinkRpm, inp.ShiftPct);

        if (Settings.ShowInputsSteering)
        {
            _wheel.UpdateAngle(inp.SteeringAngleRad);
            int steerDeg = (int)Math.Round(-inp.SteeringAngleRad * (180.0 / Math.PI));
            _steerDegText.Text = $"{steerDeg}°";
        }

        UpdateCornerMinSpeed(inp);

        if (Settings.ShowInputsElectronics)
        {
            _bbText.Text = inp.BrakeBiasPct > 10f ? $"BB {inp.BrakeBiasPct:0.0}%" : "BB --";
            _tcText.Text = inp.TractionControl >= 0 ? $"TC {inp.TractionControl}" : "TC --";
            _absSetText.Text = inp.AbsSetting >= 0 ? $"ABS {inp.AbsSetting}" : "ABS --";
        }

        if (Settings.ShowInputsDeltaBar)
        {
            if (!inp.HasLapDelta)
            {
                _deltaText.Text = "--";
                _deltaText.Foreground = TextMuted;
                _deltaBarLeft.Width = 0;
                _deltaBarRight.Width = 0;
            }
            else
            {
                float d = inp.LapDeltaToBestSec;
                double frac = Math.Clamp(Math.Abs(d) / 1.0, 0.0, 1.0); // full bar at +/- 1.0s
                if (d <= 0f)
                {
                    _deltaBarLeft.Width = Math.Round(frac * 48.0);
                    _deltaBarRight.Width = 0;
                    _deltaText.Text = $"{d:0.00}";
                    _deltaText.Foreground = DeltaGreen;
                }
                else
                {
                    _deltaBarLeft.Width = 0;
                    _deltaBarRight.Width = Math.Round(frac * 48.0);
                    _deltaText.Text = $"+{d:0.00}";
                    _deltaText.Foreground = DeltaRed;
                }
            }
        }
    }

    private void UpdateCornerMinSpeed(InputTelemetry inp)
    {
        if (!Settings.ShowInputsCornerSpeed)
        {
            _apexBadge.Visibility = Visibility.Collapsed;
            return;
        }

        long nowMs = Environment.TickCount64;
        float speedMs = inp.SpeedMs;
        float steerDegAbs = Math.Abs(inp.SteeringAngleRad * (180f / (float)Math.PI));

        // Track recent straightaway peak speed
        if (speedMs > _recentMaxSpeedMs)
            _recentMaxSpeedMs = speedMs;
        else
            _recentMaxSpeedMs = Math.Max(speedMs, _recentMaxSpeedMs - 0.05f);

        // Ignore stationary / pit crawling below 20 km/h (~5.5 m/s)
        if (speedMs < 5.5f)
        {
            _inCorner = false;
            _currentCornerMinMs = float.MaxValue;
            return;
        }

        bool brakingOrSlowingCorner = inp.Brake > 0.08f || (steerDegAbs > 24f && speedMs < _recentMaxSpeedMs - 2.5f);

        if (!_inCorner)
        {
            if (brakingOrSlowingCorner)
            {
                _inCorner = true;
                _currentCornerMinMs = speedMs;
            }
        }
        else
        {
            if (speedMs < _currentCornerMinMs)
                _currentCornerMinMs = speedMs;

            // Lock apex speed once driver releases brake and accelerates cleanly out of apex
            bool exiting = inp.Brake < 0.03f && inp.Throttle > 0.25f && speedMs > _currentCornerMinMs + 1.2f;
            if (exiting)
            {
                _lastApexSpeedMs = _currentCornerMinMs;
                _lastApexLockMs = nowMs;
                _inCorner = false;
                _recentMaxSpeedMs = speedMs;
            }
        }

        float displayMs = _inCorner ? _currentCornerMinMs : _lastApexSpeedMs;
        if (displayMs <= 0.1f || displayMs > 200f)
        {
            _apexBadge.Visibility = Visibility.Collapsed;
            return;
        }

        _apexBadge.Visibility = Visibility.Visible;
        float conv = Settings.UseImperialUnits ? 2.236936f : 3.6f;
        string unit = Settings.UseImperialUnits ? "mph" : "km/h";
        int shown = (int)Math.Round(displayMs * conv);
        _apexText.Text = $"MIN {shown} {unit}";

        bool freshLock = !_inCorner && (nowMs - _lastApexLockMs) < 5500;
        if (_inCorner)
        {
            _apexBadge.BorderBrush = ClutchBrush;
            _apexText.Foreground = ClutchBrush;
        }
        else if (freshLock)
        {
            _apexBadge.BorderBrush = DeltaGreen;
            _apexText.Foreground = TextWhite;
        }
        else
        {
            _apexBadge.BorderBrush = FormatHelpers.GetFrozenBrush("#33FFFFFF");
            _apexText.Foreground = TextMuted;
        }
    }
}
