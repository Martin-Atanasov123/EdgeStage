using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using EdgeStage.Core.Activation;
using EdgeStage.Core.Input;
using EdgeStage.Core.Interop;
using EdgeStage.Core.Thumbnails;
using EdgeStage.Core.WindowTracking;
using static EdgeStage.Core.Interop.NativeConstants;

namespace EdgeStage.Tests.Integration;

/// <summary>
/// Exercises the real Win32/DWM APIs against windows created by the test process itself. These
/// tests need an interactive desktop session; they never move the cursor or synthesize input.
/// Run only these with: dotnet test --filter TestCategory=Desktop
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
public sealed class DesktopIntegrationTests
{
    private readonly List<Window> _windows = [];

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var w in _windows)
            if (w.IsLoaded) w.Close();
        _windows.Clear();
        Pump(50);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static T RunPumping<T>(Func<Task<T>> action)
    {
        Task<T> task = action();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
            Pump(15);
        return task.GetAwaiter().GetResult();
    }

    private Window Open(string title, Action<Window>? configure = null)
    {
        var w = new Window { Title = title, Width = 420, Height = 300, Left = 300, Top = 200, ShowActivated = false };
        configure?.Invoke(w);
        w.Show();
        _windows.Add(w);
        Pump(150);
        return w;
    }

    private static nint Handle(Window w) => new WindowInteropHelper(w).Handle;

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    // ---- window enumeration --------------------------------------------------------------------

    [STATestMethod]
    public void Enumerator_ListsAppWindow_ButNotToolOrOwnedWindows()
    {
        var main = Open(Unique("EdgeStage IT main"));
        // ShowInTaskbar=false matters: WPF adds WS_EX_APPWINDOW otherwise, which (as in Alt+Tab) forces inclusion.
        var tool = Open(Unique("EdgeStage IT tool"), w => { w.WindowStyle = WindowStyle.ToolWindow; w.ShowInTaskbar = false; });
        var owned = Open(Unique("EdgeStage IT owned"), w => { w.Owner = main; w.ShowInTaskbar = false; });

        // Exclude a PID that is not ours so the test process's own windows are eligible.
        var listed = WindowEnumerator.Enumerate(excludeProcessId: 0);

        var mainInfo = listed.SingleOrDefault(i => i.Hwnd == Handle(main));
        Assert.IsNotNull(mainInfo, "main window should be listed");
        Assert.AreEqual(main.Title, mainInfo.Title);
        Assert.IsFalse(listed.Any(i => i.Hwnd == Handle(tool)), "tool window must be excluded");
        Assert.IsFalse(listed.Any(i => i.Hwnd == Handle(owned)), "owned dialog must be excluded");
        Assert.AreEqual(Handle(main), WindowQuery.GetRootOwner(Handle(owned)));
    }

    [STATestMethod]
    public void Enumerator_AppWindowStyle_ForcesInclusion_LikeAltTab()
    {
        // A tool window that also shows in the taskbar gets WS_EX_APPWINDOW and is switchable.
        var tool = Open(Unique("EdgeStage IT taskbar tool"), w => w.WindowStyle = WindowStyle.ToolWindow);

        long ex = WindowQuery.GetExStyle(Handle(tool));
        Assert.AreNotEqual(0L, ex & WS_EX_APPWINDOW, "WPF sets WS_EX_APPWINDOW when ShowInTaskbar=true");
        Assert.IsTrue(WindowEnumerator.Enumerate(excludeProcessId: 0).Any(i => i.Hwnd == Handle(tool)));
    }

    [STATestMethod]
    public void Enumerator_ExcludesOwnProcessByDefault()
    {
        var main = Open(Unique("EdgeStage IT self"));
        Assert.IsFalse(WindowEnumerator.Enumerate().Any(i => i.Hwnd == Handle(main)));
    }

    [STATestMethod]
    public void Enumerator_ReportsMinimizedState()
    {
        var main = Open(Unique("EdgeStage IT min"));
        User32.ShowWindow(Handle(main), SW_MINIMIZE);
        Pump(300);

        var info = WindowEnumerator.TryRead(Handle(main), excludeProcessId: 0);

        Assert.IsNotNull(info);
        Assert.IsTrue(info.IsMinimized);
    }

    // ---- DWM thumbnails --------------------------------------------------------------------------

    [STATestMethod]
    public void Thumbnail_RegistersAppliesAndReleases_AndDetectsDeadSource()
    {
        var host = Open(Unique("EdgeStage IT host"));
        var source = Open(Unique("EdgeStage IT source"));
        int liveBefore = DwmThumbnailService.LiveRegistrations;

        var reg = DwmThumbnailService.TryRegister(Handle(host), Handle(source), out int hr);

        Assert.IsNotNull(reg, $"DwmRegisterThumbnail failed: 0x{hr:X8}");
        Assert.AreEqual(liveBefore + 1, DwmThumbnailService.LiveRegistrations);
        Assert.IsTrue(reg.SourceSize.Width > 0 && reg.SourceSize.Height > 0, $"source size {reg.SourceSize.Width}x{reg.SourceSize.Height}");
        var placement = ThumbnailGeometry.Compute(new PixelRect(10, 10, 200, 125), new PixelRect(0, 0, 400, 300), reg.SourceSize);
        Assert.IsTrue(reg.Apply(placement));

        source.Close();
        Pump(300);
        var moved = ThumbnailGeometry.Compute(new PixelRect(20, 20, 200, 125), new PixelRect(0, 0, 400, 300), reg.SourceSize);
        Assert.IsFalse(reg.Apply(moved), "DWM should reject updates once the source window is destroyed");
        Assert.IsTrue(reg.IsBroken);

        reg.Dispose();
        reg.Dispose(); // idempotent
        Assert.AreEqual(liveBefore, DwmThumbnailService.LiveRegistrations);
    }

    [STATestMethod]
    public void Thumbnail_ManyRegistrations_LeaveNoLiveHandles()
    {
        var host = Open(Unique("EdgeStage IT host"));
        var source = Open(Unique("EdgeStage IT source"));
        int liveBefore = DwmThumbnailService.LiveRegistrations;

        for (int i = 0; i < 300; i++)
        {
            using var reg = DwmThumbnailService.TryRegister(Handle(host), Handle(source), out _);
            Assert.IsNotNull(reg);
        }

        Assert.AreEqual(liveBefore, DwmThumbnailService.LiveRegistrations);
    }

    // ---- hotkeys ---------------------------------------------------------------------------------

    [STATestMethod]
    public void Hotkey_SecondRegistrationOfSameCombination_ReportsConflict()
    {
        var combo = new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x87 /* F24 */);
        using var first = new HotkeyService(0);
        using var second = new HotkeyService(0);

        var a = first.Register(1, combo);
        if (!a.Succeeded)
            Assert.Inconclusive($"{combo} is already taken on this machine ({a.Status}); cannot run the conflict test.");

        var b = second.Register(2, combo);
        Assert.AreEqual(HotkeyRegistrationStatus.Conflict, b.Status);
        Assert.AreEqual(ERROR_HOTKEY_ALREADY_REGISTERED, b.Win32Error);

        first.Unregister(1);
        Assert.IsTrue(second.Register(2, combo).Succeeded, "combination should be free after the first owner releases it");
    }

    [STATestMethod]
    public void Hotkey_ProbeReleasesTheCombination()
    {
        var combo = new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x86 /* F23 */);
        using var service = new HotkeyService(0);

        var probe = service.Probe(combo);
        if (!probe.Succeeded)
            Assert.Inconclusive($"{combo} is taken on this machine.");

        Assert.IsTrue(service.Register(5, combo).Succeeded);
    }

    [TestMethod]
    public void Hotkey_HandleMessage_RaisesPressedOnlyForOwnIds()
    {
        using var service = new HotkeyService(0);
        var combo = new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x85 /* F22 */);
        if (!service.Register(7, combo).Succeeded)
            Assert.Inconclusive("combination taken");
        var pressed = new List<int>();
        service.Pressed += pressed.Add;

        Assert.IsTrue(service.HandleMessage(WM_HOTKEY, 7));
        Assert.IsFalse(service.HandleMessage(WM_HOTKEY, 99));
        Assert.IsFalse(service.HandleMessage(WM_CLOSE, 7));
        CollectionAssert.AreEqual(new[] { 7 }, pressed);
    }

    [TestMethod]
    public void Hotkey_InvalidCombination_IsNotRegistered()
    {
        using var service = new HotkeyService(0);
        Assert.AreEqual(HotkeyRegistrationStatus.Invalid, service.Register(1, new Hotkey(HotkeyModifiers.None, 'A')).Status);
    }

    // ---- activation --------------------------------------------------------------------------------

    [STATestMethod]
    public void Activator_RestoresMinimizedWindow_AndActivatesIt_WhenWindowsAllows()
    {
        var target = Open(Unique("EdgeStage IT activate"));
        User32.ShowWindow(Handle(target), SW_MINIMIZE);
        Pump(300);
        Assert.IsTrue(User32.IsIconic(Handle(target)));

        var result = RunPumping(() => WindowActivator.ActivateAsync(Handle(target)));

        Assert.IsFalse(User32.IsIconic(Handle(target)), "the window must be restored even if activation is refused");
        if (!result.Succeeded)
            Assert.Inconclusive($"Windows refused foreground activation for the test runner (no user input): {result.Detail}");
        Assert.IsTrue(WindowActivator.IsForegroundFamily(Handle(target)));
    }

    [STATestMethod]
    public void Activator_ReportsGoneWindow()
    {
        var w = Open(Unique("EdgeStage IT gone"));
        nint hwnd = Handle(w);
        w.Close();
        Pump(100);

        var result = RunPumping(() => WindowActivator.ActivateAsync(hwnd));

        Assert.AreEqual(ActivationOutcome.WindowGone, result.Outcome);
    }

    [TestMethod]
    public void ProcessElevation_CanInspectCurrentProcess()
    {
        Assert.IsNotNull(ProcessElevation.IsCurrentProcessElevated());
    }

    // ---- window events ----------------------------------------------------------------------------

    [STATestMethod]
    public void Tracker_ReportsShowTitleChangeAndDestroy()
    {
        var events = new List<(WindowEventKind Kind, nint Hwnd)>();
        using var tracker = new WindowTracker(includeOwnProcess: true);
        tracker.WindowEvent += (k, h) => events.Add((k, h));
        tracker.Start();
        tracker.BeginDetailedTracking();
        Assert.IsTrue(tracker.IsDetailedTracking);

        var w = Open(Unique("EdgeStage IT tracked"));
        nint hwnd = Handle(w);
        w.Title = Unique("EdgeStage IT renamed");
        Pump(300);
        w.Close();
        Pump(300);

        var mine = events.Where(e => e.Hwnd == hwnd).Select(e => e.Kind).ToList();
        CollectionAssert.Contains(mine, WindowEventKind.Shown, string.Join(",", mine));
        CollectionAssert.Contains(mine, WindowEventKind.TitleChanged, string.Join(",", mine));
        CollectionAssert.Contains(mine, WindowEventKind.Destroyed, string.Join(",", mine));

        tracker.EndDetailedTracking();
        Assert.IsFalse(tracker.IsDetailedTracking);
    }

    [STATestMethod]
    public void Tracker_DetailedEventsStopAfterEnd()
    {
        var events = new List<(WindowEventKind Kind, nint Hwnd)>();
        using var tracker = new WindowTracker(includeOwnProcess: true);
        tracker.WindowEvent += (k, h) => events.Add((k, h));
        tracker.Start();
        tracker.BeginDetailedTracking();
        tracker.EndDetailedTracking();

        var w = Open(Unique("EdgeStage IT untracked"));
        w.Title = Unique("EdgeStage IT untracked renamed");
        Pump(300);

        Assert.IsFalse(events.Any(e => e.Hwnd == Handle(w) && e.Kind == WindowEventKind.TitleChanged));
    }

    // ---- monitors, icons, edge polling --------------------------------------------------------------

    [TestMethod]
    public void MonitorLayout_LoadsConsistentMonitors()
    {
        var layout = MonitorLayout.Load();

        Assert.IsTrue(layout.Monitors.Count >= 1);
        Assert.AreEqual(1, layout.Monitors.Count(m => m.IsPrimary));
        foreach (var m in layout.Monitors)
        {
            Assert.IsTrue(m.Dpi >= 96, $"dpi {m.Dpi}");
            Assert.IsTrue(m.WorkArea.Left >= m.Bounds.Left && m.WorkArea.Right <= m.Bounds.Right);
            Assert.IsTrue(m.WorkArea.Top >= m.Bounds.Top && m.WorkArea.Bottom <= m.Bounds.Bottom);
        }
        Assert.IsTrue(layout.EdgeSegments(ScreenEdge.Left).Count >= 1);
        Assert.IsTrue(layout.EdgeSegments(ScreenEdge.Right).Count >= 1);
    }

    [TestMethod]
    public void IconResolver_ExtractsExecutableIcon_AndReleasesIt()
    {
        string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var icon = IconResolver.FromExecutable(explorer);

        Assert.IsTrue(icon.IsValid);
        Assert.IsTrue(icon.OwnedByCaller);
        icon.Release();
        Assert.IsFalse(IconResolver.FromExecutable(@"C:\does\not\exist.exe").IsValid);
    }

    [TestMethod]
    public void EdgeTriggerService_PollsOnlyWhileEnabledAndNotPaused()
    {
        using var service = new EdgeTriggerService(new SynchronizationContext(), MonitorLayout.Load());

        service.Configure(enabled: false, ScreenEdge.Left, 300);
        Thread.Sleep(350);
        Assert.AreEqual(0, service.TickCount, "disabled edge activation must not poll at all");
        Assert.IsFalse(service.IsPolling);

        service.Configure(enabled: true, ScreenEdge.Left, 300);
        Thread.Sleep(450);
        long running = service.TickCount;
        Assert.IsTrue(running >= 2, $"expected polling ticks, got {running}");

        service.Pause();
        Thread.Sleep(150); // let an in-flight tick finish
        long paused = service.TickCount;
        Thread.Sleep(400);
        Assert.AreEqual(paused, service.TickCount, "paused service must not poll");

        service.Resume();
        Thread.Sleep(350);
        Assert.IsTrue(service.TickCount > paused);
    }
}
