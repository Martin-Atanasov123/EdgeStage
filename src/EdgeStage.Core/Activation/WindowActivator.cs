using EdgeStage.Core.Interop;
using EdgeStage.Core.WindowTracking;
using static EdgeStage.Core.Interop.NativeConstants;

namespace EdgeStage.Core.Activation;

public enum ActivationOutcome
{
    /// <summary>The target (or its active popup) is now the foreground window.</summary>
    Activated,
    /// <summary>The window no longer exists.</summary>
    WindowGone,
    /// <summary>Windows did not let the window come to the foreground.</summary>
    Refused,
}

public sealed record ActivationResult(ActivationOutcome Outcome, bool TargetElevated, string Detail)
{
    public bool Succeeded => Outcome == ActivationOutcome.Activated;
}

/// <summary>
/// Restores and activates a window using only documented APIs. It does not try to defeat the
/// foreground lock: EdgeStage only activates windows in direct response to user input (the hotkey
/// or a click on the sidebar), which is exactly when Windows grants foreground rights.
/// </summary>
public static class WindowActivator
{
    private static readonly TimeSpan ConfirmTimeout = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(15);

    public static async Task<ActivationResult> ActivateAsync(nint hwnd, CancellationToken cancellationToken = default)
    {
        if (!User32.IsWindow(hwnd))
            return new ActivationResult(ActivationOutcome.WindowGone, false, "Window no longer exists.");

        bool wasMinimized = User32.IsIconic(hwnd);
        if (wasMinimized)
            User32.ShowWindow(hwnd, SW_RESTORE); // restores to the previous state (normal or maximized)

        // A disabled owner with a modal dialog: the dialog is what should receive focus.
        nint target = hwnd;
        nint popup = User32.GetLastActivePopup(hwnd);
        if (popup != 0 && popup != hwnd && User32.IsWindowVisible(popup) && User32.IsWindowEnabled(popup))
            target = popup;

        bool requested = User32.SetForegroundWindow(target);

        var deadline = DateTime.UtcNow + ConfirmTimeout;
        while (true)
        {
            if (IsForegroundFamily(hwnd))
                return new ActivationResult(ActivationOutcome.Activated, false,
                    wasMinimized ? "Restored and activated." : "Activated.");
            if (!User32.IsWindow(hwnd))
                return new ActivationResult(ActivationOutcome.WindowGone, false, "Window closed during activation.");
            if (DateTime.UtcNow >= deadline)
                break;
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(true);
        }

        bool? elevated = ProcessElevation.IsElevated(WindowQuery.GetProcessId(hwnd));
        bool targetElevated = elevated == true && ProcessElevation.IsCurrentProcessElevated() != true;
        string detail = targetElevated
            ? "The window belongs to an app running as administrator; Windows does not let a non-elevated app switch to it."
            : $"Windows kept another window in the foreground (SetForegroundWindow returned {requested}).";
        return new ActivationResult(ActivationOutcome.Refused, targetElevated, detail);
    }

    /// <summary>True when the foreground window is <paramref name="hwnd"/> or one of its owned popups.</summary>
    public static bool IsForegroundFamily(nint hwnd)
    {
        nint foreground = User32.GetForegroundWindow();
        if (foreground == 0)
            return false;
        if (foreground == hwnd)
            return true;
        return WindowQuery.GetRootOwner(foreground) == WindowQuery.GetRootOwner(hwnd);
    }
}

/// <summary>Reads whether a process runs elevated (administrator). Used only to explain failures.</summary>
public static class ProcessElevation
{
    private static readonly Lazy<bool?> Current = new(() => IsElevated((uint)Environment.ProcessId));

    public static bool? IsCurrentProcessElevated() => Current.Value;

    /// <summary>True/false, or null when the process cannot be inspected (e.g. protected or access denied).</summary>
    public static bool? IsElevated(uint processId)
    {
        nint process = Kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == 0)
            return null;
        try
        {
            if (!Advapi32.OpenProcessToken(process, Advapi32.TOKEN_QUERY, out nint token))
                return null;
            try
            {
                return Advapi32.GetTokenInformation(token, Advapi32.TokenElevation, out int elevated, sizeof(int), out _)
                    ? elevated != 0
                    : null;
            }
            finally
            {
                Kernel32.CloseHandle(token);
            }
        }
        finally
        {
            Kernel32.CloseHandle(process);
        }
    }
}
