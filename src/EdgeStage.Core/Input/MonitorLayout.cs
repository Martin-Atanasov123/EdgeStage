using System.Runtime.InteropServices;
using EdgeStage.Core.Interop;

namespace EdgeStage.Core.Input;

public enum ScreenEdge
{
    Left,
    Right,
}

/// <summary>One display in physical (device) pixels, as reported to a per-monitor-DPI-aware process.</summary>
public sealed record MonitorArea(nint Handle, RECT Bounds, RECT WorkArea, uint Dpi, bool IsPrimary)
{
    public double Scale => Dpi / 96.0;
}

/// <summary>A vertical run of pixels on a monitor's outer edge where resting the pointer triggers the sidebar.</summary>
public readonly record struct EdgeSegment(int MonitorIndex, int X, int Top, int Bottom)
{
    public bool Contains(POINT p) => p.X == X && p.Y >= Top && p.Y < Bottom;
}

/// <summary>
/// The monitor arrangement. Coordinates can be negative (monitors left of or above the primary).
/// Edge segments only cover <em>outer</em> edges: where another monitor continues the desktop the
/// pointer passes through, so those stretches are removed.
/// </summary>
public sealed class MonitorLayout
{
    public MonitorLayout(IReadOnlyList<MonitorArea> monitors)
    {
        Monitors = monitors;
        LeftSegments = ComputeSegments(ScreenEdge.Left);
        RightSegments = ComputeSegments(ScreenEdge.Right);
    }

    public IReadOnlyList<MonitorArea> Monitors { get; }
    private IReadOnlyList<EdgeSegment> LeftSegments { get; }
    private IReadOnlyList<EdgeSegment> RightSegments { get; }

    public IReadOnlyList<EdgeSegment> EdgeSegments(ScreenEdge edge) => edge == ScreenEdge.Left ? LeftSegments : RightSegments;

    /// <summary>Index of the monitor containing <paramref name="p"/>, or the nearest one.</summary>
    public int IndexOf(POINT p)
    {
        int best = 0;
        long bestDistance = long.MaxValue;
        for (int i = 0; i < Monitors.Count; i++)
        {
            RECT b = Monitors[i].Bounds;
            if (p.X >= b.Left && p.X < b.Right && p.Y >= b.Top && p.Y < b.Bottom)
                return i;
            long dx = p.X < b.Left ? b.Left - p.X : p.X >= b.Right ? p.X - b.Right + 1 : 0;
            long dy = p.Y < b.Top ? b.Top - p.Y : p.Y >= b.Bottom ? p.Y - b.Bottom + 1 : 0;
            long d = dx * dx + dy * dy;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }
        return best;
    }

    /// <summary>Monitor index whose trigger zone contains <paramref name="p"/>, or null.</summary>
    public int? ZoneAt(POINT p, ScreenEdge edge)
    {
        foreach (EdgeSegment segment in EdgeSegments(edge))
            if (segment.Contains(p))
                return segment.MonitorIndex;
        return null;
    }

    /// <summary>Horizontal distance in pixels from <paramref name="p"/> to the nearest trigger column (for adaptive polling).</summary>
    public int DistanceToEdge(POINT p, ScreenEdge edge)
    {
        int best = int.MaxValue;
        foreach (EdgeSegment s in EdgeSegments(edge))
            best = Math.Min(best, Math.Abs(p.X - s.X));
        return best;
    }

    private List<EdgeSegment> ComputeSegments(ScreenEdge edge)
    {
        var segments = new List<EdgeSegment>();
        for (int i = 0; i < Monitors.Count; i++)
        {
            MonitorArea m = Monitors[i];
            int x = edge == ScreenEdge.Left ? m.Bounds.Left : m.Bounds.Right - 1;
            var free = new List<(int Top, int Bottom)> { (m.WorkArea.Top, m.WorkArea.Bottom) };

            for (int j = 0; j < Monitors.Count; j++)
            {
                if (i == j)
                    continue;
                RECT n = Monitors[j].Bounds;
                bool abuts = edge == ScreenEdge.Left ? n.Right == m.Bounds.Left : n.Left == m.Bounds.Right;
                if (abuts)
                    free = Subtract(free, n.Top, n.Bottom);
            }

            foreach (var (top, bottom) in free)
                if (bottom > top)
                    segments.Add(new EdgeSegment(i, x, top, bottom));
        }
        return segments;
    }

    private static List<(int Top, int Bottom)> Subtract(List<(int Top, int Bottom)> ranges, int cutTop, int cutBottom)
    {
        var result = new List<(int, int)>();
        foreach (var (top, bottom) in ranges)
        {
            if (cutBottom <= top || cutTop >= bottom)
            {
                result.Add((top, bottom));
                continue;
            }
            if (cutTop > top) result.Add((top, cutTop));
            if (cutBottom < bottom) result.Add((cutBottom, bottom));
        }
        return result;
    }

    // ---- Native loading ------------------------------------------------------------------------

    /// <summary>Reads the current monitor configuration from Windows.</summary>
    public static unsafe MonitorLayout Load()
    {
        var monitors = new List<MonitorArea>();
        var gc = GCHandle.Alloc(monitors);
        try
        {
            User32.EnumDisplayMonitors(0, 0, &CollectMonitor, GCHandle.ToIntPtr(gc));
        }
        finally
        {
            gc.Free();
        }
        return new MonitorLayout(monitors);
    }

    [UnmanagedCallersOnly]
    private static unsafe int CollectMonitor(nint hMonitor, nint hdc, RECT* clip, nint data)
    {
        var info = MONITORINFO.Create();
        if (User32.GetMonitorInfo(hMonitor, ref info))
        {
            uint dpi = Shell.GetDpiForMonitor(hMonitor, Shell.MDT_EFFECTIVE_DPI, out uint dpiX, out _) >= 0 ? dpiX : 96;
            var list = (List<MonitorArea>)GCHandle.FromIntPtr(data).Target!;
            list.Add(new MonitorArea(hMonitor, info.rcMonitor, info.rcWork, dpi, (info.dwFlags & 1) != 0));
        }
        return 1;
    }
}
