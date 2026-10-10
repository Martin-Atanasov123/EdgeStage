using System.Runtime.InteropServices;

namespace EdgeStage.Core.Interop;

/// <summary>shell32.dll and shcore.dll imports.</summary>
public static partial class Shell
{
    /// <summary>QUERY_USER_NOTIFICATION_STATE values that mean "a full-screen app owns the display".</summary>
    public const int QUNS_BUSY = 2;                    // full-screen app (or presentation settings) running
    public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3; // exclusive-mode Direct3D app
    public const int QUNS_PRESENTATION_MODE = 4;

    /// <summary>MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI.</summary>
    public const int MDT_EFFECTIVE_DPI = 0;

    /// <summary>Extracts icons from an executable. Returned icons are owned by the caller (DestroyIcon).</summary>
    [LibraryImport("shell32.dll", EntryPoint = "ExtractIconExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint ExtractIconEx(string lpszFile, int nIconIndex, out nint phiconLarge, out nint phiconSmall, uint nIcons);

    [LibraryImport("shell32.dll")]
    public static partial int SHQueryUserNotificationState(out int pquns);

    [LibraryImport("shcore.dll")]
    public static partial int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);
}

/// <summary>advapi32.dll imports used to detect elevated target processes for diagnostics.</summary>
public static partial class Advapi32
{
    public const uint TOKEN_QUERY = 0x0008;
    public const int TokenElevation = 20;

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTokenInformation(nint tokenHandle, int tokenInformationClass, out int tokenInformation, int tokenInformationLength, out int returnLength);
}
