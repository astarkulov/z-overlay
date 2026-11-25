using System;
using System.Collections.Concurrent;
using System.Windows.Media;

namespace ApexHUD.Core.Telemetry;

public static class FormatHelpers
{
    private static readonly ConcurrentDictionary<string, SolidColorBrush> BrushCache = new(StringComparer.OrdinalIgnoreCase);

    public static SolidColorBrush GetFrozenBrush(string hex, string fallbackHex = "#F5A623")
    {
        string key = string.IsNullOrWhiteSpace(hex) ? fallbackHex : hex;
        return BrushCache.GetOrAdd(key, static k =>
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(k);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch
            {
                var brush = new SolidColorBrush(Color.FromRgb(0xF5, 0xA6, 0x23));
                brush.Freeze();
                return brush;
            }
        });
    }

    public static string FormatLapTime(float seconds)
    {
        if (seconds <= 0.1f || seconds > 3600f) return "--:--.--";
        int mins = (int)(seconds / 60f);
        float rem = seconds - mins * 60f;
        return mins > 0 ? $"{mins}:{rem:00.00}" : $"{rem:0.00}";
    }

    public static string FormatTimeRemaining(double seconds)
    {
        if (seconds <= 0.0) return "00:00";
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1.0
            ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
            : $"{ts.Minutes:00}:{ts.Seconds:00}";
    }

    public static string FormatIRating(int ir)
    {
        if (ir <= 0) return "--";
        return ir >= 1000 ? $"{ir / 1000.0:0.0}k" : ir.ToString();
    }

    public static string FormatGap(float sec, bool signed = false)
    {
        if (Math.Abs(sec) < 0.005f) return signed ? " 0.0" : "0.0";
        if (signed)
            return sec > 0 ? $"+{sec:0.0}" : $"{sec:0.0}";
        return sec >= 60f ? $"{(int)(sec / 60f)}:{sec % 60f:00.0}" : $"+{sec:0.0}";
    }
}
