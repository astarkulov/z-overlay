using System;
using System.Windows;
using System.Windows.Media;

namespace ApexHUD.Controls;

/// <summary>
/// High-performance 60 FPS rolling input trace (Throttle / Brake / Clutch / ABS pulse bands)
/// with adjustable translucent backdrop and configurable time window (1.0 .. 12.0 seconds).
/// </summary>
public sealed class InputGraphCanvas : FrameworkElement
{
    private const int MaxCapacity = 720; // Up to 12 seconds at 60Hz

    private readonly float[] _throttle = new float[MaxCapacity];
    private readonly float[] _brake = new float[MaxCapacity];
    private readonly float[] _clutch = new float[MaxCapacity];
    private readonly bool[] _abs = new bool[MaxCapacity];
    private int _head;
    private double _windowSeconds = 3.5;

    private SolidColorBrush _bgBrush;
    private static readonly Pen GridPen;
    private static readonly Pen ThrottlePen;
    private static readonly Pen BrakePen;
    private static readonly Pen ClutchPen;
    private static readonly SolidColorBrush AbsBandBrush;

    static InputGraphCanvas()
    {
        GridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), 1.0);
        GridPen.Freeze();

        ThrottlePen = new Pen(new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)), 2.1)
        {
            LineJoin = PenLineJoin.Round
        };
        ThrottlePen.Freeze();

        BrakePen = new Pen(new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)), 2.2)
        {
            LineJoin = PenLineJoin.Round
        };
        BrakePen.Freeze();

        ClutchPen = new Pen(new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)), 1.5)
        {
            LineJoin = PenLineJoin.Round
        };
        ClutchPen.Freeze();

        AbsBandBrush = new SolidColorBrush(Color.FromArgb(0x42, 0xF5, 0x9E, 0x0B));
        AbsBandBrush.Freeze();
    }

    public InputGraphCanvas()
    {
        _bgBrush = MakeBg(0.45);
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    public bool ShowClutch { get; set; } = true;

    public double WindowSeconds
    {
        get => _windowSeconds;
        set
        {
            double clamped = Math.Clamp(value, 1.0, 12.0);
            if (Math.Abs(clamped - _windowSeconds) > 0.01)
            {
                _windowSeconds = clamped;
                InvalidateVisual();
            }
        }
    }

    public void SetBackdropOpacity(double opacity, bool nativeSkin = false)
    {
        byte r = (byte)(nativeSkin ? 0x12 : 0x08);
        byte g = (byte)(nativeSkin ? 0x14 : 0x0B);
        byte b = (byte)(nativeSkin ? 0x1A : 0x10);
        byte a = (byte)Math.Round(Math.Clamp(opacity * 0.72, 0.0, 0.92) * 255.0);
        var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        br.Freeze();
        _bgBrush = br;
        InvalidateVisual();
    }

    private static SolidColorBrush MakeBg(double alpha)
    {
        byte a = (byte)Math.Round(Math.Clamp(alpha, 0.0, 0.92) * 255.0);
        var b = new SolidColorBrush(Color.FromArgb(a, 0x08, 0x0B, 0x10));
        b.Freeze();
        return b;
    }

    public void PushSample(float throttle, float brake, float clutch, bool absActive)
    {
        _throttle[_head] = Math.Clamp(throttle, 0f, 1f);
        _brake[_head] = Math.Clamp(brake, 0f, 1f);
        _clutch[_head] = Math.Clamp(clutch, 0f, 1f);
        _abs[_head] = absActive;
        _head = (_head + 1) % MaxCapacity;
        InvalidateVisual();
    }

    public void Clear()
    {
        Array.Clear(_throttle);
        Array.Clear(_brake);
        Array.Clear(_clutch);
        Array.Clear(_abs);
        _head = 0;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 2 || h <= 2) return;

        dc.DrawRoundedRectangle(_bgBrush, null, new Rect(0, 0, w, h), 3, 3);

        for (int i = 1; i <= 3; i++)
        {
            double gy = Math.Floor(h * (i * 0.25)) + 0.5;
            dc.DrawLine(GridPen, new Point(0, gy), new Point(w, gy));
        }

        int count = Math.Clamp((int)Math.Round(_windowSeconds * 60.0), 60, MaxCapacity);
        int startOffset = (_head - count + MaxCapacity) % MaxCapacity;

        double padY = 5.0;
        double usableH = Math.Max(4.0, h - padY * 2.0);
        double stepX = w / (count - 1);

        for (int i = 0; i < count; i++)
        {
            int idx = (startOffset + i) % MaxCapacity;
            if (_abs[idx])
            {
                double x = i * stepX;
                dc.DrawRectangle(AbsBandBrush, null, new Rect(x, 0, Math.Max(1.6, stepX + 0.5), h));
            }
        }

        if (ShowClutch)
            DrawTrace(dc, _clutch, ClutchPen, startOffset, count, h, padY, usableH, stepX, skipIfAllZero: true);
        DrawTrace(dc, _throttle, ThrottlePen, startOffset, count, h, padY, usableH, stepX, skipIfAllZero: false);
        DrawTrace(dc, _brake, BrakePen, startOffset, count, h, padY, usableH, stepX, skipIfAllZero: false);
    }

    private void DrawTrace(
        DrawingContext dc,
        float[] buffer,
        Pen pen,
        int startOffset,
        int count,
        double h,
        double padY,
        double usableH,
        double stepX,
        bool skipIfAllZero)
    {
        if (skipIfAllZero)
        {
            bool any = false;
            for (int i = 0; i < count; i++)
            {
                if (buffer[(startOffset + i) % MaxCapacity] > 0.01f) { any = true; break; }
            }
            if (!any) return;
        }

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (int i = 0; i < count; i++)
            {
                int idx = (startOffset + i) % MaxCapacity;
                double x = i * stepX;
                double y = (h - padY) - buffer[idx] * usableH;
                if (i == 0)
                    ctx.BeginFigure(new Point(x, y), false, false);
                else
                    ctx.LineTo(new Point(x, y), true, false);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }
}
