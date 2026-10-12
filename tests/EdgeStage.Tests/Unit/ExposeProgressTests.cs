using EdgeStage.UI;

namespace EdgeStage.Tests.Unit;

/// <summary>
/// Direction and shape of Exposé's animation progress (0 = previews over the real windows,
/// 1 = settled in the grid). A sign error here once made the close animation run backwards.
/// </summary>
[TestClass]
public sealed class ExposeProgressTests
{
    [TestMethod]
    public void Opening_GoesFromTheWindowsToTheGrid()
    {
        Assert.AreEqual(0, ExposeWindow.Progress(opening: true, 0), 1e-12);
        Assert.AreEqual(1, ExposeWindow.Progress(opening: true, 1), 1e-12);
    }

    [TestMethod]
    public void Closing_GoesFromTheGridToTheWindows()
    {
        Assert.AreEqual(1, ExposeWindow.Progress(opening: false, 0), 1e-12, "starts in the grid");
        Assert.AreEqual(0, ExposeWindow.Progress(opening: false, 1), 1e-12, "ends exactly on the windows");
        Assert.AreEqual(0.5, ExposeWindow.Progress(opening: false, 0.5), 1e-12, "symmetric ease-in-out");
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Progress_IsMonotonic_WithoutOvershoot(bool opening)
    {
        double previous = ExposeWindow.Progress(opening, 0);
        for (int i = 1; i <= 1000; i++)
        {
            double p = ExposeWindow.Progress(opening, i / 1000.0);
            Assert.IsTrue(p is >= 0 and <= 1, $"t={i / 1000.0}: {p} outside 0..1");
            Assert.IsTrue(opening ? p >= previous - 1e-12 : p <= previous + 1e-12, $"t={i / 1000.0}: not monotonic");
            previous = p;
        }
    }

    [TestMethod]
    public void Closing_StartsAndEndsGently()
    {
        // Ease-in-out: the first and last 5 % of the time cover well under 5 % of the way.
        Assert.IsTrue(1 - ExposeWindow.Progress(false, 0.05) < 0.01);
        Assert.IsTrue(ExposeWindow.Progress(false, 0.95) < 0.01);
    }
}
