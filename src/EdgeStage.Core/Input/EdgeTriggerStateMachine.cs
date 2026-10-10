namespace EdgeStage.Core.Input;

public enum EdgeTriggerState
{
    /// <summary>Pointer is not in a trigger zone; ready.</summary>
    Idle,
    /// <summary>Pointer is resting in a zone; fires when the delay elapses.</summary>
    Dwelling,
    /// <summary>A mouse button was down or a full-screen app owned the monitor; waits for the pointer to leave.</summary>
    Suppressed,
    /// <summary>Already fired; waits for the pointer to leave so it never repeats while parked at the edge.</summary>
    Fired,
}

/// <summary>
/// Pure decision logic for edge activation, fed with pointer samples. Rules:
/// <list type="number">
/// <item>The pointer must rest in a trigger zone for the activation delay.</item>
/// <item>Any mouse button down (dragging or resizing a window) suppresses activation until the
/// pointer leaves the zone, so releasing a drag at the edge does not open the sidebar.</item>
/// <item>A full-screen app on that monitor suppresses activation until the pointer leaves.</item>
/// <item>After firing, nothing happens again until the pointer leaves the zone.</item>
/// </list>
/// </summary>
public sealed class EdgeTriggerStateMachine
{
    private long _dwellStart;
    private int _dwellMonitor;

    public EdgeTriggerStateMachine(int delayMs)
    {
        DelayMs = delayMs;
    }

    public int DelayMs { get; set; }

    public EdgeTriggerState State { get; private set; } = EdgeTriggerState.Idle;

    /// <summary>
    /// Processes one sample. <paramref name="zoneMonitor"/> is the monitor whose trigger zone
    /// contains the pointer (null when outside every zone). Returns the monitor to open the
    /// sidebar on, or null.
    /// </summary>
    public int? Update(long nowMs, int? zoneMonitor, bool anyButtonDown, Func<int, bool> isFullscreen)
    {
        if (zoneMonitor is not int monitor)
        {
            State = EdgeTriggerState.Idle;
            return null;
        }

        if (State is EdgeTriggerState.Fired or EdgeTriggerState.Suppressed)
            return null;

        if (anyButtonDown)
        {
            State = EdgeTriggerState.Suppressed;
            return null;
        }

        if (State == EdgeTriggerState.Idle || monitor != _dwellMonitor)
        {
            State = EdgeTriggerState.Dwelling;
            _dwellStart = nowMs;
            _dwellMonitor = monitor;
        }

        if (nowMs - _dwellStart < DelayMs)
            return null;

        if (isFullscreen(monitor))
        {
            State = EdgeTriggerState.Suppressed;
            return null;
        }

        State = EdgeTriggerState.Fired;
        return monitor;
    }

    /// <summary>
    /// Requires the pointer to leave the zone before the next activation; used when the sidebar
    /// closes while the pointer is still parked at the edge.
    /// </summary>
    public void SuppressUntilExit() => State = EdgeTriggerState.Fired;

    public void Reset() => State = EdgeTriggerState.Idle;
}
