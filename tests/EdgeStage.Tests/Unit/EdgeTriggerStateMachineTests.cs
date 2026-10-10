using EdgeStage.Core.Input;

namespace EdgeStage.Tests.Unit;

[TestClass]
public sealed class EdgeTriggerStateMachineTests
{
    private static readonly Func<int, bool> NeverFullscreen = _ => false;
    private static readonly Func<int, bool> AlwaysFullscreen = _ => true;

    [TestMethod]
    public void FiresAfterDelay_NotBefore()
    {
        var m = new EdgeTriggerStateMachine(300);

        Assert.IsNull(m.Update(1000, 0, false, NeverFullscreen));
        Assert.IsNull(m.Update(1200, 0, false, NeverFullscreen));
        Assert.AreEqual(EdgeTriggerState.Dwelling, m.State);
        Assert.AreEqual(0, m.Update(1300, 0, false, NeverFullscreen));
        Assert.AreEqual(EdgeTriggerState.Fired, m.State);
    }

    [TestMethod]
    public void DoesNotFireRepeatedly_WhileParkedAtEdge()
    {
        var m = new EdgeTriggerStateMachine(300);
        m.Update(0, 0, false, NeverFullscreen);
        Assert.AreEqual(0, m.Update(300, 0, false, NeverFullscreen));

        for (long t = 340; t < 5000; t += 40)
            Assert.IsNull(m.Update(t, 0, false, NeverFullscreen));
    }

    [TestMethod]
    public void LeavingTheZone_RearmsTheTrigger()
    {
        var m = new EdgeTriggerStateMachine(300);
        m.Update(0, 0, false, NeverFullscreen);
        m.Update(300, 0, false, NeverFullscreen);

        m.Update(400, null, false, NeverFullscreen);
        Assert.AreEqual(EdgeTriggerState.Idle, m.State);

        Assert.IsNull(m.Update(500, 0, false, NeverFullscreen));
        Assert.AreEqual(0, m.Update(800, 0, false, NeverFullscreen));
    }

    [TestMethod]
    public void BriefTouch_ShorterThanDelay_DoesNotFire()
    {
        var m = new EdgeTriggerStateMachine(300);
        m.Update(0, 0, false, NeverFullscreen);
        m.Update(200, null, false, NeverFullscreen); // pointer flicked past the edge
        Assert.IsNull(m.Update(250, 0, false, NeverFullscreen));
        Assert.IsNull(m.Update(500, 0, false, NeverFullscreen)); // dwell restarted at 250
        Assert.AreEqual(0, m.Update(550, 0, false, NeverFullscreen));
    }

    [TestMethod]
    public void ButtonHeld_WhenReachingEdge_SuppressesUntilExit()
    {
        // Dragging a window to the edge and releasing there must not open the sidebar.
        var m = new EdgeTriggerStateMachine(300);
        Assert.IsNull(m.Update(0, 0, true, NeverFullscreen));
        Assert.AreEqual(EdgeTriggerState.Suppressed, m.State);
        Assert.IsNull(m.Update(100, 0, false, NeverFullscreen)); // button released at the edge
        Assert.IsNull(m.Update(2000, 0, false, NeverFullscreen));

        m.Update(2100, null, false, NeverFullscreen);
        m.Update(2200, 0, false, NeverFullscreen);
        Assert.AreEqual(0, m.Update(2500, 0, false, NeverFullscreen));
    }

    [TestMethod]
    public void ButtonPressedDuringDwell_Suppresses()
    {
        var m = new EdgeTriggerStateMachine(300);
        m.Update(0, 0, false, NeverFullscreen);
        Assert.IsNull(m.Update(200, 0, true, NeverFullscreen));
        Assert.IsNull(m.Update(600, 0, false, NeverFullscreen));
        Assert.AreEqual(EdgeTriggerState.Suppressed, m.State);
    }

    [TestMethod]
    public void FullscreenApp_SuppressesUntilExit()
    {
        var m = new EdgeTriggerStateMachine(300);
        m.Update(0, 0, false, AlwaysFullscreen);
        Assert.IsNull(m.Update(300, 0, false, AlwaysFullscreen));
        Assert.AreEqual(EdgeTriggerState.Suppressed, m.State);
        Assert.IsNull(m.Update(900, 0, false, NeverFullscreen)); // still parked: stays suppressed
    }

    [TestMethod]
    public void FullscreenCheck_UsesTheDwellMonitor()
    {
        var m = new EdgeTriggerStateMachine(100);
        Func<int, bool> onlyMonitor1 = i => i == 1;

        m.Update(0, 0, false, onlyMonitor1);
        Assert.AreEqual(0, m.Update(100, 0, false, onlyMonitor1));
    }

    [TestMethod]
    public void MovingToAnotherMonitorsEdge_RestartsDwell()
    {
        var m = new EdgeTriggerStateMachine(300);
        m.Update(0, 0, false, NeverFullscreen);
        Assert.IsNull(m.Update(250, 1, false, NeverFullscreen));
        Assert.IsNull(m.Update(500, 1, false, NeverFullscreen));
        Assert.AreEqual(1, m.Update(550, 1, false, NeverFullscreen));
    }

    [TestMethod]
    public void ZeroDelay_FiresImmediately()
    {
        var m = new EdgeTriggerStateMachine(0);
        Assert.AreEqual(0, m.Update(0, 0, false, NeverFullscreen));
    }

    [TestMethod]
    public void DelayChange_AppliesToNextDwell()
    {
        var m = new EdgeTriggerStateMachine(300) { DelayMs = 600 };
        m.Update(0, 0, false, NeverFullscreen);
        Assert.IsNull(m.Update(400, 0, false, NeverFullscreen));
        Assert.AreEqual(0, m.Update(600, 0, false, NeverFullscreen));
    }

    [TestMethod]
    public void SuppressUntilExit_BlocksUntilPointerLeaves()
    {
        // Sidebar dismissed with Esc while the pointer is still at the edge.
        var m = new EdgeTriggerStateMachine(300);
        m.SuppressUntilExit();
        Assert.IsNull(m.Update(1000, 0, false, NeverFullscreen));
        Assert.IsNull(m.Update(2000, 0, false, NeverFullscreen));
        m.Update(2100, null, false, NeverFullscreen);
        m.Update(2200, 0, false, NeverFullscreen);
        Assert.AreEqual(0, m.Update(2500, 0, false, NeverFullscreen));
    }
}
