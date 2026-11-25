using System;
using System.Windows;
using System.Windows.Media;
using ApexHUD.Core.Telemetry;

namespace ApexHUD.Controls;

/// <summary>
/// Razor-sharp vector car manufacturer icon for Relative and Standings rows.
/// Zero external image files, zero per-frame allocations, crisp at any scale up to 250%.
/// </summary>
public sealed class CarBrandIcon : FrameworkElement
{
    private CarBrand _brand = CarBrand.Unknown;

    private static readonly SolidColorBrush WhiteBrush = Freeze(Color.FromRgb(0xF8, 0xFA, 0xFC));
    private static readonly SolidColorBrush SilverBrush = Freeze(Color.FromRgb(0xD4, 0xDC, 0xE8));
    private static readonly SolidColorBrush DarkBrush = Freeze(Color.FromRgb(0x0E, 0x11, 0x17));
    private static readonly SolidColorBrush SlateBrush = Freeze(Color.FromRgb(0x33, 0x41, 0x55));

    private static readonly SolidColorBrush FerrariYellow = Freeze(Color.FromRgb(0xFA, 0xCC, 0x15));
    private static readonly SolidColorBrush FerrariRed = Freeze(Color.FromRgb(0xDC, 0x26, 0x26));
    private static readonly SolidColorBrush FerrariGreen = Freeze(Color.FromRgb(0x16, 0xA3, 0x4A));

    private static readonly SolidColorBrush PorscheGold = Freeze(Color.FromRgb(0xE0, 0xAC, 0x3E));
    private static readonly SolidColorBrush PorscheRed = Freeze(Color.FromRgb(0xC8, 0x1E, 0x2B));

    private static readonly SolidColorBrush BmwBlue = Freeze(Color.FromRgb(0x1D, 0x8F, 0xE1));
    private static readonly SolidColorBrush McLarenOrange = Freeze(Color.FromRgb(0xFF, 0x6B, 0x00));
    private static readonly SolidColorBrush FordBlue = Freeze(Color.FromRgb(0x1D, 0x4E, 0xD8));
    private static readonly SolidColorBrush ChevyGold = Freeze(Color.FromRgb(0xEA, 0xB3, 0x08));
    private static readonly SolidColorBrush AstonGreen = Freeze(Color.FromRgb(0x0F, 0x76, 0x6E));
    private static readonly SolidColorBrush DallaraBlue = Freeze(Color.FromRgb(0x02, 0x84, 0xC7));
    private static readonly SolidColorBrush LotusGreen = Freeze(Color.FromRgb(0x15, 0x80, 0x3D));

    private static readonly Pen SilverPen = FreezePen(SilverBrush, 1.3);
    private static readonly Pen ThinSilverPen = FreezePen(SilverBrush, 1.0);
    private static readonly Pen WhitePen = FreezePen(WhiteBrush, 1.25);
    private static readonly Pen GoldPen = FreezePen(PorscheGold, 1.2);
    private static readonly Pen DarkPen = FreezePen(DarkBrush, 1.0);
    private static readonly Pen RedPen = FreezePen(FerrariRed, 1.3);

