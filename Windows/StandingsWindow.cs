using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ApexHUD.Controls;
using ApexHUD.Core.Settings;
using ApexHUD.Core.Telemetry;

namespace ApexHUD.Windows;

/// <summary>
/// Dynamic auto-width Multiclass Standings overlay (SharedSizeGroup hugs actual longest driver name + active columns),
/// large bold high-contrast typography scaled by HudFontScale, and full modularity.
/// </summary>
public sealed class StandingsWindow : OverlayWindowBase
{
    private readonly Border _outerBorder;
    private readonly StackPanel _rootStack;
    private readonly Border _headerHost;
    private readonly Grid _headerGrid;
    private readonly Grid _legendGrid;
    private readonly Border _legendDivider;
    private readonly TextBlock _nativeTitleText;
    private readonly TextBlock _trackText;
    private readonly TextBlock _sessionTypeText;
    private readonly TextBlock _lapsText;
    private readonly TextBlock _timerText;
    private readonly StackPanel _bodyPanel;

    private FontFamily _uiFont = new("Segoe UI Variable Text, Segoe UI");
    private FontFamily _monoFont = new("Cascadia Mono, Consolas");

    private static readonly SolidColorBrush BgPlayerCarbon = FormatHelpers.GetFrozenBrush("#4D2563EB");
    private static readonly SolidColorBrush BgPlayerNative = FormatHelpers.GetFrozenBrush("#22F2B830");
    private static readonly SolidColorBrush BgRowAltCarbon = FormatHelpers.GetFrozenBrush("#12FFFFFF");
    private static readonly SolidColorBrush NativeRowDivider = FormatHelpers.GetFrozenBrush("#FF2C303A");
    private static readonly SolidColorBrush Hairline = FormatHelpers.GetFrozenBrush("#22FFFFFF");
    private static readonly SolidColorBrush TextWhite = FormatHelpers.GetFrozenBrush("#F3F4F6");
    private static readonly SolidColorBrush TextSoft = FormatHelpers.GetFrozenBrush("#DCE2EC");
    private static readonly SolidColorBrush TextMuted = FormatHelpers.GetFrozenBrush("#9CA3AF");
    private static readonly SolidColorBrush NativeMuted = FormatHelpers.GetFrozenBrush("#8E95A3");
    private static readonly SolidColorBrush DarkText = FormatHelpers.GetFrozenBrush("#111318");
    private static readonly SolidColorBrush IRacingGold = FormatHelpers.GetFrozenBrush("#F2B830");
    private static readonly SolidColorBrush IRacingGoldBright = FormatHelpers.GetFrozenBrush("#F5C84C");
    private static readonly SolidColorBrush AccentSky = FormatHelpers.GetFrozenBrush("#7DD3FC");
    private static readonly SolidColorBrush GainGreen = FormatHelpers.GetFrozenBrush("#4ADE80");
    private static readonly SolidColorBrush LossRed = FormatHelpers.GetFrozenBrush("#FF5A6E");
    private static readonly SolidColorBrush PitAmber = FormatHelpers.GetFrozenBrush("#FCD34D");
    private static readonly SolidColorBrush PurpleBest = FormatHelpers.GetFrozenBrush("#E879F9");

    private int _renderTick;
    private bool _lastIsRaceSession = true;

    public StandingsWindow(AppSettings settings) : base("Standings", settings)
    {
        _outerBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            SnapsToDevicePixels = true
        };

        _rootStack = new StackPanel { Effect = GetActiveShadow() };
        Grid.SetIsSharedSizeScope(_rootStack, true);

