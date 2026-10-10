using System.Runtime.InteropServices;

namespace EdgeStage.Core.Interop;

/// <summary>
/// NOTIFYICONDATAW (Windows Vista+ layout). Fixed-size character buffers keep it blittable for
/// source-generated P/Invoke.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public unsafe struct NOTIFYICONDATAW
{
    public int cbSize;
    public nint hWnd;
    public uint uID;
    public uint uFlags;
    public uint uCallbackMessage;
    public nint hIcon;
    public fixed char szTip[128];
    public uint dwState;
    public uint dwStateMask;
    public fixed char szInfo[256];
    public uint uTimeoutOrVersion;
    public fixed char szInfoTitle[64];
    public uint dwInfoFlags;
    public Guid guidItem;
    public nint hBalloonIcon;

    public static NOTIFYICONDATAW Create(nint hwnd, uint id) => new()
    {
        cbSize = sizeof(NOTIFYICONDATAW),
        hWnd = hwnd,
        uID = id,
    };

    public void SetTip(string text) { fixed (char* p = szTip) Copy(text, p, 128); }
    public void SetInfo(string text) { fixed (char* p = szInfo) Copy(text, p, 256); }
    public void SetInfoTitle(string text) { fixed (char* p = szInfoTitle) Copy(text, p, 64); }

    private static void Copy(string text, char* destination, int capacity)
    {
        int length = Math.Min(text.Length, capacity - 1);
        for (int i = 0; i < length; i++)
            destination[i] = text[i];
        destination[length] = '\0';
    }
}

/// <summary>Shell_NotifyIconW and its constants.</summary>
public static partial class NotifyIconNative
{
    public const uint NIM_ADD = 0x0;
    public const uint NIM_MODIFY = 0x1;
    public const uint NIM_DELETE = 0x2;
    public const uint NIM_SETVERSION = 0x4;

    public const uint NIF_MESSAGE = 0x01;
    public const uint NIF_ICON = 0x02;
    public const uint NIF_TIP = 0x04;
    public const uint NIF_INFO = 0x10;
    public const uint NIF_SHOWTIP = 0x80;

    public const uint NIIF_INFO = 0x01;
    public const uint NIIF_WARNING = 0x02;
    public const uint NIIF_RESPECT_QUIET_TIME = 0x80;

    public const uint NOTIFYICON_VERSION_4 = 4;

    // Callback notifications (LOWORD of lParam with NOTIFYICON_VERSION_4)
    public const uint WM_CONTEXTMENU = 0x007B;
    public const uint NIN_SELECT = 0x0400;
    public const uint NIN_KEYSELECT = 0x0401;
    public const uint NIN_BALLOONUSERCLICK = 0x0405;

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATAW lpData);
}
