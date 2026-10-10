namespace EdgeStage.Core.WindowTracking;

/// <summary>
/// Remembers when each window was last in the foreground and orders windows most-recently-used
/// first. Windows that have not been seen in the foreground since EdgeStage started keep their
/// Z-order position after all remembered windows (Z-order is itself an approximation of recency).
/// </summary>
public sealed class MruOrder
{
    private readonly Dictionary<nint, long> _lastActivated = [];
    private long _sequence;

    /// <summary>Records that <paramref name="hwnd"/> became the foreground window.</summary>
    public void Touch(nint hwnd)
    {
        if (hwnd != 0)
            _lastActivated[hwnd] = ++_sequence;
    }

    public void Forget(nint hwnd) => _lastActivated.Remove(hwnd);

    /// <summary>Drops entries for windows that are no longer present (bounded memory).</summary>
    public void Prune(IEnumerable<nint> alive)
    {
        var keep = alive as ISet<nint> ?? new HashSet<nint>(alive);
        foreach (nint hwnd in _lastActivated.Keys.Where(h => !keep.Contains(h)).ToList())
            _lastActivated.Remove(hwnd);
    }

    /// <summary>Drops entries for which <paramref name="keep"/> is false (e.g. windows that no longer exist).</summary>
    public void PruneWhere(Func<nint, bool> keep)
    {
        foreach (nint hwnd in _lastActivated.Keys.Where(h => !keep(h)).ToList())
            _lastActivated.Remove(hwnd);
    }

    public int Count => _lastActivated.Count;

    /// <summary>
    /// Orders <paramref name="zOrdered"/> (top of Z-order first): the foreground window first, then
    /// remembered windows by recency, then the rest in Z-order.
    /// </summary>
    public List<T> Order<T>(IReadOnlyList<T> zOrdered, Func<T, nint> hwndOf, nint foreground)
    {
        return zOrdered
            .Select((item, zIndex) => (item, zIndex, hwnd: hwndOf(item)))
            .OrderByDescending(x => x.hwnd == foreground && foreground != 0)
            .ThenByDescending(x => _lastActivated.TryGetValue(x.hwnd, out long seq) ? seq : long.MinValue)
            .ThenBy(x => x.zIndex)
            .Select(x => x.item)
            .ToList();
    }
}
