using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ApexHUD.Controls;
using ApexHUD.Core.Settings;
using ApexHUD.Core.Telemetry;

namespace ApexHUD.Windows;

/// <summary>
/// Racelab-grade Multiclass Standings overlay:
/// Position gain/loss, brand icons, tire compound badge, SOF + +/-iR, Gap or Interval mode,
/// purple class best lap highlight, stint/pit info, and dynamic fonts.
/// </summary>
public sealed class StandingsWindow : OverlayWindowBase
{
    private readonly Border _outerBorder;
    private readonly Grid _headerGrid;
    private readonly Grid _legendGrid;
    private readonly TextBlock _trackText;
    private readonly TextBlock _sessionMetaText;
    private readonly StackPanel _bodyPanel;

    private FontFamily _uiFont = new("Segoe UI Variable Text, Segoe UI");
    private FontFamily _monoFont = new("Cascadia Mono, Consolas");

    private static readonly SolidColorBrush BgPlayer = FormatHelpers.GetFrozenBrush("#383B82F6");
    private static readonly SolidColorBrush BgRowAlt = FormatHelpers.GetFrozenBrush("#0AFFFFFF");
    private static readonly SolidColorBrush Hairline = FormatHelpers.GetFrozenBrush("#16FFFFFF");
    private static readonly SolidColorBrush TextWhite = FormatHelpers.GetFrozenBrush("#F8FAFC");
    private static readonly SolidColorBrush TextSoft = FormatHelpers.GetFrozenBrush("#CBD5E1");
    private static readonly SolidColorBrush TextMuted = FormatHelpers.GetFrozenBrush("#94A3B8");
    private static readonly SolidColorBrush AccentSky = FormatHelpers.GetFrozenBrush("#60A5FA");
    private static readonly SolidColorBrush GainGreen = FormatHelpers.GetFrozenBrush("#22C55E");
    private static readonly SolidColorBrush LossRed = FormatHelpers.GetFrozenBrush("#F87171");
    private static readonly SolidColorBrush PitAmber = FormatHelpers.GetFrozenBrush("#FBBF24");
    private static readonly SolidColorBrush PurpleBest = FormatHelpers.GetFrozenBrush("#C084FC");

    private int _renderTick;

