using EdgeStage.Core.Interop;
using static EdgeStage.Core.Interop.NativeConstants;

namespace EdgeStage.Core.WindowTracking;

/// <summary>An HICON plus whether the caller must destroy it. Window/class icons belong to the window.</summary>
public readonly record struct IconHandle(nint Handle, bool OwnedByCaller)
{
    public bool IsValid => Handle != 0;

    public void Release()
    {
        if (OwnedByCaller && Handle != 0)
            User32.DestroyIcon(Handle);
    }
}

/// <summary>
/// Finds the best icon for a window: the window's own icon, then its class icon, then the icon
/// embedded in the process executable. Safe to call from a background thread; uses
/// <c>SendMessageTimeout</c> so a hung application can delay this by at most ~50 ms per message.
/// </summary>
public static class IconResolver
{
    private const uint TimeoutMs = 50;

    public static IconHandle Resolve(nint hwnd, string? processPath)
    {
        foreach (int kind in (ReadOnlySpan<int>)[ICON_BIG, ICON_SMALL2, ICON_SMALL])
        {
            if (User32.SendMessageTimeout(hwnd, WM_GETICON, kind, 0, SMTO_ABORTIFHUNG | SMTO_BLOCK, TimeoutMs, out nint icon) != 0 && icon != 0)
                return new IconHandle(icon, OwnedByCaller: false);
        }

        nint classIcon = User32.GetClassLongPtr(hwnd, GCLP_HICON);
        if (classIcon == 0)
            classIcon = User32.GetClassLongPtr(hwnd, GCLP_HICONSM);
        if (classIcon != 0)
            return new IconHandle(classIcon, OwnedByCaller: false);

        // UWP apps are hosted in ApplicationFrameHost; the real app process owns the child CoreWindow.
        if (WindowQuery.GetClassName(hwnd) == "ApplicationFrameWindow")
        {
            nint core = User32.FindWindowEx(hwnd, 0, "Windows.UI.Core.CoreWindow", null);
            if (core != 0)
                processPath = WindowEnumerator.GetProcessPath(WindowQuery.GetProcessId(core)) ?? processPath;
        }

        return FromExecutable(processPath);
    }

    public static IconHandle FromExecutable(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return default;
        uint count = Shell.ExtractIconEx(path, 0, out nint large, out nint small, 1);
        if (count == 0)
            return default;
        if (large != 0)
        {
            if (small != 0)
                User32.DestroyIcon(small);
            return new IconHandle(large, OwnedByCaller: true);
        }
        return small != 0 ? new IconHandle(small, OwnedByCaller: true) : default;
    }
}
