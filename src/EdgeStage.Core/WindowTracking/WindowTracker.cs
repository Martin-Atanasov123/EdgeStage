using System.Runtime.InteropServices;
using EdgeStage.Core.Diagnostics;
using EdgeStage.Core.Interop;
using static EdgeStage.Core.Interop.NativeConstants;

namespace EdgeStage.Core.WindowTracking;

public enum WindowEventKind
{
    Foreground,
    Created,
    Destroyed,
    Shown,
    Hidden,
    TitleChanged,
    MinimizeStart,
    MinimizeEnd,
    Cloaked,
    Uncloaked,
}

/// <summary>
/// Event-driven window tracking via <c>SetWinEventHook</c> (out-of-context, so no DLL injection).
/// <list type="bullet">
/// <item>Always on: only <c>EVENT_SYSTEM_FOREGROUND</c>, to keep the most-recently-used order. This
/// fires a few times per user action, so it costs nothing measurable while the sidebar is hidden.</item>
/// <item>While the sidebar is visible (<see cref="BeginDetailedTracking"/>): create/destroy/show/hide,
/// title, minimize and cloak events so the open sidebar stays current.</item>
/// </list>
/// Callbacks arrive on the thread that installed the hooks, which must pump messages (the WPF UI
/// thread). Must be created, used and disposed on that thread.
/// </summary>
public sealed unsafe class WindowTracker : IDisposable
{
    private static readonly Dictionary<nint, WindowTracker> HookOwners = [];

    private readonly uint _flags;
    private nint _foregroundHook;
    private readonly List<nint> _detailHooks = [];
    private int _detailUsers;
    private bool _disposed;

    /// <param name="includeOwnProcess">Also report EdgeStage's own windows (tests only).</param>
    public WindowTracker(bool includeOwnProcess = false)
    {
        _flags = WINEVENT_OUTOFCONTEXT | (includeOwnProcess ? 0 : WINEVENT_SKIPOWNPROCESS);
    }

    public MruOrder Mru { get; } = new();

    /// <summary>Raised on the owning thread for top-level window events.</summary>
    public event Action<WindowEventKind, nint>? WindowEvent;

    public bool IsDetailedTracking => _detailHooks.Count > 0;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_foregroundHook != 0)
            return;
        Mru.Touch(User32.GetForegroundWindow());
        _foregroundHook = Install(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND);
        if (_foregroundHook == 0)
            Log.Warn("WindowTracker: foreground hook could not be installed; MRU order falls back to Z-order.");
    }

    /// <summary>
    /// Installs the detailed hooks. Calls are counted (the sidebar and Exposé can overlap briefly);
    /// the hooks stay until every <see cref="BeginDetailedTracking"/> has its <see cref="EndDetailedTracking"/>.
    /// </summary>
    public void BeginDetailedTracking()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _detailUsers++;
        if (_detailHooks.Count > 0)
            return;
        // Narrow ranges keep unrelated accessibility events (focus, selection, value changes…) away.
        (uint Min, uint Max)[] ranges =
        [
            (EVENT_OBJECT_CREATE, EVENT_OBJECT_HIDE),
            (EVENT_OBJECT_NAMECHANGE, EVENT_OBJECT_NAMECHANGE),
            (EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED),
            (EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND),
        ];
        foreach (var (min, max) in ranges)
        {
            nint hook = Install(min, max);
            if (hook != 0)
                _detailHooks.Add(hook);
            else
                Log.Warn($"WindowTracker: hook 0x{min:X}-0x{max:X} could not be installed.");
        }
    }

    public void EndDetailedTracking()
    {
        if (_detailUsers > 0)
            _detailUsers--;
        if (_detailUsers > 0 && !_disposed)
            return;
        foreach (nint hook in _detailHooks)
            Uninstall(hook);
        _detailHooks.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        EndDetailedTracking();
        if (_foregroundHook != 0)
        {
            Uninstall(_foregroundHook);
            _foregroundHook = 0;
        }
    }

    private nint Install(uint min, uint max)
    {
        nint hook = User32.SetWinEventHook(min, max, 0, &OnWinEvent, 0, 0, _flags);
        if (hook != 0)
        {
            lock (HookOwners)
                HookOwners[hook] = this;
        }
        return hook;
    }

    private static void Uninstall(nint hook)
    {
        User32.UnhookWinEvent(hook);
        lock (HookOwners)
            HookOwners.Remove(hook);
    }

    [UnmanagedCallersOnly]
    private static void OnWinEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint threadId, uint timeMs)
    {
        // Exceptions must never cross back into native code: that would terminate the process.
        try
        {
            if (hwnd == 0 || idObject != OBJID_WINDOW || idChild != CHILDID_SELF)
                return;

            WindowTracker? owner;
            lock (HookOwners)
                HookOwners.TryGetValue(hook, out owner);
            if (owner is null || owner._disposed)
                return;

            WindowEventKind? kind = eventType switch
            {
                EVENT_SYSTEM_FOREGROUND => WindowEventKind.Foreground,
                EVENT_OBJECT_CREATE => WindowEventKind.Created,
                EVENT_OBJECT_DESTROY => WindowEventKind.Destroyed,
                EVENT_OBJECT_SHOW => WindowEventKind.Shown,
                EVENT_OBJECT_HIDE => WindowEventKind.Hidden,
                EVENT_OBJECT_NAMECHANGE => WindowEventKind.TitleChanged,
                EVENT_SYSTEM_MINIMIZESTART => WindowEventKind.MinimizeStart,
                EVENT_SYSTEM_MINIMIZEEND => WindowEventKind.MinimizeEnd,
                EVENT_OBJECT_CLOAKED => WindowEventKind.Cloaked,
                EVENT_OBJECT_UNCLOAKED => WindowEventKind.Uncloaked,
                _ => null,
            };
            if (kind is null)
                return;

            // Destroyed windows can no longer be queried; for everything else only top-level windows matter.
            if (kind != WindowEventKind.Destroyed && User32.GetAncestor(hwnd, GA_ROOT) != hwnd)
                return;

            if (kind == WindowEventKind.Foreground)
                owner.Mru.Touch(hwnd);
            else if (kind == WindowEventKind.Destroyed)
                owner.Mru.Forget(hwnd);

            owner.WindowEvent?.Invoke(kind.Value, hwnd);
        }
        catch (Exception ex)
        {
            Log.Error("WindowTracker callback failed", ex);
        }
    }
}
