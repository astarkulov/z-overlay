using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ApexHUD.Controls;
using ApexHUD.Core.Settings;
using ApexHUD.Core.Telemetry;

namespace ApexHUD.Windows;

/// <summary>
/// 100% Modular Racelab-grade Input Telemetry HUD:
/// Every module can be toggled independently (Shift bar, Pedal graph [configurable width & seconds],
/// Vertical pedal bars, Gear/Speed/RPM cluster, Steering wheel, Clutch, BB/TC/ABS, Delta Bar,
/// and Racelab 4-Corner Min Speed comparison vs Session Fastest).
/// </summary>
public sealed class InputsWindow : OverlayWindowBase
{
    private readonly Border _outerBorder;
    private readonly StackPanel _rootStack;
    private readonly Border _shiftRowHost;
    private readonly ShiftLightBar _shiftBar;

    // Main horizontal modular zones + smart dividers
    private readonly StackPanel _mainRow;
    private readonly Grid _graphZone;
    private readonly InputGraphCanvas _graph;
    private readonly Border _absBadge;

    private readonly Border _divAfterGraph;
    private readonly StackPanel _pedalsZone;
    private readonly StackPanel _clutchCol;
    private readonly Border _clutchFill;
    private readonly Border _brakeFill;
    private readonly Border _throttleFill;
    private readonly TextBlock _clutchPct;
    private readonly TextBlock _brakePct;
    private readonly TextBlock _throttlePct;

    private readonly Border _divAfterPedals;
    private readonly Grid _gearZone;
    private readonly TextBlock _gearText;
    private readonly TextBlock _speedText;
    private readonly TextBlock _unitText;
    private readonly TextBlock _rpmText;

    private readonly Border _divAfterGear;
    private readonly StackPanel _wheelZone;
    private readonly SteeringWheelVisual _wheel;
    private readonly TextBlock _steerDegText;

    // Bottom Strip 1: Electronics (BB/TC/ABS) + Live Delta Bar
    private readonly Border _elecDeltaHost;
    private readonly StackPanel _elecPanel;
    private readonly TextBlock _bbText;
    private readonly TextBlock _tcText;
    private readonly TextBlock _absSetText;
    private readonly Grid _deltaPanel;
    private readonly Border _deltaBarLeft;
    private readonly Border _deltaBarRight;
    private readonly TextBlock _deltaText;

    // Bottom Strip 2: Racelab 4-Corner Min Speed Strip (Last 4 corners: Your Min vs Session Fastest)
    private readonly Border _cornersHost;
    private readonly CornerSlotVisual[] _cornerSlots = new CornerSlotVisual[4];

    private const double PedalBarHeight = 56.0;

    private static readonly SolidColorBrush Hairline = FormatHelpers.GetFrozenBrush("#24FFFFFF");
    private static readonly SolidColorBrush TextWhite = FormatHelpers.GetFrozenBrush("#FFFFFF");
    private static readonly SolidColorBrush TextSoft = FormatHelpers.GetFrozenBrush("#E6EDF7");
    private static readonly SolidColorBrush TextMuted = FormatHelpers.GetFrozenBrush("#CBD5E1");
    private static readonly SolidColorBrush ClutchBrush = FormatHelpers.GetFrozenBrush("#38BDF8");
    private static readonly SolidColorBrush BrakeBrush = FormatHelpers.GetFrozenBrush("#FF4757");
    private static readonly SolidColorBrush ThrottleBrush = FormatHelpers.GetFrozenBrush("#2ED573");
    private static readonly SolidColorBrush AbsBrush = FormatHelpers.GetFrozenBrush("#FCD34D");
    private static readonly SolidColorBrush DeltaGreen = FormatHelpers.GetFrozenBrush("#4ADE80");
    private static readonly SolidColorBrush DeltaRed = FormatHelpers.GetFrozenBrush("#FF5A6E");
    private static readonly SolidColorBrush PurpleFastest = FormatHelpers.GetFrozenBrush("#E879F9");
    private static readonly SolidColorBrush IRacingGold = FormatHelpers.GetFrozenBrush("#F2B830");
    private static readonly SolidColorBrush CornerLiveBg = FormatHelpers.GetFrozenBrush("#2838BDF8");
    private static readonly SolidColorBrush CornerIdleBg = FormatHelpers.GetFrozenBrush("#12FFFFFF");

