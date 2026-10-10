using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using EdgeStage.Core.Activation;
using EdgeStage.Core.Interop;
using EdgeStage.Core.Thumbnails;
using EdgeStage.UI;
using static EdgeStage.Core.Interop.NativeConstants;

namespace EdgeStage.Tests.Integration;

/// <summary>Runs the real zoom transition against test windows (no cursor movement or synthesized input).</summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
public sealed class ZoomOverlayTests
{
    [TestInitialize]
    public void InstallDispatcherContext() =>
        // As in the real app: continuations after an await resume on the UI thread.
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static T Await<T>(Task<T> task, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
            Pump(10);
        Assert.IsTrue(task.IsCompleted, "zoom did not finish in time");
        return task.GetAwaiter().GetResult();
    }

    [STATestMethod]
    public void Zoom_RunsToCompletion_ReleasesItsThumbnail_AndActivatesTheTarget()
    {
        var target = new Window { Title = "EdgeStage zoom target", Width = 500, Height = 320, Left = 400, Top = 250, ShowActivated = false };
        target.Show();
        Pump(200);
        nint hwnd = new WindowInteropHelper(target).Handle;
        var overlay = new ZoomOverlay();
        int liveBefore = DwmThumbnailService.LiveRegistrations;

        RECT rect = ZoomOverlay.TargetRect(hwnd);
        Assert.IsFalse(rect.IsEmpty);
        var to = new PixelRect(rect.Left, rect.Top, rect.Width, rect.Height);
        var from = new PixelRect(20, 40, to.Width / 3, to.Height / 3);
        RECT bounds = ZoomOverlay.MonitorBoundsOf(hwnd, new RECT(0, 0, 1920, 1080));

        var started = System.Diagnostics.Stopwatch.StartNew();
        ActivationResult result = Await(overlay.PlayAsync(hwnd, bounds, from, to, () => WindowActivator.ActivateAsync(hwnd)));
        started.Stop();

        Assert.IsFalse(overlay.IsVisible, "overlay must hide itself");
        Assert.AreEqual(liveBefore, DwmThumbnailService.LiveRegistrations, "the overlay thumbnail must be released");
        Assert.IsTrue(started.ElapsedMilliseconds >= 330, $"grow + fade should take at least ~390 ms, took {started.ElapsedMilliseconds} ms");

        overlay.Close();
        target.Close();
        if (!result.Succeeded)
            Assert.Inconclusive($"Windows refused foreground activation for the test runner: {result.Detail}");
    }

    [STATestMethod]
    public void Zoom_ForMissingWindow_FallsBackToPlainActivation()
    {
        var overlay = new ZoomOverlay();
        int liveBefore = DwmThumbnailService.LiveRegistrations;

        ActivationResult result = Await(overlay.PlayAsync(0x7FFFFFF0, new RECT(0, 0, 800, 600),
            new PixelRect(0, 0, 100, 60), new PixelRect(100, 100, 400, 240), () => WindowActivator.ActivateAsync(0x7FFFFFF0)));

        Assert.AreEqual(ActivationOutcome.WindowGone, result.Outcome);
        Assert.IsFalse(overlay.IsVisible);
        Assert.AreEqual(liveBefore, DwmThumbnailService.LiveRegistrations);
        overlay.Close();
    }

    [STATestMethod]
    public void TargetRect_ForMinimizedWindow_IsItsRestoredPosition()
    {
        var w = new Window { Title = "EdgeStage zoom minimized", Width = 480, Height = 300, Left = 320, Top = 180, ShowActivated = false };
        w.Show();
        Pump(200);
        nint hwnd = new WindowInteropHelper(w).Handle;
        User32.GetWindowRect(hwnd, out RECT normal);

        User32.ShowWindow(hwnd, SW_MINIMIZE);
        Pump(400);
        RECT whileMinimized = ZoomOverlay.TargetRect(hwnd);

        Assert.AreEqual(normal, whileMinimized, "a minimized window zooms to where it will reappear, not to its taskbar slot");
        w.Close();
    }
}

