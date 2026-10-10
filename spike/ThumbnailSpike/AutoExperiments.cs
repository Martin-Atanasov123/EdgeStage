using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Media;
using EdgeStage.Core.Interop;
using static EdgeStage.Core.Interop.NativeConstants;
using Rgb = ThumbnailSpike.ScreenProbe.Rgb;

namespace ThumbnailSpike;

/// <summary>
/// Automated, objective spike experiments. Every claim in the generated report comes from a
/// measured value (HRESULTs, pixel probes, handle counts), not from assumptions.
/// </summary>
internal sealed class AutoExperiments
{
    private static readonly Color Magenta = Color.FromRgb(255, 0, 255);
    private static readonly Color Cyan = Color.FromRgb(0, 255, 255);
    private static readonly Color Yellow = Color.FromRgb(255, 255, 0);
    private static Rgb ToRgb(Color c) => new(c.R, c.G, c.B);

    private readonly StringBuilder _md = new();
    private double _x0, _y0;

    private void Line(string text = "") => _md.AppendLine(text);

    /// <summary>Second round: questions raised by the first report.</summary>
    public async Task<string> RunFollowUpsAsync()
    {
        Line("# Spike follow-up experiments");
        Line();
        Line($"- Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Line();
        Rect wa = SystemParameters.WorkArea;
        _x0 = wa.Left + 40;
        _y0 = wa.Top + 40;

        await RenderRateExperiment();
        await PreMinimizedExperiment();
        await InactiveBackdropExperiment();
        await LayeredAlphaExperiment();
        await WpfOpacityExperiment();
        return _md.ToString();
    }

    private async Task RenderRateExperiment()
    {
        Line("## A. WPF render-frame rate on this machine (CompositionTarget.Rendering, idle)");
        var host = new HostWindow(HostConfig.Solid, _x0 + 540, _y0);
        host.Show();
        await Task.Delay(400);
        int frames = 0;
        var clock = Stopwatch.StartNew();
        var done = new TaskCompletionSource();
        void OnRendering(object? s, EventArgs e) { frames++; if (clock.ElapsedMilliseconds >= 2000) done.TrySetResult(); }
        CompositionTarget.Rendering += OnRendering;
        await done.Task;
        CompositionTarget.Rendering -= OnRendering;
        Line($"- {frames / 2.0:F0} frames/s over 2 s.");
        Line();
        host.Close();
        await Task.Delay(200);
    }

    private async Task PreMinimizedExperiment()
    {
        Line("## B. Thumbnail registered for a window that was already minimized");
        var src = new SourceWindow("EdgeStage Spike PreMin", Cyan, _x0, _y0);
        src.Show();
        await Task.Delay(600);
        User32.ShowWindow(src.Hwnd, SW_MINIMIZE);
        await Task.Delay(1500);

        var host = new HostWindow(HostConfig.Solid, _x0 + 540, _y0);
        host.Show();
        await Task.Delay(400);
        int hr = Dwmapi.DwmRegisterThumbnail(host.Hwnd, src.Hwnd, out SafeThumbnailHandle thumb);
        RECT dest = host.ThumbDestPx;
        int hrUpd = Dwmapi.DwmUpdateThumbnailProperties(thumb, Props(dest, clientOnly: false));
        int hrSize = Dwmapi.DwmQueryThumbnailSourceSize(thumb, out SIZE size);
        await Task.Delay(500);
        var stats = ScreenProbe.Analyze(ScreenProbe.Inset(host.ToScreen(dest), 0.2), ToRgb(Cyan));
        Line($"- Register HR 0x{hr:X8}, update HR 0x{hrUpd:X8}, source size HR 0x{hrSize:X8} → {size.Width}x{size.Height}.");
        Line($"- Thumbnail center: cyan {stats.FractionNear:P0}, mean {stats.Mean} (placeholder #303030 means nothing drawn).");
        Line();
        thumb.Dispose();
        host.Close();
        src.Close();
        await Task.Delay(300);
    }

    private async Task InactiveBackdropExperiment()
    {
        Line("## C. Backdrop when the host is NOT the active window (edge-trigger mode)");
        var yellow = new SourceWindow("EdgeStage Spike Backdrop", Yellow, _x0 + 540 + 10, _y0 + 185, chromeless: true);
        yellow.Show();
        await Task.Delay(400);
        foreach (var config in new[] { HostConfig.AcrylicNoFrame, HostConfig.AcrylicChrome, HostConfig.WpfAllowsTransparency })
        {
            var host = new HostWindow(config, _x0 + 540, _y0) { ShowActivated = false };
            host.Show();
            await Task.Delay(700);
            bool active = User32.GetForegroundWindow() == host.Hwnd;
            var inactive = ScreenProbe.Analyze(host.BackdropProbeScreen, ToRgb(Yellow));
            host.Activate();
            await Task.Delay(700);
            bool activeNow = User32.GetForegroundWindow() == host.Hwnd;
            var activeStats = ScreenProbe.Analyze(host.BackdropProbeScreen, ToRgb(Yellow));
            Line($"- {config}: not activated (foreground={active}) strip mean {inactive.Mean}; after Activate (foreground={activeNow}) strip mean {activeStats.Mean}.");
            host.Close();
            await Task.Delay(300);
        }
        yellow.Close();
        Line();
    }

    private async Task LayeredAlphaExperiment()
    {
        Line("## D. WS_EX_LAYERED + LWA_ALPHA on a WPF window");
        var source = new SourceWindow("EdgeStage Spike Source", Magenta, _x0, _y0);
        source.Show();
        var host = new HostWindow(HostConfig.LayeredAlpha, _x0 + 540, _y0);
        host.Show();
        await Task.Delay(600);
        Line($"- Native setup: {host.NativeSetupLog}");
        Dwmapi.DwmRegisterThumbnail(host.Hwnd, source.Hwnd, out SafeThumbnailHandle thumb);
        RECT dest = host.ThumbDestPx;
        Dwmapi.DwmUpdateThumbnailProperties(thumb, Props(dest));
        await Task.Delay(400);
        RECT probe = ScreenProbe.Inset(host.ToScreen(dest), 0.2);
        bool ok = User32.SetLayeredWindowAttributes(host.Hwnd, 0, 100, LWA_ALPHA);
        int err = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
        await Task.Delay(400);
        var faded = ScreenProbe.Analyze(probe, ToRgb(Magenta));
        var bg = ScreenProbe.Analyze(host.BackdropProbeScreen, new Rgb(0x20, 0x20, 0x20), 10);
        Line($"- Runtime SetLayeredWindowAttributes(alpha 100) = {ok} (err {err}); thumbnail mean {faded.Mean}, host background strip mean {bg.Mean}.");
        Line();
        thumb.Dispose();
        host.Close();
        source.Close();
        await Task.Delay(300);
    }

    private async Task WpfOpacityExperiment()
    {
        Line("## E. WPF AllowsTransparency window with Window.Opacity = 0.4 (does the DWM thumbnail fade with it?)");
        var source = new SourceWindow("EdgeStage Spike Source", Magenta, _x0, _y0);
        source.Show();
        var host = new HostWindow(HostConfig.WpfAllowsTransparency, _x0 + 540, _y0);
        host.Show();
        await Task.Delay(600);
        Dwmapi.DwmRegisterThumbnail(host.Hwnd, source.Hwnd, out SafeThumbnailHandle thumb);
        RECT dest = host.ThumbDestPx;
        Dwmapi.DwmUpdateThumbnailProperties(thumb, Props(dest));
        await Task.Delay(400);
        RECT probe = ScreenProbe.Inset(host.ToScreen(dest), 0.2);
        host.Opacity = 0.4;
        await Task.Delay(400);
        var faded = ScreenProbe.Analyze(probe, ToRgb(Magenta));
        Line($"- Thumbnail mean with Window.Opacity 0.4: {faded.Mean} (#FF00FF = thumbnail ignores window opacity).");
        Line();
        thumb.Dispose();
        host.Close();
        source.Close();
        await Task.Delay(300);
    }

    public async Task<string> RunAsync()
    {
        Line("# Spike auto-experiment report");
        Line();
        Line($"- Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Line($"- OS: {Environment.OSVersion.VersionString} ({System.Runtime.InteropServices.RuntimeInformation.OSDescription})");
        Line($"- Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, {(Environment.Is64BitProcess ? "x64" : "x86")} process");
        Line($"- Primary work area (DIP): {SystemParameters.WorkArea}");
        Line($"- Machine: {Environment.MachineName}, {Environment.ProcessorCount} logical CPUs");
        Line();

        Rect wa = SystemParameters.WorkArea;
        _x0 = wa.Left + 40;
        _y0 = wa.Top + 40;

        var source = new SourceWindow("EdgeStage Spike Source", Magenta, _x0, _y0);
        source.Show();
        // Yellow chromeless window placed behind each host's "backdrop probe" strip (never behind the thumbnail box).
        var yellow = new SourceWindow("EdgeStage Spike Backdrop", Yellow, _x0 + 540 + 10, _y0 + 185, chromeless: true);
        yellow.Show();
        await Task.Delay(1000);
        Line($"- Display scale of source window: {VisualTreeHelper.GetDpi(source).DpiScaleX:P0}");
        Line();

        Line("## 1. Render + live update per destination-window configuration");
        Line();
        Line("Thumbnail of a solid-magenta window registered into each host; probe = central 60 % of the destination rectangle.");
        Line("Live latency = time from changing the source color to the color appearing (a) in the source window itself and (b) in the thumbnail. Probe resolution ≈ 1–3 ms.");
        Line();
        Line("| Config | Native setup | Register HR | Source size | Update HR | Thumbnail visible (frac magenta) | Edge alignment | Backdrop strip mean (yellow behind) | Opacity 128 mean | Latency src median/p95 ms | Latency thumb median/p95 ms |");
        Line("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (HostConfig config in Enum.GetValues<HostConfig>())
            await HostExperiment(config, source);
        Line();

        yellow.Close();
        await MinimizedExperiment(source);
        await AnimationCostExperiment(source);
        await LeakExperiment(source);
        await DestinationDestroyedExperiment(source);
        await SourceDestroyedExperiment();
        await ActivationExperiment();

        source.Close();
        return _md.ToString();
    }

    // ---------------------------------------------------------------------------------------------

    private static DWM_THUMBNAIL_PROPERTIES Props(RECT dest, byte opacity = 255, bool clientOnly = true) => new()
    {
        dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY,
        rcDestination = dest,
        opacity = opacity,
        fVisible = 1,
        fSourceClientAreaOnly = clientOnly ? 1 : 0,
    };

    private async Task HostExperiment(HostConfig config, SourceWindow source)
    {
        var host = new HostWindow(config, _x0 + 540, _y0);
        host.Show();
        await Task.Delay(600);

        int hrReg = Dwmapi.DwmRegisterThumbnail(host.Hwnd, source.Hwnd, out SafeThumbnailHandle thumb);
        Dwmapi.DwmQueryThumbnailSourceSize(thumb, out SIZE size);
        RECT dest = host.ThumbDestPx;
        int hrUpd = Dwmapi.DwmUpdateThumbnailProperties(thumb, Props(dest));
        await Task.Delay(500);

        RECT destScreen = host.ToScreen(dest);
        RECT probe = ScreenProbe.Inset(destScreen, 0.2);
        var stats = ScreenProbe.Analyze(probe, ToRgb(Magenta));
        bool visible = stats.FractionNear > 0.9;

        // Edge alignment: a 2-px strip just inside the destination edge must be magenta, just outside must not.
        string alignment = "n/a";
        if (visible)
        {
            var inside = ScreenProbe.Analyze(new RECT(destScreen.Left + 1, destScreen.Top + 10, destScreen.Left + 3, destScreen.Bottom - 10), ToRgb(Magenta));
            var outside = ScreenProbe.Analyze(new RECT(destScreen.Left - 3, destScreen.Top + 10, destScreen.Left - 1, destScreen.Bottom - 10), ToRgb(Magenta));
            alignment = inside.FractionNear > 0.9 && outside.FractionNear < 0.1 ? "exact (±1px)" : $"off (in {inside.FractionNear:P0}, out {outside.FractionNear:P0})";
        }

        var backdrop = ScreenProbe.Analyze(host.BackdropProbeScreen, ToRgb(Yellow));

        string opacityMean = "n/a";
        if (visible)
        {
            Dwmapi.DwmUpdateThumbnailProperties(thumb, Props(dest, opacity: 128));
            await Task.Delay(300);
            opacityMean = ScreenProbe.Analyze(probe, ToRgb(Magenta)).Mean.ToString();
            Dwmapi.DwmUpdateThumbnailProperties(thumb, Props(dest));
            await Task.Delay(200);
        }

        string latSrc = "n/a", latThumb = "n/a";
        if (visible)
        {
            var (src, th) = await MeasureLatency(source, probe, flips: 16);
            latSrc = Summarize(src);
            latThumb = Summarize(th);
        }

        Line($"| {config} | {host.NativeSetupLog} | 0x{hrReg:X8} | {size.Width}x{size.Height} | 0x{hrUpd:X8} | {(visible ? "YES" : "NO")} ({stats.FractionNear:P0}, mean {stats.Mean}) | {alignment} | {backdrop.Mean} | {opacityMean} | {latSrc} | {latThumb} |");

        if (config == HostConfig.LayeredAlpha && visible)
        {
            host.SetWindowAlpha(128);
            await Task.Delay(300);
            var faded = ScreenProbe.Analyze(probe, ToRgb(Magenta));
            host.SetWindowAlpha(255);
            await Task.Delay(200);
            var restored = ScreenProbe.Analyze(probe, ToRgb(Magenta));
            _layeredAlphaNote = $"LayeredAlpha whole-window alpha 128 → thumbnail region mean {faded.Mean} (frac magenta {faded.FractionNear:P0}); back to 255 → {restored.Mean} ({restored.FractionNear:P0}).";
        }

        thumb.Dispose();
        host.Close();
        await Task.Delay(300);

        if (_layeredAlphaNote is not null && config == HostConfig.LayeredAlpha)
        {
            Line();
            Line($"> {_layeredAlphaNote}");
            Line();
        }
    }

    private string? _layeredAlphaNote;

    private static string Summarize(List<double> values)
    {
        var ok = values.Where(v => v >= 0).OrderBy(v => v).ToList();
        if (ok.Count == 0) return "timeout";
        double median = ok[ok.Count / 2];
        double p95 = ok[Math.Min(ok.Count - 1, (int)Math.Ceiling(ok.Count * 0.95) - 1)];
        string missed = ok.Count < values.Count ? $" ({values.Count - ok.Count} timeouts)" : "";
        return $"{median:F0} / {p95:F0}{missed}";
    }

    /// <summary>Flips the source color and measures when the source and the thumbnail show it.</summary>
    private static async Task<(List<double> Source, List<double> Thumb)> MeasureLatency(SourceWindow source, RECT thumbProbe, int flips)
    {
        RECT sourceProbe = ScreenProbe.Inset(source.ClientScreenRect, 0.3);
        var src = new List<double>();
        var thumb = new List<double>();
        for (int i = 0; i < flips; i++)
        {
            Color target = i % 2 == 0 ? Cyan : Magenta;
            Rgb rgb = ToRgb(target);
            var sw = Stopwatch.StartNew();
            source.SetColor(target);
            var (ts, tt) = await Task.Run(() =>
            {
                double s = -1, t = -1;
                while (sw.ElapsedMilliseconds < 1500 && (s < 0 || t < 0))
                {
                    if (s < 0 && ScreenProbe.Analyze(sourceProbe, rgb).FractionNear > 0.9) s = sw.Elapsed.TotalMilliseconds;
                    if (t < 0 && ScreenProbe.Analyze(thumbProbe, rgb).FractionNear > 0.9) t = sw.Elapsed.TotalMilliseconds;
                }
                return (s, t);
            });
            src.Add(ts);
            thumb.Add(tt);
            await Task.Delay(60);
        }
        return (src, thumb);
    }

    // ---------------------------------------------------------------------------------------------

    private async Task MinimizedExperiment(SourceWindow source)
    {
        Line("## 2. Minimized source window");
        Line();
        var host = new HostWindow(HostConfig.Solid, _x0 + 540, _y0);
        host.Show();
        await Task.Delay(400);
        Dwmapi.DwmRegisterThumbnail(host.Hwnd, source.Hwnd, out SafeThumbnailHandle thumb);
        RECT dest = host.ThumbDestPx;
        Dwmapi.DwmUpdateThumbnailProperties(thumb, Props(dest));
        await Task.Delay(300);
        RECT probe = ScreenProbe.Inset(host.ToScreen(dest), 0.2);
        Dwmapi.DwmQueryThumbnailSourceSize(thumb, out SIZE before);
        var visibleBefore = ScreenProbe.Analyze(probe, ToRgb(Magenta));

        User32.ShowWindow(source.Hwnd, SW_MINIMIZE);
        await Task.Delay(900);
        int hrSize = Dwmapi.DwmQueryThumbnailSourceSize(thumb, out SIZE minimized);
        int hrUpd = Dwmapi.DwmUpdateThumbnailProperties(thumb, Props(dest));
        await Task.Delay(300);
        var whileMin = ScreenProbe.Analyze(probe, ToRgb(Magenta));
        var whileMinFull = ScreenProbe.Analyze(host.ToScreen(dest), ToRgb(Magenta));

        // Does a content change while minimized reach the thumbnail?
        source.SetColor(Cyan);
        await Task.Delay(700);
        var afterChange = ScreenProbe.Analyze(probe, ToRgb(Cyan));

        User32.ShowWindow(source.Hwnd, SW_RESTORE);
        await Task.Delay(900);
        Dwmapi.DwmQueryThumbnailSourceSize(thumb, out SIZE restored);
        Dwmapi.DwmUpdateThumbnailProperties(thumb, Props(dest));
        await Task.Delay(300);
        var afterRestore = ScreenProbe.Analyze(probe, ToRgb(Cyan));
        source.SetColor(Magenta);

        Line($"- Before minimize: source size {before.Width}x{before.Height}, thumbnail magenta {visibleBefore.FractionNear:P0}.");
        Line($"- Minimized: DwmQueryThumbnailSourceSize HR 0x{hrSize:X8} → {minimized.Width}x{minimized.Height}; update HR 0x{hrUpd:X8}; " +
             $"thumbnail center magenta {whileMin.FractionNear:P0} (mean {whileMin.Mean}); full dest-rect mean {whileMinFull.Mean}.");
        Line($"- Color changed to cyan while minimized: thumbnail center cyan {afterChange.FractionNear:P0} (mean {afterChange.Mean}).");
        Line($"- After restore: source size {restored.Width}x{restored.Height}; thumbnail center cyan {afterRestore.FractionNear:P0} (mean {afterRestore.Mean}).");
        Line();

        thumb.Dispose();
        host.Close();
        await Task.Delay(300);
    }

    // ---------------------------------------------------------------------------------------------

    private async Task AnimationCostExperiment(SourceWindow source)
    {
        Line("## 3. Per-frame thumbnail updates (animation feasibility)");
        Line();
        var host = new HostWindow(HostConfig.AcrylicNoFrame, _x0 + 540, _y0);
        host.Show();
        await Task.Delay(400);
        var thumbs = new List<SafeThumbnailHandle>();
        for (int i = 0; i < 8; i++)
        {
            Dwmapi.DwmRegisterThumbnail(host.Hwnd, source.Hwnd, out SafeThumbnailHandle t);
            thumbs.Add(t);
        }

        RECT baseDest = host.ThumbDestPx;
        var callTimes = new List<double>();
        int frames = 0, failures = 0;
        var clock = Stopwatch.StartNew();
        var done = new TaskCompletionSource();
        void OnRendering(object? s, EventArgs e)
        {
            double progress = Math.Min(1.0, clock.Elapsed.TotalMilliseconds / 1000.0);
            int offset = (int)(40 * Math.Sin(progress * Math.PI));
            byte opacity = (byte)(255 * (0.4 + 0.6 * Math.Abs(Math.Cos(progress * Math.PI))));
            var sw = Stopwatch.StartNew();
            foreach (var t in thumbs)
            {
                var dest = new RECT(baseDest.Left + offset, baseDest.Top, baseDest.Right + offset, baseDest.Bottom);
                if (Dwmapi.DwmUpdateThumbnailProperties(t, Props(dest, opacity)) < 0) failures++;
            }
            callTimes.Add(sw.Elapsed.TotalMilliseconds);
            frames++;
            if (progress >= 1.0) done.TrySetResult();
        }
        CompositionTarget.Rendering += OnRendering;
        await done.Task;
        CompositionTarget.Rendering -= OnRendering;

        Line($"- 8 thumbnails updated (rect + opacity) on every WPF render frame for 1 s: {frames} frames, " +
             $"{failures} failed calls, cost per frame for all 8: avg {callTimes.Average():F3} ms, max {callTimes.Max():F3} ms.");
        Line();

        foreach (var t in thumbs) t.Dispose();
        host.Close();
        await Task.Delay(300);
    }

    // ---------------------------------------------------------------------------------------------

    private readonly record struct ResourceSnapshot(int Handles, uint Gdi, uint User, long PrivateBytes)
    {
        public static ResourceSnapshot Take()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var p = Process.GetCurrentProcess();
            p.Refresh();
            return new ResourceSnapshot(p.HandleCount,
                User32.GetGuiResources(Kernel32.CurrentProcess, GR_GDIOBJECTS),
                User32.GetGuiResources(Kernel32.CurrentProcess, GR_USEROBJECTS),
                p.PrivateMemorySize64);
        }

        public override string ToString() => $"handles {Handles}, GDI {Gdi}, USER {User}, private {PrivateBytes / 1024.0 / 1024.0:F1} MB";
    }

    private async Task LeakExperiment(SourceWindow source)
    {
        Line("## 4. Native resource release (leak test)");
        Line();
        var host = new HostWindow(HostConfig.AcrylicNoFrame, _x0 + 540, _y0);
        host.Show();
        await Task.Delay(400);
        RECT dest = host.ThumbDestPx;

        // Warm-up so one-time allocations do not count as leaks.
        for (int i = 0; i < 20; i++)
        {
            Dwmapi.DwmRegisterThumbnail(host.Hwnd, source.Hwnd, out SafeThumbnailHandle t);
            Dwmapi.DwmUpdateThumbnailProperties(t, Props(dest));
            t.Dispose();
        }
        await Task.Delay(200);

        var before = ResourceSnapshot.Take();
        int failures = 0, releaseFailures = 0;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            if (Dwmapi.DwmRegisterThumbnail(host.Hwnd, source.Hwnd, out SafeThumbnailHandle t) < 0) failures++;
            Dwmapi.DwmQueryThumbnailSourceSize(t, out _);
            if (Dwmapi.DwmUpdateThumbnailProperties(t, Props(dest)) < 0) failures++;
            t.Dispose();
            if (t.ReleaseResult < 0) releaseFailures++;
            if (i % 100 == 99) await Task.Delay(1);
        }
        double perCycle = sw.Elapsed.TotalMilliseconds / 1000;
        await Task.Delay(300);
        var after = ResourceSnapshot.Take();

        // Sidebar-like pattern: register 12, update, release all — 100 times.
        var before2 = ResourceSnapshot.Take();
        for (int round = 0; round < 100; round++)
        {
            var batch = new List<SafeThumbnailHandle>();
            for (int i = 0; i < 12; i++)
            {
                Dwmapi.DwmRegisterThumbnail(host.Hwnd, source.Hwnd, out SafeThumbnailHandle t);
                Dwmapi.DwmUpdateThumbnailProperties(t, Props(dest));
                batch.Add(t);
            }
            await Task.Delay(1);
            foreach (var t in batch) t.Dispose();
        }
        await Task.Delay(300);
        var after2 = ResourceSnapshot.Take();

        Line($"- 1,000 × (register → query size → update → dispose): {failures} failed calls, {releaseFailures} failed releases, avg {perCycle:F3} ms per cycle.");
        Line($"  - Before: {before}");
        Line($"  - After:  {after}");
        Line($"- 100 × (register 12 → update → dispose all):");
        Line($"  - Before: {before2}");
        Line($"  - After:  {after2}");
        Line();

        host.Close();
        await Task.Delay(300);
    }

    private async Task DestinationDestroyedExperiment(SourceWindow source)
    {
        Line("## 5. Destination window destroyed while thumbnails are registered");
        Line();
        var host = new HostWindow(HostConfig.Solid, _x0 + 540, _y0);
        host.Show();
        await Task.Delay(300);
        var thumbs = new List<SafeThumbnailHandle>();
        for (int i = 0; i < 5; i++)
        {
            Dwmapi.DwmRegisterThumbnail(host.Hwnd, source.Hwnd, out SafeThumbnailHandle t);
            Dwmapi.DwmUpdateThumbnailProperties(t, Props(host.ThumbDestPx));
            thumbs.Add(t);
        }
        host.Close();
        await Task.Delay(300);
        var updates = thumbs.Select(t => $"0x{Dwmapi.DwmUpdateThumbnailProperties(t, Props(new RECT(0, 0, 10, 10))):X8}").ToList();
        foreach (var t in thumbs) t.Dispose();
        Line($"- Update after destination destroyed: {string.Join(", ", updates.Distinct())}");
        Line($"- Release after destination destroyed: {string.Join(", ", thumbs.Select(t => $"0x{t.ReleaseResult:X8}").Distinct())} (no exception, no crash)");
        Line();
    }

    private async Task SourceDestroyedExperiment()
    {
        Line("## 6. Source window destroyed while its thumbnail is shown");
        Line();
        var src = new SourceWindow("EdgeStage Spike Source 2", Cyan, _x0, _y0 + 360);
        src.Show();
        var host = new HostWindow(HostConfig.Solid, _x0 + 540, _y0);
        host.Show();
        await Task.Delay(500);
        Dwmapi.DwmRegisterThumbnail(host.Hwnd, src.Hwnd, out SafeThumbnailHandle thumb);
        RECT dest = host.ThumbDestPx;
        Dwmapi.DwmUpdateThumbnailProperties(thumb, Props(dest));
        await Task.Delay(300);
        RECT probe = ScreenProbe.Inset(host.ToScreen(dest), 0.2);
        var alive = ScreenProbe.Analyze(probe, ToRgb(Cyan));
        src.Close();
        await Task.Delay(500);
        var gone = ScreenProbe.Analyze(probe, ToRgb(Cyan));
        int hrUpd = Dwmapi.DwmUpdateThumbnailProperties(thumb, Props(dest));
        int hrSize = Dwmapi.DwmQueryThumbnailSourceSize(thumb, out SIZE size);
        thumb.Dispose();
        Line($"- While alive: cyan {alive.FractionNear:P0}. After source closed: cyan {gone.FractionNear:P0} (mean {gone.Mean}).");
        Line($"- Update HR 0x{hrUpd:X8}, query-size HR 0x{hrSize:X8} ({size.Width}x{size.Height}), release HR 0x{thumb.ReleaseResult:X8}.");
        Line();
        host.Close();
        await Task.Delay(300);
    }

    // ---------------------------------------------------------------------------------------------

    private async Task ActivationExperiment()
    {
        Line("## 7. Activation of an out-of-process window");
        Line();
        const string title = "EdgeStage Spike External";
        using var ext = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--source \"{title}\" #00FF00") { UseShellExecute = false })!;
        nint target = 0;
        for (int i = 0; i < 50 && target == 0; i++)
        {
            await Task.Delay(100);
            target = SpikeWindowList.FindByTitle(title);
        }
        if (target == 0)
        {
            Line("- External source window did not appear; activation NOT TESTED.");
            Line();
            return;
        }

