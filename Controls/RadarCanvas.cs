using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ApexHUD.Core.Telemetry;

namespace ApexHUD.Controls;

/// <summary>
/// Minimalist modern proximity radar with configurable translucent backdrop.
/// </summary>
public sealed class RadarCanvas : FrameworkElement
{
    private CarProximityState _proximity = CarProximityState.Clear;
    private float _nearestBehindMeters = 999f;
    private IReadOnlyList<RadarBlip> _blips = Array.Empty<RadarBlip>();
    private SolidColorBrush _bgCircleBrush;

    private static readonly Pen RingPen;
    private static readonly Pen AxisPen;
    private static readonly SolidColorBrush PlayerCarBrush;
    private static readonly Pen PlayerCarOutlinePen;
    private static readonly Pen WarnArcPen;
    private static readonly Pen DangerArcPen;

    static RadarCanvas()
    {
        RingPen = new Pen(new SolidColorBrush(Color.FromArgb(0x36, 0xFF, 0xFF, 0xFF)), 1.0);
        RingPen.Freeze();

        AxisPen = new Pen(new SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF)), 1.0);
        AxisPen.Freeze();

        PlayerCarBrush = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
        PlayerCarBrush.Freeze();

        PlayerCarOutlinePen = new Pen(new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)), 1.4);
        PlayerCarOutlinePen.Freeze();

        WarnArcPen = new Pen(new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)), 4.5)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        WarnArcPen.Freeze();

        DangerArcPen = new Pen(new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)), 5.5)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        DangerArcPen.Freeze();
    }

    public RadarCanvas()
    {
        _bgCircleBrush = MakeBg(0.55);
    }

    public void SetBackdropOpacity(double opacity)
    {
        _bgCircleBrush = MakeBg(Math.Clamp(opacity * 0.85, 0.12, 0.88));
        InvalidateVisual();
    }

    private static SolidColorBrush MakeBg(double alpha)
    {
        byte a = (byte)Math.Round(Math.Clamp(alpha, 0.0, 0.92) * 255.0);
        var b = new SolidColorBrush(Color.FromArgb(a, 0x0B, 0x0E, 0x14));
        b.Freeze();
        return b;
    }

    public void UpdateRadar(RadarTelemetry radar)
    {
        _proximity = radar.Proximity;
        _nearestBehindMeters = radar.NearestBehindMeters;
        _blips = radar.Blips;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        double size = Math.Min(w, h);
        if (size < 40) return;

        double cx = w * 0.5;
        double cy = h * 0.5;
        double outerR = size * 0.46;
        double innerR = size * 0.24;

        dc.DrawEllipse(_bgCircleBrush, RingPen, new Point(cx, cy), outerR, outerR);
        dc.DrawEllipse(null, RingPen, new Point(cx, cy), innerR, innerR);

        dc.DrawLine(AxisPen, new Point(cx - outerR, cy), new Point(cx + outerR, cy));
        dc.DrawLine(AxisPen, new Point(cx, cy - outerR), new Point(cx, cy + outerR));

        bool leftWarn = _proximity is CarProximityState.CarLeft or CarProximityState.CarLeftRight or CarProximityState.TwoCarsLeft;
        bool rightWarn = _proximity is CarProximityState.CarRight or CarProximityState.CarLeftRight or CarProximityState.TwoCarsRight;
        bool leftSevere = _proximity is CarProximityState.TwoCarsLeft or CarProximityState.CarLeftRight;
        bool rightSevere = _proximity is CarProximityState.TwoCarsRight or CarProximityState.CarLeftRight;

        if (leftWarn)
            DrawArc(dc, cx, cy, outerR - 3.5, 134, 226, leftSevere ? DangerArcPen : WarnArcPen);
        if (rightWarn)
            DrawArc(dc, cx, cy, outerR - 3.5, -46, 46, rightSevere ? DangerArcPen : WarnArcPen);
        if (_nearestBehindMeters < 10.0f && !leftWarn && !rightWarn)
            DrawArc(dc, cx, cy, outerR - 3.5, 55, 125, _nearestBehindMeters < 4.5f ? DangerArcPen : WarnArcPen);

        double pxPerMeter = (outerR - 8.0) / 16.0;
        double carW = 11.0;
        double carH = 23.0;

        for (int i = 0; i < _blips.Count; i++)
        {
            var blip = _blips[i];
            double bx = cx + blip.OffsetX * (pxPerMeter * 2.1);
            double by = cy - blip.OffsetY * pxPerMeter;

            double dx = bx - cx;
            double dy = by - cy;
            if (Math.Sqrt(dx * dx + dy * dy) > outerR - 8) continue;

            var brush = FormatHelpers.GetFrozenBrush(blip.ColorHex, "#38BDF8");
            dc.DrawRoundedRectangle(brush, null, new Rect(bx - carW * 0.5, by - carH * 0.5, carW, carH), 2, 2);
        }

        dc.DrawRoundedRectangle(PlayerCarBrush, PlayerCarOutlinePen,
            new Rect(cx - carW * 0.5, cy - carH * 0.5, carW, carH), 2, 2);
    }

    private static void DrawArc(DrawingContext dc, double cx, double cy, double r, double startDeg, double endDeg, Pen pen)
    {
        double sRad = startDeg * Math.PI / 180.0;
        double eRad = endDeg * Math.PI / 180.0;
        var p1 = new Point(cx + r * Math.Cos(sRad), cy + r * Math.Sin(sRad));
        var p2 = new Point(cx + r * Math.Cos(eRad), cy + r * Math.Sin(eRad));

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(p1, false, false);
            ctx.ArcTo(p2, new Size(r, r), 0, Math.Abs(endDeg - startDeg) > 180, SweepDirection.Clockwise, true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }
}
