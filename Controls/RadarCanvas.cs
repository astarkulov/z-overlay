using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ApexHUD.Core.Telemetry;

namespace ApexHUD.Controls;

/// <summary>
/// RaceLab-inspired minimalist proximity radar styled for Z-Overlay (iRacing Native):
/// - Pure floating tapered crosshair + front/rear bumper alignment guides + crisp white player car
/// - Horizontal crimson gradient wings that track side-by-side rivals' exact longitudinal overlap
/// - Soft iRacing Gold lower semicircle glow rising toward the rear bumper as a car closes from behind
/// </summary>
public sealed class RadarCanvas : FrameworkElement
{
    private CarProximityState _proximity = CarProximityState.Clear;
    private float _nearestBehindMeters = 999f;
    private IReadOnlyList<RadarBlip> _blips = Array.Empty<RadarBlip>();

    private static readonly SolidColorBrush PlayerCarBrush;
    private static readonly Pen PlayerCarEdgePen;

    static RadarCanvas()
    {
        PlayerCarBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        PlayerCarBrush.Freeze();

        PlayerCarEdgePen = new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0x0B, 0x0E, 0x14)), 0.9);
        PlayerCarEdgePen.Freeze();
    }

    public void SetBackdropOpacity(double opacity, bool nativeSkin = false)
    {
        // Minimalist floating RaceLab radar has zero heavy disc backdrop — always crystal-clean over track!
        InvalidateVisual();
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
        if (w < 60 || h < 60) return;

        double cx = w * 0.5;
        double cy = h * 0.5;
        double carW = 13.0;
        double carH = 30.0;
        double halfCarW = carW * 0.5;
        double halfCarH = carH * 0.5;

        bool leftWarn = _proximity is CarProximityState.CarLeft or CarProximityState.CarLeftRight or CarProximityState.TwoCarsLeft;
        bool rightWarn = _proximity is CarProximityState.CarRight or CarProximityState.CarLeftRight or CarProximityState.TwoCarsRight;
        bool leftSevere = _proximity is CarProximityState.TwoCarsLeft or CarProximityState.CarLeftRight;
        bool rightSevere = _proximity is CarProximityState.TwoCarsRight or CarProximityState.CarLeftRight;

        // 1) Rear Approaching Dome ("Машина сзади"): soft golden radial half-disc clipped below rival nose
        if (_nearestBehindMeters < 14.0f)
        {
            DrawRearApproachDome(dc, w, h, cx, cy, halfCarH, _nearestBehindMeters);
        }

        // 2) Side-by-Side Gradient Wings ("По умолчанию"): crimson wings tracking exact rival longitudinal offset
        if (leftWarn)
        {
            double wingCy = ResolveSideWingCenterY(cy, isLeft: true);
            DrawSideWing(dc, cx - halfCarW, wingCy, carH, wingW: Math.Min(74.0, cx - halfCarW - 4.0), isLeft: true, severe: leftSevere);
        }
        if (rightWarn)
        {
            double wingCy = ResolveSideWingCenterY(cy, isLeft: false);
            DrawSideWing(dc, cx + halfCarW, wingCy, carH, wingW: Math.Min(74.0, w - (cx + halfCarW) - 4.0), isLeft: false, severe: rightSevere);
        }

        // 3) Tapered Minimalist Crosshair + Front/Rear Bumper Guides (drawn cleanly OVER threat glows)
        double vReach = Math.Min(64.0, h * 0.45);
        double hReach = Math.Min(56.0, w * 0.34);
        double bumperReach = Math.Min(34.0, w * 0.22);

        // Vertical center axis (fades smoothly to transparent at top & bottom tips)
        DrawTaperedVerticalLine(dc, cx, cy - vReach, cy + vReach, thickness: 1.45, peakAlpha: 0xEE);

        // Horizontal center axis
        DrawTaperedHorizontalLine(dc, cx - hReach, cx + hReach, cy, thickness: 1.05, peakAlpha: 0xCC);

        // Front & rear bumper alignment guides (exact RaceLab precision markers)
        DrawTaperedHorizontalLine(dc, cx - bumperReach, cx + bumperReach, cy - halfCarH, thickness: 0.85, peakAlpha: 0x74);
        DrawTaperedHorizontalLine(dc, cx - bumperReach, cx + bumperReach, cy + halfCarH, thickness: 0.85, peakAlpha: 0x74);

        // 4) Pure Crisp White Player Car
        var carRect = new Rect(cx - halfCarW, cy - halfCarH, carW, carH);
        dc.DrawRoundedRectangle(PlayerCarBrush, PlayerCarEdgePen, carRect, 2.2, 2.2);
    }

    private double ResolveSideWingCenterY(double cy, bool isLeft)
    {
        float bestDist = float.MaxValue;
        float bestY = 0f;
        bool found = false;

        for (int i = 0; i < _blips.Count; i++)
        {
            var b = _blips[i];
            bool match = isLeft ? b.OffsetX < -0.5f : b.OffsetX > 0.5f;
            if (!match) continue;
            float absY = Math.Abs(b.OffsetY);
            if (absY < bestDist)
            {
                bestDist = absY;
                bestY = b.OffsetY;
                found = true;
            }
        }

        if (!found) return cy;
        // Map longitudinal offset (+ahead = higher on screen, -behind = lower on screen)
        double shift = Math.Clamp(-bestY * 4.2, -20.0, 20.0);
        return cy + shift;
    }

    private static void DrawSideWing(DrawingContext dc, double innerX, double wingCy, double wingH, double wingW, bool isLeft, bool severe)
    {
        if (wingW <= 6) return;

        // Vibrant RaceLab crimson red (slightly brighter/hotter for 3-wide / 2-cars)
        byte r = severe ? (byte)0xF0 : (byte)0xDC;
        byte g = severe ? (byte)0x26 : (byte)0x24;
        byte b = severe ? (byte)0x32 : (byte)0x24;
        byte peakA = severe ? (byte)0xEB : (byte)0xDC;
        byte midA = severe ? (byte)0xB8 : (byte)0xA4;

        var startPt = isLeft ? new Point(1, 0.5) : new Point(0, 0.5);
        var endPt = isLeft ? new Point(0, 0.5) : new Point(1, 0.5);

        var grad = new LinearGradientBrush
        {
            StartPoint = startPt,
            EndPoint = endPt
        };
        grad.GradientStops.Add(new GradientStop(Color.FromArgb(peakA, r, g, b), 0.00));
        grad.GradientStops.Add(new GradientStop(Color.FromArgb(midA, r, g, b), 0.52));
        grad.GradientStops.Add(new GradientStop(Color.FromArgb(0x38, r, g, b), 0.80));
        grad.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, r, g, b), 1.00));

        double leftX = isLeft ? innerX - wingW : innerX;
        var rect = new Rect(leftX, wingCy - wingH * 0.5, wingW, wingH);
        dc.DrawRectangle(grad, null, rect);
    }

    private static void DrawRearApproachDome(DrawingContext dc, double w, double h, double cx, double cy, double halfCarH, float distMeters)
    {
        // Closeness factor 0 (14m away) -> 1 (<=2m away)
        double closeness = Math.Clamp(1.0 - (distMeters - 1.8) / 12.2, 0.0, 1.0);

        // Flat top cutoff rises smoothly toward the rear bumper as rival approaches
        double maxGapPx = Math.Min(36.0, h * 0.24);
        double minGapPx = 4.0;
        double topY = cy + halfCarH + minGapPx + (1.0 - closeness) * (maxGapPx - minGapPx);

        // Signature iRacing Gold (#F2B830) shifting gently toward warm amber-orange at < 3.5m
        double dangerBlend = Math.Clamp((4.0 - distMeters) / 2.5, 0.0, 1.0);
        byte r = (byte)Math.Round(0xF2 + (0xF9 - 0xF2) * dangerBlend);
        byte g = (byte)Math.Round(0xB8 + (0x5E - 0xB8) * dangerBlend);
        byte b = (byte)Math.Round(0x30 + (0x16 - 0x30) * dangerBlend);

        byte peakA = (byte)Math.Round((0.36 + 0.48 * closeness) * 255.0);
        byte midA = (byte)Math.Round(peakA * 0.56);

        double rx = 52.0 + 20.0 * closeness;
        double ry = 36.0 + 18.0 * closeness;

        var radial = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.0),
            Center = new Point(0.5, 0.0),
            RadiusX = 0.5,
            RadiusY = 1.0
        };
        radial.GradientStops.Add(new GradientStop(Color.FromArgb(peakA, r, g, b), 0.00));
        radial.GradientStops.Add(new GradientStop(Color.FromArgb(midA, r, g, b), 0.48));
        radial.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, r, g, b), 1.00));

        // Clip cleanly at the exact flat horizontal top cutoff line (RaceLab signature!)
        dc.PushClip(new RectangleGeometry(new Rect(0, topY, w, Math.Max(0, h - topY))));
        dc.DrawRectangle(radial, null, new Rect(cx - rx, topY, rx * 2.0, ry));
        dc.Pop();
    }

    private static void DrawTaperedVerticalLine(DrawingContext dc, double x, double y1, double y2, double thickness, byte peakAlpha)
    {
        var grad = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1)
        };
        grad.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.00));
        grad.GradientStops.Add(new GradientStop(Color.FromArgb(peakAlpha, 0xFF, 0xFF, 0xFF), 0.24));
        grad.GradientStops.Add(new GradientStop(Color.FromArgb(peakAlpha, 0xFF, 0xFF, 0xFF), 0.76));
        grad.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.00));
        dc.DrawRectangle(grad, null, new Rect(x - thickness * 0.5, y1, thickness, Math.Max(0, y2 - y1)));
    }

    private static void DrawTaperedHorizontalLine(DrawingContext dc, double x1, double x2, double y, double thickness, byte peakAlpha)
    {
        var grad = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5)
        };
        grad.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.00));
        grad.GradientStops.Add(new GradientStop(Color.FromArgb(peakAlpha, 0xFF, 0xFF, 0xFF), 0.24));
        grad.GradientStops.Add(new GradientStop(Color.FromArgb(peakAlpha, 0xFF, 0xFF, 0xFF), 0.76));
        grad.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.00));
        dc.DrawRectangle(grad, null, new Rect(x1, y - thickness * 0.5, Math.Max(0, x2 - x1), thickness));
    }
}
