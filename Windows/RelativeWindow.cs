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
/// Racelab-grade Relative overlay:
/// Car brand icons, class strip, pace delta vs player (+/-s), OUT/PIT/OFF tags,
/// SR/iR badges, SOF, incidents, Track/Air temp, WET status, and dynamic fonts.
/// </summary>
public sealed class RelativeWindow : OverlayWindowBase
{
    private const int MaxRowPool = 13;

    private readonly Border _outerBorder;
    private readonly Grid _headerGrid;
    private readonly TextBlock _titleText;
    private readonly TextBlock _sofText;
    private readonly TextBlock _incText;
    private readonly TextBlock _tempText;
    private readonly Border _wetBadge;
    private readonly Border _flagBadge;
    private readonly TextBlock _flagText;
    private readonly List<RowVisual> _rows = new(MaxRowPool);

    private static readonly SolidColorBrush BgPlayer = FormatHelpers.GetFrozenBrush("#383B82F6");
    private static readonly SolidColorBrush BgRowAlt = FormatHelpers.GetFrozenBrush("#0BFFFFFF");
    private static readonly SolidColorBrush Hairline = FormatHelpers.GetFrozenBrush("#16FFFFFF");
    private static readonly SolidColorBrush TextWhite = FormatHelpers.GetFrozenBrush("#F8FAFC");
    private static readonly SolidColorBrush TextSoft = FormatHelpers.GetFrozenBrush("#CBD5E1");
    private static readonly SolidColorBrush TextMuted = FormatHelpers.GetFrozenBrush("#94A3B8");
    private static readonly SolidColorBrush LapAheadBrush = FormatHelpers.GetFrozenBrush("#FB7185");
    private static readonly SolidColorBrush LapDownBrush = FormatHelpers.GetFrozenBrush("#38BDF8");
    private static readonly SolidColorBrush PlayerAccent = FormatHelpers.GetFrozenBrush("#93C5FD");
    private static readonly SolidColorBrush FasterRed = FormatHelpers.GetFrozenBrush("#F87171");
    private static readonly SolidColorBrush SlowerGreen = FormatHelpers.GetFrozenBrush("#4ADE80");
    private static readonly SolidColorBrush PitTagBg = FormatHelpers.GetFrozenBrush("#38F59E0B");
    private static readonly SolidColorBrush OutTagBg = FormatHelpers.GetFrozenBrush("#3838BDF8");
    private static readonly SolidColorBrush OffTagBg = FormatHelpers.GetFrozenBrush("#38EF4444");
    private static readonly SolidColorBrush AmberText = FormatHelpers.GetFrozenBrush("#FBBF24");

    private sealed class RowVisual
    {
        public Border Container = null!;
        public ColumnDefinition BrandCol = null!;
        public ColumnDefinition LicCol = null!;
        public ColumnDefinition IrCol = null!;
        public ColumnDefinition PaceCol = null!;
        public Border ClassBar = null!;
        public TextBlock Pos = null!;
        public Border CarNumBox = null!;
        public TextBlock CarNum = null!;
        public CarBrandIcon BrandIcon = null!;
        public TextBlock Name = null!;
        public Border StatusBadge = null!;
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
            Width = 486,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            SnapsToDevicePixels = true
        };

        var rootStack = new StackPanel();