        var host = new HostWindow(HostConfig.Solid, _x0 + 540, _y0);
        host.Show();
        host.Activate();
        await Task.Delay(400);
        bool hostForeground = User32.GetForegroundWindow() == host.Hwnd;

        bool setA = User32.SetForegroundWindow(target);
        await Task.Delay(400);
        bool okA = User32.GetForegroundWindow() == target;

        User32.ShowWindow(target, SW_MINIMIZE);
        await Task.Delay(600);
        host.Activate();
        await Task.Delay(300);
        bool iconic = User32.IsIconic(target);
        User32.ShowWindow(target, SW_RESTORE);
        bool setB = User32.SetForegroundWindow(target);
        await Task.Delay(500);
        bool okB = User32.GetForegroundWindow() == target && !User32.IsIconic(target);

        Line($"- Spike host was foreground before the test: {hostForeground} (process launched from a script, so foreground rights are not guaranteed).");
        Line($"- Normal window: SetForegroundWindow returned {setA}; target became foreground: {okA}.");
        Line($"- Minimized window (iconic={iconic}): SW_RESTORE + SetForegroundWindow returned {setB}; target restored and foreground: {okB}.");
        Line();

        User32.PostMessage(target, WM_CLOSE, 0, 0);
        ext.WaitForExit(3000);
        host.Close();
        await Task.Delay(200);
    }
}
