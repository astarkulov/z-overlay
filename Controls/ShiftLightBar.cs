using System;
using System.Windows;
using System.Windows.Media;

namespace ApexHUD.Controls;

/// <summary>
/// 12-segment flat rectangular motorsport LED shift bar with redline blink.
/// </summary>
public sealed class ShiftLightBar : FrameworkElement
{
    private const int Segments = 12;
    private float _shiftPct;
    private bool _blink;

    private static readonly SolidColorBrush OffBrush;
    private static readonly SolidColorBrush GreenBrush;
    private static readonly SolidColorBrush AmberBrush;
    private static readonly SolidColorBrush RedBrush;
    private static readonly SolidColorBrush CyanFlashBrush;

    static ShiftLightBar()
    {
        OffBrush = FreezeBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
        GreenBrush = FreezeBrush(Color.FromRgb(0x1E, 0xD7, 0x60));
        AmberBrush = FreezeBrush(Color.FromRgb(0xF5, 0xA6, 0x23));
        RedBrush = FreezeBrush(Color.FromRgb(0xFF, 0x2E, 0x43));
        CyanFlashBrush = FreezeBrush(Color.FromRgb(0x00, 0xD4, 0xFF));
    }

    private static SolidColorBrush FreezeBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public void UpdateRpm(float rpm, float firstRpm, float shiftRpm, float blinkRpm, float fallbackPct)
    {
        float pct;
        if (blinkRpm > firstRpm + 200f && rpm > 0f)
            pct = Math.Clamp((rpm - firstRpm) / (blinkRpm - firstRpm), 0f, 1.05f);
        else
            pct = Math.Clamp(fallbackPct, 0f, 1f);

        bool blinkNow = rpm >= blinkRpm - 50f || pct >= 0.98f;
        bool blinkPhase = blinkNow && ((Environment.TickCount64 / 70) % 2 == 0);

        if (Math.Abs(pct - _shiftPct) > 0.015f || blinkPhase != _blink)
        {
            _shiftPct = pct;
            _blink = blinkPhase;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 4 || h <= 2) return;

        double gap = 2.0;
        double segW = (w - gap * (Segments - 1)) / Segments;
        int litCount = (int)Math.Round(Math.Clamp(_shiftPct, 0f, 1f) * Segments);

        for (int i = 0; i < Segments; i++)
        {
            Brush brush;
            if (_blink)
            {
                brush = CyanFlashBrush;
            }
            else if (i < litCount)
            {
                brush = i < 5 ? GreenBrush : i < 9 ? AmberBrush : RedBrush;
            }
            else
            {
                brush = OffBrush;
            }

            double x = i * (segW + gap);
            dc.DrawRectangle(brush, null, new Rect(x, 0, Math.Max(1, segW), h));
        }
    }
}
