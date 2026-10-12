using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using EdgeStage.Core.Interop;
using static EdgeStage.Core.Interop.NativeConstants;

namespace EdgeStage.UI;

/// <summary>
/// Per-pixel transparency without a layered window. WPF draws into its normal GPU surface, cleared
/// to transparent, and DWM composites it with alpha because the frame is extended over the whole
/// window ("glass"). A layered window (<c>AllowsTransparency</c>) instead has every frame copied into
/// screen-sized system-memory bitmaps, which for full-screen overlays costs tens of MB that the
/// process keeps afterwards. Used for Exposé's full-screen overlays and their corner masks.
/// </summary>
internal static class GlassWindow
{
    private const int HTTRANSPARENT = -1;
    private const int WM_NCHITTEST = 0x0084;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

    /// <summary>Call once the HWND exists (before the window is shown).</summary>
    /// <param name="clickThrough">Let mouse input pass to the windows below (same-thread windows such as the owner).</param>
    public static void Apply(Window window, nint hwnd, bool clickThrough = false)
    {
        if (HwndSource.FromHwnd(hwnd) is { } source)
        {
            if (source.CompositionTarget is { } target)
                target.BackgroundColor = Colors.Transparent;
            if (clickThrough)
                source.AddHook(HitTestTransparent);
        }
        Dwmapi.DwmExtendFrameIntoClientArea(hwnd, MARGINS.All(-1));
        // No Windows 11 rounded corners or accent border on a full-screen overlay.
        Dwmapi.SetIntAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_DONOTROUND);
        Dwmapi.SetIntAttribute(hwnd, DWMWA_BORDER_COLOR, DWMWA_COLOR_NONE);
        window.Background = Brushes.Transparent;
    }

    private static nint HitTestTransparent(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WM_NCHITTEST)
            return 0;
        handled = true;
        return HTTRANSPARENT;
    }
}
