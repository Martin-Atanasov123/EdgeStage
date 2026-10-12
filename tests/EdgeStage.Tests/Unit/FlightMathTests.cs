using EdgeStage.Core.Layout;

namespace EdgeStage.Tests.Unit;

[TestClass]
public sealed class FlightMathTests
{
    private static (double X, double Y, double W, double H) Apply((double S, double Ox, double Oy) m, CardBox preview, CardBox slot)
    {
        // The card is placed at the slot; its render transform acts in card-local coordinates.
        double lx = preview.X - slot.X, ly = preview.Y - slot.Y;
        return (slot.X + m.S * lx + m.Ox, slot.Y + m.S * ly + m.Oy, m.S * preview.Width, m.S * preview.Height);
    }

    [TestMethod]
    [DataRow(1100, 560, 640, 400)]       // normal window, bigger than the preview
    [DataRow(-8, -8, 1936, 1048 * 1.0)]  // maximized: window rect includes the invisible resize borders
    [DataRow(700, 300, 160, 100)]        // smaller than the preview
    public void PreviewLandsExactlyOnTheWindow(double x, double y, double w, double h)
    {
        var slot = new CardBox(682, 153, 460, 316);
        double pw = 440, ph = pw * h / w;               // the preview has the window's aspect ratio
        var preview = new CardBox(slot.X + 10, slot.Y + 30, pw, ph);
        var target = new CardBox(x, y, w, h);

        var m = FlightMath.MapOnto(preview, slot, target)!.Value;
        var landed = Apply((m.Scale, m.OffsetX, m.OffsetY), preview, slot);

        Assert.AreEqual(x, landed.X, 1e-9);
        Assert.AreEqual(y, landed.Y, 1e-9);
        Assert.AreEqual(w, landed.W, 1e-9);
        Assert.AreEqual(h, landed.H, 1e-6);
    }

    [TestMethod]
    public void EmptyRectangles_GiveNoMapping()
    {
        var slot = new CardBox(0, 0, 100, 80);
        Assert.IsNull(FlightMath.MapOnto(new CardBox(0, 0, 0, 0), slot, new CardBox(0, 0, 50, 50)));
        Assert.IsNull(FlightMath.MapOnto(new CardBox(0, 0, 90, 60), slot, new CardBox(0, 0, 0, 0)));
    }
}
