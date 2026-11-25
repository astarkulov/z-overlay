using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ApexHUD.Core.Settings;
using ApexHUD.Core.Telemetry;
using ApexHUD.Windows;

namespace ApexHUD.Core.Diagnostics;

/// <summary>
/// Renders high-DPI PNG screenshots of all live widgets and the Control Hub for GitHub README documentation.
/// Triggered via: ApexHUD.exe --capture-screenshots "path\to\dir"
/// </summary>
public static class ScreenshotExporter
{
    public static void CaptureAll(string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        var settings = new AppSettings
        {
            DemoMode = true,
            Locked = true,
            UiSkin = "IRacingNative",
            GlobalScale = 1.0,
            HudFontScale = 1.05,
            BackgroundOpacity = 0.90,
            UiFontFamily = "Segoe UI Variable Text",
            MonoFontFamily = "Cascadia Mono",
            RadarAutoHideWhenClear = false,
            ShowCarBrandIcons = true,
            ShowRelativeHeader = true,
            ShowRelativeCarNumber = true,
            ShowRelativeIRating = true,
            ShowRelativeLicense = true,
            ShowRelativePaceDelta = true,
            ShowStandingsHeader = true,
            ShowStandingsCarNumber = true,
            ShowStandingsPosDelta = true,
            ShowStandingsTireCompound = true,
            ShowStandingsIRating = true,
            ShowStandingsDeltaIR = true,
            ShowStandingsLastLap = true,
            ShowStandingsPits = true,
            StandingsMaxRows = 9,
            ShowInputsShiftBar = true,
            ShowInputsGraph = true,
            InputsGraphWidth = 250,
            InputsGraphSeconds = 3.5,
            ShowInputsPedalBars = true,
            ShowInputsGearCluster = true,
            ShowInputsSteering = true,
            ShowInputsClutch = true,
            ShowInputsElectronics = true,
            ShowInputsDeltaBar = true,
            ShowInputsCornerSpeed = true
        };

        var demo = new DemoTelemetryProvider();
        var relWin = new RelativeWindow(settings);
        var stdWin = new StandingsWindow(settings);
        var inpWin = new InputsWindow(settings);
        var fuelWin = new FuelWindow(settings);
        var radarWin = new RadarWindow(settings);

        var hubWin = new HubWindow(
            settings,
            () => { },
            () => { },
            (_, _, _) => { },
            (_, _, _) => { },
            _ => { },
            () => { },
            () => { });

        // Warm up 540 frames (~9.0s) so the 3.5s pedal graph captures heavy braking + ABS pulse + trail-braking + throttle exit!
        TelemetrySnapshot snap = demo.Step(0.016, settings.FuelSafetyMarginLaps, settings.FuelCalcMode);
        for (int i = 0; i < 515; i++)
        {
            snap = demo.Step(1.0 / 60.0, settings.FuelSafetyMarginLaps, settings.FuelCalcMode);
            inpWin.UpdateData(snap);
        }
        // Force a crisp green lap delta & active CAR RIGHT alert for showcase clarity
        snap.Inputs.LapDeltaToBestSec = -0.24f;
        snap.Inputs.HasLapDelta = true;
        snap.Radar.Proximity = CarProximityState.CarRight;
        snap.Radar.Blips.Clear();
        snap.Radar.Blips.Add(new RadarBlip { OffsetX = 2.4f, OffsetY = 0.6f, ColorHex = "#F5A623" });

        relWin.UpdateData(snap);
        stdWin.UpdateData(snap);
        inpWin.UpdateData(snap);
        fuelWin.UpdateData(snap);
        radarWin.UpdateData(snap);
        hubWin.UpdateConnectionStatus(snap);

        // Render individual widget shots on a sleek subtle dark track backdrop
        var stdBmp = RenderElementWithPadding((UIElement)stdWin.Content, 20, "#FF0F1218");
        var relBmp = RenderElementWithPadding((UIElement)relWin.Content, 20, "#FF0F1218");
        var inpBmp = RenderElementWithPadding((UIElement)inpWin.Content, 20, "#FF0F1218");
        var fuelBmp = RenderElementWithPadding((UIElement)fuelWin.Content, 16, "#FF0F1218");
        var radarBmp = RenderElementWithPadding((UIElement)radarWin.Content, 16, "#FF0F1218");

        SavePng(stdBmp, Path.Combine(outputDir, "standings.png"));
        SavePng(relBmp, Path.Combine(outputDir, "relative.png"));
        SavePng(inpBmp, Path.Combine(outputDir, "inputs.png"));
        SavePng(CombineSideBySide(fuelBmp, radarBmp, 24, "#FF0F1218"), Path.Combine(outputDir, "fuel-radar.png"));

        // Render HubWindow cleanly
        var hubContent = (UIElement)hubWin.Content;
        var hubBmp = RenderFixedElement(hubContent, 780, 590, 16, "#FF0B0E14");
        SavePng(hubBmp, Path.Combine(outputDir, "hub.png"));

        // Composite Hero Overview (Standings left, Relative right-top, Fuel+Radar right-bottom, Inputs bottom-center)
        SavePng(BuildHeroComposite(stdBmp, relBmp, inpBmp, fuelBmp, radarBmp), Path.Combine(outputDir, "hero-overview.png"));

        relWin.Close();
        stdWin.Close();
        inpWin.Close();
        fuelWin.Close();
        radarWin.Close();
        hubWin.Close();
    }

