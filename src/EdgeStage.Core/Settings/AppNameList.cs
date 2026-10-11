using System.Collections;
using System.Runtime.CompilerServices;

namespace EdgeStage.Core.Settings;

/// <summary>
/// Immutable list of app (process) names that compares by content, ignoring case, so settings
/// records holding it keep value equality.
/// </summary>
[CollectionBuilder(typeof(AppNameList), nameof(Create))]
public sealed class AppNameList : IReadOnlyList<string>, IEquatable<AppNameList>
{
    private readonly string[] _items;

    private AppNameList(string[] items) => _items = items;

    public static AppNameList Empty { get; } = new([]);

    public static AppNameList Create(ReadOnlySpan<string> items) => items.IsEmpty ? Empty : new(items.ToArray());

    public static AppNameList From(IEnumerable<string>? items) => items is null ? Empty : new([.. items]);

    public static implicit operator AppNameList(string[] items) => From(items);

    public static implicit operator AppNameList(List<string> items) => From(items);

    public int Count => _items.Length;

    public string this[int index] => _items[index];

    public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    public bool Equals(AppNameList? other) =>
        other is not null && _items.AsSpan().SequenceEqual(other._items, StringComparer.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is AppNameList other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (string item in _items)
            hash.Add(item, StringComparer.OrdinalIgnoreCase);
        return hash.ToHashCode();
    }

    public override string ToString() => $"[{string.Join(", ", _items)}]";
}
