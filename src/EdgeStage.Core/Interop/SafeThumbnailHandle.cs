using Microsoft.Win32.SafeHandles;

namespace EdgeStage.Core.Interop;

/// <summary>
/// Owns an HTHUMBNAIL from <c>DwmRegisterThumbnail</c>. Disposing (or finalization, as a last
/// resort) calls <c>DwmUnregisterThumbnail</c>, so a thumbnail relationship can never outlive
/// its owner by accident.
/// </summary>
public sealed class SafeThumbnailHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeThumbnailHandle() : base(ownsHandle: true)
    {
    }

    /// <summary>HRESULT returned by the last release attempt (0 = S_OK). Exposed for diagnostics.</summary>
    public int ReleaseResult { get; private set; }

    protected override bool ReleaseHandle()
    {
        // Unregistering after the source or destination window was destroyed is harmless;
        // DWM simply reports that the relationship no longer exists.
        ReleaseResult = Dwmapi.DwmUnregisterThumbnail(handle);
        return true;
    }
}
