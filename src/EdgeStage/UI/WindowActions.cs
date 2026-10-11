using EdgeStage.Core.Diagnostics;
using EdgeStage.Core.Interop;
using static EdgeStage.Core.Interop.NativeConstants;

namespace EdgeStage.UI;

/// <summary>Actions on other apps' windows, shared by the sidebar and Exposé.</summary>
internal static class WindowActions
{
    /// <summary>How long a window may take to close before its card explains why it is still there.</summary>
    public static readonly TimeSpan CloseGracePeriod = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Asks the window to close, exactly like its own close button (WM_CLOSE): the app may ask to
    /// save first or refuse. The process is never terminated. The card disappears when the window
    /// is actually destroyed (window events); if it is still open after <see cref="CloseGracePeriod"/>,
    /// the card says so.
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
        card.Notice = null;
        _ = FollowUpAsync(card);
        return true;
    }

    private static async Task FollowUpAsync(CardViewModel card)
    {
        await Task.Delay(CloseGracePeriod); // resumes on the UI thread
        if (User32.IsWindow(card.Hwnd) && User32.IsWindowVisible(card.Hwnd))
        {
            Log.Info($"{card.ProcessName} (0x{card.Hwnd:X}) is still open {CloseGracePeriod.TotalSeconds:0} s after the close request.");
            card.Notice = "Still open — the app may be asking something";
        }
    }
}
