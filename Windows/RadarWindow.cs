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

    private static readonly SolidColorBrush SlateBg = FormatHelpers.GetFrozenBrush("#E01E2026");
    private static readonly SolidColorBrush GoldBg = FormatHelpers.GetFrozenBrush("#FFF2B830");
    private static readonly SolidColorBrush RedBg = FormatHelpers.GetFrozenBrush("#EBDC2626");
    private static readonly SolidColorBrush DarkText = FormatHelpers.GetFrozenBrush("#FF111318");
    private static readonly SolidColorBrush WhiteText = FormatHelpers.GetFrozenBrush("#FFF8FAFC");

    public RadarWindow(AppSettings settings) : base("Radar", settings)
    {
        var host = new Grid
        {
            Width = 192,
            Height = 166,
            Background = Brushes.Transparent
        };

        _radar = new RadarCanvas
        {
            Width = 192,
            Height = 144,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top
        };
        host.Children.Add(_radar);

        _calloutText = new TextBlock
        {
            Text = "CLEAR",
            FontSize = 10,
            FontWeight = FontWeights.ExtraBold,
            Foreground = DarkText
        };
        _calloutBadge = new Border
        {
            Background = GoldBg,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(7, 1, 7, 1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Visibility = Visibility.Collapsed,
            Effect = HudTextShadow,
            Child = _calloutText
        };
        host.Children.Add(_calloutBadge);

        AttachOverlayRoot(host, "Radar");
    }

    protected override void OnAppearanceUpdated()
    {
        _radar?.SetBackdropOpacity(Settings.BackgroundOpacity, IsIRacingNative);
        if (_calloutText != null) _calloutText.FontFamily = GetMonoFont();
    }

    public void UpdateData(TelemetrySnapshot snap)
    {
        var r = snap.Radar;
        _radar.UpdateRadar(r);

        switch (r.Proximity)
        {
            case CarProximityState.CarLeft:
                SetCallout("CAR LEFT", RedBg, WhiteText);
                break;
            case CarProximityState.CarRight:
                SetCallout("CAR RIGHT", RedBg, WhiteText);
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
                    SetCallout($"REAR {r.NearestBehindMeters:0.0}m", GoldBg, DarkText);
                else if (r.NearestAheadMeters < 10f)
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
