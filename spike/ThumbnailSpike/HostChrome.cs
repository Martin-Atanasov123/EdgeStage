using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using EdgeStage.Core.Interop;
using static EdgeStage.Core.Interop.NativeConstants;

namespace ThumbnailSpike;

/// <summary>Applies one of the candidate <see cref="HostConfig"/> window configurations.</summary>
internal static class HostChrome
{
    private static readonly SolidColorBrush Charcoal = new(Color.FromRgb(0x20, 0x20, 0x20));

    /// <summary>WPF-level settings; must run before the window handle is created.</summary>
    public static void Configure(Window window, HostConfig config)
    {
        switch (config)
        {
            case HostConfig.Solid:
            case HostConfig.LayeredAlpha:
                window.WindowStyle = WindowStyle.None;
                window.Background = Charcoal;
                break;
            case HostConfig.AcrylicNoFrame:
                window.WindowStyle = WindowStyle.None;
                window.Background = Brushes.Transparent;
                break;
            case HostConfig.AcrylicChrome:
                window.WindowStyle = WindowStyle.SingleBorderWindow;
                window.Background = Brushes.Transparent;
                WindowChrome.SetWindowChrome(window, new WindowChrome
                {
                    CaptionHeight = 0,
                    GlassFrameThickness = new Thickness(-1),
                    ResizeBorderThickness = new Thickness(0),
                    UseAeroCaptionButtons = false,
                });
                break;
            case HostConfig.WpfAllowsTransparency:
                window.WindowStyle = WindowStyle.None;
                window.AllowsTransparency = true;
                window.Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x20, 0x20, 0x20));
                break;
        }
    }

    /// <summary>Native settings applied once the HWND exists; returns the HRESULTs for reporting.</summary>
    public static string ApplyNative(nint hwnd, HostConfig config)
    {
        var log = new List<string>
        {
            $"darkmode=0x{Dwmapi.SetIntAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, 1):X8}",
            $"corner=0x{Dwmapi.SetIntAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND):X8}",
        };

        if (config is HostConfig.AcrylicNoFrame or HostConfig.AcrylicChrome)
        {
            // Let the DWM backdrop show through: WPF must clear its swap chain to transparent.
            HwndSource.FromHwnd(hwnd)!.CompositionTarget!.BackgroundColor = Colors.Transparent;
            log.Add($"extendFrame=0x{Dwmapi.DwmExtendFrameIntoClientArea(hwnd, MARGINS.All(-1)):X8}");
            log.Add($"backdrop=0x{Dwmapi.SetIntAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_TRANSIENTWINDOW):X8}");
        }

        if (config == HostConfig.LayeredAlpha)
        {
            nint ex = User32.GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            User32.SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)(ex | WS_EX_LAYERED));
            int setErr = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
            bool ok = User32.SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
            int err = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
            long exAfter = User32.GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            log.Add($"layeredAlpha={ok} (err {err}, setLong err {setErr}, exstyle 0x{exAfter:X})");
        }

        return string.Join(", ", log);
    }
}
