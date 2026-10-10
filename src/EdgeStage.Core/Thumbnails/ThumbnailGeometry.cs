using EdgeStage.Core.Interop;

namespace EdgeStage.Core.Thumbnails;

/// <summary>An axis-aligned rectangle in device pixels, using doubles so layout math does not accumulate rounding.</summary>
public readonly record struct PixelRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static PixelRect FromEdges(double left, double top, double right, double bottom) =>
        new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));

    public PixelRect Intersect(PixelRect other)
    {
        double left = Math.Max(X, other.X);
        double top = Math.Max(Y, other.Y);
        double right = Math.Min(Right, other.Right);
        double bottom = Math.Min(Bottom, other.Bottom);
        return right > left && bottom > top ? FromEdges(left, top, right, bottom) : default;
    }

    /// <summary>Scales by a DPI factor (e.g. 1.5 for 150 %), converting DIPs to device pixels.</summary>
    public PixelRect Scale(double factor) => new(X * factor, Y * factor, Width * factor, Height * factor);

    public RECT ToRect() =>
        new((int)Math.Round(X), (int)Math.Round(Y), (int)Math.Round(Right), (int)Math.Round(Bottom));
}

/// <summary>Result of placing one DWM thumbnail: where it renders, and which part of the source is shown.</summary>
public readonly record struct ThumbnailPlacement(bool IsVisible, RECT Destination, RECT Source)
{
    public static ThumbnailPlacement Hidden => new(false, default, default);
}

/// <summary>
/// Pure layout math for DWM thumbnails. DWM thumbnails are not clipped by WPF (they are composited
/// above the window's content), so scrolling must be emulated by shrinking both the destination
/// rectangle and the source rectangle by the same proportion.
/// </summary>
public static class ThumbnailGeometry
{
    /// <summary>
    /// Fits a source of <paramref name="sourceWidth"/> x <paramref name="sourceHeight"/> inside
    /// <paramref name="box"/>, preserving aspect ratio, centered, and never scaled above
    /// <paramref name="maxScale"/> (1.0 = never enlarge small windows, which would look blurry).
    /// </summary>
    public static PixelRect FitInside(PixelRect box, double sourceWidth, double sourceHeight, double maxScale = 1.0)
    {
        if (box.IsEmpty || sourceWidth <= 0 || sourceHeight <= 0)
            return default;

        double scale = Math.Min(Math.Min(box.Width / sourceWidth, box.Height / sourceHeight), maxScale);
        double width = sourceWidth * scale;
        double height = sourceHeight * scale;
        return new PixelRect(box.X + (box.Width - width) / 2, box.Y + (box.Height - height) / 2, width, height);
    }

    /// <summary>
    /// Computes the destination and source rectangles for a thumbnail whose host element occupies
    /// <paramref name="host"/> and which may only be drawn inside <paramref name="viewport"/>
    /// (both in destination-window device pixels).
    /// </summary>
    public static ThumbnailPlacement Compute(PixelRect host, PixelRect viewport, SIZE sourceSize, double maxScale = 1.0)
    {
        if (sourceSize.Width <= 0 || sourceSize.Height <= 0)
            return ThumbnailPlacement.Hidden;

        PixelRect fitted = FitInside(host, sourceSize.Width, sourceSize.Height, maxScale);
        if (fitted.IsEmpty)
            return ThumbnailPlacement.Hidden;

        PixelRect visible = fitted.Intersect(viewport);
        RECT destination = visible.ToRect();
        if (visible.IsEmpty || destination.IsEmpty)
            return ThumbnailPlacement.Hidden;

        // Map the visible part of the fitted rectangle back into source coordinates.
        double scale = fitted.Width / sourceSize.Width;
        double srcLeft = (visible.X - fitted.X) / scale;
        double srcTop = (visible.Y - fitted.Y) / scale;
        double srcRight = (visible.Right - fitted.X) / scale;
        double srcBottom = (visible.Bottom - fitted.Y) / scale;

        RECT source = new(
            Clamp((int)Math.Round(srcLeft), 0, sourceSize.Width),
            Clamp((int)Math.Round(srcTop), 0, sourceSize.Height),
            Clamp((int)Math.Round(srcRight), 0, sourceSize.Width),
            Clamp((int)Math.Round(srcBottom), 0, sourceSize.Height));

        return source.IsEmpty ? ThumbnailPlacement.Hidden : new ThumbnailPlacement(true, destination, source);
    }

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
}
