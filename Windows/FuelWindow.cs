using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ApexHUD.Core.Settings;
using ApexHUD.Core.Telemetry;

namespace ApexHUD.Windows;

/// <summary>
/// Racelab-grade Fuel Strategy widget:
/// Mode badge (AVG/LAST/MAX), Pit Stops remaining, 3-column Refuel Matrix (MIN / TARGET / SAFE),
/// Est. refuel time in seconds, save target, and dynamic fonts.
/// </summary>
public sealed class FuelWindow : OverlayWindowBase
{
    private readonly Border _outerBorder;
    private readonly StackPanel _rootStack;
    private readonly Grid _headerGrid;
    private readonly TextBlock _titleText;
    private readonly TextBlock _modeBadgeText;
    private readonly TextBlock _stopsText;
    private readonly Border _statusBadge;
    private readonly TextBlock _statusText;
    private readonly Border _fuelBarFill;
    private readonly Grid _fuelBarHost;

    private readonly TextBlock _fuelLevelVal;
    private readonly TextBlock _estLapsVal;
    private readonly TextBlock _raceLapsVal;

    private readonly TextBlock _lastBurnVal;
    private readonly TextBlock _avgBurnVal;
    private readonly TextBlock _maxBurnVal;

    private readonly TextBlock _minAddVal;
    private readonly TextBlock _targetAddLabel;
    private readonly TextBlock _targetAddVal;
    private readonly TextBlock _safeAddVal;

    private readonly TextBlock _refuelTimeVal;
    private readonly TextBlock _saveTargetVal;

    private readonly TextBlock[] _allLabels;
    private readonly TextBlock[] _allValues;

    private static readonly SolidColorBrush Hairline = FormatHelpers.GetFrozenBrush("#22FFFFFF");
    private static readonly SolidColorBrush TextWhite = FormatHelpers.GetFrozenBrush("#FFFFFF");
    private static readonly SolidColorBrush TextMuted = FormatHelpers.GetFrozenBrush("#CBD5E1");
    private static readonly SolidColorBrush AccentBlue = FormatHelpers.GetFrozenBrush("#3B82F6");
    private static readonly SolidColorBrush AccentSky = FormatHelpers.GetFrozenBrush("#7DD3FC");
    private static readonly SolidColorBrush AccentAmber = FormatHelpers.GetFrozenBrush("#FCD34D");
    private static readonly SolidColorBrush IRacingGold = FormatHelpers.GetFrozenBrush("#F2B830");
    private static readonly SolidColorBrush OkGreen = FormatHelpers.GetFrozenBrush("#4ADE80");
    private static readonly SolidColorBrush DangerRed = FormatHelpers.GetFrozenBrush("#FF5A6E");

    public FuelWindow(AppSettings settings) : base("Fuel", settings)
    {
        _outerBorder = new Border
        {
            Width = 312,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            SnapsToDevicePixels = true
        };

        var labels = new System.Collections.Generic.List<TextBlock>();
        var values = new System.Collections.Generic.List<TextBlock>();

        _rootStack = new StackPanel { Effect = GetActiveShadow() };
        var root = _rootStack;

        // Header
        _headerGrid = new Grid { Height = 24 };
        _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _titleText = new TextBlock
        {
            Text = "Fuel",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextWhite,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 8, 0)
        };
        labels.Add(_titleText);
        _headerGrid.Children.Add(_titleText);

