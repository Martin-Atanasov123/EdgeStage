using static EdgeStage.Core.Interop.NativeConstants;

namespace EdgeStage.Core.WindowTracking;

/// <summary>Raw facts about one top-level window, gathered natively and evaluated by <see cref="WindowFilter"/>.</summary>
public readonly record struct WindowFacts(
    nint Hwnd,
    uint ProcessId,
    string ClassName,
    long Style,
    long ExStyle,
    bool IsVisible,
    bool IsCloaked,
    nint Owner,
    bool RootOwnerVisible,
    int Width,
    int Height);

/// <summary>Why a window was excluded (for tests and diagnostics); <see cref="Included"/> means it is switchable.</summary>
public enum FilterVerdict
{
    Included,
    Invisible,
    Cloaked,
    ChildWindow,
    ToolWindow,
    NoActivate,
    OwnedWindow,
    OwnProcess,
    ShellWindow,
    ZeroSize,
}

/// <summary>
/// Decides which top-level windows belong in the sidebar, using the same structural rules as
/// Alt+Tab. It never looks at titles: titles change and may legitimately be empty.
/// </summary>
public static class WindowFilter
{
    /// <summary>Shell and system surfaces that are visible top-level windows but never "apps".</summary>
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman",                       // desktop
        "WorkerW",                       // desktop wallpaper host
        "Shell_TrayWnd",                 // primary taskbar
        "Shell_SecondaryTrayWnd",        // taskbar on other monitors
        "Windows.UI.Core.CoreWindow",    // UWP content windows (their ApplicationFrameWindow is listed instead)
        "XamlExplorerHostIslandWindow",  // shell flyouts (Start, Search, Task View hosts)
        "TopLevelWindowForOverflowXamlIsland", // taskbar overflow
    };

    public static FilterVerdict Evaluate(in WindowFacts f, uint ownProcessId)
    {
        if (!f.IsVisible)
            return FilterVerdict.Invisible;
        if (f.IsCloaked)
            return FilterVerdict.Cloaked; // other virtual desktop, suspended UWP frame, hidden by the shell
        if ((f.Style & WS_CHILD) != 0)
            return FilterVerdict.ChildWindow;
        if (f.ProcessId == ownProcessId)
            return FilterVerdict.OwnProcess;
        if (ShellClasses.Contains(f.ClassName))
            return FilterVerdict.ShellWindow;

        bool appWindow = (f.ExStyle & WS_EX_APPWINDOW) != 0;
        if (!appWindow)
        {
            if ((f.ExStyle & WS_EX_TOOLWINDOW) != 0)
                return FilterVerdict.ToolWindow;
            if ((f.ExStyle & WS_EX_NOACTIVATE) != 0)
                return FilterVerdict.NoActivate;
            // Owned windows (dialogs, palettes) are represented by their owner, as in Alt+Tab, and
            // activating the owner brings its active popup forward (see WindowActivator). Some
            // frameworks own the main window by a hidden window; then the owned window is the app.
            if (f.Owner != 0 && f.RootOwnerVisible)
                return FilterVerdict.OwnedWindow;
        }

        if (f.Width <= 0 || f.Height <= 0)
            return FilterVerdict.ZeroSize;

        return FilterVerdict.Included;
    }

    public static bool IsSwitchable(in WindowFacts f, uint ownProcessId) =>
        Evaluate(f, ownProcessId) == FilterVerdict.Included;
}
