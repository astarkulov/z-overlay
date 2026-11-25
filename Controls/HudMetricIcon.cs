using System;
using System.Windows;
using System.Windows.Media;
using ApexHUD.Core.Telemetry;

namespace ApexHUD.Controls;

public enum HudIconKind
{
    Trophy,
    Warning,
    TrackTemp,
    AirTemp,
    Raindrop,
    Flag,
    LapLoop,
    Stopwatch,
    CarsGrid,
    Wrench,
    OutLap,
    FastestBolt,
    TireWheel,
    RatingBar
}

/// <summary>
/// Razor-sharp flat vector micro-icons (11-13px) for HUD headers, badges, and columns.
/// Scales cleanly to 250% with zero emoji font artifacts.
/// </summary>
public sealed class HudMetricIcon : FrameworkElement
{
    private HudIconKind _kind;
    private Brush _tint = FormatHelpers.GetFrozenBrush("#94A3B8");
    private Pen _pen;

    public HudMetricIcon(HudIconKind kind, string colorHex = "#94A3B8", double size = 12)
    {
        _kind = kind;
        _tint = FormatHelpers.GetFrozenBrush(colorHex);
        _pen = MakePen(_tint, 1.3);
        Width = size;
        Height = size;
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Center;
    }

    public HudIconKind Kind
    {
        get => _kind;
        set { if (_kind != value) { _kind = value; InvalidateVisual(); } }
    }

    public Brush Tint
    {
        get => _tint;
        set
        {
            if (!ReferenceEquals(_tint, value))
            {
                _tint = value;
                _pen = MakePen(value, 1.3);
                InvalidateVisual();
            }
        }
    }