        _headerGrid = new Grid { Margin = new Thickness(3, 1, 3, 1) };
        _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var leftTrack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(1, 0, 6, 0)
        };
        _nativeTitleText = new TextBlock
        {
            Text = "STANDINGS",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = IRacingGold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        };
        leftTrack.Children.Add(_nativeTitleText);
        leftTrack.Children.Add(new HudMetricIcon(HudIconKind.Flag, "#7DD3FC", 11.5) { Margin = new Thickness(0, 0, 3, 0) });
        _trackText = new TextBlock
        {
            Text = "Standings",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = TextWhite,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 185,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        leftTrack.Children.Add(_trackText);
        Grid.SetColumn(leftTrack, 0);
        _headerGrid.Children.Add(leftTrack);

        var rightMeta = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 1, 0)
        };
        _sessionTypeText = new TextBlock
        {
            Text = "RACE",
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Foreground = AccentSky,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        rightMeta.Children.Add(_sessionTypeText);

        rightMeta.Children.Add(new HudMetricIcon(HudIconKind.LapLoop, "#CBD5E1", 11.5) { Margin = new Thickness(0, 0, 2, 0) });
        _lapsText = new TextBlock
        {
            Text = "0/0",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = TextSoft,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        rightMeta.Children.Add(_lapsText);

        rightMeta.Children.Add(new HudMetricIcon(HudIconKind.Stopwatch, "#CBD5E1", 11.5) { Margin = new Thickness(0, 0, 2, 0) });
        _timerText = new TextBlock
        {
            Text = "00:00",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = TextSoft,
            VerticalAlignment = VerticalAlignment.Center
        };
        rightMeta.Children.Add(_timerText);

        Grid.SetColumn(rightMeta, 1);
        _headerGrid.Children.Add(rightMeta);

        _headerHost = new Border { Child = _headerGrid };
        _rootStack.Children.Add(_headerHost);

        _legendGrid = new Grid { Height = 15 };
        _rootStack.Children.Add(_legendGrid);
        _legendDivider = new Border { Height = 1, Background = Hairline };
        _rootStack.Children.Add(_legendDivider);

        _bodyPanel = new StackPanel { Margin = new Thickness(0) };
        _rootStack.Children.Add(_bodyPanel);

        _outerBorder.Child = _rootStack;
        AttachOverlayRoot(_outerBorder, "Standings");
    }

    protected override void OnAppearanceUpdated()
    {
        if (_outerBorder == null) return;
        bool native = IsIRacingNative;
        _uiFont = GetUiFont();
        _monoFont = GetMonoFont();

        _outerBorder.Background = MakeBackdropBrush(1.0);
        _outerBorder.BorderBrush = MakeBorderBrush();
        _outerBorder.CornerRadius = GetCardRadius();
        _rootStack.Effect = GetActiveShadow();

        _headerGrid.Background = native ? Brushes.Transparent : MakeBackdropBrush(1.18, 0x0F, 0x13, 0x1C);
        _legendGrid.Background = native ? Brushes.Transparent : MakeBackdropBrush(0.78, 0x0A, 0x0D, 0x13);
        _legendDivider.Opacity = native ? 0.35 : 1.0;
        _headerHost.Visibility = Settings.ShowStandingsHeader ? Visibility.Visible : Visibility.Collapsed;

        _nativeTitleText.Visibility = native ? Visibility.Visible : Visibility.Collapsed;
        _nativeTitleText.FontFamily = _uiFont;
        _nativeTitleText.FontSize = FontSz(12);
        _trackText.FontFamily = _uiFont;
        _trackText.FontSize = FontSz(12);
        _trackText.Foreground = native ? NativeMuted : TextWhite;
        _sessionTypeText.FontFamily = _uiFont;
        _sessionTypeText.FontSize = FontSz(11.5);
        _sessionTypeText.Foreground = native ? IRacingGold : AccentSky;
        _lapsText.FontFamily = _monoFont;
        _lapsText.FontSize = FontSz(12);
        _timerText.FontFamily = _monoFont;
        _timerText.FontSize = FontSz(12);

        RebuildLegend();
        _renderTick = 0;
    }

    private void RebuildLegend()
    {
        _legendGrid.Children.Clear();
        _legendGrid.ColumnDefinitions.Clear();
        ConfigureRowColumns(_legendGrid);

        AddColHeader(_legendGrid, 1, "P", HorizontalAlignment.Right);
        if (Settings.ShowStandingsPosDelta && _lastIsRaceSession) AddColHeader(_legendGrid, 2, "±", HorizontalAlignment.Center);
        if (Settings.ShowStandingsCarNumber) AddColHeader(_legendGrid, 3, "#", HorizontalAlignment.Center);
        AddColHeader(_legendGrid, 5, "DRIVER", HorizontalAlignment.Left);
        if (Settings.ShowStandingsTireCompound) AddIconColHeader(_legendGrid, 6, HudIconKind.TireWheel, "", HorizontalAlignment.Center);
        if (Settings.ShowStandingsIRating) AddIconColHeader(_legendGrid, 7, HudIconKind.RatingBar, "iR", HorizontalAlignment.Right);
        if (Settings.ShowStandingsDeltaIR && _lastIsRaceSession) AddColHeader(_legendGrid, 8, "±iR", HorizontalAlignment.Right);
        AddIconColHeader(_legendGrid, 9, HudIconKind.Stopwatch, Settings.ShowStandingsInterval ? "INT" : "GAP", HorizontalAlignment.Right);
        if (Settings.ShowStandingsLastLap) AddColHeader(_legendGrid, 10, _lastIsRaceSession ? "LAST" : "BEST", HorizontalAlignment.Right);
        if (Settings.ShowStandingsPits) AddIconColHeader(_legendGrid, 11, HudIconKind.Wrench, "PIT", HorizontalAlignment.Center);
    }

    private void ConfigureRowColumns(Grid g)
    {
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "StdClassNode" });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "StdPos" });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "StdDelta" });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "StdNum" });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "StdBrand" });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "StdName" });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "StdTire" });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "StdIR" });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "StdIRDelta" });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "StdGap" });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "StdLast" });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "StdPit" });
    }

    private void AddColHeader(Grid g, int col, string text, HorizontalAlignment align)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontFamily = _uiFont,
            FontSize = FontSz(9),
            FontWeight = FontWeights.Bold,
            Foreground = TextMuted,
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 0)
        };
        Grid.SetColumn(tb, col);
        g.Children.Add(tb);
    }

    private void AddIconColHeader(Grid g, int col, HudIconKind icon, string text, HorizontalAlignment align)
    {
        var sp = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 0)
        };
        sp.Children.Add(new HudMetricIcon(icon, "#CBD5E1", 9) { Margin = new Thickness(0, 0, string.IsNullOrEmpty(text) ? 0 : 1.5, 0) });
        if (!string.IsNullOrEmpty(text))
        {
            sp.Children.Add(new TextBlock
            {
                Text = text,
                FontFamily = _uiFont,
                FontSize = FontSz(9),
                FontWeight = FontWeights.Bold,
                Foreground = TextMuted,
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        Grid.SetColumn(sp, col);
        g.Children.Add(sp);
    }

    public void UpdateData(TelemetrySnapshot snap)
    {
        _renderTick++;
        if (_renderTick % 4 != 1 && _bodyPanel.Children.Count > 0) return;

        if (snap.Session.IsRaceSession != _lastIsRaceSession)
        {
            _lastIsRaceSession = snap.Session.IsRaceSession;
            RebuildLegend();
        }

        _trackText.Text = snap.Session.TrackName;
        _sessionTypeText.Text = snap.Session.SessionType;
        _lapsText.Text = snap.Session.TotalLaps > 0
            ? $"{snap.Session.CurrentLap}/{snap.Session.TotalLaps}"
            : $"{snap.Session.CurrentLap}";
        _timerText.Text = FormatHelpers.FormatTimeRemaining(snap.Session.TimeRemainingSeconds);

        _bodyPanel.Children.Clear();

        if (snap.Standings.Count == 0)
        {
            _bodyPanel.Children.Add(new TextBlock
            {
                Text = snap.IsConnected ? "Waiting for grid..." : "Waiting for iRacing session...",
                FontFamily = _uiFont,
                FontSize = FontSz(12.5),
                Foreground = TextMuted,
                Margin = new Thickness(12, 10, 12, 10)
            });
            return;
        }

        int shownClasses = Math.Min(2, snap.Standings.Count);
        for (int c = 0; c < shownClasses; c++)
        {
            var group = snap.Standings[c];
            _bodyPanel.Children.Add(BuildClassStrip(group));

            int maxForClass = c == 0
                ? Math.Min(Settings.StandingsMaxRows, group.Drivers.Count)
                : Math.Min(3, group.Drivers.Count);

            for (int i = 0; i < maxForClass; i++)
            {
                bool isVeryLast = (c == shownClasses - 1) && (i == maxForClass - 1);
                _bodyPanel.Children.Add(BuildDriverRow(group.Drivers[i], i % 2 == 1, snap.Session.IsRaceSession, isVeryLast));
            }
        }
    }

    private UIElement BuildClassStrip(StandingsClassGroup group)
    {
        bool native = IsIRacingNative;
        var strip = new Grid
        {
            Height = 17,
            Background = native ? FormatHelpers.GetFrozenBrush("#10FFFFFF") : MakeBackdropBrush(1.08, 0x13, 0x18, 0x24)
        };
        strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        strip.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        strip.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var colorBrush = FormatHelpers.GetFrozenBrush(group.ClassColorHex);
        var bar = new Border { Background = colorBrush };
        Grid.SetColumn(bar, 0);
        strip.Children.Add(bar);

        var name = new TextBlock
        {
            Text = group.ClassName,
            FontFamily = _uiFont,
            FontSize = FontSz(10.5),
            FontWeight = FontWeights.Bold,
            Foreground = colorBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5, 0, 6, 0)
        };
        Grid.SetColumn(name, 1);
        strip.Children.Add(name);

        var meta = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        };
        meta.Children.Add(new HudMetricIcon(HudIconKind.Trophy, "#FCD34D", 9.5) { Margin = new Thickness(0, 0, 2, 0) });
        meta.Children.Add(new TextBlock
        {
            Text = FormatHelpers.FormatIRating(group.Sof),
            FontFamily = _monoFont,
            FontSize = FontSz(10),
            FontWeight = FontWeights.Bold,
            Foreground = TextSoft,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        });
        meta.Children.Add(new HudMetricIcon(HudIconKind.CarsGrid, "#CBD5E1", 9.5) { Margin = new Thickness(0, 0, 2, 0) });
        meta.Children.Add(new TextBlock
        {
            Text = group.TotalDrivers.ToString(),
            FontFamily = _monoFont,
            FontSize = FontSz(10),
            FontWeight = FontWeights.SemiBold,
            Foreground = TextSoft,
            VerticalAlignment = VerticalAlignment.Center
        });

        Grid.SetColumn(meta, 3);
        strip.Children.Add(meta);

        return new Border
        {
            BorderBrush = Hairline,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = strip
        };
    }

    private UIElement BuildDriverRow(StandingsEntry d, bool alt, bool isRaceSession, bool isVeryLast)
    {
        bool native = IsIRacingNative;
        double rowH = Math.Round((native ? 20 : 19) * Math.Max(1.0, Settings.HudFontScale * 0.92));
        var g = new Grid { Height = rowH };
        ConfigureRowColumns(g);

        // Class indicator: in iRacing Native, crisp slim vertical line + node dot
        var classBrush = FormatHelpers.GetFrozenBrush(d.ClassColorHex);
        if (native)
        {
            var nodeGrid = new Grid { Width = 8, Margin = new Thickness(2, 0, 1, 0) };
            nodeGrid.Children.Add(new Border
            {
                Width = 1.5,
                Background = classBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                Opacity = 0.72
            });
            nodeGrid.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 5,
                Height = 5,
                Fill = classBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(nodeGrid, 0);
            g.Children.Add(nodeGrid);
        }
        else
        {
            var accent = new Border { Width = 3, Background = classBrush, Margin = new Thickness(0, 1, 1, 1), CornerRadius = new CornerRadius(1) };
            Grid.SetColumn(accent, 0);
            g.Children.Add(accent);
        }

        // Position: clean "P1", "P19" in iRacing Native (gold for player), crisp number in Carbon
        string posStr = native ? $"P{d.ClassPosition}" : d.ClassPosition.ToString();
        AddCell(g, 1, posStr, _monoFont, FontSz(11.5),
            d.IsPlayer ? (native ? IRacingGoldBright : AccentSky) : (native ? TextSoft : TextWhite),
            d.IsPlayer || !native ? FontWeights.Bold : FontWeights.Normal, HorizontalAlignment.Right,
            margin: new Thickness(1, 0, 2, 0));

        if (Settings.ShowStandingsPosDelta && isRaceSession)
        {
            string pd = d.PosDelta > 0 ? $"▲{d.PosDelta}" : d.PosDelta < 0 ? $"▼{Math.Abs(d.PosDelta)}" : "–";
            Brush pdBrush = d.PosDelta > 0 ? GainGreen : d.PosDelta < 0 ? LossRed : NativeMuted;
            AddCell(g, 2, pd, _monoFont, FontSz(10), pdBrush, FontWeights.SemiBold, HorizontalAlignment.Center,
                margin: new Thickness(1, 0, 2, 0));
        }

        if (Settings.ShowStandingsCarNumber)
        {
            var numBox = new Border
            {
                Background = native ? Brushes.Transparent : FormatHelpers.GetFrozenBrush("#24FFFFFF"),
                CornerRadius = new CornerRadius(2),
                Padding = native ? new Thickness(1, 0, 1, 0) : new Thickness(3, 0, 3, 0),
                Margin = new Thickness(1, 1, 2, 1),
                Child = new TextBlock
                {
                    Text = string.IsNullOrEmpty(d.CarNumber) ? "--" : (native ? $"#{d.CarNumber}" : d.CarNumber),
                    FontFamily = _monoFont,
                    FontSize = FontSz(11),
                    FontWeight = native ? FontWeights.SemiBold : FontWeights.Bold,
                    Foreground = native ? IRacingGold : TextSoft,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(numBox, 3);
            g.Children.Add(numBox);
        }

        if (Settings.ShowCarBrandIcons)
        {
            var icon = new CarBrandIcon { Brand = d.Brand, Width = 16, Height = 14, Margin = new Thickness(0, 0, 3, 0) };
            Grid.SetColumn(icon, 4);
            g.Children.Add(icon);
        }

        var nameTb = new TextBlock
        {
            Text = d.DriverName,
            FontFamily = _uiFont,
            FontSize = FontSz(12),
            FontWeight = d.IsPlayer ? FontWeights.SemiBold : (native ? FontWeights.Normal : FontWeights.SemiBold),
            Foreground = d.IsPlayer
                ? (native ? IRacingGoldBright : TextWhite)
                : (d.IsOnPitRoad ? NativeMuted : TextWhite),
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 190,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 5, 0)
        };
        Grid.SetColumn(nameTb, 5);
        g.Children.Add(nameTb);

        if (Settings.ShowStandingsTireCompound)
        {
            var tireColor = FormatHelpers.GetFrozenBrush(d.TireColorHex, "#CBD5E1");
            var tirePill = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(7),
                BorderBrush = tireColor,
                BorderThickness = new Thickness(1.4),
                Margin = new Thickness(1, 0, 2, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = d.TireCode,
                    FontFamily = _monoFont,
                    FontSize = FontSz(8.5),
                    FontWeight = FontWeights.Bold,
                    Foreground = tireColor,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(tirePill, 6);
            g.Children.Add(tirePill);
        }

        if (Settings.ShowStandingsIRating)
        {
            AddCell(g, 7, FormatHelpers.FormatIRating(d.IRating), _monoFont, FontSz(11.5),
                TextSoft, FontWeights.SemiBold, HorizontalAlignment.Right,
                margin: new Thickness(2, 0, 2, 0));
        }

        if (Settings.ShowStandingsDeltaIR && isRaceSession)
        {
            string deltaStr = d.ProjectedIRDelta > 0 ? $"+{d.ProjectedIRDelta}" : d.ProjectedIRDelta.ToString();
            Brush deltaBrush = d.ProjectedIRDelta > 0 ? GainGreen : d.ProjectedIRDelta < 0 ? LossRed : TextMuted;
            AddCell(g, 8, deltaStr, _monoFont, FontSz(11.5), deltaBrush, FontWeights.Bold, HorizontalAlignment.Right,
                margin: new Thickness(2, 0, 2, 0));
        }

        float gapVal = Settings.ShowStandingsInterval ? d.IntervalSeconds : d.GapToLeaderSeconds;
        string gapStr = d.ClassPosition == 1
            ? (isRaceSession ? "LDR" : "P1")
            : !isRaceSession && d.BestLapSeconds <= 10f
                ? "--"
                : !isRaceSession
                    ? FormatHelpers.FormatQualiGap(gapVal)
                    : d.LapsDown > 0 && !Settings.ShowStandingsInterval
                        ? $"+{d.LapsDown}L"
                        : FormatHelpers.FormatGap(gapVal);
        AddCell(g, 9, gapStr, _monoFont, FontSz(12),
            d.IsPlayer && native ? IRacingGoldBright : (d.ClassPosition == 1 ? (native ? IRacingGold : AccentSky) : TextWhite),
            FontWeights.Bold, HorizontalAlignment.Right,
            margin: new Thickness(2, 0, 3, 0));

        if (Settings.ShowStandingsLastLap)
        {
            var lapPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 3, 0)
            };
            if (d.IsClassBestLap)
                lapPanel.Children.Add(new HudMetricIcon(HudIconKind.FastestBolt, "#E879F9", 9.5) { Margin = new Thickness(0, 0, 1.5, 0) });

            float shownLap = isRaceSession ? d.LastLapSeconds : d.BestLapSeconds;
            lapPanel.Children.Add(new TextBlock
            {
                Text = FormatHelpers.FormatLapTime(shownLap),
                FontFamily = _monoFont,
                FontSize = FontSz(11.5),
                Foreground = d.IsClassBestLap ? PurpleBest : TextSoft,
                FontWeight = d.IsClassBestLap ? FontWeights.Bold : FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(lapPanel, 10);
            g.Children.Add(lapPanel);
        }

        if (Settings.ShowStandingsPits)
        {
            var pitPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(1, 0, 4, 0)
            };
            if (d.IsOnPitRoad)
            {
                pitPanel.Children.Add(new HudMetricIcon(HudIconKind.Wrench, "#FCD34D", 9) { Margin = new Thickness(0, 0, 1.5, 0) });
                pitPanel.Children.Add(new TextBlock { Text = "PIT", FontFamily = _monoFont, FontSize = FontSz(9.5), FontWeight = FontWeights.Bold, Foreground = PitAmber });
            }
            else
            {
                string pitStr = d.PitStops > 0 ? $"P{d.PitStops}·{d.StintLaps}" : (d.StintLaps > 0 ? $"L{d.StintLaps}" : "-");
                pitPanel.Children.Add(new TextBlock { Text = pitStr, FontFamily = _monoFont, FontSize = FontSz(10), FontWeight = FontWeights.SemiBold, Foreground = TextMuted });
            }
            Grid.SetColumn(pitPanel, 11);
            g.Children.Add(pitPanel);
        }

        return new Border
        {
            Background = d.IsPlayer
                ? (native ? BgPlayerNative : BgPlayerCarbon)
                : (native ? Brushes.Transparent : (alt ? BgRowAltCarbon : Brushes.Transparent)),
            BorderBrush = native && !isVeryLast ? NativeRowDivider : Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, native && !isVeryLast ? 1 : 0),
            Child = g
        };
    }

    private static void AddCell(
        Grid g,
        int col,
        string text,
        FontFamily font,
        double size,
        Brush fg,
        FontWeight weight,
        HorizontalAlignment align,
        double minW = 0,
        Thickness? margin = null)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontFamily = font,
            FontSize = size,
            Foreground = fg,
            FontWeight = weight,
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = minW,
            Margin = margin ?? new Thickness(2, 0, 2, 0)
        };
        Grid.SetColumn(tb, col);
        g.Children.Add(tb);
    }
}
