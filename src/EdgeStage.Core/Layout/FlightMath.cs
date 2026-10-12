namespace EdgeStage.Core.Layout;

/// <summary>Uniform scale and offset for a card's render transform during Exposé's flights.</summary>
public static class FlightMath
{
    /// <summary>
    /// The scale <c>s</c> and offset <c>(ox, oy)</c> such that the transform <c>p → s·p + o</c>, applied
    /// to the card at <paramref name="slot"/> (card-local coordinates measured from the slot's corner),
    /// moves the preview <paramref name="preview"/> exactly onto <paramref name="target"/>. Preview and
    /// target share an aspect ratio (both show the same window), so one scale fits both sides; with a
    /// different aspect the widths match and the top-left corners coincide. Null when unusable.
    /// </summary>
    public static (double Scale, double OffsetX, double OffsetY)? MapOnto(CardBox preview, CardBox slot, CardBox target)
    {
        if (preview.Width <= 0 || preview.Height <= 0 || target.Width <= 0 || target.Height <= 0)
            return null;
        double s = target.Width / preview.Width;
        double qx = preview.X - slot.X, qy = preview.Y - slot.Y;
        return (s, target.X - slot.X - s * qx, target.Y - slot.Y - s * qy);
    }
}
