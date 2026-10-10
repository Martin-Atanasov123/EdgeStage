using EdgeStage.Core.Input;
using EdgeStage.Core.Interop;
using EdgeStage.Core.Layout;

namespace EdgeStage.Tests.Unit;

[TestClass]
public sealed class SidebarPlacementTests
{
    private static MonitorArea Monitor(int left, int top, int width, int height, uint dpi = 96, int taskbar = 48) =>
        new(0, new RECT(left, top, left + width, top + height), new RECT(left, top, left + width, top + height - taskbar), dpi, true);

    [TestMethod]
    public void LeftEdge_FloatsMarginFromEdge_AndCentersVertically()
    {
        var m = Monitor(0, 0, 1920, 1080);

        RECT r = SidebarPlacement.Compute(m, ScreenEdge.Left, contentHeightDip: 400);

        Assert.AreEqual(12, r.Left);
        Assert.AreEqual(264, r.Width);
        Assert.AreEqual(400, r.Height);
        Assert.AreEqual((1032 - 400) / 2, r.Top);
    }

    [TestMethod]
    public void RightEdge_IsMirrored()
    {
        var m = Monitor(0, 0, 1920, 1080);

        RECT r = SidebarPlacement.Compute(m, ScreenEdge.Right, 400);

        Assert.AreEqual(1920 - 12, r.Right);
        Assert.AreEqual(264, r.Width);
    }

    [TestMethod]
    public void TallContent_IsCappedToWorkArea_LeavingMargins()
    {
        var m = Monitor(0, 0, 1920, 1080);

        RECT r = SidebarPlacement.Compute(m, ScreenEdge.Left, 5000);

        Assert.AreEqual(12, r.Top);
        Assert.AreEqual(1032 - 12, r.Bottom);
    }

    [TestMethod]
    public void TinyContent_UsesMinimumHeight()
    {
        RECT r = SidebarPlacement.Compute(Monitor(0, 0, 1920, 1080), ScreenEdge.Left, 10);
        Assert.AreEqual(120, r.Height);
    }

    [TestMethod]
    [DataRow(144u, 1.5)]
    [DataRow(192u, 2.0)]
    public void HighDpiMonitor_ScalesSizeAndMargin(uint dpi, double scale)
    {
        var m = Monitor(0, 0, 3840, 2160, dpi);

        RECT r = SidebarPlacement.Compute(m, ScreenEdge.Left, 400);

        Assert.AreEqual((int)Math.Round(12 * scale), r.Left);
        Assert.AreEqual((int)Math.Round(264 * scale), r.Width);
        Assert.AreEqual((int)Math.Ceiling(400 * scale), r.Height);
    }

    [TestMethod]
    public void MonitorWithNegativeCoordinates_PlacesSidebarOnThatMonitor()
    {
        var m = Monitor(-2560, -200, 2560, 1440);

        RECT r = SidebarPlacement.Compute(m, ScreenEdge.Left, 400);

        Assert.AreEqual(-2560 + 12, r.Left);
        Assert.IsTrue(r.Top >= -200 && r.Bottom <= -200 + 1392);
    }

    [TestMethod]
    public void KeepOpenRegion_CoversGapToEdge_AndFullHeight()
    {
        var m = Monitor(0, 0, 1920, 1080);
        RECT sidebar = SidebarPlacement.Compute(m, ScreenEdge.Left, 400);

        RECT region = SidebarPlacement.KeepOpenRegion(sidebar, ScreenEdge.Left, m);

        Assert.IsTrue(SidebarPlacement.Contains(region, new POINT(0, 5)), "pointer parked at the top of the edge stays inside");
        Assert.IsTrue(SidebarPlacement.Contains(region, new POINT(5, 500)), "the gap between edge and sidebar is inside");
        Assert.IsTrue(SidebarPlacement.Contains(region, new POINT(sidebar.Right + 10, 500)), "small tolerance past the sidebar");
        Assert.IsFalse(SidebarPlacement.Contains(region, new POINT(sidebar.Right + 40, 500)), "well past the sidebar is outside");
        Assert.IsFalse(SidebarPlacement.Contains(region, new POINT(5, 1040)), "taskbar area is outside");
    }

    [TestMethod]
    public void KeepOpenRegion_RightEdge()
    {
        var m = Monitor(0, 0, 1920, 1080);
        RECT sidebar = SidebarPlacement.Compute(m, ScreenEdge.Right, 400);

        RECT region = SidebarPlacement.KeepOpenRegion(sidebar, ScreenEdge.Right, m);

        Assert.IsTrue(SidebarPlacement.Contains(region, new POINT(1919, 100)));
        Assert.IsFalse(SidebarPlacement.Contains(region, new POINT(sidebar.Left - 40, 500)));
    }
}
