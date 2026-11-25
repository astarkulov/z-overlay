using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ApexHUD.Controls;
using ApexHUD.Core.Settings;
using ApexHUD.Core.Telemetry;

namespace ApexHUD.Windows;

/// <summary>
/// Dynamic auto-width Relative overlay (zero wasted horizontal space via SharedSizeGroup),
/// maximum-legibility bold typography scaled by HudFontScale, and full column/header modularity.
/// </summary>
public sealed class RelativeWindow : OverlayWindowBase
{
    private const int MaxRowPool = 13;

    private readonly Border _outerBorder;
    private readonly StackPanel _rootStack;
    private readonly Border _headerHost;
    private readonly Grid _headerGrid;
    private readonly TextBlock _titleText;
    private readonly TextBlock _sofText;
    private readonly HudMetricIcon _incIcon;
    private readonly TextBlock _incText;
    private readonly TextBlock _trackTempText;
    private readonly TextBlock _airTempText;
    private readonly Border _wetBadge;
    private readonly Border _flagBadge;
    private readonly HudMetricIcon _flagIcon;
    private readonly TextBlock _flagText;
    private readonly List<RowVisual> _rows = new(MaxRowPool);

    private static readonly SolidColorBrush BgPlayerCarbon = FormatHelpers.GetFrozenBrush("#4D2563EB");
    private static readonly SolidColorBrush BgPlayerNative = FormatHelpers.GetFrozenBrush("#22F2B830");
    private static readonly SolidColorBrush BgRowAltCarbon = FormatHelpers.GetFrozenBrush("#12FFFFFF");
    private static readonly SolidColorBrush NativeRowDivider = FormatHelpers.GetFrozenBrush("#FF2C303A");
    private static readonly SolidColorBrush Hairline = FormatHelpers.GetFrozenBrush("#22FFFFFF");
    private static readonly SolidColorBrush TextWhite = FormatHelpers.GetFrozenBrush("#FFFFFF");
    private static readonly SolidColorBrush TextSoft = FormatHelpers.GetFrozenBrush("#E6EDF7");
    private static readonly SolidColorBrush TextMuted = FormatHelpers.GetFrozenBrush("#CBD5E1");
    private static readonly SolidColorBrush NativeCarNumMuted = FormatHelpers.GetFrozenBrush("#9CA3AF");
    private static readonly SolidColorBrush DarkText = FormatHelpers.GetFrozenBrush("#111318");
    private static readonly SolidColorBrush IRacingGold = FormatHelpers.GetFrozenBrush("#F2B830");
    private static readonly SolidColorBrush IRacingGoldBright = FormatHelpers.GetFrozenBrush("#F5C84C");
    private static readonly SolidColorBrush LapAheadBrush = FormatHelpers.GetFrozenBrush("#FF6B81");
    private static readonly SolidColorBrush LapDownBrush = FormatHelpers.GetFrozenBrush("#60A5FA");
    private static readonly SolidColorBrush PlayerAccentCarbon = FormatHelpers.GetFrozenBrush("#93C5FD");
    private static readonly SolidColorBrush FasterRed = FormatHelpers.GetFrozenBrush("#FF5A6E");
    private static readonly SolidColorBrush SlowerGreen = FormatHelpers.GetFrozenBrush("#4ADE80");
    private static readonly SolidColorBrush PitTagBg = FormatHelpers.GetFrozenBrush("#FFF2B830");
    private static readonly SolidColorBrush OutTagBg = FormatHelpers.GetFrozenBrush("#FF0284C7");
    private static readonly SolidColorBrush OffTagBg = FormatHelpers.GetFrozenBrush("#FFDC2626");
    private static readonly SolidColorBrush AmberText = FormatHelpers.GetFrozenBrush("#FCD34D");
    private static readonly SolidColorBrush CarNumBoxBg = FormatHelpers.GetFrozenBrush("#24FFFFFF");

    private sealed class RowVisual
    {
        public Border Container = null!;
        public Grid RowGrid = null!;
        public Border ClassBar = null!;
        public Border PosPill = null!;
        public TextBlock Pos = null!;
        public Border CarNumBox = null!;
        public TextBlock CarNum = null!;
        public CarBrandIcon BrandIcon = null!;
        public TextBlock Name = null!;
        public Border StatusBadge = null!;
        public HudMetricIcon StatusIcon = null!;
        public TextBlock StatusText = null!;
        public Border LicBadge = null!;
        public TextBlock LicText = null!;
        public TextBlock IRating = null!;
        public TextBlock PaceDelta = null!;
        public TextBlock Gap = null!;
    }

