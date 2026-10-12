using EdgeStage.Core.Interop;

namespace EdgeStage.Hosting;

/// <summary>
/// One EdgeStage per user session. A second launch hands over to the running instance (to show
/// the sidebar, or to exit with <c>--exit</c>) and quits.
/// </summary>
internal static class SingleInstance
{
    private const string MutexName = @"Local\EdgeStage.SingleInstance";
    private static Mutex? s_mutex;

    public static uint ShowSidebarMessage { get; } = User32.RegisterWindowMessage("EdgeStage.ShowSidebar.v1");
    public static uint ExitMessage { get; } = User32.RegisterWindowMessage("EdgeStage.Exit.v1");

    /// <summary>Opens Exposé, or closes it when open (<c>EdgeStage.exe --expose</c>).</summary>
    public static uint ToggleExposeMessage { get; } = User32.RegisterWindowMessage("EdgeStage.ToggleExpose.v1");

#if DEBUG
    /// <summary>DEBUG only: activates the selected card, exercising the real activation path from scripts.</summary>
    public static uint TestActivateSelectedMessage { get; } = User32.RegisterWindowMessage("EdgeStage.Test.ActivateSelected.v1");

    /// <summary>DEBUG only: opens the sidebar exactly as the edge trigger does, without moving the cursor.</summary>
    public static uint TestShowEdgeMessage { get; } = User32.RegisterWindowMessage("EdgeStage.Test.ShowEdge.v1");

    /// <summary>DEBUG only: opens the settings window (for screenshots).</summary>
    public static uint TestOpenSettingsMessage { get; } = User32.RegisterWindowMessage("EdgeStage.Test.OpenSettings.v1");

    /// <summary>DEBUG only: opens the tray menu at a fixed position (for screenshots).</summary>
    public static uint TestTrayMenuMessage { get; } = User32.RegisterWindowMessage("EdgeStage.Test.TrayMenu.v1");

    /// <summary>DEBUG only: a numbered test command (wParam = <see cref="TestCommand"/>).</summary>
    public static uint TestCommandMessage { get; } = User32.RegisterWindowMessage("EdgeStage.Test.Command.v1");

    public enum TestCommand
    {
        ExposeShow = 1,
        ExposeActivateSelected,
        ExposeEscape,
        ExposeRight,
        ExposeLeft,
        ExposeUp,
        ExposeDown,
        ExposeDescribe,
        SidebarExposeButton,
        ExposeTypeSearch,
        ExposeEscapeKey,
        ExposeToggleGrouping,
        ExposeCloseSelected,
        ExposeSelectStubborn,
        ExposeSelectOtherDisplay,
        SettingsAppearance,
        SettingsSidebar,
        SettingsExpose,
        SettingsGeneral,
        SettingsHiddenApps,
        SettingsClose,
        ExposeHitTest,
    }
#endif

    public static bool TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return false;
        }
        s_mutex = mutex;
        return true;
    }

    /// <summary>Posts <paramref name="message"/> to the running instance; returns false if none was found.</summary>
    public static bool Signal(uint message, nint wParam = 0)
    {
        nint target = User32.FindWindow(null, MessageWindow.WindowTitle);
        if (target == 0)
            return false;
        // This process was just launched by the user, so it may hand its foreground right over.
        User32.GetWindowThreadProcessId(target, out uint processId);
        User32.AllowSetForegroundWindow(processId);
        return User32.PostMessage(target, message, wParam, 0);
    }

    public static void Release()
    {
        if (s_mutex is null)
            return;
        s_mutex.ReleaseMutex();
        s_mutex.Dispose();
        s_mutex = null;
    }
}
