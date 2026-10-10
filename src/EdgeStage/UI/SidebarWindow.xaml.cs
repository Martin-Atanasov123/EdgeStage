using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using EdgeStage.Core.Activation;
using EdgeStage.Core.Diagnostics;
using EdgeStage.Core.Input;
using EdgeStage.Core.Interop;
using EdgeStage.Core.Layout;
using EdgeStage.Core.Settings;
using EdgeStage.Core.WindowTracking;
using static EdgeStage.Core.Interop.NativeConstants;

namespace EdgeStage.UI;

public enum SidebarOpenMode
{
    /// <summary>Opened by the hotkey (or tray/relaunch): takes keyboard focus; arrows, Enter and Esc work.</summary>
    Hotkey,
    /// <summary>Opened by resting the pointer at the edge: never steals focus; mouse to pick, Esc via a temporary hotkey.</summary>
    Edge,
}

/// <summary>
/// The floating sidebar. Lifetime of everything costly is bounded by <see cref="Open"/> and
/// <see cref="Dismiss"/>: thumbnails, detailed window hooks and the pointer timer exist only while
/// the sidebar is on screen.
/// </summary>
public partial class SidebarWindow : Window
{
    public static readonly Thickness ListMargin = new(
        SidebarViewModel.ListPadding, SidebarViewModel.ListPadding,
        SidebarViewModel.ListPadding, SidebarViewModel.ListPadding - SidebarViewModel.CardSpacing);

    private static readonly TimeSpan PointerInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan SourceSizeInterval = TimeSpan.FromMilliseconds(500);

    private readonly SidebarViewModel _vm = new();
    private readonly WindowTracker _tracker;
    private readonly IconCache _icons = new();
    private readonly ThumbnailLayoutCoordinator _thumbs;
    private readonly DispatcherTimer _pointerTimer;
    private readonly DispatcherTimer _sourceSizeTimer;
    private readonly nint _hwnd;

    private MonitorArea? _monitor;
    private ScreenEdge _edge;
    private int _dismissDelayMs;
    private RECT _keepOpen;
    private bool _pointerEntered;
    private long _outsideSinceMs = -1;
    private bool _activating;

    public SidebarWindow(WindowTracker tracker)
    {
        InitializeComponent();
        _tracker = tracker;
        DataContext = _vm;
        _thumbs = new ThumbnailLayoutCoordinator(this, CardList, Scroller);
        _thumbs.SourceLost += card => RemoveCard(card, "source window gone");

        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        SetExStyle(WS_EX_TOOLWINDOW, true); // never listed in Alt+Tab or the taskbar

        _pointerTimer = new DispatcherTimer(PointerInterval, DispatcherPriority.Input, OnPointerTick, Dispatcher);
        _pointerTimer.Stop();
        _sourceSizeTimer = new DispatcherTimer(SourceSizeInterval, DispatcherPriority.Background, (_, _) =>
        {
            if (_thumbs.RefreshSourceSizes())
                _thumbs.Update();
        }, Dispatcher);
        _sourceSizeTimer.Stop();

        CardList.LayoutUpdated += (_, _) => _thumbs.Update();
        Scroller.ScrollChanged += (_, _) => _thumbs.Update();
        _vm.Cards.CollectionChanged += OnCardsChanged;
        Deactivated += (_, _) =>
        {
            if (IsOpen && Mode == SidebarOpenMode.Hotkey)
                Dismiss(_activating ? "window selected" : "clicked outside");
        };
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public bool IsOpen { get; private set; }
    public SidebarOpenMode Mode { get; private set; }
    public int CardCount => _vm.Cards.Count;
    public int LiveThumbnailCount => _thumbs.Count;

    /// <summary>Raised after the sidebar has hidden and released its resources, with the reason.</summary>
    public event Action<string>? Dismissed;

    // ---- open / dismiss --------------------------------------------------------------------------

    public void Open(SidebarOpenMode mode, MonitorArea monitor, AppSettings settings)
    {
        if (IsOpen)
            return;

        Mode = mode;
        _monitor = monitor;
        _edge = settings.Edge;
        _dismissDelayMs = settings.DismissDelayMs;

        // Capture the foreground window before EdgeStage itself appears.
        nint foreground = User32.GetForegroundWindow();
        List<WindowInfo> windows = WindowEnumerator.Enumerate().Where(TestHooks.IsListed).ToList();
        _tracker.Mru.PruneWhere(User32.IsWindow);
        List<WindowInfo> ordered = _tracker.Mru.Order(windows, w => w.Hwnd, foreground);

        // Hotkey: pre-select the previously used window so the hotkey + Enter switches back.
        int initialSelection = mode == SidebarOpenMode.Hotkey ? Math.Min(1, ordered.Count - 1) : -1;
        _vm.Load(ordered, foreground, initialSelection);
        for (int i = 0; i < ordered.Count; i++)
            _icons.Apply(_vm.Cards[i], ordered[i]);
        EmptyText.Visibility = _vm.IsEmpty ? Visibility.Visible : Visibility.Collapsed;

        SetExStyle(WS_EX_NOACTIVATE, mode == SidebarOpenMode.Edge);
        PlaceWindow();

        _tracker.WindowEvent += OnWindowEvent;
        _tracker.BeginDetailedTracking();

        // Shown transparent first; revealed together with the thumbnails once layout has run, so
        // cards never appear without their previews.
        Opacity = 0;
        ShowActivated = mode == SidebarOpenMode.Hotkey;
        Show();
        IsOpen = true;
        if (mode == SidebarOpenMode.Hotkey)
        {
            Activate();
            Focus();
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!IsOpen)
                return;
            _thumbs.Sync(_vm.Cards, _hwnd);
            UpdateLayout();
            _thumbs.Update();
            Opacity = 1;
            ScrollSelectedIntoView();
            Log.Info($"Sidebar opened ({mode}) on monitor {monitor.Bounds} @ {monitor.Dpi} dpi: " +
                     $"{_vm.Cards.Count} windows, {_thumbs.Count} live thumbnails.");
        });

