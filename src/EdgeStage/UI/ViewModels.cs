using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using EdgeStage.Core.Layout;
using EdgeStage.Core.WindowTracking;

namespace EdgeStage.UI;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One preview card. Identity is the window handle; everything else can change while the list is open.</summary>
public sealed class CardViewModel : ObservableObject
{
    private string _title;
    private ImageSource? _icon;
    private bool _isMinimized;
    private bool _isActive;
    private bool _isSelected;
    private string? _notice;
    private bool _previewUnavailable;

    public CardViewModel(WindowInfo info)
    {
        Hwnd = info.Hwnd;
        ProcessName = info.ProcessName;
        _title = info.DisplayTitle;
        _isMinimized = info.IsMinimized;
    }

    public nint Hwnd { get; }
    public string ProcessName { get; }

    /// <summary>Grouping key: the app's process name (case-insensitive), or the title for unknown processes.</summary>
    public string GroupKey => ProcessName.Length > 0 ? ProcessName.ToLowerInvariant() : "?";

    public string Title { get => _title; set => Set(ref _title, value); }
    public ImageSource? Icon { get => _icon; set => Set(ref _icon, value); }
    public bool IsMinimized { get => _isMinimized; set => Set(ref _isMinimized, value); }

    /// <summary>The window that was in the foreground when the list opened.</summary>
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }

    /// <summary>Keyboard/hover selection.</summary>
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    /// <summary>Short problem message shown in place of the title (e.g. Windows refused the switch).</summary>
    public string? Notice { get => _notice; set => Set(ref _notice, value); }

    /// <summary>DWM could not create a live thumbnail for this window.</summary>
    public bool PreviewUnavailable { get => _previewUnavailable; set => Set(ref _previewUnavailable, value); }

    /// <summary>True when <paramref name="filter"/> (case-insensitive) occurs in the title or the app name.</summary>
    public bool Matches(string filter) =>
        filter.Length == 0
        || Title.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
        || ProcessName.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
}

/// <summary>Header of an application group (only present when grouping is on).</summary>
public sealed class GroupHeaderViewModel : ObservableObject
{
    private bool _isCollapsed;
    private int _count;
    private ImageSource? _icon;

    public GroupHeaderViewModel(string key, string appName)
    {
        Key = key;
        AppName = appName;
    }

    public string Key { get; }
    public string AppName { get; }
    public ImageSource? Icon { get => _icon; set => Set(ref _icon, value); }

    /// <summary>Windows of this app that match the current filter.</summary>
    public int Count { get => _count; set { if (Set(ref _count, value)) Raise(nameof(Summary)); } }

    public bool IsCollapsed { get => _isCollapsed; set { if (Set(ref _isCollapsed, value)) Raise(nameof(Summary)); } }

    public string Summary => _count == 1 ? "1 window" : $"{_count} windows";
}

/// <summary>
/// The window list shared by the sidebar and Exposé: filtering (type-to-search), selection and
/// optional grouping by application. <see cref="Items"/> is what is displayed (group headers and
/// cards, in order); <see cref="Cards"/> are the selectable cards in the same order. Nothing is
/// reshuffled while the list is open: new windows join their group (or the end), closed ones leave.
/// </summary>
public class WindowListViewModel : ObservableObject
{
    private readonly List<CardViewModel> _all = [];
    private readonly Dictionary<string, GroupHeaderViewModel> _groups = new(StringComparer.Ordinal);
    private int _selectedIndex = -1;
    private string _filter = "";
    private bool _grouping;

    public ObservableCollection<CardViewModel> Cards { get; } = [];

    /// <summary>Display list: <see cref="GroupHeaderViewModel"/> and <see cref="CardViewModel"/> items.</summary>
    public ObservableCollection<object> Items { get; } = [];

    public bool IsEmpty => Cards.Count == 0;

    public int GroupCount => Items.Count(i => i is GroupHeaderViewModel);

