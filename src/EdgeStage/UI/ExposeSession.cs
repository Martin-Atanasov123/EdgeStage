using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using EdgeStage.Core.Activation;
using EdgeStage.Core.Diagnostics;
using EdgeStage.Core.Input;
using EdgeStage.Core.Interop;
using EdgeStage.Core.Layout;
using EdgeStage.Core.Settings;
using EdgeStage.Core.WindowTracking;
using static EdgeStage.Core.Interop.NativeConstants;

namespace EdgeStage.UI;

/// <summary>
/// Exposé across displays. By default one overlay opens on every display, each showing the windows
/// on that display (so every window can be picked, wherever it is); the overlay under the pointer
/// takes the keyboard. The overlays act as one: search applies to all, any Esc / choice / background
/// click closes all, ← → continue onto the neighboring display, and the return to the previously
/// active window happens once.
/// </summary>
internal sealed class ExposeSession : IExposeHost
{
    private readonly WindowTracker _tracker;
    private readonly Dispatcher _dispatcher;
    private readonly List<ExposeWindow> _pool = [];
    private readonly List<ExposeWindow> _open = [];
    private readonly DispatcherTimer _releaseTimer;
    private nint _previous;
    private bool _closing;

    /// <summary>
    /// How long closed overlays are kept for a quick reopen. Each full-screen transparent overlay
    /// holds screen-sized surfaces (tens of MB), so they are destroyed when Exposé is not in use.
    /// </summary>
    public static readonly TimeSpan KeepOverlaysFor = TimeSpan.FromSeconds(15);

    public ExposeSession(WindowTracker tracker)
    {
        _tracker = tracker;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _releaseTimer = new DispatcherTimer(KeepOverlaysFor, DispatcherPriority.Background, (_, _) => ReleaseOverlays(), _dispatcher);
        _releaseTimer.Stop();
    }

    /// <summary>Overlay windows currently alive (open or kept for a quick reopen).</summary>
    public int OverlayWindowCount => _pool.Count;

