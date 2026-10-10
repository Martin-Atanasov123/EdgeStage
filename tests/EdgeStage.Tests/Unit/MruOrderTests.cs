using EdgeStage.Core.WindowTracking;

namespace EdgeStage.Tests.Unit;

[TestClass]
public sealed class MruOrderTests
{
    private static List<nint> Order(MruOrder mru, nint[] zOrder, nint foreground) =>
        mru.Order(zOrder, h => h, foreground);

    [TestMethod]
    public void NoHistory_KeepsZOrder_WithForegroundFirst()
    {
        var mru = new MruOrder();
        CollectionAssert.AreEqual(new nint[] { 3, 1, 2 }, Order(mru, [1, 2, 3], foreground: 3));
    }

    [TestMethod]
    public void RecentlyActivatedWindows_ComeBeforeUnseenOnes()
    {
        var mru = new MruOrder();
        mru.Touch(4);
        mru.Touch(2);
        mru.Touch(5); // current foreground

        CollectionAssert.AreEqual(new nint[] { 5, 2, 4, 1, 3 }, Order(mru, [1, 2, 3, 4, 5], foreground: 5));
    }

    [TestMethod]
    public void TouchingAgain_MovesWindowToFront()
    {
        var mru = new MruOrder();
        mru.Touch(1);
        mru.Touch(2);
        mru.Touch(1);

        CollectionAssert.AreEqual(new nint[] { 1, 2 }, Order(mru, [2, 1], foreground: 0));
    }

    [TestMethod]
    public void ForegroundAlwaysFirst_EvenIfNotTouched()
    {
        var mru = new MruOrder();
        mru.Touch(1);
        mru.Touch(2);

        CollectionAssert.AreEqual(new nint[] { 3, 2, 1 }, Order(mru, [1, 2, 3], foreground: 3));
    }

    [TestMethod]
    public void SecondCard_IsThePreviouslyUsedWindow()
    {
        // Hotkey-open pre-selects card #2, so Enter switches back like Alt+Tab.
        var mru = new MruOrder();
        mru.Touch(10);
        mru.Touch(20);
        mru.Touch(30);

        var order = Order(mru, [20, 30, 10], foreground: 30);
        Assert.AreEqual((nint)20, order[1]);
    }

    [TestMethod]
    public void ForgetAndPrune_RemoveStaleEntries()
    {
        var mru = new MruOrder();
        mru.Touch(1);
        mru.Touch(2);
        mru.Touch(3);
        mru.Forget(2);
        mru.Prune([1]);

        Assert.AreEqual(1, mru.Count);
    }

    [TestMethod]
    public void ZeroHandle_IsIgnored()
    {
        var mru = new MruOrder();
        mru.Touch(0);
        Assert.AreEqual(0, mru.Count);
    }
}