        _pointerEntered = mode == SidebarOpenMode.Edge; // the pointer is at the edge already
        _outsideSinceMs = -1;
        _pointerTimer.Start();
        _sourceSizeTimer.Start();
    }

    public void Dismiss(string reason)
    {
        if (!IsOpen)
            return;
        IsOpen = false;

        _pointerTimer.Stop();
        _sourceSizeTimer.Stop();
        _tracker.WindowEvent -= OnWindowEvent;
        _tracker.EndDetailedTracking();
        _thumbs.ReleaseAll();
        Hide();
        _vm.Clear();

        Log.Info($"Sidebar dismissed: {reason}.");
        Dismissed?.Invoke(reason);
    }

    // ---- placement -------------------------------------------------------------------------------

    private void PlaceWindow()
    {
        if (_monitor is null)
            return;
        RECT r = SidebarPlacement.Compute(_monitor, _edge, _vm.ContentHeightDip);
        // Physical pixels via SetWindowPos: unambiguous on mixed-DPI monitor setups.
        User32.SetWindowPos(_hwnd, HWND_TOPMOST, r.Left, r.Top, r.Width, r.Height, SWP_NOACTIVATE);
        _keepOpen = SidebarPlacement.KeepOpenRegion(r, _edge, _monitor);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        // WPF applies Windows' suggested rectangle on a DPI change; restore the computed one.
        if (IsOpen)
        {
            PlaceWindow();
            _thumbs.Update();
        }
    }

    // ---- live updates while open ------------------------------------------------------------------

    private void OnWindowEvent(WindowEventKind kind, nint hwnd)
    {
        if (!IsOpen)
            return;
        CardViewModel? card = _vm.Find(hwnd);
        switch (kind)
        {
            case WindowEventKind.Shown or WindowEventKind.Uncloaked:
                if (card is null && WindowEnumerator.TryRead(hwnd) is { } info && TestHooks.IsListed(info))
                    AddCard(info);
                break;

            case WindowEventKind.Destroyed:
                if (card is not null)
                    RemoveCard(card, "window closed");
                break;

            case WindowEventKind.Hidden or WindowEventKind.Cloaked:
                if (card is not null && (!User32.IsWindowVisible(hwnd) || Dwmapi.IsCloaked(hwnd)))
                    RemoveCard(card, $"window {kind.ToString().ToLowerInvariant()}");
                break;

            case WindowEventKind.TitleChanged:
                if (card is not null && WindowEnumerator.TryRead(hwnd) is { } renamed)
                    card.Title = renamed.DisplayTitle;
                break;

            case WindowEventKind.MinimizeStart:
                if (card is not null)
                    card.IsMinimized = true;
                break;

            case WindowEventKind.MinimizeEnd:
                if (card is not null)
                    card.IsMinimized = false;
                break;

            case WindowEventKind.Foreground:
                _vm.SetActive(hwnd);
                break;
        }
    }

    private void AddCard(WindowInfo info)
    {
        CardViewModel card = _vm.Append(info);
        _icons.Apply(card, info);
    }

    private void RemoveCard(CardViewModel card, string reason)
    {
        Log.Debug($"Card 0x{card.Hwnd:X} removed: {reason}.");
        _vm.Remove(card);
    }

    private void OnCardsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!IsOpen)
            return; // bulk load during Open() is handled there
        EmptyText.Visibility = _vm.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        PlaceWindow();
        _thumbs.Sync(_vm.Cards, _hwnd);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, _thumbs.Update);
    }

    // ---- input -------------------------------------------------------------------------------------

    private void OnPointerTick(object? sender, EventArgs e)
    {
        if (!IsOpen || _activating || !User32.GetCursorPos(out POINT p))
            return;

        if (SidebarPlacement.Contains(_keepOpen, p))
        {
            _pointerEntered = true;
            _outsideSinceMs = -1;
            return;
        }

        // Pointer-leave only dismisses once the pointer has been over the sidebar during this
        // showing; a hotkey-opened sidebar stays put while the mouse is elsewhere.
        if (!_pointerEntered)
            return;
        long now = Environment.TickCount64;
        if (_outsideSinceMs < 0)
            _outsideSinceMs = now;
        else if (now - _outsideSinceMs >= _dismissDelayMs)
            Dismiss("pointer left");
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case Key.Escape:
                Dismiss("escape");
                break;
            case Key.Down or Key.Right:
            case Key.Tab when !shift:
                MoveSelection(1);
                break;
            case Key.Up or Key.Left:
            case Key.Tab when shift:
                MoveSelection(-1);
                break;
            case Key.PageDown:
                MoveSelection(3);
                break;
            case Key.PageUp:
                MoveSelection(-3);
                break;
            case Key.Home:
                _vm.SelectedIndex = 0;
                ScrollSelectedIntoView();
                break;
            case Key.End:
                _vm.SelectedIndex = _vm.Cards.Count - 1;
                ScrollSelectedIntoView();
                break;
            case Key.Enter or Key.Space:
                if (_vm.Selected is { } selected)
                    ActivateCard(selected);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void MoveSelection(int delta)
    {
        _vm.MoveSelection(delta);
        ScrollSelectedIntoView();
    }

    private void ScrollSelectedIntoView()
    {
        if (_vm.SelectedIndex >= 0 && CardList.ItemContainerGenerator.ContainerFromIndex(_vm.SelectedIndex) is FrameworkElement container)
            container.BringIntoView();
    }

    private void OnCardMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CardViewModel card })
            _vm.SelectedIndex = _vm.Cards.IndexOf(card);
    }

    private void OnCardClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CardViewModel card })
            ActivateCard(card);
    }

