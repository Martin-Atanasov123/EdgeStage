using EdgeStage.Core.Input;
using EdgeStage.Core.Interop;

namespace EdgeStage.Tests.Unit;

[TestClass]
public sealed class MonitorLayoutTests
{
    private static MonitorArea Monitor(int left, int top, int width, int height, int taskbar = 48, uint dpi = 96, bool primary = false) =>
        new(0, new RECT(left, top, left + width, top + height), new RECT(left, top, left + width, top + height - taskbar), dpi, primary);

    [TestMethod]
    public void SingleMonitor_LeftEdgeCoversWorkAreaHeight()
    {
        var layout = new MonitorLayout([Monitor(0, 0, 1920, 1080, primary: true)]);

        var segments = layout.EdgeSegments(ScreenEdge.Left);

        Assert.AreEqual(1, segments.Count);
        Assert.AreEqual(new EdgeSegment(0, 0, 0, 1032), segments[0]);
    }

    [TestMethod]
    public void SingleMonitor_RightEdgeIsLastPixelColumn()
    {
        var layout = new MonitorLayout([Monitor(0, 0, 1920, 1080)]);
        Assert.AreEqual(1919, layout.EdgeSegments(ScreenEdge.Right)[0].X);
    }

    [TestMethod]
    public void TaskbarArea_IsNotATriggerZone()
    {
        var layout = new MonitorLayout([Monitor(0, 0, 1920, 1080)]);

        Assert.AreEqual(0, layout.ZoneAt(new POINT(0, 1031), ScreenEdge.Left));
        Assert.IsNull(layout.ZoneAt(new POINT(0, 1040), ScreenEdge.Left));
    }

    [TestMethod]
    public void PointerMustBeOnTheEdgeColumn()
    {
        var layout = new MonitorLayout([Monitor(0, 0, 1920, 1080)]);
        Assert.IsNull(layout.ZoneAt(new POINT(1, 500), ScreenEdge.Left));
    }

    [TestMethod]
    public void SideBySide_InnerSeamIsNotAZone()
    {
        // Secondary monitor to the LEFT of the primary (negative coordinates).
        var layout = new MonitorLayout(
        [
            Monitor(0, 0, 1920, 1080, primary: true),
            Monitor(-2560, 0, 2560, 1440),
        ]);

        // Primary's left edge (x=0) abuts the secondary for y in [0,1440): no zone there.
        Assert.IsNull(layout.ZoneAt(new POINT(0, 500), ScreenEdge.Left));
        // The secondary's left edge is the outer edge of the desktop.
        Assert.AreEqual(1, layout.ZoneAt(new POINT(-2560, 500), ScreenEdge.Left));
        // Right edge: primary's right is outer, secondary's right (x=-1) abuts the primary.
        Assert.AreEqual(0, layout.ZoneAt(new POINT(1919, 500), ScreenEdge.Right));
        Assert.IsNull(layout.ZoneAt(new POINT(-1, 500), ScreenEdge.Right));
    }

    [TestMethod]
    public void VerticallyOffsetNeighbor_LeavesUncoveredStretchesAsZones()
    {
        // Left monitor is shorter and starts 400 px lower: the primary's left edge above y=400 is outer.
        var layout = new MonitorLayout(
        [
            Monitor(0, 0, 1920, 1200, taskbar: 0, primary: true),
            Monitor(-1280, 400, 1280, 600, taskbar: 0),
        ]);

        var primaryLeft = layout.EdgeSegments(ScreenEdge.Left).Where(s => s.MonitorIndex == 0).ToList();

        CollectionAssert.AreEqual(
            new[] { new EdgeSegment(0, 0, 0, 400), new EdgeSegment(0, 0, 1000, 1200) },
            primaryLeft);
    }

    [TestMethod]
    public void StackedMonitors_BothLeftEdgesAreZones()
    {
        // A monitor above the primary does not cover any left/right edge.
        var layout = new MonitorLayout(
        [
            Monitor(0, 0, 1920, 1080, primary: true),
            Monitor(0, -1080, 1920, 1080),
        ]);

        Assert.AreEqual(0, layout.ZoneAt(new POINT(0, 10), ScreenEdge.Left));
        Assert.AreEqual(1, layout.ZoneAt(new POINT(0, -500), ScreenEdge.Left));
    }

    [TestMethod]
    public void IndexOf_FindsContainingMonitor_OrNearest()
    {
        var layout = new MonitorLayout(
        [
            Monitor(0, 0, 1920, 1080, primary: true),
            Monitor(-2560, 0, 2560, 1440),
        ]);

        Assert.AreEqual(1, layout.IndexOf(new POINT(-100, 1200)));
        Assert.AreEqual(0, layout.IndexOf(new POINT(500, 500)));
        Assert.AreEqual(0, layout.IndexOf(new POINT(5000, 500)), "off-screen points map to the nearest monitor");
    }

    [TestMethod]
    public void DistanceToEdge_IsHorizontalDistanceToNearestZoneColumn()
    {
        var layout = new MonitorLayout([Monitor(0, 0, 1920, 1080)]);
        Assert.AreEqual(30, layout.DistanceToEdge(new POINT(30, 500), ScreenEdge.Left));
        Assert.AreEqual(19, layout.DistanceToEdge(new POINT(1900, 500), ScreenEdge.Right));
    }

    [TestMethod]
    [DataRow(96u, 1.0)]
    [DataRow(120u, 1.25)]
    [DataRow(144u, 1.5)]
    [DataRow(192u, 2.0)]
    public void Scale_FollowsMonitorDpi(uint dpi, double scale) =>
        Assert.AreEqual(scale, Monitor(0, 0, 100, 100, dpi: dpi).Scale, 1e-9);
}
