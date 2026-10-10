using EdgeStage.Core.Input;
using EdgeStage.Core.Interop;

namespace EdgeStage.Core.Layout;

/// <summary>
/// Where the sidebar goes on a monitor, in physical pixels. The sidebar floats a small margin from
/// the chosen edge, is only as tall as its cards (up to the work area), and is centered vertically,
/// so it covers as little of the desktop as possible.
/// </summary>
public static class SidebarPlacement
{
    public const double WidthDip = 264;
    public const double MarginDip = 12;
    public const double MinHeightDip = 120;

    /// <summary>Extra pixels around the sidebar column that still count as "inside" for pointer-leave dismissal.</summary>
    public const double KeepOpenToleranceDip = 16;

    public static RECT Compute(MonitorArea monitor, ScreenEdge edge, double contentHeightDip,
        double widthDip = WidthDip, double marginDip = MarginDip)
    {
        double scale = monitor.Scale;
        RECT work = monitor.WorkArea;
        int width = (int)Math.Round(widthDip * scale);
        int margin = (int)Math.Round(marginDip * scale);
        int maxHeight = Math.Max(1, work.Height - 2 * margin);
        int minHeight = Math.Min(maxHeight, (int)Math.Round(MinHeightDip * scale));
        int height = Math.Clamp((int)Math.Ceiling(contentHeightDip * scale), minHeight, maxHeight);

        int left = edge == ScreenEdge.Left ? work.Left + margin : work.Right - margin - width;
        int top = work.Top + (work.Height - height) / 2;
        return new RECT(left, top, left + width, top + height);
    }

    /// <summary>
    /// The region in which the pointer keeps the sidebar open: the full-height column from the
    /// monitor edge to just past the sidebar's inner side. It covers the gap between the screen edge
    /// and the floating sidebar, and stretches vertically so a pointer that triggered the sidebar near
    /// the top or bottom of the edge is still "inside" a short, centered sidebar.
    /// </summary>
    public static RECT KeepOpenRegion(RECT sidebar, ScreenEdge edge, MonitorArea monitor)
    {
        int tolerance = (int)Math.Round(KeepOpenToleranceDip * monitor.Scale);
        return edge == ScreenEdge.Left
            ? new RECT(monitor.Bounds.Left, monitor.WorkArea.Top, sidebar.Right + tolerance, monitor.WorkArea.Bottom)
            : new RECT(sidebar.Left - tolerance, monitor.WorkArea.Top, monitor.Bounds.Right, monitor.WorkArea.Bottom);
    }

    public static bool Contains(RECT r, POINT p) => p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
}