#if DEBUG
    internal void ActivateSelectedForTest()
    {
        if (_vm.Selected is { } card)
            ActivateCard(card);
    }
#endif

    /// <summary>Activation runs first; the sidebar hides only after Windows confirms the switch.</summary>
    private async void ActivateCard(CardViewModel card)
    {
        if (_activating || !IsOpen)
            return;
        _activating = true;
        try
        {
            card.Notice = null;
            ActivationResult result = await WindowActivator.ActivateAsync(card.Hwnd);
            switch (result.Outcome)
            {
                case ActivationOutcome.Activated:
                    Dismiss("window selected");
                    break;
                case ActivationOutcome.WindowGone:
                    RemoveCard(card, "window closed before activation");
                    break;
                default:
                    card.Notice = result.TargetElevated
                        ? "Can't switch: runs as administrator"
                        : "Windows didn't allow the switch";
                    Log.Warn($"Activation refused for 0x{card.Hwnd:X} ({card.ProcessName}): {result.Detail}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Activation of 0x{card.Hwnd:X} failed", ex);
        }
        finally
        {
            _activating = false;
        }
    }

    // ---- native helpers ----------------------------------------------------------------------------

    private void SetExStyle(long flag, bool enabled)
    {
        long ex = User32.GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
        long updated = enabled ? ex | flag : ex & ~flag;
        if (updated != ex)
            User32.SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (nint)updated);
    }
}
