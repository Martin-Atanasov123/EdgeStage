using EdgeStage.Core.Interop;

namespace EdgeStage.UI;

/// <summary>
/// Hover selects a card only after the pointer has actually moved. When a list appears under a
/// resting pointer, WPF reports "mouse enter" for whatever card lands there, which would silently
/// replace the keyboard selection (e.g. the hotkey's pre-selected "previous window").
/// </summary>
internal sealed class HoverGuard
{
    private POINT _armedAt;
    private bool _moved;

    /// <summary>Call when the list appears.</summary>
    public void Arm()
    {
        _moved = false;
        if (!User32.GetCursorPos(out _armedAt))
            _moved = true;
    }

    /// <summary>True once the pointer has left the position it had when <see cref="Arm"/> was called.</summary>
    public bool Allows()
    {
        if (_moved)
            return true;
        if (User32.GetCursorPos(out POINT p) && (p.X != _armedAt.X || p.Y != _armedAt.Y))
            _moved = true;
        return _moved;
    }
}