    private void ReleaseOverlays()
    {
        _releaseTimer.Stop();
        if (IsOpen || _pool.Any(o => o.IsVisible))
        {
            if (!IsOpen)
                _releaseTimer.Start(); // a closing animation is still running: try again later
            return;
        }
        int count = _pool.Count;
        foreach (ExposeWindow overlay in _pool)
            overlay.Close();
        _pool.Clear();
        _open.Clear();
        if (count == 0)
            return;
        // An idle tray app allocates nothing, so the garbage collector would not run on its own and the
        // released overlays' memory (screen-sized surfaces) would stay committed. Collect once, after
        // the windows' native resources have been handed back.
        _dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            long before = Environment.WorkingSet;
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            Log.Info($"Exposé: released {count} idle overlay window(s); working set {before / 1048576} → {Environment.WorkingSet / 1048576} MB.");
        });
    }

    public bool IsOpen => _open.Any(o => o.IsOpen);

    public int CardCount => _open.Sum(o => o.IsOpen ? o.CardCount : 0);

    public int LiveThumbnailCount => _pool.Sum(o => o.LiveThumbnailCount);

    /// <summary>Raised once when Exposé closes, with the reason.</summary>
    public event Action<string>? Dismissed;

    /// <summary>Windows refused the switch (card title, result).</summary>
    public event Action<string, ActivationResult>? ActivationFailed;

    /// <summary>The user chose "Never show this app" for the given process name.</summary>
    public event Action<string>? HideAppRequested;

    /// <summary>Opens Exposé; <paramref name="pointerMonitor"/> is the display under the pointer (it takes the keyboard).</summary>
    public void Open(IReadOnlyList<MonitorArea> monitors, int pointerMonitor, AppSettings settings, ExposeOptions options)
    {
        if (IsOpen || monitors.Count == 0)
            return;
        _releaseTimer.Stop();
        pointerMonitor = Math.Clamp(pointerMonitor, 0, monitors.Count - 1);
        MonitorArea home = monitors[pointerMonitor];

        // Remember where to return on Esc, before EdgeStage itself takes the foreground.
        _previous = User32.GetForegroundWindow();
        _tracker.Mru.PruneWhere(User32.IsWindow);
        _closing = false;

        var plan = new List<(MonitorArea Monitor, Func<WindowInfo, bool> Listed)>();
        switch (options.Monitors)
        {
            case ExposeMonitors.AllOnOneDisplay:
                plan.Add((home, _ => true));
                break;
            case ExposeMonitors.ThisDisplay:
                plan.Add((home, OnDisplay(home)));
                break;
            default:
                // The keyboard overlay last, so it ends up in front and focused.
                plan.AddRange(monitors.Where(m => m != home).Select(m => (m, OnDisplay(m))));
                plan.Add((home, OnDisplay(home)));
                break;
        }

        while (_pool.Count < plan.Count)
        {
            var overlay = new ExposeWindow(_tracker, this);
            overlay.ApplySettings(settings);
            _pool.Add(overlay);
        }

        _open.Clear();
        for (int i = 0; i < plan.Count; i++)
        {
            ExposeWindow overlay = _pool[i];
            _open.Add(overlay);
            overlay.Open(plan[i].Monitor, settings, options, plan[i].Listed, _previous, activate: plan[i].Monitor == home);
        }
        Log.Info($"Exposé: {_open.Count} overlay(s) ({options.Monitors}), {CardCount} windows.");
    }

    private static Func<WindowInfo, bool> OnDisplay(MonitorArea monitor) =>
        w => User32.MonitorFromWindow(w.Hwnd, MONITOR_DEFAULTTONEAREST) == monitor.Handle;

    /// <summary>Closes Exposé; by default returns to the window that was active before it opened.</summary>
    public void Dismiss(string reason, bool immediate = false, bool returnToPrevious = true)
    {
        if (IsOpen)
            CloseAll(reason, immediate, target: null, returnToPrevious);
    }

    public void ApplySettings(AppSettings settings)
    {
        foreach (ExposeWindow overlay in _pool)
            overlay.ApplySettings(settings);
    }

    /// <summary>Closes the overlay windows for good (exit).</summary>
    public void Close()
    {
        _releaseTimer.Stop();
        foreach (ExposeWindow overlay in _pool)
            overlay.Close();
        _pool.Clear();
        _open.Clear();
    }

    private void CloseAll(string reason, bool immediate, CardViewModel? target, bool returnToPrevious)
    {
        if (_closing)
            return;
        _closing = true;
        nint previous = _previous;
        bool canReturn = returnToPrevious && previous != 0 && User32.IsWindow(previous)
                         && _pool.All(o => o.Handle != previous);
        // The previews fly back; the previous window is activated when they land, right before the
        // overlays disappear (this process keeps the foreground until then), so the desktop is revealed
        // in its old state in one step instead of the window jumping to the front under the dim.
        ExposeWindow? lead = _open.FirstOrDefault(o => o.IsActive) ?? _open.LastOrDefault(o => o.IsOpen);
        Log.Info($"Exposé dismissed: {reason}.");
        Dismissed?.Invoke(reason);
        _releaseTimer.Start();
        foreach (ExposeWindow overlay in _open)
            overlay.BeginClose(reason, immediate, target: null,
                landed: canReturn && overlay == lead ? () => _ = ReturnToPreviousAsync(previous) : null);
    }

    private static async Task ReturnToPreviousAsync(nint previous)
    {
        ActivationResult result = await WindowActivator.ActivateAsync(previous);
        if (result.Outcome != ActivationOutcome.Activated)
            Log.Info($"Could not return to the previous window 0x{previous:X}: {result.Outcome} ({result.Detail}).");
    }

    // ---- IExposeHost ---------------------------------------------------------------------------------

    void IExposeHost.RequestClose(ExposeWindow from, string reason, bool immediate, bool returnToPrevious) =>
        CloseAll(reason, immediate, target: null, returnToPrevious);

    async void IExposeHost.RequestActivate(ExposeWindow from, CardViewModel card)
    {
        if (_closing)
            return;
        _closing = true;
        Log.Info("Exposé dismissed: window selected.");
        Dismissed?.Invoke("window selected");
        _releaseTimer.Start();

        // Revalidate the chosen window right before the transition.
        if (!User32.IsWindow(card.Hwnd))
        {
            Log.Info($"Chosen window 0x{card.Hwnd:X} closed meanwhile; Exposé fades out.");
            foreach (ExposeWindow overlay in _open)
                overlay.BeginClose("chosen window closed", immediate: false, target: null);
            return;
        }
        // A minimized window is restored now, so its real bounds exist for the preview to land on.
        // Its own restore animation plays underneath the overlay while the preview flies.
        if (User32.IsIconic(card.Hwnd))
            User32.ShowWindow(card.Hwnd, SW_RESTORE);

        // Everything else is activated at the landing, right before the overlay disappears: until then
        // this process keeps the foreground (so Windows allows the switch), and the real window comes
        // forward exactly when its preview has reached it — no jump to the front under the dim.
        var activation = new TaskCompletionSource<ActivationResult>();
        foreach (ExposeWindow overlay in _open)
            overlay.BeginClose("window selected", immediate: false, overlay == from ? card : null,
                landed: overlay == from ? () => StartActivation(card, activation) : null);
        if (!from.IsVisible)
            StartActivation(card, activation); // safety net: the overlay was not on screen to land

        try
        {
            ActivationResult result = await activation.Task;
            if (result.Outcome == ActivationOutcome.Refused)
            {
                Log.Warn($"Activation refused for 0x{card.Hwnd:X} ({card.ProcessName}): {result.Detail}");
                ActivationFailed?.Invoke(card.Title, result);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Activation of 0x{card.Hwnd:X} failed", ex);
        }
    }

    /// <summary>Starts the activation once (its first, synchronous step brings the window forward).</summary>
    private static async void StartActivation(CardViewModel card, TaskCompletionSource<ActivationResult> done)
    {
        if (done.Task.IsCompleted)
            return;
        try
        {
            Task<ActivationResult> task = WindowActivator.ActivateAsync(card.Hwnd);
            done.TrySetResult(await task);
        }
        catch (Exception ex)
        {
            done.TrySetException(ex);
        }
    }

    void IExposeHost.FilterChanged(ExposeWindow from, string filter)
    {
        foreach (ExposeWindow overlay in _open)
        {
            overlay.SetFilter(filter);
            // Filtering selects the first match; only the overlay with the keyboard may show a selection.
            if (overlay != from)
                overlay.ClearSelection();
        }
    }

    void IExposeHost.NavigateBeyond(ExposeWindow from, NavDirection direction)
    {
        if (from.Monitor is not { } here)
            return;
        ExposeWindow? next = Neighbor(here, direction);
        if (next is null)
            return;
        from.ClearSelection();
        next.EnterFrom(direction);
    }

    /// <summary>The open overlay on the nearest display in <paramref name="direction"/>.</summary>
    private ExposeWindow? Neighbor(MonitorArea here, NavDirection direction)
    {
        var overlays = _open.Where(o => o.IsOpen && o.Monitor is not null).ToList();
        int from = overlays.FindIndex(o => o.Monitor == here);
        int next = DisplayNavigation.Neighbor(overlays.Select(o => ToBox(o.Monitor!.Bounds)).ToList(), from, direction);
        return next >= 0 ? overlays[next] : null;
    }

    private static CardBox ToBox(RECT r) => new(r.Left, r.Top, r.Width, r.Height);

    void IExposeHost.OverlayDeactivated(ExposeWindow from)
    {
        if (_closing)
            return;
        // Activation moves only after this notification; look once it has settled.
        _dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_closing || !IsOpen)
                return;
            nint foreground = User32.GetForegroundWindow();
            if (_open.Any(o => o.Handle == foreground))
            {
                from.ClearSelection(); // the keyboard moved to another display's overlay
                return;
            }
            // Another app took the foreground (Alt+Tab, a notification…): get out of the way and
            // never pull the user back.
            Log.Debug($"Exposé deactivated; foreground now belongs to {ProcessNameOf(foreground)}.");
            CloseAll("deactivated", immediate: true, target: null, returnToPrevious: false);
        });
    }

    void IExposeHost.HideApp(string processName) => HideAppRequested?.Invoke(processName);

    private static string ProcessNameOf(nint hwnd)
    {
        if (hwnd == 0)
            return "nobody";
        User32.GetWindowThreadProcessId(hwnd, out uint pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return $"{process.ProcessName} (0x{hwnd:X})";
        }
        catch (ArgumentException)
        {
            return $"pid {pid}";
        }
    }

#if DEBUG
    /// <summary>The overlay with the keyboard (test hooks act on it).</summary>
    private ExposeWindow? Active =>
        _open.FirstOrDefault(o => o.IsOpen && o.IsActive) ?? _open.LastOrDefault(o => o.IsOpen);

    internal void ActivateSelectedForTest() => Active?.ActivateSelectedForTest();
    internal void SelectForTest(string key) => Active?.SelectForTest(key);
    internal void TypeForTest(string text) => Active?.TypeForTest(text);
    internal void EscapeKeyForTest() => Active?.EscapeKeyForTest();
    internal void ToggleGroupingForTest() => Active?.ToggleGroupingForTest();
    internal void CloseSelectedForTest() => Active?.CloseSelectedForTest();
    internal string HitTestSelectedForTest() => Active?.HitTestSelectedForTest() ?? "no overlay";

    internal void SelectTitleForTest(string title)
    {
        foreach (ExposeWindow overlay in _open.Where(o => o.IsOpen))
            overlay.SelectTitleForTest(title);
    }

    internal string DescribeForTest() =>
        $"overlays={_open.Count(o => o.IsOpen)} | " + string.Join(" | ", _open.Where(o => o.IsOpen).Select(o => o.DescribeForTest()));
#endif
}
