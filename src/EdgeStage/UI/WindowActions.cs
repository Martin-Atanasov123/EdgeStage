using EdgeStage.Core.Diagnostics;
using EdgeStage.Core.Interop;
using static EdgeStage.Core.Interop.NativeConstants;

namespace EdgeStage.UI;

/// <summary>Actions on other apps' windows, shared by the sidebar and Exposé.</summary>
internal static class WindowActions
{
    /// <summary>
    /// Asks the window to close, exactly like its own close button (WM_CLOSE): the app may ask to
    /// save first or refuse. The process is never terminated. The card disappears when the window
    /// is actually destroyed (window events).
    /// </summary>
    public static bool RequestClose(CardViewModel card)
    {
        if (!User32.PostMessage(card.Hwnd, WM_CLOSE, 0, 0))
        {
            Log.Warn($"Could not post WM_CLOSE to 0x{card.Hwnd:X} ({card.ProcessName}).");
            card.Notice = "Couldn't ask the window to close";
            return false;
        }
        Log.Info($"Asked {card.ProcessName} (0x{card.Hwnd:X}) to close.");
        return true;
    }
}
