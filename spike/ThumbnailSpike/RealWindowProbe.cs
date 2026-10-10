using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Media;
using EdgeStage.Core.Interop;
using EdgeStage.Core.Thumbnails;
using static EdgeStage.Core.Interop.NativeConstants;
using Rgb = ThumbnailSpike.ScreenProbe.Rgb;

namespace ThumbnailSpike;

/// <summary>
/// Runs the sidebar probe against the real desktop. Privacy: only aggregate pixel statistics are
/// recorded (fraction of a thumbnail area that is not the empty placeholder color); no images of
/// the user's windows are saved, and window titles are not written to the report.
/// </summary>
internal sealed class RealWindowProbe
{
    private static readonly Rgb Placeholder = new(0x7A, 0x4A, 0x10); // distinctive so dark app content is not mistaken for "empty"
    private static readonly Rgb Magenta = new(255, 0, 255);
    private static readonly Rgb Cyan = new(0, 255, 255);
    private const string FlipTitle = "EdgeStage Spike Flip";
    private const string MinTitle = "EdgeStage Spike Minimized";

    private readonly StringBuilder _md = new();
    private void Line(string s = "") => _md.AppendLine(s);

    public async Task<string> RunAsync()
    {
        Line("# Spike real-window probe (WpfAllowsTransparency sidebar)");
        Line();
        Line($"- Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Line();

        string exe = Environment.ProcessPath!;
        var flip = Process.Start(new ProcessStartInfo(exe, $"--source-flip \"{FlipTitle}\" #FF00FF") { UseShellExecute = false })!;
        var min = Process.Start(new ProcessStartInfo(exe, $"--source-min \"{MinTitle}\" #00FFFF") { UseShellExecute = false })!;
        await Task.Delay(3000);

        var probe = new SidebarProbeWindow(HostConfig.WpfAllowsTransparency);
        probe.Show();
        await Task.Delay(1500);
        probe.UpdateNow();
        await Task.Delay(300);

        var origin = new POINT(0, 0);
        User32.ClientToScreen(probe.WindowHandle, ref origin);
        RECT ToScreen(RECT r) => new(origin.X + r.Left, origin.Y + r.Top, origin.X + r.Right, origin.Y + r.Bottom);

        // ---- 1. Every listed window: registered? visible? content drawn? ------------------------
        Line("## 1. Thumbnails of the windows currently open");
        Line();
        Line("| # | Process | Window class | Minimized | Registered | In viewport | Content drawn (non-placeholder fraction) |");
        Line("|---|---|---|---|---|---|---|");
        var snapshot = probe.Snapshot();
        for (int i = 0; i < snapshot.Count; i++)
        {
            var (w, placement, registered) = snapshot[i];
            string content = "—";
            if (placement.IsVisible)
            {
                var stats = ScreenProbe.Analyze(ScreenProbe.Inset(ToScreen(placement.Destination), 0.1), Placeholder, 8);
                double drawn = 1 - stats.FractionNear;
                content = $"{(drawn > 0.5 ? "YES" : "NO")} ({drawn:P0})";
            }
            Line($"| {i + 1} | {ProcessName(w.ProcessId, w.Title)} | {w.ClassName} | {w.IsMinimized} | {registered} | {placement.IsVisible} | {content} |");
        }
        Line();
        Line($"Registered {probe.RegisteredCount} of {snapshot.Count}. Titles omitted for privacy except EdgeStage's own test windows.");
        Line();

        // ---- 2. Out-of-process live updates inside the sidebar -----------------------------------
        Line("## 2. Live updates from another process (source flips magenta↔cyan every 400 ms)");
        Line();
        int flipIndex = snapshot.ToList().FindIndex(s => s.Window.Title == FlipTitle);
        if (flipIndex < 0)
        {
            Line("- Flip window not listed — NOT TESTED.");
        }
        else
        {
            probe.BringCardIntoView(flipIndex);
            await Task.Delay(400);
            probe.UpdateNow();
            await Task.Delay(200);
            var placement = probe.Snapshot()[flipIndex].Placement;
            RECT r = ScreenProbe.Inset(ToScreen(placement.Destination), 0.25);
            var seen = new List<char>();
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 4000)
            {
                var m = ScreenProbe.Analyze(r, Magenta);
                var c = ScreenProbe.Analyze(r, Cyan);
                seen.Add(m.FractionNear > 0.8 ? 'M' : c.FractionNear > 0.8 ? 'C' : '?');
                await Task.Delay(50);
            }
            string compact = new(seen.Where((ch, k) => k == 0 || ch != seen[k - 1]).ToArray());
            int transitions = Math.Max(0, compact.Count(ch => ch != '?') - 1);
            Line($"- Samples every 50 ms for 4 s, collapsed sequence: `{compact}` → {transitions} color changes observed (≈10 expected).");
        }
        Line();

        // ---- 3. Scroll clipping ------------------------------------------------------------------
        Line("## 3. Scroll clipping (DWM thumbnails are not clipped by WPF; EdgeStage clips them itself)");
        Line();
        var scroller = probe.Scroller;
        if (scroller.ScrollableHeight <= 0)
        {
            Line($"- List not scrollable with {snapshot.Count} windows — NOT TESTED in this run.");
        }
        else
        {
            scroller.ScrollToTop();
            await Task.Delay(300);
            Rect vpDip = scroller.TransformToAncestor(probe).TransformBounds(new Rect(0, 0, scroller.ViewportWidth, scroller.ViewportHeight));
            double dpi = VisualTreeHelper.GetDpi(probe).DpiScaleX;
            int vpTop = (int)Math.Round(vpDip.Top * dpi);
            int vpBottom = (int)Math.Round(vpDip.Bottom * dpi);
            int vpLeft = (int)Math.Round(vpDip.Left * dpi), vpRight = (int)Math.Round(vpDip.Right * dpi);
            RECT band = ToScreen(new RECT(vpLeft + 20, vpTop - 6, vpRight - 20, vpTop - 1));
            var bandBefore = ScreenProbe.Analyze(band, Placeholder);

            scroller.ScrollToVerticalOffset(90);
            await Task.Delay(400);
            probe.UpdateNow();
            await Task.Delay(300);
            var bandAfter = ScreenProbe.Analyze(band, Placeholder);
            var after = probe.Snapshot();
            var clippedTop = after.Where(s => s.Placement.IsVisible && s.Placement.Source.Top > 0).ToList();
            bool allInside = after.Where(s => s.Placement.IsVisible).All(s => s.Placement.Destination.Top >= vpTop - 1 && s.Placement.Destination.Bottom <= vpBottom + 1);
            Line($"- Scrolled 90 DIP. Visible thumbnails all inside viewport [{vpTop},{vpBottom}] px: {allInside}.");
            Line($"- Thumbnails cropped at the top edge (rcSource.Top > 0): {clippedTop.Count} " +
                 string.Join(", ", clippedTop.Select(s => $"dest {s.Placement.Destination} src {s.Placement.Source}")));
            Line($"- 5-px band just above the viewport: mean before {bandBefore.Mean}, after {bandAfter.Mean} (unchanged ⇒ no thumbnail bleeding outside the list).");
        }
        Line();

        // ---- 4. Fade --------------------------------------------------------------------------------
        await probe.RunFade();
        Line("## 4. 360 ms fade (thumbnail opacity + WPF content opacity per frame)");
        Line();
        Line($"- {probe.LastFade.Frames} frames; per-frame thumbnail update cost avg {probe.LastFade.AvgMs:F2} ms, max {probe.LastFade.MaxMs:F2} ms.");
        Line();

        // ---- 5. Leak: full rebuilds ---------------------------------------------------------------
        Line("## 5. 50 full rebuilds (unregister all → enumerate → register all)");
        Line();
        string before = Counters();
        for (int i = 0; i < 50; i++)
        {
            probe.RebuildNow();
            await Task.Delay(20);
        }
        string afterLeak = Counters();
        Line($"- Before: {before}");
        Line($"- After:  {afterLeak}");
        Line();

        probe.Close();
        foreach (var p in new[] { flip, min })
        {
            try
            {
                nint h = SpikeWindowList.FindByTitle(p == flip ? FlipTitle : MinTitle);
                if (h != 0) User32.PostMessage(h, WM_CLOSE, 0, 0);
                if (!p.WaitForExit(3000)) p.Kill();
            }
            catch (InvalidOperationException) { }
        }
        return _md.ToString();
    }

    private static string ProcessName(uint pid, string title)
    {
        string name;
        try { name = Process.GetProcessById((int)pid).ProcessName; }
        catch (Exception) { name = "(unavailable)"; }
        return title.StartsWith("EdgeStage Spike", StringComparison.Ordinal) ? $"{name} — \"{title}\"" : name;
    }

    private static string Counters()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var p = Process.GetCurrentProcess();
        p.Refresh();
        return $"handles {p.HandleCount}, GDI {User32.GetGuiResources(Kernel32.CurrentProcess, GR_GDIOBJECTS)}, USER {User32.GetGuiResources(Kernel32.CurrentProcess, GR_USEROBJECTS)}, private {p.PrivateMemorySize64 / 1048576.0:F1} MB";
    }
}