    private static Pen MakePen(Brush b, double t)
    {
        var p = new Pen(b, t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        p.Freeze();
        return p;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double s = Math.Min(ActualWidth > 1 ? ActualWidth : Width, ActualHeight > 1 ? ActualHeight : Height);
        double u = s / 12.0; // normalized 12x12 design grid

        switch (_kind)
        {
            case HudIconKind.Trophy:
                // Cup body + stem + base
                var cup = new StreamGeometry();
                using (var c = cup.Open())
                {
                    c.BeginFigure(new Point(2.8 * u, 1.8 * u), true, true);
                    c.LineTo(new Point(9.2 * u, 1.8 * u), true, false);
                    c.LineTo(new Point(8.2 * u, 6.2 * u), true, false);
                    c.QuadraticBezierTo(new Point(6.0 * u, 8.2 * u), new Point(3.8 * u, 6.2 * u), true, false);
                }
                cup.Freeze();
                dc.DrawGeometry(_tint, null, cup);
                dc.DrawLine(_pen, new Point(6 * u, 7.2 * u), new Point(6 * u, 10.2 * u));
                dc.DrawLine(_pen, new Point(3.8 * u, 10.4 * u), new Point(8.2 * u, 10.4 * u));
                dc.DrawLine(_pen, new Point(1.6 * u, 3.0 * u), new Point(2.8 * u, 4.6 * u));
                dc.DrawLine(_pen, new Point(10.4 * u, 3.0 * u), new Point(9.2 * u, 4.6 * u));
                break;

            case HudIconKind.Warning:
                var tri = new StreamGeometry();
                using (var c = tri.Open())
                {
                    c.BeginFigure(new Point(6 * u, 1.4 * u), false, true);
                    c.LineTo(new Point(10.8 * u, 10.2 * u), true, false);
                    c.LineTo(new Point(1.2 * u, 10.2 * u), true, false);
                }
                tri.Freeze();
                dc.DrawGeometry(null, _pen, tri);
                dc.DrawLine(_pen, new Point(6 * u, 4.5 * u), new Point(6 * u, 7.2 * u));
                dc.DrawEllipse(_tint, null, new Point(6 * u, 8.8 * u), 0.75 * u, 0.75 * u);
                break;

            case HudIconKind.TrackTemp:
                // Thermometer + road bar underneath
                dc.DrawLine(_pen, new Point(4.6 * u, 2.0 * u), new Point(4.6 * u, 7.6 * u));
                dc.DrawEllipse(_tint, null, new Point(4.6 * u, 8.6 * u), 2.1 * u, 2.1 * u);
                dc.DrawLine(_pen, new Point(7.6 * u, 3.2 * u), new Point(10.2 * u, 3.2 * u));
                dc.DrawLine(_pen, new Point(7.6 * u, 5.6 * u), new Point(9.4 * u, 5.6 * u));
                dc.DrawLine(_pen, new Point(7.6 * u, 8.0 * u), new Point(10.2 * u, 8.0 * u));
                break;

            case HudIconKind.AirTemp:
                // Crisp airflow / wind lines
                dc.DrawLine(_pen, new Point(1.5 * u, 3.6 * u), new Point(8.6 * u, 3.6 * u));
                dc.DrawLine(_pen, new Point(2.5 * u, 6.2 * u), new Point(10.4 * u, 6.2 * u));
                dc.DrawLine(_pen, new Point(1.5 * u, 8.8 * u), new Point(7.4 * u, 8.8 * u));
                break;

            case HudIconKind.Raindrop:
                var drop = new StreamGeometry();
                using (var c = drop.Open())
                {
                    c.BeginFigure(new Point(6 * u, 1.4 * u), true, true);
                    c.QuadraticBezierTo(new Point(10.2 * u, 6.8 * u), new Point(6 * u, 10.6 * u), true, false);
                    c.QuadraticBezierTo(new Point(1.8 * u, 6.8 * u), new Point(6 * u, 1.4 * u), true, false);
                }
                drop.Freeze();
                dc.DrawGeometry(_tint, null, drop);
                break;

            case HudIconKind.Flag:
                dc.DrawLine(_pen, new Point(2.6 * u, 1.6 * u), new Point(2.6 * u, 10.6 * u));
                var flag = new StreamGeometry();
                using (var c = flag.Open())
                {
                    c.BeginFigure(new Point(3.0 * u, 1.8 * u), true, true);
                    c.LineTo(new Point(10.4 * u, 2.6 * u), true, false);
                    c.LineTo(new Point(9.4 * u, 6.8 * u), true, false);
                    c.LineTo(new Point(3.0 * u, 6.2 * u), true, false);
                }
                flag.Freeze();
                dc.DrawGeometry(_tint, null, flag);
                break;

            case HudIconKind.LapLoop:
                dc.DrawRoundedRectangle(null, _pen, new Rect(1.6 * u, 3.0 * u, 8.8 * u, 6.0 * u), 2.5 * u, 2.5 * u);
                dc.DrawLine(_pen, new Point(7.2 * u, 1.5 * u), new Point(9.0 * u, 3.0 * u));
                dc.DrawLine(_pen, new Point(7.2 * u, 4.5 * u), new Point(9.0 * u, 3.0 * u));
                break;

            case HudIconKind.Stopwatch:
                dc.DrawLine(_pen, new Point(4.6 * u, 1.3 * u), new Point(7.4 * u, 1.3 * u));
                dc.DrawEllipse(null, _pen, new Point(6 * u, 6.6 * u), 4.1 * u, 4.1 * u);
                dc.DrawLine(_pen, new Point(6 * u, 6.6 * u), new Point(6 * u, 4.2 * u));
                dc.DrawLine(_pen, new Point(6 * u, 6.6 * u), new Point(8.0 * u, 7.6 * u));
                break;

            case HudIconKind.CarsGrid:
                dc.DrawRoundedRectangle(_tint, null, new Rect(1.8 * u, 2.2 * u, 3.4 * u, 7.4 * u), 1.0 * u, 1.0 * u);
                dc.DrawRoundedRectangle(_tint, null, new Rect(6.8 * u, 3.6 * u, 3.4 * u, 6.8 * u), 1.0 * u, 1.0 * u);
                break;

            case HudIconKind.Wrench:
                dc.DrawLine(_pen, new Point(2.4 * u, 9.6 * u), new Point(7.4 * u, 4.6 * u));
                dc.DrawEllipse(null, _pen, new Point(8.6 * u, 3.4 * u), 2.1 * u, 2.1 * u);
                break;

            case HudIconKind.OutLap:
                // Cold/exit arrow icon
                dc.DrawLine(_pen, new Point(2.2 * u, 6.0 * u), new Point(9.8 * u, 6.0 * u));
                dc.DrawLine(_pen, new Point(6.6 * u, 2.8 * u), new Point(9.8 * u, 6.0 * u));
                dc.DrawLine(_pen, new Point(6.6 * u, 9.2 * u), new Point(9.8 * u, 6.0 * u));
                break;

            case HudIconKind.FastestBolt:
                var bolt = new StreamGeometry();
                using (var c = bolt.Open())
                {
                    c.BeginFigure(new Point(7.0 * u, 1.2 * u), true, true);
                    c.LineTo(new Point(2.6 * u, 6.6 * u), true, false);
                    c.LineTo(new Point(5.8 * u, 6.6 * u), true, false);
                    c.LineTo(new Point(4.8 * u, 10.8 * u), true, false);
                    c.LineTo(new Point(9.4 * u, 5.2 * u), true, false);
                    c.LineTo(new Point(6.2 * u, 5.2 * u), true, false);
                }
                bolt.Freeze();
                dc.DrawGeometry(_tint, null, bolt);
                break;

            case HudIconKind.TireWheel:
                dc.DrawEllipse(null, _pen, new Point(6 * u, 6 * u), 4.2 * u, 4.2 * u);
                dc.DrawEllipse(_tint, null, new Point(6 * u, 6 * u), 1.5 * u, 1.5 * u);
                break;

            case HudIconKind.RatingBar:
                dc.DrawRectangle(_tint, null, new Rect(1.8 * u, 7.2 * u, 2.0 * u, 3.2 * u));
                dc.DrawRectangle(_tint, null, new Rect(5.0 * u, 4.6 * u, 2.0 * u, 5.8 * u));
                dc.DrawRectangle(_tint, null, new Rect(8.2 * u, 2.0 * u, 2.0 * u, 8.4 * u));
                break;
        }
    }
}
