using System;
using System.Windows;
using System.Windows.Media;

namespace ApexHUD.Controls;

/// <summary>
/// Crisp minimal motorsport steering wheel angle indicator with amber top-center marker.
/// </summary>
public sealed class SteeringWheelVisual : FrameworkElement
{
    private double _angleDeg;

    private static readonly Pen RimPen;
    private static readonly Pen SpokePen;
    private static readonly Pen CenterMarkPen;
    private static readonly SolidColorBrush HubBrush;
    private static readonly Pen TickPen;

    static SteeringWheelVisual()
    {
        RimPen = new Pen(new SolidColorBrush(Color.FromRgb(0xD5, 0xDE, 0xEC)), 3.2);
        RimPen.Freeze();

        SpokePen = new Pen(new SolidColorBrush(Color.FromRgb(0x6D, 0x7A, 0x91)), 2.2);
        SpokePen.Freeze();

        CenterMarkPen = new Pen(new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)), 4.0);
        CenterMarkPen.Freeze();

        HubBrush = new SolidColorBrush(Color.FromRgb(0x1C, 0x23, 0x31));
        HubBrush.Freeze();

        TickPen = new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), 1.0);
        TickPen.Freeze();
    }

    public void UpdateAngle(float steeringAngleRad)
    {
        // iRacing positive steering angle = turning left (counter-clockwise on screen -> negative WPF rotate)
        double deg = -steeringAngleRad * (180.0 / Math.PI);
        if (Math.Abs(deg - _angleDeg) > 0.3)
        {
            _angleDeg = deg;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        double size = Math.Min(w, h);
        if (size < 16) return;

        double cx = w * 0.5;
        double cy = h * 0.5;
        double r = size * 0.42;

        // Fixed top-dead-center reference tick
        dc.DrawLine(TickPen, new Point(cx, cy - r - 4), new Point(cx, cy - r - 1));

        dc.PushTransform(new RotateTransform(_angleDeg, cx, cy));

        // Outer wheel rim
        dc.DrawEllipse(null, RimPen, new Point(cx, cy), r, r);

        // Minimal GT3 spokes (left, right, bottom)
        dc.DrawLine(SpokePen, new Point(cx - r + 2, cy + 2), new Point(cx + r - 2, cy + 2));
        dc.DrawLine(SpokePen, new Point(cx, cy + 2), new Point(cx, cy + r - 2));

        // Center hub
        dc.DrawEllipse(HubBrush, SpokePen, new Point(cx, cy), r * 0.24, r * 0.24);

        // High-contrast top center stripe
        dc.DrawLine(CenterMarkPen, new Point(cx, cy - r - 1.5), new Point(cx, cy - r + 5.5));

        dc.Pop();
    }
}
