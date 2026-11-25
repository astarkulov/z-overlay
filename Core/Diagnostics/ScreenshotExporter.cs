using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ApexHUD.Core.Settings;
using ApexHUD.Core.Telemetry;
using ApexHUD.Windows;

namespace ApexHUD.Core.Diagnostics;

/// <summary>
/// Renders high-DPI PNG screenshots AND compact, smooth infinite-loop animated GIFs (GIF89a)
/// with dirty-rect delta compression & identical-frame delay merging for GitHub README documentation.
/// Triggered via: ApexHUD.exe --capture-screenshots "path\to\dir"
/// </summary>
public static class ScreenshotExporter
{
    public static void CaptureAll(string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        var settings = CreateShowcaseSettings();
        var demo = new DemoTelemetryProvider();

        var relWin = new RelativeWindow(settings);
        var stdWin = new StandingsWindow(settings);
        var inpWin = new InputsWindow(settings);
        var fuelWin = new FuelWindow(settings);
        var radarWin = new RadarWindow(settings);
        var hubWin = new HubWindow(
            settings, () => { }, () => { }, (_, _, _) => { }, (_, _, _) => { }, _ => { }, () => { }, () => { });

        // Warm up 515 frames (~8.6s) for static PNG showcase shots
        TelemetrySnapshot snap = demo.Step(0.016, settings.FuelSafetyMarginLaps, settings.FuelCalcMode);
        for (int i = 0; i < 515; i++)
        {
            snap = demo.Step(1.0 / 60.0, settings.FuelSafetyMarginLaps, settings.FuelCalcMode);
            inpWin.UpdateData(snap);
        }
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

        // Render Hub static PNG
        var hubBmp = RenderFixedElement((UIElement)hubWin.Content, 780, 590, 16, "#FF0B0E14");
        SavePng(hubBmp, Path.Combine(outputDir, "hub.png"));

        // Mount all 5 widgets into a hidden offscreen WPF Window so WPF's real 60Hz animation clock
        // drives Standings overtake slide & green/red/purple flash animations smoothly!
        var stdHost = WrapInHost(DetachContent(stdWin), 14, "#FF0F1218");
        var relHost = WrapInHost(DetachContent(relWin), 14, "#FF0F1218");
        var inpHost = WrapInHost(DetachContent(inpWin), 14, "#FF0F1218");
        var fuelHost = WrapInHost(DetachContent(fuelWin), 12, "#FF0F1218");
        var radarHost = WrapInHost(DetachContent(radarWin), 12, "#FF0F1218");

        var stageGrid = new Grid { Background = FormatHelpers.GetFrozenBrush("#FF0F1218") };
        stageGrid.Children.Add(stdHost);
        stageGrid.Children.Add(relHost);
        stageGrid.Children.Add(inpHost);
        stageGrid.Children.Add(fuelHost);
        stageGrid.Children.Add(radarHost);

        var stageWin = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = false,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -24000,
            Top = 0,
            Width = 1400,
            Height = 1000,
            Content = stageGrid
        };
        stageWin.Show();
        PumpUi(100);

        // Static HiDPI PNGs (crisp fallback / archive)
        var stdPng = CaptureHost(stdHost, 144);
        var relPng = CaptureHost(relHost, 144);
        var inpPng = CaptureHost(inpHost, 144);
        var fuelPng = CaptureHost(fuelHost, 144);
        var radarPng = CaptureHost(radarHost, 144);
        SavePng(stdPng, Path.Combine(outputDir, "standings.png"));
        SavePng(relPng, Path.Combine(outputDir, "relative.png"));
        SavePng(inpPng, Path.Combine(outputDir, "inputs.png"));
        SavePng(CombineSideBySide(fuelPng, radarPng, 18, "#FF0F1218"), Path.Combine(outputDir, "fuel-radar.png"));
        SavePng(BuildHeroComposite(stdPng, relPng, inpPng, fuelPng, radarPng), Path.Combine(outputDir, "hero-overview.png"));