    /// <summary>Type-to-search text; empty shows every window.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            value ??= "";
            if (value == _filter)
                return;
            _filter = value;
            Rebuild(keepSelection: true, selectFirstIfLost: true);
        }
    }

    public bool HasFilter => _filter.Length > 0;

    public string EmptyMessage => HasFilter ? "No matching windows" : "No other open windows";

    /// <summary>Group cards by application (collapsible). Every window stays individually selectable.</summary>
    public bool Grouping
    {
        get => _grouping;
        set
        {
            if (_grouping == value)
                return;
            _grouping = value;
            Rebuild(keepSelection: true, selectFirstIfLost: false);
        }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            int clamped = Cards.Count == 0 ? -1 : Math.Clamp(value, -1, Cards.Count - 1);
            for (int i = 0; i < Cards.Count; i++)
                Cards[i].IsSelected = i == clamped;
            Set(ref _selectedIndex, clamped);
        }
    }

    public CardViewModel? Selected => _selectedIndex >= 0 && _selectedIndex < Cards.Count ? Cards[_selectedIndex] : null;

    public IEnumerable<CardViewModel> AllCards => _all;

    public void Load(IEnumerable<WindowInfo> ordered, nint foreground, int initialSelection)
    {
        _filter = "";
        _all.Clear();
        _groups.Clear();
        foreach (WindowInfo info in ordered)
            _all.Add(new CardViewModel(info) { IsActive = info.Hwnd == foreground });
        Items.Clear();
        Cards.Clear();
        Rebuild(keepSelection: false, selectFirstIfLost: false);
        SelectedIndex = initialSelection;
    }

    /// <summary>Finds a card among all windows, including ones hidden by the filter or a collapsed group.</summary>
    public CardViewModel? Find(nint hwnd) => _all.FirstOrDefault(c => c.Hwnd == hwnd);

    public GroupHeaderViewModel? GroupOf(CardViewModel card) => _groups.GetValueOrDefault(card.GroupKey);

    public CardViewModel Append(WindowInfo info)
    {
        var card = new CardViewModel(info);
        _all.Add(card);
        Rebuild(keepSelection: true, selectFirstIfLost: false);
        return card;
    }

    public void Remove(CardViewModel card)
    {
        if (!_all.Remove(card))
            return;
        int index = Cards.IndexOf(card);
        CardViewModel? selected = Selected;
        Rebuild(keepSelection: true, selectFirstIfLost: false);
        // If the selected card itself went away, select its neighbor.
        if (selected == card && Cards.Count > 0)
            SelectedIndex = Math.Min(Math.Max(index, 0), Cards.Count - 1);
    }

    public void ToggleGroup(GroupHeaderViewModel group)
    {
        group.IsCollapsed = !group.IsCollapsed;
        Rebuild(keepSelection: true, selectFirstIfLost: false);
    }

    public void MoveSelection(int delta)
    {
        if (Cards.Count == 0)
            return;
        int start = _selectedIndex < 0 ? (delta > 0 ? -1 : Cards.Count) : _selectedIndex;
        SelectedIndex = Math.Clamp(start + delta, 0, Cards.Count - 1);
    }

    /// <summary>Moves the selection by <paramref name="delta"/> and wraps around the ends (hold-to-switch).</summary>
    public void CycleSelection(int delta)
    {
        if (Cards.Count == 0)
            return;
        int start = _selectedIndex < 0 ? 0 : _selectedIndex;
        SelectedIndex = ((start + delta) % Cards.Count + Cards.Count) % Cards.Count;
    }

    public void SetActive(nint foreground)
    {
        foreach (CardViewModel card in _all)
            card.IsActive = card.Hwnd == foreground;
    }

    public void Clear()
    {
        _all.Clear();
        _groups.Clear();
        Items.Clear();
        Cards.Clear();
        _filter = "";
        _selectedIndex = -1;
        Raise(nameof(SelectedIndex));
        RaiseListState();
    }

    /// <summary>Group sizes in display order (expanded groups only), for grid layout.</summary>
    public IReadOnlyList<int> VisibleGroupSizes()
    {
        if (!_grouping)
            return [Cards.Count];
        var sizes = new List<int>();
        int current = -1;
        foreach (object item in Items)
        {
            if (item is GroupHeaderViewModel)
            {
                if (current >= 0)
                    sizes.Add(current);
                current = 0;
            }
            else if (current >= 0)
            {
                current++;
            }
        }
        if (current >= 0)
            sizes.Add(current);
        return sizes;
    }

    protected virtual void OnListChanged()
    {
    }

    private void Rebuild(bool keepSelection, bool selectFirstIfLost)
    {
        CardViewModel? keep = keepSelection ? Selected : null;
        List<CardViewModel> matching = _all.Where(c => c.Matches(_filter)).ToList();

        var items = new List<object>();
        var cards = new List<CardViewModel>();
        if (!_grouping)
        {
            items.AddRange(matching);
            cards.AddRange(matching);
        }
        else
        {
            // Groups ordered by their most recently used window; windows keep MRU order inside.
            foreach (var group in matching.GroupBy(c => c.GroupKey))
            {
                CardViewModel first = group.First();
                if (!_groups.TryGetValue(group.Key, out GroupHeaderViewModel? header))
                {
                    header = new GroupHeaderViewModel(group.Key, first.ProcessName.Length > 0 ? first.ProcessName : first.Title);
                    _groups[group.Key] = header;
                }
                header.Count = group.Count();
                header.Icon ??= group.Select(c => c.Icon).FirstOrDefault(i => i is not null);
                items.Add(header);
                // While searching, matches are shown even inside collapsed groups.
                if (!header.IsCollapsed || HasFilter)
                {
                    items.AddRange(group);
                    cards.AddRange(group);
                }
            }
        }

        Sync(Items, items);
        Sync(Cards, cards);

        int index = keep is not null ? Cards.IndexOf(keep) : -1;
        if (index < 0 && selectFirstIfLost)
            index = Cards.Count > 0 ? 0 : -1; // the selected card was filtered out: take the first match
        SelectedIndex = index;
        RaiseListState();
        OnListChanged();
    }

    private void RaiseListState()
    {
        Raise(nameof(Filter));
        Raise(nameof(HasFilter));
        Raise(nameof(IsEmpty));
        Raise(nameof(EmptyMessage));
        Raise(nameof(GroupCount));
    }

    /// <summary>Makes <paramref name="target"/> equal <paramref name="desired"/> with minimal moves (keeps containers alive).</summary>
    private static void Sync<T>(ObservableCollection<T> target, IList<T> desired) where T : class
    {
        var wanted = new HashSet<T>(desired, ReferenceEqualityComparer.Instance);
        for (int i = target.Count - 1; i >= 0; i--)
            if (!wanted.Contains(target[i]))
                target.RemoveAt(i);
        for (int i = 0; i < desired.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], desired[i]))
                continue;
            int existing = -1;
            for (int j = i + 1; j < target.Count; j++)
                if (ReferenceEquals(target[j], desired[i])) { existing = j; break; }
            if (existing >= 0)
                target.Move(existing, i);
            else
                target.Insert(i, desired[i]);
        }
    }
}

/// <summary>The sidebar's list plus its size, which depends on the card metrics.</summary>
public sealed class SidebarViewModel : WindowListViewModel
{
    private CardMetrics _metrics = CardMetrics.Default;

    public CardMetrics Metrics
    {
        get => _metrics;
        set
        {
            if (Set(ref _metrics, value))
                Raise(nameof(ContentHeightDip));
        }
    }

    /// <summary>Whether the search/header bar is shown at the top of the sidebar.</summary>
    public bool ShowSearchBar { get; set; } = true;

    /// <summary>Height the list needs, used to size the floating sidebar.</summary>
    public double ContentHeightDip => _metrics.ContentHeight(Cards.Count, GroupCount, ShowSearchBar);

    protected override void OnListChanged() => Raise(nameof(ContentHeightDip));
}
