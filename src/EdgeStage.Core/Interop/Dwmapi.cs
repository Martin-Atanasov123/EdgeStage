using System.Runtime.InteropServices;

namespace EdgeStage.Core.Interop;

/// <summary>
/// dwmapi.dll imports. All functions return an HRESULT (negative = failure).
/// </summary>
/// <remarks>
/// DWM thumbnails are composited by the Desktop Window Manager directly into the destination
/// window's visual, above any content the destination window renders itself. The application
/// never sees the pixels: it only controls where (rcDestination), which part (rcSource), how
/// opaque, and whether the thumbnail is visible.
/// </remarks>
public static partial class Dwmapi
{
    private const string Lib = "dwmapi.dll";

    /// <summary>Creates a live thumbnail relationship between <paramref name="hwndDestination"/> (must be top-level) and <paramref name="hwndSource"/>.</summary>
    [LibraryImport(Lib)]
    public static partial int DwmRegisterThumbnail(nint hwndDestination, nint hwndSource, out SafeThumbnailHandle phThumbnailId);

    /// <summary>Raw release used by <see cref="SafeThumbnailHandle"/> only.</summary>
    [LibraryImport(Lib)]
    internal static partial int DwmUnregisterThumbnail(nint hThumbnailId);

    [LibraryImport(Lib)]
    public static partial int DwmUpdateThumbnailProperties(SafeThumbnailHandle hThumbnailId, in DWM_THUMBNAIL_PROPERTIES ptnProperties);

    [LibraryImport(Lib)]
    public static partial int DwmQueryThumbnailSourceSize(SafeThumbnailHandle hThumbnail, out SIZE pSize);

    [LibraryImport(Lib)]
    public static partial int DwmGetWindowAttribute(nint hwnd, uint dwAttribute, out int pvAttribute, int cbAttribute);

    [LibraryImport(Lib, EntryPoint = "DwmGetWindowAttribute")]
    public static partial int DwmGetWindowAttributeRect(nint hwnd, uint dwAttribute, out RECT pvAttribute, int cbAttribute);

    [LibraryImport(Lib)]
    public static partial int DwmSetWindowAttribute(nint hwnd, uint dwAttribute, in int pvAttribute, int cbAttribute);

    [LibraryImport(Lib)]
    public static partial int DwmExtendFrameIntoClientArea(nint hWnd, in MARGINS pMarInset);

    [LibraryImport(Lib)]
    public static partial int DwmGetColorizationColor(out uint pcrColorization, [MarshalAs(UnmanagedType.Bool)] out bool pfOpaqueBlend);

    /// <summary>Convenience: sets a 32-bit DWM window attribute and returns the HRESULT.</summary>
    public static int SetIntAttribute(nint hwnd, uint attribute, int value) =>
        DwmSetWindowAttribute(hwnd, attribute, in value, sizeof(int));

    /// <summary>
    /// True when DWM reports the window as cloaked: hidden by the shell (other virtual desktop,
    /// suspended UWP frame) or by the app, even though <c>IsWindowVisible</c> may return true.
    /// </summary>
    public static bool IsCloaked(nint hwnd) =>
        DwmGetWindowAttribute(hwnd, NativeConstants.DWMWA_CLOAKED, out int cloaked, sizeof(int)) >= 0 && cloaked != 0;
}
