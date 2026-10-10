using EdgeStage.Core.Interop;
using static EdgeStage.Core.Interop.NativeConstants;

namespace EdgeStage.Core.Thumbnails;

/// <summary>
/// One live DWM thumbnail relationship (source window → destination window). Disposing releases
/// the native relationship immediately. Not thread-safe: use from the destination window's thread.
/// </summary>
public sealed class ThumbnailRegistration : IDisposable
{
    private readonly SafeThumbnailHandle _handle;
    private ThumbnailPlacement _lastPlacement;
    private byte _lastOpacity;
    private bool _applied;

    internal ThumbnailRegistration(SafeThumbnailHandle handle, nint source)
    {
        _handle = handle;
        Source = source;
    }

    public nint Source { get; }

    /// <summary>Size of the whole source window as DWM reports it (invisible resize borders excluded).</summary>
    public SIZE SourceSize { get; private set; }

    public bool IsDisposed => _handle.IsClosed;

    /// <summary>Set once DWM reports the relationship invalid (typically: the source window was destroyed).</summary>
    public bool IsBroken { get; private set; }

    /// <summary>Re-reads the source size; returns false if the source no longer exists.</summary>
    public bool RefreshSourceSize()
    {
        if (IsDisposed)
            return false;
        int hr = Dwmapi.DwmQueryThumbnailSourceSize(_handle, out SIZE size);
        if (hr < 0)
        {
            IsBroken = true;
            return false;
        }
        SourceSize = size;
        return true;
    }

    /// <summary>Applies placement and opacity; skips the native call when nothing changed.</summary>
    public bool Apply(in ThumbnailPlacement placement, byte opacity = 255)
    {
        if (IsDisposed || IsBroken)
            return false;
        if (_applied && placement == _lastPlacement && opacity == _lastOpacity)
            return true;

        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_VISIBLE | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY
                      | (placement.IsVisible ? DWM_TNP_RECTDESTINATION | DWM_TNP_RECTSOURCE : 0),
            fVisible = placement.IsVisible ? 1 : 0,
            rcDestination = placement.Destination,
            rcSource = placement.Source,
            opacity = opacity,
            fSourceClientAreaOnly = 0,
        };
        int hr = Dwmapi.DwmUpdateThumbnailProperties(_handle, props);
        if (hr < 0)
        {
            IsBroken = true;
            return false;
        }
        _applied = true;
        _lastPlacement = placement;
        _lastOpacity = opacity;
        return true;
    }

    public void Dispose()
    {
        if (_handle.IsClosed)
            return;
        _handle.Dispose();
        DwmThumbnailService.OnReleased();
    }
}

/// <summary>Creates thumbnail registrations and counts live ones (a leak check for diagnostics and tests).</summary>
public static class DwmThumbnailService
{
    private static int s_live;

    /// <summary>Number of registrations created and not yet disposed, process-wide.</summary>
    public static int LiveRegistrations => Volatile.Read(ref s_live);

    /// <summary>
    /// Registers a live thumbnail of <paramref name="source"/> inside top-level window
    /// <paramref name="destination"/>. The thumbnail starts hidden until <see cref="ThumbnailRegistration.Apply"/>.
    /// </summary>
    public static ThumbnailRegistration? TryRegister(nint destination, nint source, out int hresult)
    {
        hresult = Dwmapi.DwmRegisterThumbnail(destination, source, out SafeThumbnailHandle handle);
        if (hresult < 0 || handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        Interlocked.Increment(ref s_live);
        var registration = new ThumbnailRegistration(handle, source);

        // Select whole-window mode before querying the size: the reported size depends on it.
        var initial = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_VISIBLE | DWM_TNP_SOURCECLIENTAREAONLY,
            fVisible = 0,
            fSourceClientAreaOnly = 0,
        };
        Dwmapi.DwmUpdateThumbnailProperties(handle, initial);
        registration.RefreshSourceSize();
        return registration;
    }

    internal static void OnReleased() => Interlocked.Decrement(ref s_live);
}