    private sealed class CornerSlotVisual
    {
        public Border Box = null!;
        public TextBlock TurnLabel = null!;      // e.g. T14
        public TextBlock PlayerSpeed = null!;    // WHITE: current / last
        public TextBlock Slash1 = null!;
        public TextBlock PersonalBest = null!;   // GREEN: my session max in this corner
        public TextBlock Slash2 = null!;
        public TextBlock SessionFastest = null!; // PURPLE: fastest-lap holder's min speed
    }

    public InputsWindow(AppSettings settings) : base("Inputs", settings)
    {
        _outerBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            SnapsToDevicePixels = true
        };

        _rootStack = new StackPanel { Effect = GetActiveShadow() };

        // 1) Top Shift Light Bar
        _shiftBar = new ShiftLightBar
        {
            Height = 5,
            Margin = new Thickness(4, 3, 4, 3)
        };
        _shiftRowHost = new Border
        {
            BorderBrush = Hairline,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = _shiftBar
        };
        _rootStack.Children.Add(_shiftRowHost);

        // 2) Main Modular Horizontal Row
        _mainRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 80
        };

        // Zone A: 60Hz Pedal Graph (configurable width & duration)
        _graphZone = new Grid
        {
            Width = 240,
            Margin = new Thickness(4, 3, 4, 3)
        };
        _graph = new InputGraphCanvas();
        _graphZone.Children.Add(_graph);

        _absBadge = new Border
        {
            Background = AbsBrush,
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(4, 0, 4, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 3, 3, 0),
            Visibility = Visibility.Collapsed,
            Child = new TextBlock
            {
                Text = "ABS",
                FontSize = 9.5,
                FontWeight = FontWeights.ExtraBold,
                Foreground = FormatHelpers.GetFrozenBrush("#0B0E14")
            }
        };
        _graphZone.Children.Add(_absBadge);
        _mainRow.Children.Add(_graphZone);

        _divAfterGraph = MakeVerticalDivider();
        _mainRow.Children.Add(_divAfterGraph);

        // Zone B: Vertical Pedal Bars
        _pedalsZone = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(4, 3, 4, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        (_clutchCol, _clutchFill, _clutchPct) = MakePedalColumn(ClutchBrush);
        var (brakeCol, brakeFill, brakePct) = MakePedalColumn(BrakeBrush);
        var (throttleCol, throttleFill, throttlePct) = MakePedalColumn(ThrottleBrush);
        _brakeFill = brakeFill;
        _brakePct = brakePct;
        _throttleFill = throttleFill;
        _throttlePct = throttlePct;

        _pedalsZone.Children.Add(_clutchCol);
        _pedalsZone.Children.Add(brakeCol);
        _pedalsZone.Children.Add(throttleCol);
        _mainRow.Children.Add(_pedalsZone);

        _divAfterPedals = MakeVerticalDivider();
        _mainRow.Children.Add(_divAfterPedals);

        // Zone C: Large Crisp Gear / Speed / RPM Cluster
        _gearZone = new Grid
        {
            MinWidth = 72,
            Margin = new Thickness(4, 1, 4, 1)
        };
        _gearZone.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        _gearZone.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        _gearZone.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });

        _gearText = new TextBlock
        {
            Text = "N",
            FontSize = 32,
            FontWeight = FontWeights.Bold,
            Foreground = TextWhite,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(_gearText, 0);
        _gearZone.Children.Add(_gearText);

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
            FontWeight = FontWeights.Bold,
            Foreground = TextWhite,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        _unitText = new TextBlock
        {
            Text = "km/h",
            FontSize = 9.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextMuted,
            Margin = new Thickness(2, 0, 0, 2),
            VerticalAlignment = VerticalAlignment.Bottom
        };
        speedRow.Children.Add(_speedText);
        speedRow.Children.Add(_unitText);
        Grid.SetRow(speedRow, 1);
        _gearZone.Children.Add(speedRow);

        _rpmText = new TextBlock
        {
            Text = "0 rpm",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(_rpmText, 2);
        _gearZone.Children.Add(_rpmText);
        _mainRow.Children.Add(_gearZone);

        _divAfterGear = MakeVerticalDivider();
        _mainRow.Children.Add(_divAfterGear);

        // Zone D: Steering Wheel Visual
        _wheelZone = new StackPanel
        {
            Width = 64,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _wheel = new SteeringWheelVisual { Width = 46, Height = 46 };
        _steerDegText = new TextBlock
        {
            Text = "0°",
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextSoft,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 0)
        };
        _wheelZone.Children.Add(_wheel);
        _wheelZone.Children.Add(_steerDegText);
        _mainRow.Children.Add(_wheelZone);

        _rootStack.Children.Add(_mainRow);

        // 3) Bottom Strip A: Electronics (BB/TC/ABS) + Live Delta Bar
        var elecDeltaGrid = new Grid { Height = 20, Margin = new Thickness(5, 0, 5, 0) };
        elecDeltaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        elecDeltaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _elecPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _bbText = MakeElecPill("BB --", "#FF6B81");
        _tcText = MakeElecPill("TC --", "#67E8F9");
        _absSetText = MakeElecPill("ABS --", "#FCD34D");
        _elecPanel.Children.Add(_bbText);
        _elecPanel.Children.Add(_tcText);
        _elecPanel.Children.Add(_absSetText);
        Grid.SetColumn(_elecPanel, 0);
        elecDeltaGrid.Children.Add(_elecPanel);

        _deltaPanel = new Grid { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        _deltaPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        _deltaPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });

        var barBg = new Grid { Height = 6, Background = FormatHelpers.GetFrozenBrush("#28FFFFFF"), VerticalAlignment = VerticalAlignment.Center };
        barBg.ColumnDefinitions.Add(new ColumnDefinition());
        barBg.ColumnDefinitions.Add(new ColumnDefinition());
        _deltaBarLeft = new Border { Background = DeltaGreen, HorizontalAlignment = HorizontalAlignment.Right, Width = 0, CornerRadius = new CornerRadius(1, 0, 0, 1) };
        _deltaBarRight = new Border { Background = DeltaRed, HorizontalAlignment = HorizontalAlignment.Left, Width = 0, CornerRadius = new CornerRadius(0, 1, 1, 0) };
        Grid.SetColumn(_deltaBarLeft, 0);
        Grid.SetColumn(_deltaBarRight, 1);
        barBg.Children.Add(_deltaBarLeft);
        barBg.Children.Add(_deltaBarRight);
        var centerTick = new Border { Width = 1, Background = FormatHelpers.GetFrozenBrush("#88FFFFFF"), HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetColumnSpan(centerTick, 2);
        barBg.Children.Add(centerTick);
        Grid.SetColumn(barBg, 0);
        _deltaPanel.Children.Add(barBg);

        _deltaText = new TextBlock
        {
            Text = "+0.00",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = TextSoft,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_deltaText, 1);
        _deltaPanel.Children.Add(_deltaText);

        Grid.SetColumn(_deltaPanel, 1);
        elecDeltaGrid.Children.Add(_deltaPanel);

        _elecDeltaHost = new Border
        {
            BorderBrush = Hairline,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = elecDeltaGrid
        };
        _rootStack.Children.Add(_elecDeltaHost);

        // 4) Bottom Strip B: Racelab 4-Corner Min Speed Strip (Your Min vs Session Fastest)
        var cornersGrid = new Grid { Margin = new Thickness(4, 3, 4, 3) };
        for (int i = 0; i < 4; i++)
        {
            cornersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var slot = CreateCornerSlot();
            _cornerSlots[i] = slot;
            Grid.SetColumn(slot.Box, i);
            cornersGrid.Children.Add(slot.Box);
        }

        _cornersHost = new Border
        {
            BorderBrush = Hairline,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = cornersGrid
        };
        _rootStack.Children.Add(_cornersHost);

        _outerBorder.Child = _rootStack;
        AttachOverlayRoot(_outerBorder, "Inputs");
    }

    private static CornerSlotVisual CreateCornerSlot()
    {
        var s = new CornerSlotVisual();
        var slashBrush = FormatHelpers.GetFrozenBrush("#64748B");

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 2, 4, 2)
        };

        // Turn number on the left + space (e.g. "T14 ")
        s.TurnLabel = new TextBlock
        {
            Text = "T-",
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Foreground = TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        };
        row.Children.Add(s.TurnLabel);

        // Single-row slash speeds: WHITE / GREEN / PURPLE
        s.PlayerSpeed = new TextBlock
        {
            Text = "--",
            FontSize = 12.5,
            FontWeight = FontWeights.Bold,
            Foreground = TextWhite,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(s.PlayerSpeed);

        s.Slash1 = new TextBlock
        {
            Text = "/",
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = slashBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(1, 0, 1, 0)
        };
        row.Children.Add(s.Slash1);

        s.PersonalBest = new TextBlock
        {
            Text = "--",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = DeltaGreen,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(s.PersonalBest);

        s.Slash2 = new TextBlock
        {
            Text = "/",
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = slashBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(1, 0, 1, 0)
        };
        row.Children.Add(s.Slash2);

        s.SessionFastest = new TextBlock
        {
            Text = "--",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = PurpleFastest,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(s.SessionFastest);

        s.Box = new Border
        {
            Background = CornerIdleBg,
            BorderBrush = FormatHelpers.GetFrozenBrush("#22FFFFFF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(1.5, 0, 1.5, 0),
            Child = row
        };

        return s;
    }

    private static Border MakeVerticalDivider() => new()
    {
        Width = 1,
        Background = Hairline,
        VerticalAlignment = VerticalAlignment.Stretch
    };

    private static TextBlock MakeElecPill(string init, string colorHex) => new()
    {
        Text = init,
        FontSize = 10.5,
        FontWeight = FontWeights.Bold,
        Foreground = FormatHelpers.GetFrozenBrush(colorHex),
        Margin = new Thickness(0, 0, 7, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    protected override void OnAppearanceUpdated()
    {
        if (_outerBorder == null) return;
        bool native = IsIRacingNative;

        _outerBorder.Background = MakeBackdropBrush(1.0);
        _outerBorder.BorderBrush = MakeBorderBrush();
        _outerBorder.CornerRadius = GetCardRadius();
        _rootStack.Effect = GetActiveShadow();

        _graph.SetBackdropOpacity(Settings.BackgroundOpacity, native);
        _graph.ShowClutch = Settings.ShowInputsClutch;
        _graph.WindowSeconds = Settings.InputsGraphSeconds;
        _graphZone.Width = Math.Clamp(Settings.InputsGraphWidth, 140.0, 460.0);

        _gearText.Foreground = native ? IRacingGold : TextWhite;

        var uiFont = GetUiFont();
        var monoFont = GetMonoFont();

        _clutchPct.FontFamily = monoFont;
        _clutchPct.FontSize = FontSz(10);
        _brakePct.FontFamily = monoFont;
        _brakePct.FontSize = FontSz(10);
        _throttlePct.FontFamily = monoFont;
        _throttlePct.FontSize = FontSz(10);

        _gearText.FontFamily = monoFont;
        _gearText.FontSize = FontSz(32);
        _speedText.FontFamily = monoFont;
        _speedText.FontSize = FontSz(17);
        _unitText.FontFamily = uiFont;
        _unitText.FontSize = FontSz(9.5);
        _rpmText.FontFamily = monoFont;
        _rpmText.FontSize = FontSz(10);
        _steerDegText.FontFamily = monoFont;
        _steerDegText.FontSize = FontSz(10.5);

        _bbText.FontFamily = monoFont;
        _bbText.FontSize = FontSz(10.5);
        _tcText.FontFamily = monoFont;
        _tcText.FontSize = FontSz(10.5);
        _absSetText.FontFamily = monoFont;
        _absSetText.FontSize = FontSz(10.5);
        _deltaText.FontFamily = monoFont;
        _deltaText.FontSize = FontSz(11);

        foreach (var slot in _cornerSlots)
        {
            slot.TurnLabel.FontFamily = monoFont;
            slot.TurnLabel.FontSize = FontSz(11);
            slot.PlayerSpeed.FontFamily = monoFont;
            slot.PlayerSpeed.FontSize = FontSz(12.5);
            slot.Slash1.FontFamily = monoFont;
            slot.Slash1.FontSize = FontSz(11);
            slot.PersonalBest.FontFamily = monoFont;
            slot.PersonalBest.FontSize = FontSz(12);
            slot.Slash2.FontFamily = monoFont;
            slot.Slash2.FontSize = FontSz(11);
            slot.SessionFastest.FontFamily = monoFont;
            slot.SessionFastest.FontSize = FontSz(12);
        }

        // 100% Modular visibility + clean divider management
        bool showGraph = Settings.ShowInputsGraph;
        bool showPedals = Settings.ShowInputsPedalBars;
        bool showGear = Settings.ShowInputsGearCluster;
        bool showWheel = Settings.ShowInputsSteering;

        _shiftRowHost.Visibility = Settings.ShowInputsShiftBar ? Visibility.Visible : Visibility.Collapsed;
        _graphZone.Visibility = showGraph ? Visibility.Visible : Visibility.Collapsed;
        _pedalsZone.Visibility = showPedals ? Visibility.Visible : Visibility.Collapsed;
        _clutchCol.Visibility = Settings.ShowInputsClutch ? Visibility.Visible : Visibility.Collapsed;
        _gearZone.Visibility = showGear ? Visibility.Visible : Visibility.Collapsed;
        _wheelZone.Visibility = showWheel ? Visibility.Visible : Visibility.Collapsed;

        // Show dividers only when something is visible on BOTH sides
        _divAfterGraph.Visibility = (showGraph && (showPedals || showGear || showWheel)) ? Visibility.Visible : Visibility.Collapsed;
        _divAfterPedals.Visibility = (showPedals && (showGear || showWheel)) ? Visibility.Visible : Visibility.Collapsed;
        _divAfterGear.Visibility = (showGear && showWheel) ? Visibility.Visible : Visibility.Collapsed;

        bool anyMainZone = showGraph || showPedals || showGear || showWheel;
        _mainRow.Visibility = anyMainZone ? Visibility.Visible : Visibility.Collapsed;

        _elecPanel.Visibility = Settings.ShowInputsElectronics ? Visibility.Visible : Visibility.Collapsed;
        _deltaPanel.Visibility = Settings.ShowInputsDeltaBar ? Visibility.Visible : Visibility.Collapsed;
        _elecDeltaHost.Visibility = (Settings.ShowInputsElectronics || Settings.ShowInputsDeltaBar)
            ? Visibility.Visible
            : Visibility.Collapsed;

        _cornersHost.Visibility = Settings.ShowInputsCornerSpeed ? Visibility.Visible : Visibility.Collapsed;
    }

    private static (StackPanel Col, Border Fill, TextBlock Label) MakePedalColumn(Brush fillBrush)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) };

        var track = new Border
        {
            Width = 12,
            Height = PedalBarHeight,
            Background = FormatHelpers.GetFrozenBrush("#1EFFFFFF"),
            BorderBrush = FormatHelpers.GetFrozenBrush("#2CFFFFFF"),
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
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextSoft,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 0),
            Width = 20,
            TextAlignment = TextAlignment.Center
        };

        stack.Children.Add(track);
        stack.Children.Add(pct);
        return (stack, fill, pct);
    }

    public void UpdateData(TelemetrySnapshot snap)
    {
        var inp = snap.Inputs;

        if (Settings.ShowInputsGraph)
        {
            _graph.PushSample(inp.Throttle, inp.Brake, inp.Clutch, inp.AbsActive);
            _absBadge.Visibility = inp.AbsActive ? Visibility.Visible : Visibility.Collapsed;
        }

        if (Settings.ShowInputsPedalBars)
        {
            _clutchFill.Height = Math.Round(inp.Clutch * (PedalBarHeight - 2));
            _brakeFill.Height = Math.Round(inp.Brake * (PedalBarHeight - 2));
            _throttleFill.Height = Math.Round(inp.Throttle * (PedalBarHeight - 2));

            _clutchPct.Text = ((int)Math.Round(inp.Clutch * 100)).ToString();
            _brakePct.Text = ((int)Math.Round(inp.Brake * 100)).ToString();
            _throttlePct.Text = ((int)Math.Round(inp.Throttle * 100)).ToString();
        }

        if (Settings.ShowInputsGearCluster)
        {
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
        }

        if (Settings.ShowInputsShiftBar)
            _shiftBar.UpdateRpm(inp.Rpm, inp.ShiftFirstRpm, inp.ShiftOptimalRpm, inp.ShiftBlinkRpm, inp.ShiftPct);

        if (Settings.ShowInputsSteering)
        {
            _wheel.UpdateAngle(inp.SteeringAngleRad);
            int steerDeg = (int)Math.Round(-inp.SteeringAngleRad * (180.0 / Math.PI));
            _steerDegText.Text = $"{steerDeg}°";
        }

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
                double frac = Math.Clamp(Math.Abs(d) / 1.0, 0.0, 1.0);
                if (d <= 0f)
                {
                    _deltaBarLeft.Width = Math.Round(frac * 36.0);
                    _deltaBarRight.Width = 0;
                    _deltaText.Text = $"{d:0.00}";
                    _deltaText.Foreground = DeltaGreen;
                }
                else
                {
                    _deltaBarLeft.Width = 0;
                    _deltaBarRight.Width = Math.Round(frac * 36.0);
                    _deltaText.Text = $"+{d:0.00}";
                    _deltaText.Foreground = DeltaRed;
                }
            }
        }

        if (Settings.ShowInputsCornerSpeed)
            UpdateCornerStrip(inp);
    }

    private void UpdateCornerStrip(InputTelemetry inp)
    {
        float conv = Settings.UseImperialUnits ? 2.236936f : 3.6f;
        var list = inp.RecentCorners;
        bool native = IsIRacingNative;

        // Slot 0 = most recent corner you just drove (brightest), slots 1..3 fade softly
        double[] slotOpacities = { 1.0, 0.84, 0.68, 0.54 };

        for (int i = 0; i < 4; i++)
        {
            var slot = _cornerSlots[i];
            slot.Box.Opacity = slotOpacities[i];

            if (i >= list.Count)
            {
                slot.Box.Background = CornerIdleBg;
                slot.Box.BorderBrush = FormatHelpers.GetFrozenBrush("#18FFFFFF");
                slot.TurnLabel.Text = "T-";
                slot.TurnLabel.Foreground = TextMuted;
                slot.PlayerSpeed.Text = "--";
                slot.PlayerSpeed.Foreground = TextMuted;
                slot.PersonalBest.Text = "--";
                slot.SessionFastest.Text = "--";
                continue;
            }

            var rec = list[i];
            int curSpd = (int)Math.Round(rec.PlayerMinMs * conv);
            int pbSpd = (int)Math.Round(rec.PlayerBestMinMs * conv);
            int fastSpd = (int)Math.Round(rec.SessionFastestMinMs * conv);

            bool isLatest = i == 0;
            Brush accentColor = rec.IsLiveCorner ? ClutchBrush : (native ? IRacingGold : TextWhite);

            slot.Box.Background = rec.IsLiveCorner
                ? CornerLiveBg
                : (isLatest ? FormatHelpers.GetFrozenBrush("#1CFFFFFF") : CornerIdleBg);
            slot.Box.BorderBrush = isLatest
                ? accentColor
                : FormatHelpers.GetFrozenBrush("#22FFFFFF");

            slot.TurnLabel.Text = string.IsNullOrEmpty(rec.Label) ? "T-" : rec.Label;
            slot.TurnLabel.Foreground = isLatest ? accentColor : TextMuted;

            // Single row: Turn + space + WHITE (current/last) / GREEN (my PB) / PURPLE (fastest-lap holder)
            slot.PlayerSpeed.Text = curSpd.ToString();
            slot.PlayerSpeed.Foreground = TextWhite;

            slot.PersonalBest.Text = rec.HasBestRef && pbSpd > 10 ? pbSpd.ToString() : "--";
            slot.SessionFastest.Text = rec.HasFastestRef && fastSpd > 10 ? fastSpd.ToString() : "--";
        }
    }
}
