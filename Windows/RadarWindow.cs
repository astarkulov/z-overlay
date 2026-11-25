using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ApexHUD.Controls;
using ApexHUD.Core.Settings;
using ApexHUD.Core.Telemetry;

namespace ApexHUD.Windows;

/// <summary>
/// Racelab-grade minimalist proximity radar:
/// Left/Right/3-Wide/Rear-closing arcs, live meter distance badge, dynamic fonts, and auto-hide when clear.
/// </summary>
public sealed class RadarWindow : OverlayWindowBase
{
    private readonly RadarCanvas _radar;
    private readonly Border _calloutBadge;
    private readonly TextBlock _calloutText;

    private static readonly SolidColorBrush SlateBg = FormatHelpers.GetFrozenBrush("#D81E293B");
    private static readonly SolidColorBrush AmberBg = FormatHelpers.GetFrozenBrush("#E0F59E0B");
    private static readonly SolidColorBrush RedBg = FormatHelpers.GetFrozenBrush("#E6EF4444");
    private static readonly SolidColorBrush DarkText = FormatHelpers.GetFrozenBrush("#0B0E14");
    private static readonly SolidColorBrush WhiteText = FormatHelpers.GetFrozenBrush("#F8FAFC");

    public RadarWindow(AppSettings settings) : base("Radar", settings)
    {
        var host = new Grid
        {
            Width = 186,
            Height = 208,
            Background = Brushes.Transparent
        };

        _radar = new RadarCanvas
        {
            Width = 180,
            Height = 180,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top
        };
        host.Children.Add(_radar);

        _calloutText = new TextBlock
        {
            Text = "CLEAR",
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Foreground = DarkText
        };
        _calloutBadge = new Border
        {
            Background = AmberBg,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 2, 8, 2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Visibility = Visibility.Collapsed,
            Child = _calloutText
        };
        host.Children.Add(_calloutBadge);

        AttachOverlayRoot(host, "Radar");
    }

    protected override void OnAppearanceUpdated()
    {
        _radar?.SetBackdropOpacity(Settings.BackgroundOpacity);
        if (_calloutText != null) _calloutText.FontFamily = GetMonoFont();
    }

    public void UpdateData(TelemetrySnapshot snap)
    {
        var r = snap.Radar;
        _radar.UpdateRadar(r);

        switch (r.Proximity)
        {
            case CarProximityState.CarLeft:
                SetCallout("CAR LEFT", AmberBg, DarkText);
                break;
            case CarProximityState.CarRight:
                SetCallout("CAR RIGHT", AmberBg, DarkText);
                break;
            case CarProximityState.CarLeftRight:
                SetCallout("3 WIDE", RedBg, WhiteText);
                break;
            case CarProximityState.TwoCarsLeft:
                SetCallout("2 LEFT", RedBg, WhiteText);
                break;
            case CarProximityState.TwoCarsRight:
                SetCallout("2 RIGHT", RedBg, WhiteText);
                break;
            default:
                if (r.NearestBehindMeters < 12f)
                    SetCallout($"REAR {r.NearestBehindMeters:0.0}m", SlateBg, WhiteText);
                else if (r.NearestAheadMeters < 12f)
                    SetCallout($"AHEAD {r.NearestAheadMeters:0.0}m", SlateBg, WhiteText);
                else
                    _calloutBadge.Visibility = Visibility.Collapsed;
                break;
        }

        var layout = Settings.GetWidget(WidgetKey);
        if (!IsEditMode && Settings.RadarAutoHideWhenClear && !r.HasActiveThreat)
            Opacity = 0.0;
        else
            Opacity = layout.Opacity;
    }

    private void SetCallout(string text, Brush bg, Brush fg)
    {
        _calloutText.Text = text;
        _calloutText.Foreground = fg;
        _calloutBadge.Background = bg;
        _calloutBadge.Visibility = Visibility.Visible;
    }
}
