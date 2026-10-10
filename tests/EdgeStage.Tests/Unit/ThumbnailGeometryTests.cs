using EdgeStage.Core.Interop;
using EdgeStage.Core.Thumbnails;

namespace EdgeStage.Tests.Unit;

[TestClass]
public sealed class ThumbnailGeometryTests
{
    private static SIZE Size(int w, int h) => new() { Width = w, Height = h };

    [TestMethod]
    public void FitInside_WideSource_FitsWidthAndCentersVertically()
    {
        var fitted = ThumbnailGeometry.FitInside(new PixelRect(0, 0, 200, 200), 400, 100);

        Assert.AreEqual(0, fitted.X, 1e-9);
        Assert.AreEqual(200, fitted.Width, 1e-9);
        Assert.AreEqual(50, fitted.Height, 1e-9);
        Assert.AreEqual(75, fitted.Y, 1e-9);
    }

    [TestMethod]
    public void FitInside_TallSource_FitsHeightAndCentersHorizontally()
    {
        var fitted = ThumbnailGeometry.FitInside(new PixelRect(10, 10, 160, 100), 500, 1000);

        Assert.AreEqual(100, fitted.Height, 1e-9);
        Assert.AreEqual(50, fitted.Width, 1e-9);
        Assert.AreEqual(10 + 55, fitted.X, 1e-9);
    }

    [TestMethod]
    public void FitInside_SmallSource_IsNeverEnlargedBeyondMaxScale()
    {
        var fitted = ThumbnailGeometry.FitInside(new PixelRect(0, 0, 400, 300), 100, 50);

        Assert.AreEqual(100, fitted.Width, 1e-9);
        Assert.AreEqual(50, fitted.Height, 1e-9);
        Assert.AreEqual(150, fitted.X, 1e-9);
        Assert.AreEqual(125, fitted.Y, 1e-9);
    }

    [TestMethod]
    [DataRow(0, 100)]
    [DataRow(100, 0)]
    [DataRow(-5, 100)]
    public void Compute_InvalidSourceSize_IsHidden(int w, int h)
    {
        var placement = ThumbnailGeometry.Compute(new PixelRect(0, 0, 100, 100), new PixelRect(0, 0, 500, 500), Size(w, h));

        Assert.IsFalse(placement.IsVisible);
    }

    [TestMethod]
    public void Compute_FullyInsideViewport_UsesWholeSource()
    {
        var placement = ThumbnailGeometry.Compute(new PixelRect(10, 20, 160, 100), new PixelRect(0, 0, 300, 600), Size(1600, 1000));

        Assert.IsTrue(placement.IsVisible);
        Assert.AreEqual(new RECT(10, 20, 170, 120), placement.Destination);
        Assert.AreEqual(new RECT(0, 0, 1600, 1000), placement.Source);
    }

    [TestMethod]
    public void Compute_PartiallyScrolledAboveViewport_CropsDestinationAndSourceProportionally()
    {
        // Host 160x100 at y=-50; viewport starts at y=0, so the top half is hidden.
        var placement = ThumbnailGeometry.Compute(new PixelRect(0, -50, 160, 100), new PixelRect(0, 0, 160, 500), Size(1600, 1000));

        Assert.IsTrue(placement.IsVisible);
        Assert.AreEqual(new RECT(0, 0, 160, 50), placement.Destination);
        Assert.AreEqual(new RECT(0, 500, 1600, 1000), placement.Source);
    }

    [TestMethod]
    public void Compute_PartiallyBelowViewport_CropsBottom()
    {
        var placement = ThumbnailGeometry.Compute(new PixelRect(0, 450, 160, 100), new PixelRect(0, 0, 160, 500), Size(1600, 1000));

        Assert.AreEqual(new RECT(0, 450, 160, 500), placement.Destination);
        Assert.AreEqual(new RECT(0, 0, 1600, 500), placement.Source);
    }

    [TestMethod]
    public void Compute_EntirelyOutsideViewport_IsHidden()
    {
        var placement = ThumbnailGeometry.Compute(new PixelRect(0, 600, 160, 100), new PixelRect(0, 0, 160, 500), Size(1600, 1000));

        Assert.IsFalse(placement.IsVisible);
    }

    [TestMethod]
    [DataRow(1.0)]
    [DataRow(1.25)]
    [DataRow(1.5)]
    [DataRow(1.75)]
    [DataRow(2.0)]
    public void Compute_DipBoxesScaledByDpi_ProduceDestinationWithinScaledBox(double dpi)
    {
        PixelRect hostDip = new(20, 40, 220, 137.5);
        PixelRect viewportDip = new(0, 30, 260, 900);

        var placement = ThumbnailGeometry.Compute(hostDip.Scale(dpi), viewportDip.Scale(dpi), Size(1920, 1080));

        PixelRect hostPx = hostDip.Scale(dpi);
        Assert.IsTrue(placement.IsVisible);
        Assert.IsTrue(placement.Destination.Left >= Math.Floor(hostPx.X));
        Assert.IsTrue(placement.Destination.Right <= Math.Ceiling(hostPx.Right));
        Assert.IsTrue(placement.Destination.Top >= Math.Floor(hostPx.Y));
        Assert.IsTrue(placement.Destination.Bottom <= Math.Ceiling(hostPx.Bottom));
        // 16:9 source in a 16:10 box fills the width.
        Assert.AreEqual(hostPx.Width, placement.Destination.Width, 1.0);
    }

    [TestMethod]
    public void PixelRect_Intersect_DisjointIsEmpty()
    {
        var a = new PixelRect(0, 0, 10, 10);
        var b = new PixelRect(20, 20, 5, 5);

        Assert.IsTrue(a.Intersect(b).IsEmpty);
    }
}
