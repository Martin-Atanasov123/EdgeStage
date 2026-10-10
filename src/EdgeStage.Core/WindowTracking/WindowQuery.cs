using EdgeStage.Core.Interop;
using static EdgeStage.Core.Interop.NativeConstants;

namespace EdgeStage.Core.WindowTracking;

/// <summary>Thin, allocation-conscious wrappers for reading facts about a top-level window.</summary>
public static class WindowQuery
{
    public static string GetTitle(nint hwnd)
    {
        int length = User32.GetWindowTextLength(hwnd);
        if (length <= 0)
            return string.Empty;

        Span<char> buffer = length < 512 ? stackalloc char[length + 1] : new char[length + 1];
        unsafe
        {
            fixed (char* p = buffer)
            {
                int copied = User32.GetWindowText(hwnd, p, buffer.Length);
                return new string(buffer[..Math.Max(0, copied)]);
            }
        }
    }

    public static string GetClassName(nint hwnd)
    {
        Span<char> buffer = stackalloc char[256];
        unsafe
        {
            fixed (char* p = buffer)
            {
                int copied = User32.GetClassName(hwnd, p, buffer.Length);
                return new string(buffer[..Math.Max(0, copied)]);
            }
        }
    }

    public static uint GetProcessId(nint hwnd)
    {
        User32.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid;
    }

    /// <summary>
    /// Top of the owner chain (the window itself when unowned). Walks GW_OWNER explicitly:
    /// GetAncestor(GA_ROOTOWNER) follows GetParent, which only reports the owner of WS_POPUP
    /// windows, so it misses owned overlapped windows such as WPF/WinForms dialogs.
    /// </summary>
    public static nint GetRootOwner(nint hwnd)
    {
        nint current = hwnd;
        for (int depth = 0; depth < 64; depth++)
        {
            nint owner = User32.GetWindow(current, GW_OWNER);
            if (owner == 0 || owner == current)
                break;
            current = owner;
        }
        return current;
    }

    public static long GetStyle(nint hwnd) => User32.GetWindowLongPtr(hwnd, GWL_STYLE);

    public static long GetExStyle(nint hwnd) => User32.GetWindowLongPtr(hwnd, GWL_EXSTYLE);

    /// <summary>Full path of the process image, or null when access is denied (e.g. protected processes).</summary>
    public static string? GetProcessImagePath(uint processId)
    {
        nint process = Kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == 0)
            return null;

        try
        {
            Span<char> buffer = stackalloc char[1024];
            int size = buffer.Length;
            unsafe
            {
                fixed (char* p = buffer)
                {
                    return Kernel32.QueryFullProcessImageName(process, 0, p, ref size) ? new string(buffer[..size]) : null;
                }
            }
        }
        finally
        {
            Kernel32.CloseHandle(process);
        }
    }
}
