using EdgeStage.Core.Layout;

namespace EdgeStage.Tests.Unit;

[TestClass]
public sealed class GridNavigationTests
{
    // Two centered rows like Exposé draws 5 cards: 3 on top, 2 below (offset by half a card).
    private static readonly CardBox[] FiveCards =
    [
        new(196, 153, 460, 316), new(682, 153, 460, 316), new(1168, 153, 460, 316),
        new(439, 495, 460, 316), new(925, 495, 460, 316),
    ];

    [TestMethod]
    public void LeftAndRight_FollowReadingOrder_AndStopAtTheEnds()
    {
        Assert.AreEqual(1, GridNavigation.Next(FiveCards, 0, NavDirection.Right));
        Assert.AreEqual(3, GridNavigation.Next(FiveCards, 2, NavDirection.Right)); // wraps onto the next row
        Assert.AreEqual(4, GridNavigation.Next(FiveCards, 4, NavDirection.Right));
        Assert.AreEqual(0, GridNavigation.Next(FiveCards, 0, NavDirection.Left));
    }

    [TestMethod]
    public void Down_GoesToTheNearestCardBelow()
    {
        Assert.AreEqual(3, GridNavigation.Next(FiveCards, 0, NavDirection.Down));
        Assert.AreEqual(4, GridNavigation.Next(FiveCards, 2, NavDirection.Down));
        Assert.AreEqual(3, GridNavigation.Next(FiveCards, 3, NavDirection.Down), "last row: stays");
    }

    [TestMethod]
    public void Up_GoesToTheNearestCardAbove()
    {
        // Card 4 sits exactly between cards 1 and 2: a tie goes to the left one.
        Assert.AreEqual(1, GridNavigation.Next(FiveCards, 4, NavDirection.Up));
        Assert.AreEqual(1, GridNavigation.Next(FiveCards, 1, NavDirection.Up), "first row: stays");

        CardBox[] aligned = [new(0, 0, 100, 60), new(110, 0, 100, 60), new(0, 70, 100, 60), new(110, 70, 100, 60)];
        Assert.AreEqual(1, GridNavigation.Next(aligned, 3, NavDirection.Up), "straight up in an aligned grid");
    }

    [TestMethod]
    public void Up_SkipsOverAGroupHeaderGap_ToTheClosestRow()
    {
        CardBox[] grouped =
        [
            new(0, 0, 100, 60), new(110, 0, 100, 60),     // group A
            new(0, 120, 100, 60),                          // group B (below a header)
            new(0, 260, 100, 60), new(110, 260, 100, 60),  // group C
        ];
        Assert.AreEqual(2, GridNavigation.Next(grouped, 4, NavDirection.Up), "nearest row above, not the first row");
        Assert.AreEqual(3, GridNavigation.Next(grouped, 2, NavDirection.Down));
    }

    [TestMethod]
    public void NoSelection_StartsAtTheFirstCard_EmptyGridHasNone()
    {
        Assert.AreEqual(0, GridNavigation.Next(FiveCards, -1, NavDirection.Down));
        Assert.AreEqual(-1, GridNavigation.Next([], 0, NavDirection.Right));
    }
}

[TestClass]
public sealed class DisplayNavigationTests
{
    // This machine's layout: a second display to the left, 4 px lower.
    private static readonly CardBox[] TwoSideBySide = [new(0, 0, 1920, 1080), new(-1920, 4, 1920, 1080)];

    [TestMethod]
    public void LeftAndRight_FindTheSideBySideDisplay()
    {
        Assert.AreEqual(1, DisplayNavigation.Neighbor(TwoSideBySide, 0, NavDirection.Left));
        Assert.AreEqual(0, DisplayNavigation.Neighbor(TwoSideBySide, 1, NavDirection.Right));
        Assert.AreEqual(-1, DisplayNavigation.Neighbor(TwoSideBySide, 0, NavDirection.Right), "nothing to the right");
        Assert.AreEqual(-1, DisplayNavigation.Neighbor(TwoSideBySide, 0, NavDirection.Up), "a 4 px offset is not 'above'");
    }

    [TestMethod]
    public void ThreeDisplays_PickTheNearestInThatDirection_AndStackedOnesForUpDown()
    {
        CardBox[] displays = [new(0, 0, 1920, 1080), new(1920, 0, 2560, 1440), new(-1920, 0, 1920, 1080), new(0, -1080, 1920, 1080)];

        Assert.AreEqual(1, DisplayNavigation.Neighbor(displays, 0, NavDirection.Right));
        Assert.AreEqual(2, DisplayNavigation.Neighbor(displays, 0, NavDirection.Left));
        Assert.AreEqual(3, DisplayNavigation.Neighbor(displays, 0, NavDirection.Up));
        Assert.AreEqual(0, DisplayNavigation.Neighbor(displays, 3, NavDirection.Down));
        Assert.AreEqual(0, DisplayNavigation.Neighbor(displays, 2, NavDirection.Right), "the nearest, not the farthest");
    }

    [TestMethod]
    public void SingleDisplay_HasNoNeighbors() =>
        Assert.AreEqual(-1, DisplayNavigation.Neighbor([new(0, 0, 1920, 1080)], 0, NavDirection.Left));
}
