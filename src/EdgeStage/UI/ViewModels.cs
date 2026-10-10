using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
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

/// <summary>One preview card. Identity is the window handle; everything else can change while the sidebar is open.</summary>
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

    public string Title { get => _title; set => Set(ref _title, value); }
    public ImageSource? Icon { get => _icon; set => Set(ref _icon, value); }
    public bool IsMinimized { get => _isMinimized; set => Set(ref _isMinimized, value); }

    /// <summary>The window that was in the foreground when the sidebar opened.</summary>
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }

    /// <summary>Keyboard/hover selection.</summary>
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    /// <summary>Short problem message shown in place of the title (e.g. Windows refused the switch).</summary>
    public string? Notice { get => _notice; set => Set(ref _notice, value); }

    /// <summary>DWM could not create a live thumbnail for this window.</summary>
    public bool PreviewUnavailable { get => _previewUnavailable; set => Set(ref _previewUnavailable, value); }
}

/// <summary>
/// The card list. Order is fixed while the sidebar is open: new windows are appended, closed ones
/// removed, nothing is reshuffled under the user's pointer.
/// </summary>
public sealed class SidebarViewModel : ObservableObject
{
    // Layout constants (DIPs) shared with the XAML templates.
    public const double ListPadding = 10;
    public const double CardHeight = 182;   // 8 padding + 140 thumbnail + 6 gap + 20 title + 8 padding
    public const double CardSpacing = 8;
    public const double EmptyHeight = 120;

    private int _selectedIndex = -1;

    public ObservableCollection<CardViewModel> Cards { get; } = [];

    public bool IsEmpty => Cards.Count == 0;

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

    /// <summary>Height the card list needs, used to size the floating sidebar.</summary>
    /// <remarks>Each card carries its spacing as a bottom margin; the list's bottom margin is reduced by the
    /// same amount so the panel has equal padding top and bottom.</remarks>
    public double ContentHeightDip => Cards.Count == 0
        ? EmptyHeight
        : ListPadding + Cards.Count * (CardHeight + CardSpacing) + (ListPadding - CardSpacing) + 2 /* panel border */;

    public void Load(IEnumerable<WindowInfo> ordered, nint foreground, int initialSelection)
    {
        Cards.Clear();
        foreach (WindowInfo info in ordered)
            Cards.Add(new CardViewModel(info) { IsActive = info.Hwnd == foreground });
        SelectedIndex = initialSelection;
        Raise(nameof(IsEmpty));
    }

    public CardViewModel? Find(nint hwnd) => Cards.FirstOrDefault(c => c.Hwnd == hwnd);

    public CardViewModel Append(WindowInfo info)
    {
        var card = new CardViewModel(info);
        Cards.Add(card);
        Raise(nameof(IsEmpty));
        return card;
    }

    public void Remove(CardViewModel card)
    {
        int index = Cards.IndexOf(card);
        if (index < 0)
            return;
        CardViewModel? selected = Selected;
        Cards.RemoveAt(index);
        // Keep the same card selected; if the selected card itself went away, select its neighbor.
        SelectedIndex = selected is not null && selected != card
            ? Cards.IndexOf(selected)
            : Math.Min(index, Cards.Count - 1);
        Raise(nameof(IsEmpty));
    }

    public void MoveSelection(int delta)
    {
        if (Cards.Count == 0)
            return;
        int start = _selectedIndex < 0 ? (delta > 0 ? -1 : Cards.Count) : _selectedIndex;
        SelectedIndex = Math.Clamp(start + delta, 0, Cards.Count - 1);
    }

    public void SetActive(nint foreground)
    {
        foreach (CardViewModel card in Cards)
            card.IsActive = card.Hwnd == foreground;
    }

    public void Clear()
    {
        Cards.Clear();
        _selectedIndex = -1;
        Raise(nameof(SelectedIndex));
        Raise(nameof(IsEmpty));
    }
}
