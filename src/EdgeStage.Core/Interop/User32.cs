using System.Runtime.InteropServices;

namespace EdgeStage.Core.Interop;

/// <summary>
/// user32.dll imports. Source-generated (<see cref="LibraryImportAttribute"/>) so marshalling is
/// visible at compile time. Callbacks use unmanaged function pointers to static
/// <see cref="UnmanagedCallersOnlyAttribute"/> methods, which avoids delegates being collected
/// while native code still holds them.
/// </summary>
public static partial class User32
{
    private const string Lib = "user32.dll";

    // ---- Window enumeration and inspection ---------------------------------------------------

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool EnumWindows(delegate* unmanaged<nint, nint, int> lpEnumFunc, nint lParam);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hWnd);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint hWnd);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsZoomed(nint hWnd);

    [LibraryImport(Lib, EntryPoint = "GetWindowTextLengthW")]
    public static partial int GetWindowTextLength(nint hWnd);

    [LibraryImport(Lib, EntryPoint = "GetWindowTextW")]
    public static unsafe partial int GetWindowText(nint hWnd, char* lpString, int nMaxCount);

    [LibraryImport(Lib, EntryPoint = "GetClassNameW")]
    public static unsafe partial int GetClassName(nint hWnd, char* lpClassName, int nMaxCount);

    [LibraryImport(Lib, EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [LibraryImport(Lib, EntryPoint = "GetWindowLongW")]
    private static partial int GetWindowLong32(nint hWnd, int nIndex);

    [LibraryImport(Lib, EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport(Lib, EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static partial int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

    /// <summary>GetWindowLongPtr is only exported on 64-bit Windows; fall back to GetWindowLong on 32-bit.</summary>
    public static nint GetWindowLongPtr(nint hWnd, int nIndex) =>
        nint.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

    public static nint SetWindowLongPtr(nint hWnd, int nIndex, nint value) =>
        nint.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, value) : SetWindowLong32(hWnd, nIndex, (int)value);

    [LibraryImport(Lib, EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint FindWindowEx(nint hWndParent, nint hWndChildAfter, string? lpszClass, string? lpszWindow);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowEnabled(nint hWnd);

    [LibraryImport(Lib)]
    public static partial nint GetAncestor(nint hWnd, uint gaFlags);

    [LibraryImport(Lib)]
    public static partial nint GetLastActivePopup(nint hWnd);

    [LibraryImport(Lib)]
    public static partial nint GetWindow(nint hWnd, uint uCmd);

    [LibraryImport(Lib)]
    public static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(nint hWnd, out RECT lpRect);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    [LibraryImport(Lib)]
    public static partial uint GetDpiForWindow(nint hWnd);

    // ---- Activation and placement ------------------------------------------------------------

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport(Lib)]
    public static partial nint GetForegroundWindow();

    /// <summary>Lets <paramref name="dwProcessId"/> take the foreground (used when a second launch hands over to the running instance).</summary>
    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(uint dwProcessId);

    [LibraryImport(Lib, EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint FindWindow(string? lpClassName, string? lpWindowName);

    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);

    // ---- Cursor and monitors -----------------------------------------------------------------

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport(Lib)]
    public static partial nint MonitorFromPoint(POINT pt, uint dwFlags);

    [LibraryImport(Lib)]
    public static partial nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [LibraryImport(Lib, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool EnumDisplayMonitors(nint hdc, nint lprcClip, delegate* unmanaged<nint, nint, RECT*, nint, int> lpfnEnum, nint dwData);

    [LibraryImport(Lib)]
    public static partial short GetAsyncKeyState(int vKey);

    // ---- Messages, icons, resources ----------------------------------------------------------

    [LibraryImport(Lib, EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    public static partial nint SendMessageTimeout(nint hWnd, uint msg, nint wParam, nint lParam, uint fuFlags, uint uTimeout, out nint lpdwResult);

    [LibraryImport(Lib, EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport(Lib, EntryPoint = "GetClassLongPtrW")]
    public static partial nint GetClassLongPtr(nint hWnd, int nIndex);

    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint hIcon);

    [LibraryImport(Lib)]
    public static partial uint GetGuiResources(nint hProcess, uint uiFlags);

    [LibraryImport(Lib, EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessage(string lpString);

    // ---- Hotkeys -----------------------------------------------------------------------------

    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint hWnd, int id);

    // ---- Event hooks -------------------------------------------------------------------------

    [LibraryImport(Lib)]
    public static unsafe partial nint SetWinEventHook(
        uint eventMin, uint eventMax, nint hmodWinEventProc,
        delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> pfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWinEvent(nint hWinEventHook);

    [LibraryImport(Lib, EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    public static unsafe partial nint SetWindowsHookEx(int idHook, delegate* unmanaged<int, nint, nint, nint> lpfn, nint hmod, uint dwThreadId);

    [LibraryImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport(Lib)]
    public static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    // ---- Message loop (used by the dedicated mouse-hook thread) -------------------------------

    [LibraryImport(Lib, EntryPoint = "GetMessageW")]
    public static partial int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(in MSG lpMsg);

    [LibraryImport(Lib, EntryPoint = "DispatchMessageW")]
    public static partial nint DispatchMessage(in MSG lpMsg);

    [LibraryImport(Lib, EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostThreadMessage(uint idThread, uint msg, nint wParam, nint lParam);
}
