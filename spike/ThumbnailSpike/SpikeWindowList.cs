using System.Runtime.InteropServices;
using EdgeStage.Core.Interop;
using EdgeStage.Core.WindowTracking;
using static EdgeStage.Core.Interop.NativeConstants;

namespace ThumbnailSpike;

internal sealed record SpikeWindow(nint Hwnd, string Title, string ClassName, uint ProcessId, bool IsMinimized, bool IsCloaked);

/// <summary>Minimal Alt+Tab-style enumeration for the spike (the production filter is built in Phase 2).</summary>
internal static unsafe class SpikeWindowList
{
    public static List<SpikeWindow> Enumerate(bool includeCloaked = false)
    {
        var handles = new List<nint>();
        var gc = GCHandle.Alloc(handles);
        try
        {
            User32.EnumWindows(&Collect, GCHandle.ToIntPtr(gc));
        }
        finally
        {
            gc.Free();
        }

        uint self = (uint)Environment.ProcessId;
        var result = new List<SpikeWindow>();
        foreach (nint hwnd in handles)
        {
            if (!User32.IsWindowVisible(hwnd))
                continue;
            long ex = WindowQuery.GetExStyle(hwnd);
            if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0)
                continue;
            if ((ex & WS_EX_NOACTIVATE) != 0)
                continue;
            nint rootOwner = User32.GetAncestor(hwnd, GA_ROOTOWNER);
            if (User32.GetLastActivePopup(rootOwner) != hwnd && rootOwner != hwnd)
                continue;
            uint pid = WindowQuery.GetProcessId(hwnd);
            if (pid == self)
                continue;
            bool cloaked = Dwmapi.IsCloaked(hwnd);
            if (cloaked && !includeCloaked)
                continue;
            string title = WindowQuery.GetTitle(hwnd);
            string cls = WindowQuery.GetClassName(hwnd);
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                continue;
            result.Add(new SpikeWindow(hwnd, title, cls, pid, User32.IsIconic(hwnd), cloaked));
        }
        return result;
    }

    public static nint FindByTitle(string title)
    {
        foreach (var w in Enumerate(includeCloaked: true))
            if (w.Title == title)
                return w.Hwnd;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int Collect(nint hwnd, nint lParam)
    {
        ((List<nint>)GCHandle.FromIntPtr(lParam).Target!).Add(hwnd);
        return 1; // continue enumeration
    }
}