    private static SolidColorBrush Freeze(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    private static Pen FreezePen(Brush b, double t) { var p = new Pen(b, t) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; p.Freeze(); return p; }

    public CarBrandIcon()
    {
        Width = 18;
        Height = 16;
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Center;
    }

    public CarBrand Brand
    {
        get => _brand;
        set
        {
            if (_brand != value)
            {
                _brand = value;
                InvalidateVisual();
            }
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth > 2 ? ActualWidth : 18;
        double h = ActualHeight > 2 ? ActualHeight : 16;
        double cx = w * 0.5;
        double cy = h * 0.5;

        switch (_brand)
        {
            case CarBrand.Porsche:
                DrawPorsche(dc, cx, cy);
                break;
            case CarBrand.Ferrari:
                DrawFerrari(dc, cx, cy);
                break;
            case CarBrand.Bmw:
                DrawBmw(dc, cx, cy);
                break;
            case CarBrand.Mercedes:
                DrawMercedes(dc, cx, cy);
                break;
            case CarBrand.Audi:
                DrawAudi(dc, cx, cy);
                break;
            case CarBrand.McLaren:
                DrawMcLaren(dc, cx, cy);
                break;
            case CarBrand.Lamborghini:
                DrawLamborghini(dc, cx, cy);
                break;
            case CarBrand.Ford:
                DrawFord(dc, cx, cy);
                break;
            case CarBrand.Chevrolet:
                DrawChevrolet(dc, cx, cy);
                break;
            case CarBrand.AstonMartin:
                DrawAstonMartin(dc, cx, cy);
                break;
            case CarBrand.Acura:
                DrawAcura(dc, cx, cy);
                break;
            case CarBrand.Honda:
                DrawHonda(dc, cx, cy);
                break;
            case CarBrand.Cadillac:
                DrawCadillac(dc, cx, cy);
                break;
            case CarBrand.Toyota:
                DrawToyota(dc, cx, cy);
                break;
            case CarBrand.Mazda:
                DrawMazda(dc, cx, cy);
                break;
            case CarBrand.Dallara:
                DrawDallara(dc, cx, cy);
                break;
            case CarBrand.Hyundai:
                DrawHyundai(dc, cx, cy);
                break;
            case CarBrand.Lotus:
                DrawLotus(dc, cx, cy);
                break;
            case CarBrand.Formula:
                DrawFormula(dc, cx, cy);
                break;
            default:
                DrawFallback(dc, cx, cy);
                break;
        }
    }

    private static StreamGeometry MakeShield(double cx, double cy, double hw, double hh)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(cx - hw, cy - hh), true, true);
            c.LineTo(new Point(cx + hw, cy - hh), true, false);
            c.LineTo(new Point(cx + hw, cy + hh * 0.2), true, false);
            c.QuadraticBezierTo(new Point(cx + hw * 0.6, cy + hh * 0.85), new Point(cx, cy + hh), true, false);
            c.QuadraticBezierTo(new Point(cx - hw * 0.6, cy + hh * 0.85), new Point(cx - hw, cy + hh * 0.2), true, false);
        }
        g.Freeze();
        return g;
    }

    private static void DrawPorsche(DrawingContext dc, double cx, double cy)
    {
        var shield = MakeShield(cx, cy, 5.5, 6.8);
        dc.DrawGeometry(PorscheGold, GoldPen, shield);
        // 4 iconic crest quadrants (top-left/bottom-right gold, top-right/bottom-left red+black stripes)
        dc.DrawRectangle(PorscheRed, null, new Rect(cx + 0.3, cy - 4.5, 4.4, 4.2));
        dc.DrawRectangle(DarkBrush, null, new Rect(cx + 0.3, cy - 2.9, 4.4, 1.1));
        dc.DrawRectangle(PorscheRed, null, new Rect(cx - 4.7, cy + 0.3, 4.4, 3.6));
        dc.DrawRectangle(DarkBrush, null, new Rect(cx - 4.7, cy + 1.6, 4.4, 1.1));
        // Center tiny black crest dot
        dc.DrawRectangle(DarkBrush, null, new Rect(cx - 1.3, cy - 1.5, 2.6, 3.0));
    }

    private static void DrawFerrari(DrawingContext dc, double cx, double cy)
    {
        var shield = MakeShield(cx, cy, 5.5, 6.8);
        dc.DrawGeometry(FerrariYellow, DarkPen, shield);
        // Italian tricolor top bar
        dc.DrawRectangle(FerrariGreen, null, new Rect(cx - 4.8, cy - 6.0, 3.2, 1.7));
        dc.DrawRectangle(WhiteBrush, null, new Rect(cx - 1.6, cy - 6.0, 3.2, 1.7));
        dc.DrawRectangle(FerrariRed, null, new Rect(cx + 1.6, cy - 6.0, 3.2, 1.7));
        // Stylized prancing horse silhouette
        var horse = new StreamGeometry();
        using (var c = horse.Open())
        {
            c.BeginFigure(new Point(cx - 2.2, cy + 4.0), true, true);
            c.LineTo(new Point(cx - 0.5, cy + 0.6), true, false);
            c.LineTo(new Point(cx - 2.3, cy - 1.2), true, false);
            c.LineTo(new Point(cx + 0.4, cy - 3.2), true, false);
            c.LineTo(new Point(cx + 2.3, cy - 2.1), true, false);
            c.LineTo(new Point(cx + 1.0, cy + 0.5), true, false);
            c.LineTo(new Point(cx + 2.2, cy + 3.8), true, false);
            c.LineTo(new Point(cx + 0.2, cy + 2.1), true, false);
        }
        horse.Freeze();
        dc.DrawGeometry(DarkBrush, null, horse);
    }

    private static void DrawBmw(DrawingContext dc, double cx, double cy)
    {
        double r = 6.5;
        dc.DrawEllipse(DarkBrush, SilverPen, new Point(cx, cy), r, r);
        double ir = 4.2;
        dc.DrawEllipse(WhiteBrush, null, new Point(cx, cy), ir, ir);

        // Top-left & bottom-right BMW blue quadrants
        DrawPieQuarter(dc, cx, cy, ir, 180, 270, BmwBlue);
        DrawPieQuarter(dc, cx, cy, ir, 0, 90, BmwBlue);
    }

    private static void DrawPieQuarter(DrawingContext dc, double cx, double cy, double r, double startDeg, double endDeg, Brush fill)
    {
        double s = startDeg * Math.PI / 180.0;
        double e = endDeg * Math.PI / 180.0;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(cx, cy), true, true);
            c.LineTo(new Point(cx + r * Math.Cos(s), cy + r * Math.Sin(s)), false, false);
            c.ArcTo(new Point(cx + r * Math.Cos(e), cy + r * Math.Sin(e)), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
        }
        g.Freeze();
        dc.DrawGeometry(fill, null, g);
    }

    private static void DrawMercedes(DrawingContext dc, double cx, double cy)
    {
        double r = 6.4;
        dc.DrawEllipse(null, SilverPen, new Point(cx, cy), r, r);
        // 3-pointed star
        for (int i = 0; i < 3; i++)
        {
            double a = (-90 + i * 120) * Math.PI / 180.0;
            dc.DrawLine(SilverPen, new Point(cx, cy), new Point(cx + (r - 0.8) * Math.Cos(a), cy + (r - 0.8) * Math.Sin(a)));
        }
    }

    private static void DrawAudi(DrawingContext dc, double cx, double cy)
    {
        double r = 2.6;
        double step = 3.5;
        double startX = cx - step * 1.5;
        for (int i = 0; i < 4; i++)
            dc.DrawEllipse(null, ThinSilverPen, new Point(startX + i * step, cy), r, r);
    }

    private static void DrawMcLaren(DrawingContext dc, double cx, double cy)
    {
        // Iconic papaya/red speedmark swoosh
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(cx - 7.0, cy + 2.8), true, true);
            c.QuadraticBezierTo(new Point(cx + 0.5, cy - 6.2), new Point(cx + 7.2, cy - 2.6), true, false);
            c.QuadraticBezierTo(new Point(cx + 3.6, cy + 0.8), new Point(cx + 1.0, cy + 2.6), true, false);
            c.QuadraticBezierTo(new Point(cx + 1.8, cy - 1.2), new Point(cx - 7.0, cy + 2.8), true, false);
        }
        g.Freeze();
        dc.DrawGeometry(McLarenOrange, null, g);
    }

    private static void DrawLamborghini(DrawingContext dc, double cx, double cy)
    {
        var shield = MakeShield(cx, cy, 5.5, 6.8);
        dc.DrawGeometry(DarkBrush, GoldPen, shield);
        // Stylized gold bull mark
        var bull = new StreamGeometry();
        using (var c = bull.Open())
        {
            c.BeginFigure(new Point(cx - 3.0, cy + 2.5), true, true);
            c.LineTo(new Point(cx - 1.6, cy - 1.5), true, false);
            c.LineTo(new Point(cx + 1.8, cy - 2.4), true, false);
            c.LineTo(new Point(cx + 3.2, cy - 0.4), true, false);
            c.LineTo(new Point(cx + 1.5, cy + 1.2), true, false);
            c.LineTo(new Point(cx + 2.4, cy + 3.4), true, false);
            c.LineTo(new Point(cx - 0.6, cy + 1.4), true, false);
        }
        bull.Freeze();
        dc.DrawGeometry(PorscheGold, null, bull);
    }

    private static void DrawFord(DrawingContext dc, double cx, double cy)
    {
        dc.DrawEllipse(FordBlue, ThinSilverPen, new Point(cx, cy), 7.4, 4.4);
        dc.DrawLine(WhitePen, new Point(cx - 4.0, cy + 0.2), new Point(cx + 4.0, cy - 0.6));
        dc.DrawLine(WhitePen, new Point(cx - 3.2, cy - 1.6), new Point(cx + 2.0, cy - 1.8));
    }

    private static void DrawChevrolet(DrawingContext dc, double cx, double cy)
    {
        // Classic gold bowtie
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(cx - 7.0, cy - 1.8), true, true);
            c.LineTo(new Point(cx - 2.4, cy - 1.8), true, false);
            c.LineTo(new Point(cx - 2.0, cy - 3.2), true, false);
            c.LineTo(new Point(cx + 2.0, cy - 3.2), true, false);
            c.LineTo(new Point(cx + 2.4, cy - 1.8), true, false);
            c.LineTo(new Point(cx + 7.4, cy - 1.8), true, false);
            c.LineTo(new Point(cx + 6.4, cy + 1.8), true, false);
            c.LineTo(new Point(cx + 2.4, cy + 1.8), true, false);
            c.LineTo(new Point(cx + 2.0, cy + 3.2), true, false);
            c.LineTo(new Point(cx - 2.0, cy + 3.2), true, false);
            c.LineTo(new Point(cx - 2.4, cy + 1.8), true, false);
            c.LineTo(new Point(cx - 7.6, cy + 1.8), true, false);
        }
        g.Freeze();
        dc.DrawGeometry(ChevyGold, ThinSilverPen, g);
    }

    private static void DrawAstonMartin(DrawingContext dc, double cx, double cy)
    {
        var wings = new StreamGeometry();
        using (var c = wings.Open())
        {
            c.BeginFigure(new Point(cx - 7.8, cy - 2.4), true, true);
            c.LineTo(new Point(cx + 7.8, cy - 2.4), true, false);
            c.LineTo(new Point(cx + 5.2, cy + 2.6), true, false);
            c.LineTo(new Point(cx - 5.2, cy + 2.6), true, false);
        }
        wings.Freeze();
        dc.DrawGeometry(SilverBrush, null, wings);
        dc.DrawRectangle(AstonGreen, null, new Rect(cx - 4.4, cy - 1.8, 8.8, 2.2));
    }

    private static void DrawAcura(DrawingContext dc, double cx, double cy)
    {
        dc.DrawEllipse(null, ThinSilverPen, new Point(cx, cy), 6.4, 5.6);
        dc.DrawLine(SilverPen, new Point(cx, cy - 4.2), new Point(cx - 3.2, cy + 3.6));
        dc.DrawLine(SilverPen, new Point(cx, cy - 4.2), new Point(cx + 3.2, cy + 3.6));
        dc.DrawLine(ThinSilverPen, new Point(cx - 1.8, cy + 0.8), new Point(cx + 1.8, cy + 0.8));
    }

    private static void DrawHonda(DrawingContext dc, double cx, double cy)
    {
        dc.DrawRoundedRectangle(null, ThinSilverPen, new Rect(cx - 6.2, cy - 5.2, 12.4, 10.4), 2.2, 2.2);
        dc.DrawLine(SilverPen, new Point(cx - 3.2, cy - 3.2), new Point(cx - 2.4, cy + 3.2));
        dc.DrawLine(SilverPen, new Point(cx + 3.2, cy - 3.2), new Point(cx + 2.4, cy + 3.2));
        dc.DrawLine(SilverPen, new Point(cx - 2.7, cy + 0.3), new Point(cx + 2.7, cy + 0.3));
    }

    private static void DrawCadillac(DrawingContext dc, double cx, double cy)
    {
        var crest = new StreamGeometry();
        using (var c = crest.Open())
        {
            c.BeginFigure(new Point(cx - 7.0, cy - 3.4), true, true);
            c.LineTo(new Point(cx + 7.0, cy - 3.4), true, false);
            c.LineTo(new Point(cx + 5.2, cy + 2.2), true, false);
            c.LineTo(new Point(cx, cy + 4.4), true, false);
            c.LineTo(new Point(cx - 5.2, cy + 2.2), true, false);
        }
        crest.Freeze();
        dc.DrawGeometry(DarkBrush, ThinSilverPen, crest);
        dc.DrawRectangle(FerrariRed, null, new Rect(cx - 4.8, cy - 2.4, 3.2, 3.0));
        dc.DrawRectangle(PorscheGold, null, new Rect(cx - 1.6, cy - 2.4, 3.2, 3.0));
        dc.DrawRectangle(FordBlue, null, new Rect(cx + 1.6, cy - 2.4, 3.2, 3.0));
    }

    private static void DrawToyota(DrawingContext dc, double cx, double cy)
    {
        dc.DrawEllipse(null, RedPen, new Point(cx, cy), 7.0, 4.8);
        dc.DrawEllipse(null, ThinSilverPen, new Point(cx, cy), 2.1, 4.4);
        dc.DrawEllipse(null, ThinSilverPen, new Point(cx, cy - 1.6), 4.6, 1.5);
    }

    private static void DrawMazda(DrawingContext dc, double cx, double cy)
    {
        dc.DrawEllipse(null, ThinSilverPen, new Point(cx, cy), 6.8, 5.2);
        dc.DrawLine(SilverPen, new Point(cx - 4.8, cy - 2.2), new Point(cx, cy + 2.2));
        dc.DrawLine(SilverPen, new Point(cx + 4.8, cy - 2.2), new Point(cx, cy + 2.2));
    }

    private static void DrawDallara(DrawingContext dc, double cx, double cy)
    {
        var shield = MakeShield(cx, cy, 5.6, 6.5);
        dc.DrawGeometry(DallaraBlue, ThinSilverPen, shield);
        dc.DrawRectangle(FerrariYellow, null, new Rect(cx - 4.4, cy - 2.2, 8.8, 2.2));
        dc.DrawRectangle(WhiteBrush, null, new Rect(cx - 2.5, cy + 1.0, 5.0, 1.6));
    }

    private static void DrawHyundai(DrawingContext dc, double cx, double cy)
    {
        dc.DrawEllipse(null, ThinSilverPen, new Point(cx, cy), 7.0, 4.6);
        dc.DrawLine(SilverPen, new Point(cx - 2.2, cy - 2.8), new Point(cx - 3.6, cy + 2.8));
        dc.DrawLine(SilverPen, new Point(cx + 3.6, cy - 2.8), new Point(cx + 2.2, cy + 2.8));
        dc.DrawLine(SilverPen, new Point(cx - 2.8, cy + 0.4), new Point(cx + 2.8, cy - 0.6));
    }

    private static void DrawLotus(DrawingContext dc, double cx, double cy)
    {
        dc.DrawEllipse(FerrariYellow, null, new Point(cx, cy), 6.2, 6.2);
        var tri = new StreamGeometry();
        using (var c = tri.Open())
        {
            c.BeginFigure(new Point(cx, cy - 4.6), true, true);
            c.LineTo(new Point(cx + 4.6, cy + 3.6), true, false);
            c.LineTo(new Point(cx - 4.6, cy + 3.6), true, false);
        }
        tri.Freeze();
        dc.DrawGeometry(LotusGreen, null, tri);
    }

    private static void DrawFormula(DrawingContext dc, double cx, double cy)
    {
        // Sleek open-wheel nose + front wing icon
        dc.DrawRectangle(FerrariRed, null, new Rect(cx - 6.8, cy - 4.4, 13.6, 2.2));
        dc.DrawRectangle(WhiteBrush, null, new Rect(cx - 2.2, cy - 2.0, 4.4, 6.6));
        dc.DrawRectangle(SilverBrush, null, new Rect(cx - 5.2, cy + 2.8, 10.4, 2.0));
    }

    private static void DrawFallback(DrawingContext dc, double cx, double cy)
    {
        dc.DrawRoundedRectangle(SlateBrush, ThinSilverPen, new Rect(cx - 5.5, cy - 5.5, 11, 11), 2.5, 2.5);
        dc.DrawEllipse(SilverBrush, null, new Point(cx, cy), 2.2, 2.2);
    }
}
