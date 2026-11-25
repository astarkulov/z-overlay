using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Media;

namespace ApexHUD.Core.Telemetry;

public static class FormatHelpers
{
    private static readonly ConcurrentDictionary<string, SolidColorBrush> BrushCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static SolidColorBrush GetFrozenBrush(string hex, string fallbackHex = "#F2B830")
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
                var brush = new SolidColorBrush(Color.FromRgb(0xF2, 0xB8, 0x30));
                brush.Freeze();
                return brush;
            }
        });
    }

    public static string FormatLapTime(float seconds, bool threeDecimals = true)
    {
        if (seconds <= 1.0f || seconds > 3600f) return "--:--.---";
        int mins = (int)(seconds / 60f);
        float rem = seconds - mins * 60f;
        if (threeDecimals)
            return mins > 0
                ? string.Format(Inv, "{0}:{1:00.000}", mins, rem)
                : string.Format(Inv, "{0:0.000}", rem);

        return mins > 0
            ? string.Format(Inv, "{0}:{1:00.00}", mins, rem)
            : string.Format(Inv, "{0:0.00}", rem);
    }

    public static string FormatTimeRemaining(double seconds)
    {
        if (seconds <= 0.0) return "00:00";
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1.0
            ? string.Format(Inv, "{0}:{1:00}:{2:00}", (int)ts.TotalHours, ts.Minutes, ts.Seconds)
            : string.Format(Inv, "{0:00}:{1:00}", ts.Minutes, ts.Seconds);
    }

    public static string FormatIRating(int ir)
    {
        if (ir <= 0) return "--";
        return ir >= 1000 ? string.Format(Inv, "{0:0.0}k", ir / 1000.0) : ir.ToString(Inv);
    }

    public static string FormatGap(float sec, bool signed = false)
    {
        if (Math.Abs(sec) < 0.005f) return signed ? "0.0" : "0.0";
        if (signed)
            return sec > 0 ? string.Format(Inv, "+{0:0.0}", sec) : string.Format(Inv, "{0:0.0}", sec);
        return sec >= 60f
            ? string.Format(Inv, "{0}:{1:00.0}", (int)(sec / 60f), sec % 60f)
            : string.Format(Inv, "+{0:0.0}", sec);
    }

    public static string FormatQualiGap(float sec)
    {
        if (Math.Abs(sec) < 0.0005f) return "0.000";
        return string.Format(Inv, "+{0:0.000}", sec);
    }
}