        var hdrMeta = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _modeBadgeText = new TextBlock
        {
            Text = "AVG",
            FontSize = 9.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = AccentSky,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        _stopsText = new TextBlock
        {
            Text = "NO STOP",
            FontSize = 9.5,
            Foreground = TextMuted,
            VerticalAlignment = VerticalAlignment.Center
        };
        values.Add(_modeBadgeText);
        values.Add(_stopsText);
        hdrMeta.Children.Add(_modeBadgeText);
        hdrMeta.Children.Add(_stopsText);
        Grid.SetColumn(hdrMeta, 1);
        _headerGrid.Children.Add(hdrMeta);

        _statusText = new TextBlock
        {
            Text = "OK",
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
            Foreground = TextWhite
        };
        labels.Add(_statusText);
        _statusBadge = new Border
        {
            Background = OkGreen,
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(0, 3, 7, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _statusText
        };
        Grid.SetColumn(_statusBadge, 2);
        _headerGrid.Children.Add(_statusBadge);
        root.Children.Add(_headerGrid);

        // Tank fill bar
        _fuelBarHost = new Grid { Height = 3, Background = FormatHelpers.GetFrozenBrush("#1EFFFFFF") };
        _fuelBarFill = new Border { Background = AccentBlue, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        _fuelBarHost.Children.Add(_fuelBarFill);
        root.Children.Add(_fuelBarHost);

        // Row 1: Primary stats
        var topMetrics = new Grid { Margin = new Thickness(10, 7, 10, 6) };
        topMetrics.ColumnDefinitions.Add(new ColumnDefinition());
        topMetrics.ColumnDefinitions.Add(new ColumnDefinition());
        topMetrics.ColumnDefinitions.Add(new ColumnDefinition());

        _fuelLevelVal = AddMetric(topMetrics, 0, "REMAIN", "0.0 L", 16, TextWhite, labels, values);
        _estLapsVal = AddMetric(topMetrics, 1, "EST LAPS", "--", 16, AccentSky, labels, values);
        _raceLapsVal = AddMetric(topMetrics, 2, "RACE REM", "--", 16, TextMuted, labels, values);
        root.Children.Add(topMetrics);

        root.Children.Add(new Border { Height = 1, Background = Hairline });

        // Row 2: Burn rates
        var burnGrid = new Grid { Margin = new Thickness(10, 5, 10, 5) };
        burnGrid.ColumnDefinitions.Add(new ColumnDefinition());
        burnGrid.ColumnDefinitions.Add(new ColumnDefinition());
        burnGrid.ColumnDefinitions.Add(new ColumnDefinition());

        _lastBurnVal = AddMetric(burnGrid, 0, "LAST /L", "--", 12, TextWhite, labels, values);
        _avgBurnVal = AddMetric(burnGrid, 1, "AVG 5L", "--", 12, TextWhite, labels, values);
        _maxBurnVal = AddMetric(burnGrid, 2, "MAX /L", "--", 12, TextMuted, labels, values);
        root.Children.Add(burnGrid);

        root.Children.Add(new Border { Height = 1, Background = Hairline });

        // Row 3: Racelab 3-column Refuel Strategy Matrix (MIN | TARGET | SAFE)
        var matrixGrid = new Grid { Margin = new Thickness(10, 6, 10, 6) };
        matrixGrid.ColumnDefinitions.Add(new ColumnDefinition());
        matrixGrid.ColumnDefinitions.Add(new ColumnDefinition());
        matrixGrid.ColumnDefinitions.Add(new ColumnDefinition());

        _minAddVal = AddMetric(matrixGrid, 0, "MIN (+0L)", "--", 13.5, TextMuted, labels, values);
        (_targetAddLabel, _targetAddVal) = AddMetricWithLabel(matrixGrid, 1, "ADD (+0.5L)", "--", 15, AccentAmber, labels, values);
        _safeAddVal = AddMetric(matrixGrid, 2, "SAFE (+1.5L)", "--", 13.5, TextWhite, labels, values);
        root.Children.Add(matrixGrid);

        root.Children.Add(new Border { Height = 1, Background = Hairline });

        // Row 4: Refuel Time + Target Save Burn
        var footerGrid = new Grid { Margin = new Thickness(10, 4, 10, 6) };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition());
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition());

        _refuelTimeVal = new TextBlock
        {
            Text = "PIT FILL: 0.0s",
            FontSize = 10.5,
            Foreground = TextMuted,
            VerticalAlignment = VerticalAlignment.Center
        };
        values.Add(_refuelTimeVal);
        Grid.SetColumn(_refuelTimeVal, 0);
        footerGrid.Children.Add(_refuelTimeVal);

        _saveTargetVal = new TextBlock
        {
            Text = "SAVE: -- /L",
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = AccentSky,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        values.Add(_saveTargetVal);
        Grid.SetColumn(_saveTargetVal, 1);
        footerGrid.Children.Add(_saveTargetVal);
        root.Children.Add(footerGrid);

        _allLabels = labels.ToArray();
        _allValues = values.ToArray();

        _outerBorder.Child = root;
        AttachOverlayRoot(_outerBorder, "Fuel");
    }

    protected override void OnAppearanceUpdated()
    {
        if (_outerBorder == null) return;
        bool native = IsIRacingNative;

        _outerBorder.Background = MakeBackdropBrush(1.0);
        _outerBorder.BorderBrush = MakeBorderBrush();
        _outerBorder.CornerRadius = GetCardRadius();
        _rootStack.Effect = GetActiveShadow();

        // Unified matte charcoal card + iRacing Gold FUEL title in Native skin
        _headerGrid.Background = native ? Brushes.Transparent : MakeBackdropBrush(1.18, 0x0F, 0x13, 0x1C);
        _titleText.Text = native ? "FUEL" : "Fuel";
        _titleText.Foreground = native ? IRacingGold : TextWhite;
        _modeBadgeText.Foreground = native ? IRacingGold : AccentSky;

        var uiFont = GetUiFont();
        var monoFont = GetMonoFont();
        foreach (var l in _allLabels) l.FontFamily = uiFont;
        foreach (var v in _allValues) v.FontFamily = monoFont;

        _titleText.FontSize = FontSz(11.5);
        _modeBadgeText.FontSize = FontSz(10.5);
        _stopsText.FontSize = FontSz(10.5);
        _fuelLevelVal.FontSize = FontSz(19);
        _estLapsVal.FontSize = FontSz(19);
        _raceLapsVal.FontSize = FontSz(19);
        _lastBurnVal.FontSize = FontSz(14);
        _avgBurnVal.FontSize = FontSz(14);
        _maxBurnVal.FontSize = FontSz(14);
        _minAddVal.FontSize = FontSz(15.5);
        _targetAddVal.FontSize = FontSz(17.5);
        _safeAddVal.FontSize = FontSz(15.5);
        _refuelTimeVal.FontSize = FontSz(11.5);
        _saveTargetVal.FontSize = FontSz(11.5);
    }

    private static TextBlock AddMetric(
        Grid g, int col, string label, string init, double size, Brush color,
        System.Collections.Generic.List<TextBlock> labels,
        System.Collections.Generic.List<TextBlock> values)
        => AddMetricWithLabel(g, col, label, init, size, color, labels, values).Val;

    private static (TextBlock Label, TextBlock Val) AddMetricWithLabel(
        Grid g, int col, string label, string init, double size, Brush color,
        System.Collections.Generic.List<TextBlock> labels,
        System.Collections.Generic.List<TextBlock> values)
    {
        var sp = new StackPanel();
        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextMuted
        };
        var val = new TextBlock
        {
            Text = init,
            FontSize = size,
            FontWeight = FontWeights.Bold,
            Foreground = color
        };
        labels.Add(lbl);
        values.Add(val);
        sp.Children.Add(lbl);
        sp.Children.Add(val);
        Grid.SetColumn(sp, col);
        g.Children.Add(sp);
        return (lbl, val);
    }

