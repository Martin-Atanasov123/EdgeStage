using EdgeStage.Core.Interop;
using EdgeStage.Core.WindowTracking;
using static EdgeStage.Core.Interop.NativeConstants;

namespace EdgeStage.Core.Input;

/// <summary>Detects whether a full-screen application (game, video, presentation) owns a monitor.</summary>
public static class FullscreenDetector
{
    private const long WS_CAPTION = 0x00C00000L;

    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
    };

    /// <summary>
    /// Pure rule: a window is full-screen on a monitor when it covers the whole monitor and is not
    /// an ordinary captioned window (a maximized window with an auto-hidden taskbar also covers the
    /// monitor but keeps its caption).
    /// </summary>
    public static bool IsFullscreen(RECT window, long style, RECT monitor) =>
        window.Left <= monitor.Left && window.Top <= monitor.Top
        && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom
        && (style & WS_CAPTION) != WS_CAPTION;

    public static bool IsFullscreenOn(MonitorArea monitor)
    {
        // Exclusive Direct3D and presentation mode are reported by the shell directly.
        if (Shell.SHQueryUserNotificationState(out int state) >= 0
            && state is Shell.QUNS_RUNNING_D3D_FULL_SCREEN or Shell.QUNS_PRESENTATION_MODE)
            return true;

        nint foreground = User32.GetForegroundWindow();
        if (foreground == 0 || ShellClasses.Contains(WindowQuery.GetClassName(foreground)))
            return false;
        if (User32.MonitorFromWindow(foreground, MONITOR_DEFAULTTONULL) != monitor.Handle)
            return false;
        if (!User32.GetWindowRect(foreground, out RECT rect))
            return false;
        return IsFullscreen(rect, WindowQuery.GetStyle(foreground), monitor.Bounds);
    }
}