        // --- ANIMATED GIF CAPTURE (real-time GPU animations + 60Hz demo telemetry!) ---
        // Advance demo to ~15.8s (0.7s before battleStage 1 overtake at 16.5s!) so GIF shows:
        // 1) P4->P3 smooth overtake slide + green/red flash (~0.7s)
        // 2) Purple Fastest Lap shimmer on P1 (~2.2s)
        // 3) Second overtake swap (~3.6s)
        for (int i = 0; i < 435; i++)
        {
            snap = demo.Step(1.0 / 60.0, settings.FuelSafetyMarginLaps, settings.FuelCalcMode);
            inpWin.UpdateData(snap);
        }
        // Seed Standings baseline cleanly before recording
        for (int k = 0; k < 4; k++) stdWin.UpdateData(snap);
        relWin.UpdateData(snap);
        fuelWin.UpdateData(snap);
        radarWin.UpdateData(snap);
        PumpUi(80);

        // Record ~4.6 seconds at 14 FPS (66 frames, 70ms per frame) — smooth & super compact!
        const int gifFps = 14;
        const int delayCs = 7; // 70ms (~14.3 fps)
        const int totalFrames = 66;

        var stdFrames = new List<BitmapSource>(totalFrames);
        var relFrames = new List<BitmapSource>(totalFrames);
        var inpFrames = new List<BitmapSource>(totalFrames);
        var fuelRadarFrames = new List<BitmapSource>(totalFrames);
        var heroFrames = new List<BitmapSource>(totalFrames / 2 + 1);

        var sw = Stopwatch.StartNew();
        for (int f = 0; f < totalFrames; f++)
        {
            // Step 4 telemetry ticks (~67ms of 60Hz sim time) per GIF frame
            for (int t = 0; t < 4; t++)
            {
                snap = demo.Step(1.0 / 60.0, settings.FuelSafetyMarginLaps, settings.FuelCalcMode);
                inpWin.UpdateData(snap);
            }

            // Choreograph a crisp purple Fastest Lap at frame 32 (~2.2s) in GT3!
            if (f >= 32 && snap.Standings.Count > 0 && snap.Standings[0].Drivers.Count > 0)
            {
                var leader = snap.Standings[0].Drivers[0];
                leader.BestLapSeconds = 136.680f;
                leader.LastLapSeconds = 136.680f;
                leader.IsClassBestLap = true;
            }

            // Choreograph a second overtake swap at frame 48 (~3.4s) between P5 and P6!
            if (f >= 48 && snap.Standings.Count > 0 && snap.Standings[0].Drivers.Count >= 6)
            {
                var list = snap.Standings[0].Drivers;
                (list[4], list[5]) = (list[5], list[4]);
                list[4].ClassPosition = 5;
                list[5].ClassPosition = 6;
            }

            // Choreograph smooth Radar sweep (CAR RIGHT frames 5..31, CAR LEFT frames 36..60)
            snap.Radar.Blips.Clear();
            if (f is >= 5 and <= 31)
            {
                float prog = (f - 5) / 26f;
                float y = -4.2f + prog * 8.4f;
                snap.Radar.Proximity = CarProximityState.CarRight;
                snap.Radar.Blips.Add(new RadarBlip { OffsetX = 2.4f, OffsetY = y, ColorHex = "#F5A623" });
            }
            else if (f is >= 36 and <= 60)
            {
                float prog = (f - 36) / 24f;
                float y = 4.2f - prog * 8.4f;
                snap.Radar.Proximity = CarProximityState.CarLeft;
                snap.Radar.Blips.Add(new RadarBlip { OffsetX = -2.4f, OffsetY = y, ColorHex = "#38BDF8" });
            }
            else
            {
                snap.Radar.Proximity = CarProximityState.Clear;
            }

            // Update Standings every 3rd frame (~4.5Hz, matching real runtime cadence while letting animations run!)
            if (f % 3 == 0)
            {
                for (int k = 0; k < 4; k++) stdWin.UpdateData(snap);
            }

            relWin.UpdateData(snap);
            fuelWin.UpdateData(snap);
            radarWin.UpdateData(snap);

            // Wait real wall-clock ~71ms while pumping Dispatcher so WPF DoubleAnimation clocks advance smoothly!
            long targetMs = (f + 1) * (1000L / gifFps);
            while (sw.ElapsedMilliseconds < targetMs)
            {
                PumpUi(10);
            }
            PumpUi(4);

            // Capture crisp 96 DPI (1.0x native pixel sharp) frames
            var sBmp = CaptureHost(stdHost, 96);
            var rBmp = CaptureHost(relHost, 96);
            var iBmp = CaptureHost(inpHost, 96);
            var fBmp = CaptureHost(fuelHost, 96);
            var rdBmp = CaptureHost(radarHost, 96);

            stdFrames.Add(sBmp);
            relFrames.Add(rBmp);
            inpFrames.Add(iBmp);
            fuelRadarFrames.Add(CombineSideBySide(fBmp, rdBmp, 14, "#FF0F1218"));

            // Hero overview every 2nd frame (~7 fps, 33 frames -> crisp & fast-loading < 3 MB on GitHub!)
            if (f % 2 == 0)
            {
                var hs = CaptureHost(stdHost, 78);
                var hr = CaptureHost(relHost, 78);
                var hi = CaptureHost(inpHost, 78);
                var hf = CaptureHost(fuelHost, 78);
                var hrd = CaptureHost(radarHost, 78);
                heroFrames.Add(BuildHeroComposite(hs, hr, hi, hf, hrd, pad: 12, colGap: 10, rowGap: 10));
            }
        }

