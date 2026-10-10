using System.Runtime.InteropServices;
using EdgeStage.Core.Interop;
using static EdgeStage.Core.Interop.NativeConstants;

namespace EdgeStage.Core.WindowTracking;

/// <summary>Enumerates switchable top-level windows in Z-order (topmost first).</summary>
public static unsafe class WindowEnumerator
{
    private static readonly uint OwnProcessId = (uint)Environment.ProcessId;

    /// <summary>Process image paths cached by PID; PIDs are reused rarely enough for a UI hint.</summary>
    private static readonly Dictionary<uint, string?> ProcessPathCache = [];

    /// <param name="excludeProcessId">Process whose windows are never listed (EdgeStage itself by default).</param>
    public static List<WindowInfo> Enumerate(uint? excludeProcessId = null)
    {
        uint exclude = excludeProcessId ?? OwnProcessId;
        var result = new List<WindowInfo>();
        foreach (nint hwnd in TopLevelWindows())
        {
            // Cheapest test first: most top-level windows are invisible helpers.
            if (!User32.IsWindowVisible(hwnd))
                continue;

            WindowFacts facts = ReadFacts(hwnd);
            if (!WindowFilter.IsSwitchable(facts, exclude))
                continue;

            result.Add(new WindowInfo(
                hwnd,
                facts.ProcessId,
                WindowQuery.GetTitle(hwnd),
                facts.ClassName,
                GetProcessPath(facts.ProcessId),
                User32.IsIconic(hwnd)));
        }
        return result;
    }

    /// <summary>Re-reads mutable facts for one window, or null when it should no longer be listed.</summary>
    public static WindowInfo? TryRead(nint hwnd, uint? excludeProcessId = null)
    {
        if (!User32.IsWindow(hwnd) || !User32.IsWindowVisible(hwnd))
            return null;
        WindowFacts facts = ReadFacts(hwnd);
        if (!WindowFilter.IsSwitchable(facts, excludeProcessId ?? OwnProcessId))
            return null;
        return new WindowInfo(hwnd, facts.ProcessId, WindowQuery.GetTitle(hwnd), facts.ClassName,
            GetProcessPath(facts.ProcessId), User32.IsIconic(hwnd));
    }

    public static WindowFacts ReadFacts(nint hwnd)
    {
        User32.GetWindowRect(hwnd, out RECT rect);
        nint owner = User32.GetWindow(hwnd, GW_OWNER);
        nint rootOwner = owner == 0 ? 0 : WindowQuery.GetRootOwner(hwnd);
        bool rootOwnerVisible = rootOwner != 0 && rootOwner != hwnd
            && User32.IsWindowVisible(rootOwner) && !Dwmapi.IsCloaked(rootOwner);
        return new WindowFacts(
            hwnd,
            WindowQuery.GetProcessId(hwnd),
            WindowQuery.GetClassName(hwnd),
            WindowQuery.GetStyle(hwnd),
            WindowQuery.GetExStyle(hwnd),
            User32.IsWindowVisible(hwnd),
            Dwmapi.IsCloaked(hwnd),
            owner,
            rootOwnerVisible,
            rect.Width,
            rect.Height);
    }

    public static string? GetProcessPath(uint processId)
    {
        lock (ProcessPathCache)
        {
            if (ProcessPathCache.TryGetValue(processId, out string? cached))
                return cached;
        }
        string? path = WindowQuery.GetProcessImagePath(processId);
        lock (ProcessPathCache)
        {
            if (ProcessPathCache.Count > 512)
                ProcessPathCache.Clear();
            ProcessPathCache[processId] = path;
        }
        return path;
    }

    private static List<nint> TopLevelWindows()
    {
        var handles = new List<nint>(256);
        var gc = GCHandle.Alloc(handles);
        try
        {
            User32.EnumWindows(&Collect, GCHandle.ToIntPtr(gc));
        }
        finally
        {
            gc.Free();
        }
        return handles;
    }

    [UnmanagedCallersOnly]
    private static int Collect(nint hwnd, nint lParam)
    {
        ((List<nint>)GCHandle.FromIntPtr(lParam).Target!).Add(hwnd);
        return 1;
    }
}