        _headerGrid = new Grid { Height = 24 };
        _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _titleText = new TextBlock
        {
            Text = "Relative",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextWhite,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 12, 0)
        };
        Grid.SetColumn(_titleText, 0);
        _headerGrid.Children.Add(_titleText);

        var metaPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        _sofText = MakeHeaderMeta("SOF --");
        _incText = MakeHeaderMeta("0/17x");
        _tempText = MakeHeaderMeta("T --° A --°");
        metaPanel.Children.Add(_sofText);
        metaPanel.Children.Add(_incText);
        metaPanel.Children.Add(_tempText);
        Grid.SetColumn(metaPanel, 1);
        _headerGrid.Children.Add(metaPanel);

        _wetBadge = new Border
        {
            Background = FormatHelpers.GetFrozenBrush("#2563EB"),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(0, 3, 4, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Child = new TextBlock
            {
                Text = "WET",
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = TextWhite
            }
        };
        Grid.SetColumn(_wetBadge, 2);
        _headerGrid.Children.Add(_wetBadge);

        _flagText = new TextBlock
        {
            Text = "LIVE",
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
            Foreground = TextWhite
        };
        _flagBadge = new Border
        {
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(2, 3, 7, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _flagText
        };
        Grid.SetColumn(_flagBadge, 3);
        _headerGrid.Children.Add(_flagBadge);

        rootStack.Children.Add(_headerGrid);
        rootStack.Children.Add(new Border { Height = 1, Background = Hairline });

        var rowsHost = new StackPanel { Margin = new Thickness(0, 1, 0, 2) };
        for (int i = 0; i < MaxRowPool; i++)
        {
            var rv = CreateRowVisual();
            _rows.Add(rv);
            rowsHost.Children.Add(rv.Container);
        }
        rootStack.Children.Add(rowsHost);

        _outerBorder.Child = rootStack;
        AttachOverlayRoot(_outerBorder, "Relative");
    }

    protected override void OnAppearanceUpdated()
    {
        if (_outerBorder == null) return;
        _outerBorder.Background = MakeBackdropBrush(1.0);
        _outerBorder.BorderBrush = MakeBorderBrush();
        _headerGrid.Background = MakeBackdropBrush(1.18, 0x0F, 0x13, 0x1C);

        var uiFont = GetUiFont();
        var monoFont = GetMonoFont();

        _titleText.FontFamily = uiFont;
        _sofText.FontFamily = monoFont;
        _incText.FontFamily = monoFont;
        _tempText.FontFamily = monoFont;
        _flagText.FontFamily = uiFont;

        double brandW = Settings.ShowCarBrandIcons ? 22 : 0;
        double licW = Settings.ShowRelativeLicense ? 46 : 0;
        double irW = Settings.ShowRelativeIRating ? 42 : 0;
        double paceW = Settings.ShowRelativePaceDelta ? 44 : 0;

        foreach (var rv in _rows)
        {
            rv.BrandCol.Width = new GridLength(brandW);
            rv.BrandIcon.Visibility = Settings.ShowCarBrandIcons ? Visibility.Visible : Visibility.Collapsed;
            rv.LicCol.Width = new GridLength(licW);
            rv.IrCol.Width = new GridLength(irW);
            rv.PaceCol.Width = new GridLength(paceW);
            rv.LicBadge.Visibility = Settings.ShowRelativeLicense ? Visibility.Visible : Visibility.Collapsed;
            rv.IRating.Visibility = Settings.ShowRelativeIRating ? Visibility.Visible : Visibility.Collapsed;
            rv.PaceDelta.Visibility = Settings.ShowRelativePaceDelta ? Visibility.Visible : Visibility.Collapsed;

            rv.Name.FontFamily = uiFont;
            rv.StatusText.FontFamily = uiFont;
            rv.Pos.FontFamily = monoFont;
            rv.CarNum.FontFamily = monoFont;
            rv.LicText.FontFamily = monoFont;
            rv.IRating.FontFamily = monoFont;
            rv.PaceDelta.FontFamily = monoFont;
            rv.Gap.FontFamily = monoFont;
        }
    }

    private static TextBlock MakeHeaderMeta(string text) => new()
    {
        Text = text,
        FontSize = 10.5,
        Foreground = TextMuted,
        Margin = new Thickness(0, 0, 10, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static RowVisual CreateRowVisual()
    {
        var rv = new RowVisual();

        var grid = new Grid { Height = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        rv.BrandCol = new ColumnDefinition { Width = new GridLength(22) };
        grid.ColumnDefinitions.Add(rv.BrandCol);
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rv.LicCol = new ColumnDefinition { Width = new GridLength(46) };
        rv.IrCol = new ColumnDefinition { Width = new GridLength(42) };
        rv.PaceCol = new ColumnDefinition { Width = new GridLength(44) };
        grid.ColumnDefinitions.Add(rv.LicCol);
        grid.ColumnDefinitions.Add(rv.IrCol);
        grid.ColumnDefinitions.Add(rv.PaceCol);
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });

        rv.ClassBar = new Border { Width = 3, Margin = new Thickness(0, 2, 0, 2), CornerRadius = new CornerRadius(1) };
        Grid.SetColumn(rv.ClassBar, 0);
        grid.Children.Add(rv.ClassBar);

        rv.Pos = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        };
        Grid.SetColumn(rv.Pos, 1);
        grid.Children.Add(rv.Pos);

        rv.CarNum = new TextBlock
        {
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextSoft,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        rv.CarNumBox = new Border
        {
            Background = FormatHelpers.GetFrozenBrush("#1AFFFFFF"),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(2, 4, 3, 4),
            Child = rv.CarNum
        };
        Grid.SetColumn(rv.CarNumBox, 2);
        grid.Children.Add(rv.CarNumBox);

        rv.BrandIcon = new CarBrandIcon { Margin = new Thickness(1, 0, 2, 0) };
        Grid.SetColumn(rv.BrandIcon, 3);
        grid.Children.Add(rv.BrandIcon);

        rv.Name = new TextBlock
        {
            FontSize = 12.5,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(3, 0, 5, 0)
        };
        Grid.SetColumn(rv.Name, 4);
        grid.Children.Add(rv.Name);

        rv.StatusText = new TextBlock
        {
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };
        rv.StatusBadge = new Border
        {
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(4, 0, 4, 0),
            Margin = new Thickness(0, 4, 4, 4),
            Visibility = Visibility.Collapsed,
            Child = rv.StatusText
        };
        Grid.SetColumn(rv.StatusBadge, 5);
        grid.Children.Add(rv.StatusBadge);

        rv.LicText = new TextBlock
        {
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextWhite,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        rv.LicBadge = new Border
        {
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(2, 4, 2, 4),
            Child = rv.LicText
        };
        Grid.SetColumn(rv.LicBadge, 6);
        grid.Children.Add(rv.LicBadge);

        rv.IRating = new TextBlock
        {
            FontSize = 11.5,
            Foreground = TextMuted,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };
        Grid.SetColumn(rv.IRating, 7);
        grid.Children.Add(rv.IRating);

        rv.PaceDelta = new TextBlock
        {
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };
        Grid.SetColumn(rv.PaceDelta, 8);
        grid.Children.Add(rv.PaceDelta);

        rv.Gap = new TextBlock
        {
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 9, 0)
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
            ? $"SOF {FormatHelpers.FormatIRating(snap.Session.OverallSof)}"
            : "SOF --";
        _incText.Text = $"{snap.Session.Incidents}/{snap.Session.IncidentLimit}x";
        _tempText.Text = $"T {snap.Session.TrackTempC:0}° A {snap.Session.AirTempC:0}°";
        _wetBadge.Visibility = snap.Session.IsWetTrack ? Visibility.Visible : Visibility.Collapsed;

        UpdateFlagBadge(snap);

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
                ? BgPlayer
                : (i % 2 == 1 ? BgRowAlt : Brushes.Transparent);

            rv.ClassBar.Background = FormatHelpers.GetFrozenBrush(e.ClassColorHex);
            rv.Pos.Text = e.ClassPosition > 0 ? e.ClassPosition.ToString() : "-";
            rv.CarNum.Text = string.IsNullOrEmpty(e.CarNumber) ? "--" : e.CarNumber;
            rv.BrandIcon.Brand = e.Brand;
            rv.Name.Text = e.DriverName;

            Brush nameColor = e.IsPlayer
                ? TextWhite
                : e.LapRelation switch
                {
                    LapRelation.LapAhead => LapAheadBrush,
                    LapRelation.LapDown => LapDownBrush,
                    _ => TextWhite
                };

            rv.Pos.Foreground = e.IsPlayer ? PlayerAccent : TextMuted;
            rv.Name.Foreground = e.IsOnPitRoad && !e.IsPlayer ? TextMuted : nameColor;
            rv.Name.FontWeight = e.IsPlayer ? FontWeights.SemiBold : FontWeights.Normal;

            if (e.IsOnPitRoad)
            {
                rv.StatusBadge.Visibility = Visibility.Visible;
                rv.StatusBadge.Background = PitTagBg;
                rv.StatusText.Text = "PIT";
                rv.StatusText.Foreground = AmberText;
            }
            else if (e.IsOutLap)
            {
                rv.StatusBadge.Visibility = Visibility.Visible;
                rv.StatusBadge.Background = OutTagBg;
                rv.StatusText.Text = "OUT";
                rv.StatusText.Foreground = LapDownBrush;
            }
            else if (e.IsOffTrack)
            {
                rv.StatusBadge.Visibility = Visibility.Visible;
                rv.StatusBadge.Background = OffTagBg;
                rv.StatusText.Text = "OFF";
                rv.StatusText.Foreground = LapAheadBrush;
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
                // Positive = rival is slower than player (green), Negative = rival is faster (red)
                float d = e.PaceDeltaVsPlayerSec;
                rv.PaceDelta.Text = d >= 0 ? $"+{d:0.0}s" : $"{d:0.0}s";
                rv.PaceDelta.Foreground = d < -0.15f ? FasterRed : d > 0.15f ? SlowerGreen : TextMuted;
            }

            rv.Gap.Text = e.IsPlayer ? "0.0" : FormatHelpers.FormatGap(e.GapSeconds, signed: true);
            rv.Gap.Foreground = e.IsPlayer ? PlayerAccent : nameColor;
        }
    }

    private void UpdateFlagBadge(TelemetrySnapshot snap)
    {
        if (!snap.IsConnected)
        {
            _flagBadge.Background = FormatHelpers.GetFrozenBrush("#334155");
            _flagText.Text = "WAIT";
            _flagText.Foreground = TextSoft;
            return;
        }

        var f = snap.Session.Flags;
        if (f.HasFlag(SessionFlagMask.Yellow) || f.HasFlag(SessionFlagMask.YellowWaving) || f.HasFlag(SessionFlagMask.Caution))
        {
            _flagBadge.Background = FormatHelpers.GetFrozenBrush("#EAB308");
            _flagText.Text = "YELLOW";
            _flagText.Foreground = FormatHelpers.GetFrozenBrush("#0B0E14");
        }
        else if (f.HasFlag(SessionFlagMask.Blue))
        {
            _flagBadge.Background = FormatHelpers.GetFrozenBrush("#2563EB");
            _flagText.Text = "BLUE";
            _flagText.Foreground = TextWhite;
        }
        else if (f.HasFlag(SessionFlagMask.Black) || f.HasFlag(SessionFlagMask.Repair))
        {
            _flagBadge.Background = FormatHelpers.GetFrozenBrush("#EF4444");
            _flagText.Text = f.HasFlag(SessionFlagMask.Repair) ? "MEATBALL" : "BLACK";
            _flagText.Foreground = TextWhite;
        }
        else if (f.HasFlag(SessionFlagMask.Checkered))
        {
            _flagBadge.Background = TextWhite;
            _flagText.Text = "FINISH";
            _flagText.Foreground = FormatHelpers.GetFrozenBrush("#0B0E14");
        }
        else
        {
            _flagBadge.Background = snap.IsDemo
                ? FormatHelpers.GetFrozenBrush("#2563EB")
                : FormatHelpers.GetFrozenBrush("#16A34A");
            _flagText.Text = snap.IsDemo ? "DEMO" : "GREEN";
            _flagText.Foreground = TextWhite;
        }
    }
}
