using EdgeStage.Core.Diagnostics;
using EdgeStage.Core.Interop;
using static EdgeStage.Core.Interop.NativeConstants;

namespace EdgeStage.Core.Input;

/// <summary>
/// Watches for the pointer resting at the configured screen edge.
/// </summary>
/// <remarks>
/// Uses light cursor polling (<c>GetCursorPos</c> + <c>GetAsyncKeyState</c>) on a one-shot
/// thread-pool timer rather than a low-level mouse hook. A WH_MOUSE_LL hook sits in the
/// system-wide input path: every mouse event (up to 1,000/s with gaming mice) is routed through
/// this process, and any pause here (e.g. a garbage collection) would stall the cursor for every
/// application. Polling never touches other applications' input. The interval adapts: slow when
/// the pointer is far from the edge, faster near it. Polling stops entirely while edge activation
/// is disabled, the sidebar is open, or the session is locked.
/// </remarks>
public sealed class EdgeTriggerService : IDisposable
{
    public const int FarIntervalMs = 100;
    public const int NearIntervalMs = 40;
    private const int NearDistancePx = 48;

    private readonly SynchronizationContext _context;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private readonly EdgeTriggerStateMachine _machine = new(300);
    private MonitorLayout _layout;
    private ScreenEdge _edge = ScreenEdge.Left;
    private bool _enabled;
    private bool _allowInFullscreen;
    private bool _paused;
    private bool _disposed;
    private long _ticks;

    public EdgeTriggerService(SynchronizationContext context, MonitorLayout layout)
    {
        _context = context;
        _layout = layout;
        _timer = new Timer(_ => OnTick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Raised on the captured context with the monitor index and the layout it refers to.</summary>
    public event Action<int, MonitorLayout>? Triggered;

    /// <summary>Number of polling ticks executed (diagnostics: proves polling stops when idle-disabled).</summary>
    public long TickCount => Interlocked.Read(ref _ticks);

    public bool IsPolling { get; private set; }

    /// <param name="allowInFullscreen">Also trigger while a full-screen app is in front of that monitor (off by default: games and videos are never interrupted).</param>
    public void Configure(bool enabled, ScreenEdge edge, int delayMs, bool allowInFullscreen = false)
    {
        lock (_gate)
        {
            _enabled = enabled;
            _allowInFullscreen = allowInFullscreen;
            _edge = edge;
            _machine.DelayMs = delayMs;
            _machine.Reset();
        }
        Reschedule(FarIntervalMs);
    }

    public void UpdateLayout(MonitorLayout layout)
    {
        lock (_gate)
        {
            _layout = layout;
            _machine.Reset();
        }
    }

    /// <summary>Stops polling (sidebar open, session locked). The pointer must leave the edge before the next trigger.</summary>
    public void Pause()
    {
        lock (_gate)
        {
            _paused = true;
        }
        Reschedule(Timeout.Infinite);
    }

    public void Resume()
    {
        lock (_gate)
        {
            _paused = false;
            // If the pointer is still parked at the edge (e.g. the sidebar was dismissed with Esc),
            // require it to leave first so the sidebar does not immediately reopen.
            if (GetCursor(out POINT p) && _layout.ZoneAt(p, _edge) is not null)
                _machine.SuppressUntilExit();
        }
        Reschedule(FarIntervalMs);
    }

    private void Reschedule(int dueMs)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            bool run = _enabled && !_paused && dueMs != Timeout.Infinite;
            IsPolling = run;
            _timer.Change(run ? dueMs : Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void OnTick()
    {
        int next = FarIntervalMs;
        try
        {
            Interlocked.Increment(ref _ticks);
            int? fire = null;
            MonitorLayout layout;
            lock (_gate)
            {
                if (_disposed || !_enabled || _paused)
                    return;
                layout = _layout;
                if (!GetCursor(out POINT p))
                    return;

                int? zone = layout.ZoneAt(p, _edge);
                fire = _machine.Update(Environment.TickCount64, zone, AnyButtonDown(),
                    monitor => !_allowInFullscreen && FullscreenDetector.IsFullscreenOn(layout.Monitors[monitor]));
                next = zone is not null || layout.DistanceToEdge(p, _edge) <= NearDistancePx ? NearIntervalMs : FarIntervalMs;
            }

            if (fire is int monitorIndex)
                _context.Post(_ => Triggered?.Invoke(monitorIndex, layout), null);
        }
        catch (Exception ex)
        {
            Log.Error("Edge trigger tick failed", ex);
        }
        finally
        {
            Reschedule(next);
        }
    }

    private static bool GetCursor(out POINT p) => User32.GetCursorPos(out p);

    private static bool AnyButtonDown()
    {
        // High bit set = currently down. Physical buttons, so swapped-button setups behave the same.
        foreach (int vk in (ReadOnlySpan<int>)[VK_LBUTTON, VK_RBUTTON, VK_MBUTTON, VK_XBUTTON1, VK_XBUTTON2])
            if ((User32.GetAsyncKeyState(vk) & 0x8000) != 0)
                return true;
        return false;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            IsPolling = false;
        }
        _timer.Dispose();
    }
}