        WriteAnimatedGif(stdFrames, delayCs, Path.Combine(outputDir, "standings.gif"));
        WriteAnimatedGif(relFrames, delayCs, Path.Combine(outputDir, "relative.gif"));
        WriteAnimatedGif(inpFrames, delayCs, Path.Combine(outputDir, "inputs.gif"));
        WriteAnimatedGif(fuelRadarFrames, delayCs, Path.Combine(outputDir, "fuel-radar.gif"));
        WriteAnimatedGif(heroFrames, delayCs * 2, Path.Combine(outputDir, "hero-overview.gif"));

        stageWin.Close();
        relWin.Close();
        stdWin.Close();
        inpWin.Close();
        fuelWin.Close();
        radarWin.Close();
        hubWin.Close();
    }

    private static AppSettings CreateShowcaseSettings() => new()
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

    private static UIElement DetachContent(Window win)
    {
        var content = (UIElement)win.Content;
        win.Content = null;
        return content;
    }

    private static Border WrapInHost(UIElement elem, double pad, string bgHex) => new()
    {
        Background = FormatHelpers.GetFrozenBrush(bgHex),
        Padding = new Thickness(pad),
        VerticalAlignment = VerticalAlignment.Top,
        HorizontalAlignment = HorizontalAlignment.Left,
        Child = elem
    };

    private static void PumpUi(int sleepMs)
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new DispatcherOperationCallback(_ => { frame.Continue = false; return null; }),
            null);
        Dispatcher.PushFrame(frame);
        if (sleepMs > 0) Thread.Sleep(sleepMs);
    }

    /// <summary>
    /// Renders a visual at exact (0, 0) via VisualBrush so parent layout offset never clips or blanks the bitmap.
    /// </summary>
    private static BitmapSource CaptureHost(Border host, double dpi)
    {
        host.UpdateLayout();
        double w = host.ActualWidth > 1 ? host.ActualWidth : host.DesiredSize.Width;
        double h = host.ActualHeight > 1 ? host.ActualHeight : host.DesiredSize.Height;
        double scale = dpi / 96.0;
        int pxW = Math.Max(1, (int)Math.Ceiling(w * scale));
        int pxH = Math.Max(1, (int)Math.Ceiling(h * scale));

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var vb = new VisualBrush(host) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
            dc.DrawRectangle(vb, null, new Rect(0, 0, w, h));
        }

        var rtb = new RenderTargetBitmap(pxW, pxH, dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(dv);
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
        int w = left.PixelWidth + gapPx + right.PixelWidth + 16;
        int h = Math.Max(left.PixelHeight, right.PixelHeight) + 16;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(FormatHelpers.GetFrozenBrush(bgHex), null, new Rect(0, 0, w, h));
            dc.DrawImage(left, new Rect(8, (h - left.PixelHeight) / 2.0, left.PixelWidth, left.PixelHeight));
            dc.DrawImage(right, new Rect(8 + left.PixelWidth + gapPx, (h - right.PixelHeight) / 2.0, right.PixelWidth, right.PixelHeight));
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
        BitmapSource radar,
        int pad = 28,
        int colGap = 24,
        int rowGap = 20)
    {
        int rightColW = Math.Max(rel.PixelWidth, fuel.PixelWidth + 14 + radar.PixelWidth);
        int topRowW = std.PixelWidth + colGap + rightColW;
        int totalW = Math.Max(topRowW, inp.PixelWidth) + pad * 2;

        int rightColH = rel.PixelHeight + rowGap + Math.Max(fuel.PixelHeight, radar.PixelHeight);
        int topRowH = Math.Max(std.PixelHeight, rightColH);
        int totalH = pad + topRowH + rowGap + inp.PixelHeight + pad;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var bgGrad = new LinearGradientBrush(
                Color.FromRgb(0x0D, 0x10, 0x17),
                Color.FromRgb(0x16, 0x1A, 0x24),
                45);
            dc.DrawRectangle(bgGrad, null, new Rect(0, 0, totalW, totalH));

            dc.DrawImage(std, new Rect(pad, pad, std.PixelWidth, std.PixelHeight));

            int rx = pad + std.PixelWidth + colGap;
            dc.DrawImage(rel, new Rect(rx, pad, rel.PixelWidth, rel.PixelHeight));

            int ry2 = pad + rel.PixelHeight + rowGap;
            dc.DrawImage(fuel, new Rect(rx, ry2, fuel.PixelWidth, fuel.PixelHeight));
            dc.DrawImage(radar, new Rect(rx + fuel.PixelWidth + 14, ry2 + (fuel.PixelHeight - radar.PixelHeight) / 2.0, radar.PixelWidth, radar.PixelHeight));

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

    private sealed class PreparedGifFrame
    {
        public BitmapSource Crop = null!;
        public ushort Left;
        public ushort Top;
        public ushort DelayCs;
    }

    /// <summary>
    /// Spec-compliant GIF89a animated encoder with NETSCAPE2.0 infinite looping,
    /// identical-frame delay merging, and dirty-rect sub-frame cropping for 5-8x smaller files!
    /// </summary>
    private static void WriteAnimatedGif(List<BitmapSource> frames, int delayCentiseconds, string path)
    {
        if (frames.Count == 0) return;

        int canvasW = 0, canvasH = 0;
        foreach (var fr in frames)
        {
            if (fr.PixelWidth > canvasW) canvasW = fr.PixelWidth;
            if (fr.PixelHeight > canvasH) canvasH = fr.PixelHeight;
        }

        ushort baseDelay = (ushort)Math.Clamp(delayCentiseconds, 2, 1000);
        int stride = canvasW * 4;
        byte[]? prevPixels = null;
        byte[] curPixels = new byte[stride * canvasH];
        var prepared = new List<PreparedGifFrame>(frames.Count);

        foreach (var frame in frames)
        {
            BitmapSource norm = (frame.PixelWidth == canvasW && frame.PixelHeight == canvasH)
                ? frame
                : PadToCanvas(frame, canvasW, canvasH);

            norm.CopyPixels(curPixels, stride, 0);

            if (prevPixels == null)
            {
                prepared.Add(new PreparedGifFrame { Crop = norm, Left = 0, Top = 0, DelayCs = baseDelay });
                prevPixels = (byte[])curPixels.Clone();
                continue;
            }

            // Compute tight dirty bounding box vs previous frame
            int minX = canvasW, minY = canvasH, maxX = -1, maxY = -1;
            for (int y = 0; y < canvasH; y++)
            {
                int rowOff = y * stride;
                for (int x = 0; x < canvasW; x++)
                {
                    int p = rowOff + x * 4;
                    if (Math.Abs(curPixels[p] - prevPixels[p]) > 3
                        || Math.Abs(curPixels[p + 1] - prevPixels[p + 1]) > 3
                        || Math.Abs(curPixels[p + 2] - prevPixels[p + 2]) > 3)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (maxX < minX)
            {
                // Identical frame -> merge delay into previous frame (0 extra bytes!)
                prepared[^1].DelayCs = (ushort)Math.Min(6000, prepared[^1].DelayCs + baseDelay);
                continue;
            }

            // Pad dirty rect by 1px to avoid subpixel edge seams
            minX = Math.Max(0, minX - 1);
            minY = Math.Max(0, minY - 1);
            maxX = Math.Min(canvasW - 1, maxX + 1);
            maxY = Math.Min(canvasH - 1, maxY + 1);
            int cropW = maxX - minX + 1;
            int cropH = maxY - minY + 1;

            var cropped = new CroppedBitmap(norm, new Int32Rect(minX, minY, cropW, cropH));
            cropped.Freeze();

            prepared.Add(new PreparedGifFrame
            {
                Crop = cropped,
                Left = (ushort)minX,
                Top = (ushort)minY,
                DelayCs = baseDelay
            });

            Buffer.BlockCopy(curPixels, 0, prevPixels, 0, curPixels.Length);
        }

        using var outFs = File.Create(path);
        using var bw = new BinaryWriter(outFs);

        // 1. GIF89a Header + Logical Screen Descriptor
        bw.Write(Encoding.ASCII.GetBytes("GIF89a"));
        bw.Write((ushort)canvasW);
        bw.Write((ushort)canvasH);
        bw.Write((byte)0x70); // GCT=0, 8-bit color resolution
        bw.Write((byte)0x00);
        bw.Write((byte)0x00);

        // 2. NETSCAPE2.0 Infinite Loop Application Extension
        bw.Write(new byte[]
        {
            0x21, 0xFF, 0x0B,
            (byte)'N', (byte)'E', (byte)'T', (byte)'S', (byte)'C', (byte)'A', (byte)'P', (byte)'E',
            (byte)'2', (byte)'.', (byte)'0',
            0x03, 0x01, 0x00, 0x00, 0x00
        });

        foreach (var pf in prepared)
        {
            using var ms = new MemoryStream();
            var enc = new GifBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(pf.Crop));
            enc.Save(ms);
            AppendSingleFrameToGif(bw, ms.ToArray(), pf.Left, pf.Top, pf.DelayCs);
        }

        // 3. GIF Trailer
        bw.Write((byte)0x3B);
    }

    private static BitmapSource PadToCanvas(BitmapSource src, int w, int h)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(FormatHelpers.GetFrozenBrush("#FF0F1218"), null, new Rect(0, 0, w, h));
            dc.DrawImage(src, new Rect(0, 0, src.PixelWidth, src.PixelHeight));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    private static void AppendSingleFrameToGif(BinaryWriter bw, byte[] src, ushort left, ushort top, ushort delayCs)
    {
        if (src.Length < 20) return;

        int pos = 6;
        byte lsdPacked = src[pos + 4];
        pos += 7;

        byte[]? gct = null;
        int gctSizeCode = lsdPacked & 0x07;
        if ((lsdPacked & 0x80) != 0)
        {
            int gctLen = 3 * (1 << (gctSizeCode + 1));
            gct = new byte[gctLen];
            Buffer.BlockCopy(src, pos, gct, 0, gctLen);
            pos += gctLen;
        }

        while (pos < src.Length)
        {
            byte blockId = src[pos++];
            if (blockId == 0x21)
            {
                pos++;
                while (pos < src.Length)
                {
                    int subLen = src[pos++];
                    if (subLen == 0) break;
                    pos += subLen;
                }
            }
            else if (blockId == 0x2C)
            {
                // Graphic Control Extension (disposal=1 keep previous frame, exact delay)
                bw.Write((byte)0x21);
                bw.Write((byte)0xF9);
                bw.Write((byte)0x04);
                bw.Write((byte)0x04);
                bw.Write(delayCs);
                bw.Write((byte)0x00);
                bw.Write((byte)0x00);

                // Image Descriptor (patch Left & Top to our dirty-rect origin!)
                byte[] imgDesc = new byte[9];
                Buffer.BlockCopy(src, pos, imgDesc, 0, 9);
                pos += 9;

                imgDesc[0] = (byte)(left & 0xFF);
                imgDesc[1] = (byte)((left >> 8) & 0xFF);
                imgDesc[2] = (byte)(top & 0xFF);
                imgDesc[3] = (byte)((top >> 8) & 0xFF);

                byte imgPacked = imgDesc[8];
                bool hasLct = (imgPacked & 0x80) != 0;

                if (hasLct)
                {
                    int lctLen = 3 * (1 << ((imgPacked & 0x07) + 1));
                    bw.Write((byte)0x2C);
                    bw.Write(imgDesc);
                    bw.Write(src, pos, lctLen);
                    pos += lctLen;
                }
                else if (gct != null)
                {
                    imgDesc[8] = (byte)(imgPacked | 0x80 | gctSizeCode);
                    bw.Write((byte)0x2C);
                    bw.Write(imgDesc);
                    bw.Write(gct);
                }
                else
                {
                    bw.Write((byte)0x2C);
                    bw.Write(imgDesc);
                }

                // LZW min code size + sub-blocks
                bw.Write(src[pos++]);
                while (pos < src.Length)
                {
                    int subLen = src[pos++];
                    bw.Write((byte)subLen);
                    if (subLen == 0) break;
                    bw.Write(src, pos, subLen);
                    pos += subLen;
                }
                return;
            }
            else
            {
                break;
            }
        }
    }
}
