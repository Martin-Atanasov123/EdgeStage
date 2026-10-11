using EdgeStage.Core.Layout;

namespace EdgeStage.Tests.Unit;

[TestClass]
public sealed class ExposeLayoutTests
{
    private const double Chrome = 3 + 2 * 5 + 20 + 4; // border + padding + title + gap
    private const double Padding = 5;

    private static ExposeLayoutInput Input(int count, double w = 1824, double h = 900, double min = 190, double max = 460, double spacing = 26, int[]? groups = null, double header = 0) =>
        new(w, h, groups ?? [count], Chrome, Padding, spacing, header, min, max);

    [TestMethod]
    public void NoWindows_IsEmpty() =>
        Assert.AreEqual(ExposeGrid.Empty, ExposeLayout.Compute(Input(0)));

    [TestMethod]
    public void OneWindow_GetsTheMaximumCardWidth()
    {
        var g = ExposeLayout.Compute(Input(1));
        Assert.AreEqual(1, g.Columns);
        Assert.AreEqual(460, g.CardWidth);
        Assert.IsFalse(g.NeedsScroll);
    }

    [TestMethod]
    [DataRow(2)]
    [DataRow(4)]
    [DataRow(6)]
    [DataRow(9)]
    [DataRow(12)]
    [DataRow(20)]
    public void TypicalCounts_FitWithoutScrolling_AndUseTheSpace(int n)
    {
        var g = ExposeLayout.Compute(Input(n));

        Assert.IsFalse(g.NeedsScroll, $"{n} windows should fit");
        Assert.IsTrue(g.Columns * g.CardWidth + (g.Columns - 1) * 26 <= 1824 + 1);
        Assert.IsTrue(g.TotalHeight <= 900 + 0.5);
        Assert.IsTrue(g.CardWidth >= 190, $"cards stay readable ({g.CardWidth})");
        Assert.AreEqual((n + g.Columns - 1) / g.Columns, g.Rows);
    }

    [TestMethod]
    public void MoreWindows_NeverMakeCardsLarger()
    {
        double previous = double.MaxValue;
        for (int n = 1; n <= 40; n++)
        {
            double width = ExposeLayout.Compute(Input(n)).CardWidth;
            Assert.IsTrue(width <= previous + 0.01, $"n={n}: {width} > {previous}");
            previous = width;
        }
    }

    [TestMethod]
    public void TooManyWindows_KeepMinimumSize_AndScroll()
    {
        var g = ExposeLayout.Compute(Input(120));

        Assert.IsTrue(g.NeedsScroll);
        Assert.AreEqual(190, g.CardWidth);
        Assert.AreEqual(8, g.Columns); // floor((1824 + 26) / (190 + 26))
        Assert.AreEqual(15, g.Rows);
    }

    [TestMethod]
    public void Groups_StartOnNewRows_AndReserveHeaderSpace()
    {
        var g = ExposeLayout.Compute(Input(0, groups: [3, 1, 2], header: 30));

        int expectedRows = new[] { 3, 1, 2 }.Sum(s => (s + g.Columns - 1) / g.Columns);
        Assert.AreEqual(expectedRows, g.Rows);
        Assert.IsTrue(g.TotalHeight <= 900 + 0.5);
        Assert.IsTrue(g.TotalHeight >= 3 * (30 + 26), "three header rows are part of the height");
    }

    [TestMethod]
    public void NarrowArea_UsesOneColumn()
    {
        var g = ExposeLayout.Compute(Input(3, w: 300, h: 2000));
        Assert.AreEqual(1, g.Columns);
        Assert.IsTrue(g.CardWidth <= 300);
    }

    [TestMethod]
    public void DensityAndSize_ChangeRanges()
    {
        var dense = ExposeLayout.SizeRange(ExposeDensity.Dense, CardSize.Standard);
        var auto = ExposeLayout.SizeRange(ExposeDensity.Auto, CardSize.Standard);
        var spacious = ExposeLayout.SizeRange(ExposeDensity.Spacious, CardSize.Standard);
        var large = ExposeLayout.SizeRange(ExposeDensity.Auto, CardSize.Large);

        Assert.IsTrue(dense.Max < auto.Max && auto.Max < spacious.Max);
        Assert.IsTrue(dense.Spacing < auto.Spacing && auto.Spacing < spacious.Spacing);
        Assert.IsTrue(large.Min > auto.Min && large.Max > auto.Max);
    }

    [TestMethod]
    public void CardHeight_FollowsThumbAspect()
    {
        double h = ExposeLayout.CardHeightFor(330, Chrome, Padding, 16.0 / 10.0);
        Assert.AreEqual(Chrome + 320 / 1.6, h, 1e-9);
    }
}