    public StandingsWindow(AppSettings settings) : base("Standings", settings)
    {
        _outerBorder = new Border
        {
            Width = 562,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            SnapsToDevicePixels = true
        };

        var stack = new StackPanel();

        _headerGrid = new Grid { Height = 26 };
        _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _trackText = new TextBlock
        {
            Text = "Standings",
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextWhite,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(10, 0, 8, 0)
        };
        Grid.SetColumn(_trackText, 0);
        _headerGrid.Children.Add(_trackText);

        _sessionMetaText = new TextBlock
        {
            Text = "WAIT  •  L 0/0  •  00:00",
            FontSize = 10.5,
            Foreground = TextSoft,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(_sessionMetaText, 1);
        _headerGrid.Children.Add(_sessionMetaText);

        stack.Children.Add(_headerGrid);

        _legendGrid = new Grid { Height = 18 };
        stack.Children.Add(_legendGrid);
        stack.Children.Add(new Border { Height = 1, Background = Hairline });

        _bodyPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
        stack.Children.Add(_bodyPanel);

        _outerBorder.Child = stack;
        AttachOverlayRoot(_outerBorder, "Standings");
    }

    protected override void OnAppearanceUpdated()
    {
        if (_outerBorder == null) return;
        _uiFont = GetUiFont();
        _monoFont = GetMonoFont();

        _outerBorder.Background = MakeBackdropBrush(1.0);
        _outerBorder.BorderBrush = MakeBorderBrush();
        _headerGrid.Background = MakeBackdropBrush(1.18, 0x0F, 0x13, 0x1C);
        _legendGrid.Background = MakeBackdropBrush(0.75, 0x0A, 0x0D, 0x13);

        _trackText.FontFamily = _uiFont;
        _sessionMetaText.FontFamily = _monoFont;

        RebuildLegend();
        _renderTick = 0;
    }

    private void RebuildLegend()
    {
        _legendGrid.Children.Clear();
        _legendGrid.ColumnDefinitions.Clear();
        ConfigureRowColumns(_legendGrid);

        AddColHeader(_legendGrid, 1, "P", HorizontalAlignment.Right);
        if (Settings.ShowStandingsPosDelta) AddColHeader(_legendGrid, 2, "±", HorizontalAlignment.Center);
        AddColHeader(_legendGrid, 3, "#", HorizontalAlignment.Center);
        AddColHeader(_legendGrid, 5, "DRIVER", HorizontalAlignment.Left);
        if (Settings.ShowStandingsTireCompound) AddColHeader(_legendGrid, 6, "T", HorizontalAlignment.Center);
        AddColHeader(_legendGrid, 7, "iR", HorizontalAlignment.Right);
        if (Settings.ShowStandingsDeltaIR) AddColHeader(_legendGrid, 8, "±iR", HorizontalAlignment.Right);
        AddColHeader(_legendGrid, 9, Settings.ShowStandingsInterval ? "INT" : "GAP", HorizontalAlignment.Right);
        if (Settings.ShowStandingsLastLap) AddColHeader(_legendGrid, 10, "LAST", HorizontalAlignment.Right);
        if (Settings.ShowStandingsPits) AddColHeader(_legendGrid, 11, "PIT", HorizontalAlignment.Center);
    }

    private void ConfigureRowColumns(Grid g)
    {
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });                                          // 0: class strip
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });                                         // 1: pos
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Settings.ShowStandingsPosDelta ? 26 : 0) });    // 2: pos delta
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });                                         // 3: car#
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Settings.ShowCarBrandIcons ? 22 : 0) });        // 4: brand icon
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });                       // 5: driver
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Settings.ShowStandingsTireCompound ? 22 : 0) });// 6: tire
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });                                         // 7: iR
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Settings.ShowStandingsDeltaIR ? 40 : 0) });     // 8: +/-iR
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });                                         // 9: gap/int
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Settings.ShowStandingsLastLap ? 62 : 0) });     // 10: last lap
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Settings.ShowStandingsPits ? 38 : 0) });        // 11: pit/stint
    }

    private void AddColHeader(Grid g, int col, string text, HorizontalAlignment align)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontFamily = _uiFont,
            FontSize = 9.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextMuted,
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 3, 0)
        };
        Grid.SetColumn(tb, col);
        g.Children.Add(tb);
    }

    public void UpdateData(TelemetrySnapshot snap)
    {
        _renderTick++;
        if (_renderTick % 4 != 1 && _bodyPanel.Children.Count > 0) return;

        string lapsStr = snap.Session.TotalLaps > 0
            ? $"Lap {snap.Session.CurrentLap}/{snap.Session.TotalLaps}"
            : $"Lap {snap.Session.CurrentLap}";
        string timeStr = FormatHelpers.FormatTimeRemaining(snap.Session.TimeRemainingSeconds);

        _trackText.Text = snap.Session.TrackName;
        _sessionMetaText.Text = $"{snap.Session.SessionType}  •  {lapsStr}  •  {timeStr}";

        _bodyPanel.Children.Clear();

        if (snap.Standings.Count == 0)
        {
            _bodyPanel.Children.Add(new TextBlock
            {
                Text = snap.IsConnected ? "Waiting for grid..." : "Waiting for iRacing session...",
                FontFamily = _uiFont,
                FontSize = 11.5,
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
                _bodyPanel.Children.Add(BuildDriverRow(group.Drivers[i], i % 2 == 1));
        }
    }

    private UIElement BuildClassStrip(StandingsClassGroup group)
    {
        var strip = new Grid { Height = 21, Background = MakeBackdropBrush(1.05, 0x13, 0x18, 0x24) };
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
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Foreground = colorBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0)
        };
        Grid.SetColumn(name, 1);
        strip.Children.Add(name);

        var meta = new TextBlock
        {
            Text = $"SOF {FormatHelpers.FormatIRating(group.Sof)}  •  {group.TotalDrivers} cars",
            FontFamily = _monoFont,
            FontSize = 10,
            Foreground = TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 9, 0)
        };
        Grid.SetColumn(meta, 3);
        strip.Children.Add(meta);

        return new Border
        {
            BorderBrush = Hairline,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = strip
        };
    }

    private UIElement BuildDriverRow(StandingsEntry d, bool alt)
    {
        var g = new Grid { Height = 23 };
        ConfigureRowColumns(g);

        var accent = new Border { Background = FormatHelpers.GetFrozenBrush(d.ClassColorHex), Margin = new Thickness(0, 2, 0, 2), CornerRadius = new CornerRadius(1) };
        Grid.SetColumn(accent, 0);
        g.Children.Add(accent);

        AddCell(g, 1, d.ClassPosition.ToString(), _monoFont, 12,
            d.IsPlayer ? AccentSky : TextWhite, FontWeights.SemiBold, HorizontalAlignment.Right);

        if (Settings.ShowStandingsPosDelta)
        {
            string pd = d.PosDelta > 0 ? $"▲{d.PosDelta}" : d.PosDelta < 0 ? $"▼{Math.Abs(d.PosDelta)}" : "–";
            Brush pdBrush = d.PosDelta > 0 ? GainGreen : d.PosDelta < 0 ? LossRed : TextMuted;
            AddCell(g, 2, pd, _monoFont, 9.5, pdBrush, FontWeights.SemiBold, HorizontalAlignment.Center);
        }

        var numBox = new Border
        {
            Background = FormatHelpers.GetFrozenBrush("#1AFFFFFF"),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(2, 3, 2, 3),
            Child = new TextBlock
            {
                Text = string.IsNullOrEmpty(d.CarNumber) ? "--" : d.CarNumber,
                FontFamily = _monoFont,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextSoft,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(numBox, 3);
        g.Children.Add(numBox);

        if (Settings.ShowCarBrandIcons)
        {
            var icon = new CarBrandIcon { Brand = d.Brand, Margin = new Thickness(1, 0, 2, 0) };
            Grid.SetColumn(icon, 4);
            g.Children.Add(icon);
        }

        var nameTb = new TextBlock
        {
            Text = d.DriverName,
            FontFamily = _uiFont,
            FontSize = 12,
            FontWeight = d.IsPlayer ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = d.IsPlayer ? TextWhite : (d.IsOnPitRoad ? TextMuted : TextWhite),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 0, 4, 0)
        };
        Grid.SetColumn(nameTb, 5);
        g.Children.Add(nameTb);

        if (Settings.ShowStandingsTireCompound)
        {
            var tireColor = FormatHelpers.GetFrozenBrush(d.TireColorHex, "#94A3B8");
            var tirePill = new Border
            {
                Width = 15,
                Height = 15,
                CornerRadius = new CornerRadius(7.5),
                BorderBrush = tireColor,
                BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = d.TireCode,
                    FontFamily = _monoFont,
                    FontSize = 8.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = tireColor,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(tirePill, 6);
            g.Children.Add(tirePill);
        }

        AddCell(g, 7, FormatHelpers.FormatIRating(d.IRating), _monoFont, 11,
            TextMuted, FontWeights.Normal, HorizontalAlignment.Right);

        if (Settings.ShowStandingsDeltaIR)
        {
            string deltaStr = d.ProjectedIRDelta > 0 ? $"+{d.ProjectedIRDelta}" : d.ProjectedIRDelta.ToString();
            Brush deltaBrush = d.ProjectedIRDelta > 0 ? GainGreen : d.ProjectedIRDelta < 0 ? LossRed : TextMuted;
            AddCell(g, 8, deltaStr, _monoFont, 11, deltaBrush, FontWeights.SemiBold, HorizontalAlignment.Right);
        }

        float gapVal = Settings.ShowStandingsInterval ? d.IntervalSeconds : d.GapToLeaderSeconds;
        string gapStr = d.ClassPosition == 1
            ? "Leader"
            : d.LapsDown > 0 && !Settings.ShowStandingsInterval
                ? $"+{d.LapsDown}L"
                : FormatHelpers.FormatGap(gapVal);
        AddCell(g, 9, gapStr, _monoFont, 11.5,
            d.ClassPosition == 1 ? AccentSky : TextWhite, FontWeights.SemiBold, HorizontalAlignment.Right);

        if (Settings.ShowStandingsLastLap)
        {
            Brush lapBrush = d.IsClassBestLap ? PurpleBest : TextSoft;
            FontWeight lapWeight = d.IsClassBestLap ? FontWeights.Bold : FontWeights.Normal;
            AddCell(g, 10, FormatHelpers.FormatLapTime(d.LastLapSeconds), _monoFont, 11,
                lapBrush, lapWeight, HorizontalAlignment.Right);
        }

        if (Settings.ShowStandingsPits)
        {
            string pitStr = d.IsOnPitRoad
                ? "PIT"
                : d.PitStops > 0
                    ? $"P{d.PitStops}·{d.StintLaps}"
                    : (d.StintLaps > 0 ? $"L{d.StintLaps}" : "-");
            Brush pitBrush = d.IsOnPitRoad ? PitAmber : TextMuted;
            AddCell(g, 11, pitStr, _monoFont, 9.5, pitBrush,
                d.IsOnPitRoad ? FontWeights.Bold : FontWeights.Normal, HorizontalAlignment.Center);
        }

        return new Border
        {
            Background = d.IsPlayer ? BgPlayer : (alt ? BgRowAlt : Brushes.Transparent),
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
        HorizontalAlignment align)
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
            Margin = new Thickness(2, 0, 4, 0)
        };
        Grid.SetColumn(tb, col);
        g.Children.Add(tb);
    }
}