    public RelativeWindow(AppSettings settings) : base("Relative", settings)
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
        _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _titleText = new TextBlock
        {
            Text = "Relative",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = TextWhite,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(1, 0, 6, 0)
        };
        Grid.SetColumn(_titleText, 0);
        _headerGrid.Children.Add(_titleText);

        var metaPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        _sofText = MakeHeaderText("--");
        metaPanel.Children.Add(MakeIconMetric(new HudMetricIcon(HudIconKind.Trophy, "#FCD34D", 12), _sofText));

        _incIcon = new HudMetricIcon(HudIconKind.Warning, "#CBD5E1", 12);
        _incText = MakeHeaderText("0/17");
        metaPanel.Children.Add(MakeIconMetric(_incIcon, _incText));

        _trackTempText = MakeHeaderText("--°");
        metaPanel.Children.Add(MakeIconMetric(new HudMetricIcon(HudIconKind.TrackTemp, "#FB923C", 12), _trackTempText));

        _airTempText = MakeHeaderText("--°");
        metaPanel.Children.Add(MakeIconMetric(new HudMetricIcon(HudIconKind.AirTemp, "#67E8F9", 12), _airTempText));

        Grid.SetColumn(metaPanel, 1);
        _headerGrid.Children.Add(metaPanel);

        var wetStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        wetStack.Children.Add(new HudMetricIcon(HudIconKind.Raindrop, "#FFFFFF", 10.5) { Margin = new Thickness(0, 0, 2, 0) });
        wetStack.Children.Add(new TextBlock { Text = "WET", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = TextWhite, VerticalAlignment = VerticalAlignment.Center });
        _wetBadge = new Border
        {
            Background = FormatHelpers.GetFrozenBrush("#2563EB"),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(4, 0, 4, 0),
            Margin = new Thickness(3, 1, 0, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Child = wetStack
        };
        Grid.SetColumn(_wetBadge, 2);
        _headerGrid.Children.Add(_wetBadge);

        _flagIcon = new HudMetricIcon(HudIconKind.Flag, "#FFFFFF", 10.5) { Margin = new Thickness(0, 0, 3, 0) };
        _flagText = new TextBlock
        {
            Text = "LIVE",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = TextWhite,
            VerticalAlignment = VerticalAlignment.Center
        };
        var flagStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        flagStack.Children.Add(_flagIcon);
        flagStack.Children.Add(_flagText);
        _flagBadge = new Border
        {
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(3, 1, 0, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = flagStack
        };
        Grid.SetColumn(_flagBadge, 3);
        _headerGrid.Children.Add(_flagBadge);

        _headerHost = new Border
        {
            BorderBrush = Hairline,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = _headerGrid
        };
        _rootStack.Children.Add(_headerHost);

        var rowsHost = new StackPanel { Margin = new Thickness(0) };
        for (int i = 0; i < MaxRowPool; i++)
        {
            var rv = CreateRowVisual();
            _rows.Add(rv);
            rowsHost.Children.Add(rv.Container);
        }
        _rootStack.Children.Add(rowsHost);

        _outerBorder.Child = _rootStack;
        AttachOverlayRoot(_outerBorder, "Relative");
    }

    private static StackPanel MakeIconMetric(HudMetricIcon icon, TextBlock tb)
    {
        icon.Margin = new Thickness(0, 0, 2, 0);
        var sp = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        sp.Children.Add(icon);
        sp.Children.Add(tb);
        return sp;
    }

    private static TextBlock MakeHeaderText(string text) => new()
    {
        Text = text,
        FontSize = 12.5,
        FontWeight = FontWeights.Bold,
        Foreground = TextSoft,
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

        // In iRacing Native: unified seamless charcoal card with gold uppercase title (no harsh dark header bar!)
        _headerGrid.Background = native ? Brushes.Transparent : MakeBackdropBrush(1.18, 0x0F, 0x13, 0x1C);
        _headerHost.BorderThickness = new Thickness(0, 0, 0, native ? 0 : 1);
        _headerHost.Visibility = Settings.ShowRelativeHeader ? Visibility.Visible : Visibility.Collapsed;

        var uiFont = GetUiFont();
        var monoFont = GetMonoFont();

        _titleText.Text = native ? "RELATIVE" : "Relative";
        _titleText.Foreground = native ? IRacingGold : TextWhite;
        _titleText.FontFamily = uiFont;
        _titleText.FontSize = FontSz(12);
        _sofText.FontFamily = monoFont;
        _sofText.FontSize = FontSz(12.5);
        _incText.FontFamily = monoFont;
        _incText.FontSize = FontSz(12.5);
        _trackTempText.FontFamily = monoFont;
        _trackTempText.FontSize = FontSz(12.5);
        _airTempText.FontFamily = monoFont;
        _airTempText.FontSize = FontSz(12.5);
        _flagText.FontFamily = uiFont;
        _flagText.FontSize = FontSz(10.5);

        double rowH = Math.Round(21 * Math.Max(1.0, Settings.HudFontScale * 0.92));

        foreach (var rv in _rows)
        {
            rv.RowGrid.Height = rowH;
            rv.CarNumBox.Visibility = Settings.ShowRelativeCarNumber ? Visibility.Visible : Visibility.Collapsed;
            rv.CarNumBox.Background = native ? Brushes.Transparent : CarNumBoxBg;
            rv.CarNumBox.Padding = native ? new Thickness(1, 0, 1, 0) : new Thickness(3, 0, 3, 0);
            rv.BrandIcon.Visibility = Settings.ShowCarBrandIcons ? Visibility.Visible : Visibility.Collapsed;
            rv.LicBadge.Visibility = Settings.ShowRelativeLicense ? Visibility.Visible : Visibility.Collapsed;
            rv.IRating.Visibility = Settings.ShowRelativeIRating ? Visibility.Visible : Visibility.Collapsed;
            rv.PaceDelta.Visibility = Settings.ShowRelativePaceDelta ? Visibility.Visible : Visibility.Collapsed;

            rv.Name.FontFamily = uiFont;
            rv.Name.FontSize = FontSz(12.5);
            rv.StatusText.FontFamily = uiFont;
            rv.StatusText.FontSize = FontSz(9.5);
            rv.Pos.FontFamily = monoFont;
            rv.Pos.FontSize = FontSz(12);
            rv.CarNum.FontFamily = monoFont;
            rv.CarNum.FontSize = FontSz(11.5);
            rv.LicText.FontFamily = monoFont;
            rv.LicText.FontSize = FontSz(10.5);
            rv.IRating.FontFamily = monoFont;
            rv.IRating.FontSize = FontSz(12);
            rv.PaceDelta.FontFamily = monoFont;
            rv.PaceDelta.FontSize = FontSz(11.5);
            rv.Gap.FontFamily = monoFont;
            rv.Gap.FontSize = FontSz(13);
        }
    }

    private static RowVisual CreateRowVisual()
    {
        var rv = new RowVisual();

        var grid = new Grid { Height = 20 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "RelPos" });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "RelNum" });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "RelBrand" });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "RelName" });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "RelStatus" });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "RelLic" });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "RelIR" });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "RelPace" });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "RelGap" });
        rv.RowGrid = grid;

        rv.ClassBar = new Border { Width = 3, Margin = new Thickness(0, 1, 0, 1), CornerRadius = new CornerRadius(1) };
        Grid.SetColumn(rv.ClassBar, 0);
        grid.Children.Add(rv.ClassBar);

        rv.Pos = new TextBlock
        {
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        rv.PosPill = new Border
        {
            CornerRadius = new CornerRadius(2),
            MinWidth = 18,
            Padding = new Thickness(3, 0, 3, 0),
            Margin = new Thickness(2, 1, 2, 1),
            Child = rv.Pos
        };
        Grid.SetColumn(rv.PosPill, 1);
        grid.Children.Add(rv.PosPill);

        rv.CarNum = new TextBlock
        {
            FontWeight = FontWeights.Bold,
            Foreground = TextSoft,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        rv.CarNumBox = new Border
        {
            Background = FormatHelpers.GetFrozenBrush("#24FFFFFF"),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(3, 0, 3, 0),
            Margin = new Thickness(0, 1, 2, 1),
            Child = rv.CarNum
        };
        Grid.SetColumn(rv.CarNumBox, 2);
        grid.Children.Add(rv.CarNumBox);

        rv.BrandIcon = new CarBrandIcon { Width = 16, Height = 14, Margin = new Thickness(0, 0, 3, 0) };
        Grid.SetColumn(rv.BrandIcon, 3);
        grid.Children.Add(rv.BrandIcon);

        rv.Name = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 190,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 5, 0)
        };
        Grid.SetColumn(rv.Name, 4);
        grid.Children.Add(rv.Name);

        rv.StatusIcon = new HudMetricIcon(HudIconKind.Wrench, "#FCD34D", 9) { Margin = new Thickness(0, 0, 2, 0) };
        rv.StatusText = new TextBlock
        {
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var stStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        stStack.Children.Add(rv.StatusIcon);
        stStack.Children.Add(rv.StatusText);
        rv.StatusBadge = new Border
        {
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(3, 0, 3, 0),
            Margin = new Thickness(0, 1, 3, 1),
            Visibility = Visibility.Collapsed,
            Child = stStack
        };
        Grid.SetColumn(rv.StatusBadge, 5);
        grid.Children.Add(rv.StatusBadge);

        rv.LicText = new TextBlock
        {
            FontWeight = FontWeights.Bold,
            Foreground = TextWhite,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        rv.LicBadge = new Border
        {
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(3, 0, 3, 0),
            Margin = new Thickness(0, 1, 3, 1),
            Child = rv.LicText
        };
        Grid.SetColumn(rv.LicBadge, 6);
        grid.Children.Add(rv.LicBadge);

        rv.IRating = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            Foreground = TextSoft,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };
        Grid.SetColumn(rv.IRating, 7);
        grid.Children.Add(rv.IRating);

        rv.PaceDelta = new TextBlock
        {
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };
        Grid.SetColumn(rv.PaceDelta, 8);
        grid.Children.Add(rv.PaceDelta);

        rv.Gap = new TextBlock
        {
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 3, 0)
        };
        Grid.SetColumn(rv.Gap, 9);
        grid.Children.Add(rv.Gap);

        rv.Container = new Border
        {
            Child = grid,
            Visibility = Visibility.Collapsed
        };

        return rv;
    }

    public void UpdateData(TelemetrySnapshot snap)
    {
        _sofText.Text = snap.Session.OverallSof > 0
            ? FormatHelpers.FormatIRating(snap.Session.OverallSof)
            : "--";

        _incText.Text = $"{snap.Session.Incidents}/{snap.Session.IncidentLimit}x";
        bool incHigh = snap.Session.IncidentLimit > 0 && snap.Session.Incidents >= snap.Session.IncidentLimit - 4;
        var incBrush = incHigh ? FasterRed : snap.Session.Incidents > 0 ? AmberText : TextSoft;
        _incIcon.Tint = incBrush;
        _incText.Foreground = incBrush;

        _trackTempText.Text = $"{snap.Session.TrackTempC:0}°";
        _airTempText.Text = $"{snap.Session.AirTempC:0}°";
        _wetBadge.Visibility = snap.Session.IsWetTrack ? Visibility.Visible : Visibility.Collapsed;

        UpdateFlagBadge(snap);

        bool native = IsIRacingNative;
        int count = Math.Min(MaxRowPool, snap.Relative.Count);
        for (int i = 0; i < MaxRowPool; i++)
        {
            var rv = _rows[i];
            if (i >= count)
            {
                rv.Container.Visibility = Visibility.Collapsed;
                continue;
            }

            var e = snap.Relative[i];
            rv.Container.Visibility = Visibility.Visible;

            rv.Container.Background = e.IsPlayer
                ? (native ? BgPlayerNative : BgPlayerCarbon)
                : (native ? Brushes.Transparent : (i % 2 == 1 ? BgRowAltCarbon : Brushes.Transparent));
            rv.Container.BorderBrush = native && i < count - 1 ? NativeRowDivider : Brushes.Transparent;
            rv.Container.BorderThickness = new Thickness(0, 0, 0, native && i < count - 1 ? 1 : 0);

            rv.ClassBar.Background = FormatHelpers.GetFrozenBrush(e.ClassColorHex);
            rv.Pos.Text = e.ClassPosition > 0 ? e.ClassPosition.ToString() : "-";
            rv.CarNum.Text = string.IsNullOrEmpty(e.CarNumber) ? "--" : (native ? $"#{e.CarNumber}" : e.CarNumber);
            rv.CarNum.Foreground = native ? IRacingGold : TextSoft;
            rv.BrandIcon.Brand = e.Brand;
            rv.Name.Text = e.DriverName;

            if (native && e.IsPlayer)
            {
                rv.PosPill.Background = IRacingGold;
                rv.Pos.Foreground = DarkText;
            }
            else
            {
                rv.PosPill.Background = Brushes.Transparent;
                rv.Pos.Foreground = native ? TextSoft : (e.IsPlayer ? PlayerAccentCarbon : TextSoft);
            }

            Brush nameColor = e.IsPlayer
                ? (native ? IRacingGoldBright : TextWhite)
                : e.LapRelation switch
                {
                    LapRelation.LapAhead => LapAheadBrush,
                    LapRelation.LapDown => LapDownBrush,
                    _ => TextWhite
                };

            rv.Name.Foreground = e.IsOnPitRoad && !e.IsPlayer ? TextMuted : nameColor;
            rv.Name.FontWeight = e.IsPlayer ? FontWeights.SemiBold : (native ? FontWeights.Normal : FontWeights.SemiBold);

            if (e.IsOnPitRoad)
            {
                rv.StatusBadge.Visibility = Visibility.Visible;
                rv.StatusBadge.Background = PitTagBg;
                rv.StatusIcon.Kind = HudIconKind.Wrench;
                rv.StatusIcon.Tint = DarkText;
                rv.StatusText.Text = "PIT";
                rv.StatusText.Foreground = DarkText;
            }
            else if (e.IsOutLap)
            {
                rv.StatusBadge.Visibility = Visibility.Visible;
                rv.StatusBadge.Background = OutTagBg;
                rv.StatusIcon.Kind = HudIconKind.OutLap;
                rv.StatusIcon.Tint = TextWhite;
                rv.StatusText.Text = "OUT";
                rv.StatusText.Foreground = TextWhite;
            }
            else if (e.IsOffTrack)
            {
                rv.StatusBadge.Visibility = Visibility.Visible;
                rv.StatusBadge.Background = OffTagBg;
                rv.StatusIcon.Kind = HudIconKind.Warning;
                rv.StatusIcon.Tint = TextWhite;
                rv.StatusText.Text = "OFF";
                rv.StatusText.Foreground = TextWhite;
            }
            else
            {
                rv.StatusBadge.Visibility = Visibility.Collapsed;
            }

            rv.LicBadge.Background = FormatHelpers.GetFrozenBrush(e.LicenseColorHex, "#2563EB");
            rv.LicText.Text = e.LicenseText;
            rv.IRating.Text = FormatHelpers.FormatIRating(e.IRating);

            if (e.IsPlayer || !e.HasPaceDelta || Math.Abs(e.PaceDeltaVsPlayerSec) > 15f)
            {
                rv.PaceDelta.Text = "";
            }
            else
            {
                float d = e.PaceDeltaVsPlayerSec;
                rv.PaceDelta.Text = d >= 0 ? $"▲{d:0.0}s" : $"▼{Math.Abs(d):0.0}s";
                rv.PaceDelta.Foreground = d < -0.15f ? FasterRed : d > 0.15f ? SlowerGreen : TextMuted;
            }

            rv.Gap.Text = e.IsPlayer ? "0.0" : FormatHelpers.FormatGap(e.GapSeconds, signed: true);
            rv.Gap.Foreground = e.IsPlayer ? (native ? IRacingGoldBright : PlayerAccentCarbon) : nameColor;
        }
    }

    private void UpdateFlagBadge(TelemetrySnapshot snap)
    {
        if (!snap.IsConnected)
        {
            _flagBadge.Background = FormatHelpers.GetFrozenBrush("#334155");
            _flagText.Text = "WAIT";
            _flagText.Foreground = TextSoft;
            _flagIcon.Tint = TextSoft;
            return;
        }

        var f = snap.Session.Flags;
        if (f.HasFlag(SessionFlagMask.Yellow) || f.HasFlag(SessionFlagMask.YellowWaving) || f.HasFlag(SessionFlagMask.Caution))
        {
            _flagBadge.Background = FormatHelpers.GetFrozenBrush("#EAB308");
            _flagText.Text = "YELLOW";
            _flagText.Foreground = DarkText;
            _flagIcon.Tint = DarkText;
        }
        else if (f.HasFlag(SessionFlagMask.Blue))
        {
            _flagBadge.Background = FormatHelpers.GetFrozenBrush("#2563EB");
            _flagText.Text = "BLUE";
            _flagText.Foreground = TextWhite;
            _flagIcon.Tint = TextWhite;
        }
        else if (f.HasFlag(SessionFlagMask.Black) || f.HasFlag(SessionFlagMask.Repair))
        {
            _flagBadge.Background = FormatHelpers.GetFrozenBrush("#EF4444");
            _flagText.Text = f.HasFlag(SessionFlagMask.Repair) ? "MEATBALL" : "BLACK";
            _flagText.Foreground = TextWhite;
            _flagIcon.Tint = TextWhite;
        }
        else if (f.HasFlag(SessionFlagMask.Checkered))
        {
            _flagBadge.Background = TextWhite;
            _flagText.Text = "FINISH";
            _flagText.Foreground = DarkText;
            _flagIcon.Tint = DarkText;
        }
        else
        {
            _flagBadge.Background = snap.IsDemo
                ? FormatHelpers.GetFrozenBrush("#2563EB")
                : FormatHelpers.GetFrozenBrush("#16A34A");
            _flagText.Text = snap.IsDemo ? "DEMO" : "GREEN";
            _flagText.Foreground = TextWhite;
            _flagIcon.Tint = TextWhite;
        }
    }
}