    public void UpdateData(TelemetrySnapshot snap)
    {
        var f = snap.Fuel;
        bool imp = Settings.UseImperialUnits;
        float mult = imp ? 0.264172f : 1f;
        string unit = imp ? "gal" : "L";

        _modeBadgeText.Text = Settings.FuelCalcMode.ToUpperInvariant();
        _stopsText.Text = f.PitStopsRemaining == 0 ? "NO STOP" : $"{f.PitStopsRemaining} STOP{(f.PitStopsRemaining > 1 ? "S" : "")}";

        _fuelLevelVal.Text = $"{f.FuelLevelLiters * mult:0.0} {unit}";
        _estLapsVal.Text = f.HasReliableBurn ? $"{f.EstLapsOnFuel:0.0}" : "--";
        _raceLapsVal.Text = f.RaceLapsRemaining > 0.05f ? $"{f.RaceLapsRemaining:0.0}" : "--";

        _lastBurnVal.Text = f.LastLapBurn > 0.05f ? $"{f.LastLapBurn * mult:0.00}" : "--";
        _avgBurnVal.Text = f.AvgLapBurn > 0.05f ? $"{f.AvgLapBurn * mult:0.00}" : "--";
        _maxBurnVal.Text = f.MaxLapBurn > 0.05f ? $"{f.MaxLapBurn * mult:0.00}" : "--";

        _targetAddLabel.Text = $"ADD (+{Settings.FuelSafetyMarginLaps:0.0}L)";
        if (!f.HasReliableBurn || f.RaceLapsRemaining <= 0.05f)
        {
            _minAddVal.Text = "--";
            _targetAddVal.Text = "--";
            _safeAddVal.Text = "--";
            _targetAddVal.Foreground = TextMuted;
        }
        else
        {
            _minAddVal.Text = f.FuelToAddMinLiters <= 0.1f ? "0.0" : $"+{f.FuelToAddMinLiters * mult:0.0}";
            _safeAddVal.Text = f.FuelToAddSafeLiters <= 0.1f ? "0.0" : $"+{f.FuelToAddSafeLiters * mult:0.0}";

            if (f.FuelToAddLiters <= 0.1f)
            {
                _targetAddVal.Text = $"OK 0.0";
                _targetAddVal.Foreground = OkGreen;
            }
            else
            {
                _targetAddVal.Text = $"+{f.FuelToAddLiters * mult:0.0} {unit}";
                _targetAddVal.Foreground = AccentAmber;
            }
        }

        _refuelTimeVal.Text = f.EstRefuelSeconds > 0.1f ? $"PIT FILL ~{f.EstRefuelSeconds:0.0}s" : "PIT FILL: 0.0s";
        _saveTargetVal.Text = f.TargetBurnPerLap > 0.05f && f.RaceLapsRemaining > 0.1f
            ? $"SAVE: {f.TargetBurnPerLap * mult:0.00} {unit}/L"
            : "SAVE: --";

        double barW = _fuelBarHost.ActualWidth > 10 ? _fuelBarHost.ActualWidth : 310;
        _fuelBarFill.Width = Math.Clamp(f.FuelPct, 0f, 1f) * barW;

        if (f.IsLowFuel)
        {
            _fuelBarFill.Background = DangerRed;
            _statusBadge.Background = DangerRed;
            _statusText.Text = "LOW FUEL";
            _estLapsVal.Foreground = DangerRed;
        }
        else if (f.IsPitWindowOpen)
        {
            _fuelBarFill.Background = AccentAmber;
            _statusBadge.Background = AccentAmber;
            _statusText.Text = "PIT";
            _estLapsVal.Foreground = AccentAmber;
        }
        else
        {
            _fuelBarFill.Background = AccentBlue;
            _statusBadge.Background = FormatHelpers.GetFrozenBrush("#16A34A");
            _statusText.Text = "OK";
            _estLapsVal.Foreground = AccentSky;
        }
    }
}