    private static BitmapSource RenderElementWithPadding(UIElement elem, double pad, string bgHex)
    {
        if (elem is FrameworkElement fe)
        {
            if (fe.Parent is ContentControl cc) cc.Content = null;
        }

        var host = new Border
        {
            Background = FormatHelpers.GetFrozenBrush(bgHex),
            Padding = new Thickness(pad),
            Child = elem
        };

        // Two layout passes ensure SharedSizeGroup tables settle to exact unclipped width
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        host.Arrange(new Rect(0, 0, host.DesiredSize.Width, host.DesiredSize.Height));
        host.UpdateLayout();
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var sz = host.DesiredSize;
        host.Arrange(new Rect(0, 0, sz.Width, sz.Height));
        host.UpdateLayout();

        const double dpi = 144.0; // crisp 1.5x HiDPI
        double scale = dpi / 96.0;
        var rtb = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(sz.Width * scale)),
            Math.Max(1, (int)Math.Ceiling(sz.Height * scale)),
            dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(host);
        rtb.Freeze();
        return rtb;
    }

    private static BitmapSource RenderFixedElement(UIElement elem, double w, double h, double pad, string bgHex)
    {
        if (elem is FrameworkElement fe && fe.Parent is ContentControl cc)
            cc.Content = null;

        var host = new Border
        {
            Background = FormatHelpers.GetFrozenBrush(bgHex),
            Padding = new Thickness(pad),
            Width = w + pad * 2,
            Height = h + pad * 2,
            Child = elem
        };
        host.Measure(new Size(host.Width, host.Height));
        host.Arrange(new Rect(0, 0, host.Width, host.Height));
        host.UpdateLayout();

        const double dpi = 144.0;
        double scale = dpi / 96.0;
        var rtb = new RenderTargetBitmap(
            (int)Math.Ceiling(host.Width * scale),
            (int)Math.Ceiling(host.Height * scale),
            dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(host);
        rtb.Freeze();
        return rtb;
    }

    private static BitmapSource CombineSideBySide(BitmapSource left, BitmapSource right, int gapPx, string bgHex)
    {
        int w = left.PixelWidth + gapPx + right.PixelWidth + 32;
        int h = Math.Max(left.PixelHeight, right.PixelHeight) + 32;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(FormatHelpers.GetFrozenBrush(bgHex), null, new Rect(0, 0, w, h));
            dc.DrawImage(left, new Rect(16, (h - left.PixelHeight) / 2.0, left.PixelWidth, left.PixelHeight));
            dc.DrawImage(right, new Rect(16 + left.PixelWidth + gapPx, (h - right.PixelHeight) / 2.0, right.PixelWidth, right.PixelHeight));
        }

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    private static BitmapSource BuildHeroComposite(
        BitmapSource std,
        BitmapSource rel,
        BitmapSource inp,
        BitmapSource fuel,
        BitmapSource radar)
    {
        int colGap = 28;
        int rowGap = 24;
        int pad = 32;

        int rightColW = Math.Max(rel.PixelWidth, fuel.PixelWidth + 20 + radar.PixelWidth);
        int topRowW = std.PixelWidth + colGap + rightColW;
        int totalW = Math.Max(topRowW, inp.PixelWidth) + pad * 2;

        int rightColH = rel.PixelHeight + rowGap + Math.Max(fuel.PixelHeight, radar.PixelHeight);
        int topRowH = Math.Max(std.PixelHeight, rightColH);
        int totalH = pad + topRowH + rowGap + inp.PixelHeight + pad;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            // Subtle motorsport dark gradient backdrop
            var bgGrad = new LinearGradientBrush(
                Color.FromRgb(0x0D, 0x10, 0x17),
                Color.FromRgb(0x16, 0x1A, 0x24),
                45);
            dc.DrawRectangle(bgGrad, null, new Rect(0, 0, totalW, totalH));

            // Left: Standings
            dc.DrawImage(std, new Rect(pad, pad, std.PixelWidth, std.PixelHeight));

            // Right top: Relative
            int rx = pad + std.PixelWidth + colGap;
            dc.DrawImage(rel, new Rect(rx, pad, rel.PixelWidth, rel.PixelHeight));

            // Right bottom: Fuel + Radar
            int ry2 = pad + rel.PixelHeight + rowGap;
            dc.DrawImage(fuel, new Rect(rx, ry2, fuel.PixelWidth, fuel.PixelHeight));
            dc.DrawImage(radar, new Rect(rx + fuel.PixelWidth + 20, ry2 + (fuel.PixelHeight - radar.PixelHeight) / 2.0, radar.PixelWidth, radar.PixelHeight));

            // Bottom center: Inputs
            double ix = (totalW - inp.PixelWidth) / 2.0;
            int iy = pad + topRowH + rowGap;
            dc.DrawImage(inp, new Rect(ix, iy, inp.PixelWidth, inp.PixelHeight));
        }

        var rtb = new RenderTargetBitmap(totalW, totalH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    private static void SavePng(BitmapSource bmp, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
