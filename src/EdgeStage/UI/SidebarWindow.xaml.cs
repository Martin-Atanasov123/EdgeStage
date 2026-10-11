using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
    private const double ShowDurationMs = 180;
    private const double HideDurationMs = 140;
    private const double SlideDistanceDip = 24;

    /// <summary>Room around the panel (except on the screen-edge side) for the soft shadow.</summary>
    private const double ShadowDip = 10;

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
    private readonly ZoomOverlay _zoom = new();
    private readonly HoverGuard _hover = new();
    private readonly CornerMaskWindow _mask;
    private AppSettings _settings = AppSettings.Default;

    private readonly Stopwatch _animationClock = new();
    private bool _animating;
    private bool _animatingIn;
    private bool _hiding;
    private int _animationFrames;

    public SidebarWindow(WindowTracker tracker)
    {
        InitializeComponent();
        _tracker = tracker;
        DataContext = _vm;
        _mask = new CornerMaskWindow(new WindowInteropHelper(this).EnsureHandle());
        _thumbs = new ThumbnailLayoutCoordinator(this, CardList, Scroller, _mask);
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
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SidebarViewModel.Filter))
                ShowFilterText();
            if (!IsOpen)
                return;
            // The corner shapes take the card's hover/selected color.
            if (e.PropertyName == nameof(SidebarViewModel.SelectedIndex))
                _thumbs.Update();
            else if (e.PropertyName == nameof(SidebarViewModel.ContentHeightDip))
            {
                PlaceWindow();
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, _thumbs.Update);
            }
        };
        ApplyMetrics(CardMetrics.Default);
        // The corner shapes are drawn in the card color, which follows the theme.
        _mask.SetColors(ThemeManager.CardBackground, ThemeManager.CardHover);
        ThemeManager.Changed += OnThemeChanged;
        Closed += (_, _) => ThemeManager.Changed -= OnThemeChanged;
        Deactivated += (_, _) =>
        {
            if (IsOpen && Mode == SidebarOpenMode.Hotkey)
                Dismiss(_activating ? "window selected" : "clicked outside");
        };
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewTextInput += OnPreviewTextInput;
        AddHandler(PreviewCard.CloseRequestedEvent, new RoutedEventHandler(OnCloseRequested));
        AddHandler(PreviewCard.HideAppRequestedEvent, new RoutedEventHandler(OnHideAppRequested));
        Closed += (_, _) =>
        {
            _zoom.Close();
            _mask.Close();
        };
    }

    private void OnThemeChanged()
    {
        _mask.SetColors(ThemeManager.CardBackground, ThemeManager.CardHover);
        if (IsOpen)
            _thumbs.Update();
    }

    public bool IsOpen { get; private set; }
    public SidebarOpenMode Mode { get; private set; }
    public int CardCount => _vm.Cards.Count;
    public int LiveThumbnailCount => _thumbs.Count;

    /// <summary>Raised after the sidebar has hidden and released its resources, with the reason.</summary>
    public event Action<string>? Dismissed;

    /// <summary>Windows refused the switch after the sidebar had already closed (card title, result).</summary>
    public event Action<string, ActivationResult>? ActivationFailed;

    /// <summary>The user chose "Never show this app" for the given process name.</summary>
    public event Action<string>? HideAppRequested;

    /// <summary>The Exposé button in the header was clicked.</summary>
    public event Action? ExposeRequested;

    /// <summary>An edge-opened sidebar took the keyboard (search box clicked); Esc now arrives as a key press.</summary>
    public event Action? KeyboardTaken;

    /// <summary>Sizes the cards, list and panel from the appearance settings.</summary>
    public void ApplyMetrics(CardMetrics metrics)
    {
        _vm.Metrics = metrics;
        Resources["CardCornerRadius"] = new CornerRadius(metrics.CornerRadius);
        Resources["ThumbCornerRadius"] = new CornerRadius(metrics.ThumbCornerRadius);
        Resources["CardPaddingThickness"] = new Thickness(metrics.CardPadding);
        Resources["SidebarCardHeight"] = metrics.CardHeight;
        Resources["CardSpacingMargin"] = new Thickness(0, 0, 0, metrics.CardSpacing);
        CardList.Margin = new Thickness(metrics.ListPadding, metrics.ListPadding, metrics.ListPadding, metrics.ListBottomMargin);
        _mask.Radius = metrics.ThumbCornerRadius > 0 ? metrics.ThumbCornerRadius + 1 : 0;
        if (IsOpen)
        {
            PlaceWindow();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, _thumbs.Update);
        }
    }

    private void ShowFilterText()
    {
        FilterText.Text = _vm.Filter;
        SearchPlaceholder.Visibility = _vm.HasFilter ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- open / dismiss --------------------------------------------------------------------------

    public void Open(SidebarOpenMode mode, MonitorArea monitor, AppSettings settings)
    {
        if (IsOpen)
            return;
        if (_hiding)
            FinishHide(); // reopened during the hide animation

        Mode = mode;
        _monitor = monitor;
        _edge = settings.Edge;
        _dismissDelayMs = settings.DismissDelayMs;
        ApplySettings(settings);
        _vm.Filter = "";
        ShowFilterText();

        // Capture the foreground window before EdgeStage itself appears.
        nint foreground = User32.GetForegroundWindow();
        List<WindowInfo> windows = WindowEnumerator.Enumerate().Where(IsListed).ToList();
        _tracker.Mru.PruneWhere(User32.IsWindow);
        List<WindowInfo> ordered = _tracker.Mru.Order(windows, w => w.Hwnd, foreground);

        // Hotkey: pre-select the previously used window so the hotkey + Enter switches back.
        int initialSelection = mode == SidebarOpenMode.Hotkey ? Math.Min(1, ordered.Count - 1) : -1;
        _vm.Load(ordered, foreground, initialSelection);
        for (int i = 0; i < ordered.Count; i++)
            _icons.Apply(_vm.Cards[i], ordered[i]);
        EmptyText.Visibility = _vm.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        Log.Debug($"Listing {ordered.Count} windows ({_settings.ExcludedApps.Count} hidden apps, " +
                  $"{(_settings.OnlyPointerMonitor ? "this monitor only" : "all monitors")}).");

        SetExStyle(WS_EX_NOACTIVATE, mode == SidebarOpenMode.Edge);
        PlaceWindow();

        _tracker.WindowEvent += OnWindowEvent;
        _tracker.BeginDetailedTracking();

        // Shown fully transparent first; the reveal animation starts once layout has run and the
        // thumbnails are registered, so cards never appear without their previews.
        Root.Opacity = 0;
        Slide.X = SlideOffset(1.0);
        _thumbs.Opacity = 0;
        IsHitTestVisible = true;
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
            ScrollSelectedIntoView();
            _mask.ShowMask();
            _thumbs.Update();
            StartAnimation(show: true);
            Log.Info($"Sidebar opened ({mode}) on monitor {monitor.Bounds} @ {monitor.Dpi} dpi: " +
                     $"{_vm.Cards.Count} windows, {_thumbs.Count} live thumbnails.");
        });

        _hover.Arm();
        _pointerEntered = mode == SidebarOpenMode.Edge; // the pointer is at the edge already
        _outsideSinceMs = -1;
        _pointerTimer.Start();
        _sourceSizeTimer.Start();
    }

    /// <summary>
    /// Closes the sidebar. Window hooks and timers stop at once; the short fade-out runs only after
    /// any window switch has already happened, so animation never delays the switch.
    /// </summary>
    public void Dismiss(string reason, bool immediate = false)
    {
        if (!IsOpen)
            return;
        IsOpen = false;

        _pointerTimer.Stop();
        _sourceSizeTimer.Stop();
        _tracker.WindowEvent -= OnWindowEvent;
        _tracker.EndDetailedTracking();
        IsHitTestVisible = false;

        Log.Info($"Sidebar dismissed: {reason}.");
        Dismissed?.Invoke(reason);

        _hiding = true;
        if (immediate || !IsVisible)
            FinishHide();
        else
            StartAnimation(show: false);
    }

    private void FinishHide()
    {
        StopAnimation();
        _hiding = false;
        _thumbs.ReleaseAll();
        Hide();
        _vm.Clear();
        Root.Opacity = 0;
    }

    // ---- animation ---------------------------------------------------------------------------------

    /// <summary>Offset of the sliding panel: 0 at rest, SlideDistance toward the screen edge when hidden.</summary>
    private double SlideOffset(double hiddenFraction) => !_settings.UseMotion ? 0 :
        (_edge == ScreenEdge.Left ? -1 : 1) * SlideDistanceDip * hiddenFraction;

    private void StartAnimation(bool show)
    {
        _animatingIn = show;
        _animationFrames = 0;
        _animationClock.Restart();
        if (!_animating)
        {
            _animating = true;
            CompositionTarget.Rendering += OnAnimationFrame;
        }
    }

    private void StopAnimation()
    {
        if (!_animating)
            return;
        _animating = false;
        CompositionTarget.Rendering -= OnAnimationFrame;
        _animationClock.Stop();
    }

    /// <summary>
    /// One frame. The WPF panel (slide + opacity) and the DWM thumbnails (rect + opacity) are
    /// updated together; DWM thumbnails ignore WPF opacity, so they are faded explicitly.
    /// </summary>
    private void OnAnimationFrame(object? sender, EventArgs e)
    {
        // Animations off: appear at once. Reduced motion: a short fade without the slide (see SlideOffset).
        double duration = !_settings.Animations ? 1
            : (_animatingIn ? ShowDurationMs : HideDurationMs) / _settings.AnimationSpeed;
        double elapsed = _animationClock.Elapsed.TotalMilliseconds;
        double t = Math.Clamp(elapsed / duration, 0, 1);
        _animationFrames++;
        double visible = _animatingIn
            ? 1 - Math.Pow(1 - t, 3)   // ease-out cubic: fast start, gentle landing
            : 1 - t * t;               // ease-in quadratic: leaves quickly

        Root.Opacity = visible;
        Slide.X = SlideOffset(1 - visible);
        _thumbs.Opacity = (byte)Math.Round(255 * visible);
        _thumbs.Update();

        if (t < 1)
            return;
        StopAnimation();
        Log.Info($"{(_animatingIn ? "Show" : "Hide")} animation: {_animationFrames} frames in {elapsed:F0} ms.");
        if (!_animatingIn)
            FinishHide();
    }

    // ---- placement -------------------------------------------------------------------------------

    private void PlaceWindow()
    {
        if (_monitor is null)
            return;
        double scale = _monitor.Scale;
        RECT panel = SidebarPlacement.Compute(_monitor, _edge, _vm.ContentHeightDip, _vm.Metrics.SidebarWidth);
        int shadow = (int)Math.Round(ShadowDip * scale);
        RECT work = _monitor.WorkArea;

        // The window covers the gap to the screen edge (so the panel can slide in from it) and a
        // little room on the other sides for the shadow.
        RECT window = _edge == ScreenEdge.Left
            ? new RECT(work.Left, panel.Top - shadow, panel.Right + shadow, panel.Bottom + shadow)
            : new RECT(panel.Left - shadow, panel.Top - shadow, work.Right, panel.Bottom + shadow);

        var margin = new Thickness(
            (panel.Left - window.Left) / scale, (panel.Top - window.Top) / scale,
            (window.Right - panel.Right) / scale, (window.Bottom - panel.Bottom) / scale);
        Panel.Margin = margin;
        Shadow1.Margin = Grow(margin, 3, 2);
        Shadow2.Margin = Grow(margin, 6, 3);
        Shadow3.Margin = Grow(margin, 9, 4);

        // Physical pixels via SetWindowPos: unambiguous on mixed-DPI monitor setups.
        User32.SetWindowPos(_hwnd, HWND_TOPMOST, window.Left, window.Top, window.Width, window.Height, SWP_NOACTIVATE);
        _mask.Place(window);
        _keepOpen = SidebarPlacement.KeepOpenRegion(panel, _edge, _monitor);
    }

    /// <summary>Margin for a shadow layer <paramref name="spread"/> DIPs larger than the panel, shifted down by <paramref name="drop"/>.</summary>
    private static Thickness Grow(Thickness m, double spread, double drop) =>
        new(m.Left - spread, m.Top - spread + drop, m.Right - spread, m.Bottom - spread - drop);

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
                if (card is null && WindowEnumerator.TryRead(hwnd) is { } info && IsListed(info))
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
                if (_vm.HasFilter)
                    _vm.Filter = "";      // first Esc clears the filter, the next one closes
                else
                    Dismiss("escape");
                break;
            case Key.Back when _vm.HasFilter:
                _vm.Filter = _vm.Filter[..^1];
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
            case Key.W when Keyboard.Modifiers == ModifierKeys.Control:
                if (_vm.Selected is { } toClose)
                    WindowActions.RequestClose(toClose); // Ctrl+W: close the selected window
                break;
            case Key.Space when _vm.HasFilter:
                return; // part of the filter text; handled in PreviewTextInput
            case Key.Enter or Key.Space:
                if (_vm.Selected is { } selected)
                    ActivateCard(selected);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>Printable characters typed while the sidebar has focus filter the cards.</summary>
    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        string text = e.Text;
        if (!IsOpen || !_settings.SidebarSearch || string.IsNullOrEmpty(text) || text.Any(char.IsControl) || _vm.Filter.Length >= 40)
            return;
        if (text == " " && !_vm.HasFilter)
            return;
        _vm.Filter += text;
        e.Handled = true;
    }

    /// <summary>Whether a window belongs in the sidebar under the current settings.</summary>
    private bool IsListed(WindowInfo window)
    {
        if (!TestHooks.IsListed(window) || _settings.IsExcluded(window.ProcessName))
            return false;
        if (_settings.OnlyPointerMonitor && _monitor is not null
            && User32.MonitorFromWindow(window.Hwnd, MONITOR_DEFAULTTONEAREST) != _monitor.Handle)
            return false;
        return true;
    }

    /// <summary>Applies changed settings (appearance, search, grouping; an app just hidden disappears at once).</summary>
    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        if (_vm.Metrics != settings.CardMetrics)
            ApplyMetrics(settings.CardMetrics);
        _vm.ShowSearchBar = true; // the header also holds the Exposé button
        SearchBox.Visibility = settings.SidebarSearch ? Visibility.Visible : Visibility.Hidden;
        _vm.Grouping = settings.SidebarGrouping;
        if (!settings.SidebarSearch && _vm.HasFilter)
            _vm.Filter = "";
        if (!IsOpen)
            return;
        foreach (CardViewModel card in _vm.AllCards.Where(c => settings.IsExcluded(c.ProcessName)).ToList())
            RemoveCard(card, "app hidden");
    }

    /// <summary>Hold-to-switch: moves the selection with wrap-around (negative = backwards).</summary>
    public void CycleSelection(int delta)
    {
        if (!IsOpen)
            return;
        _vm.CycleSelection(delta);
        ScrollSelectedIntoView();
    }

    /// <summary>Switches to the selected card (used when the hold-to-switch modifiers are released).</summary>
    public void ActivateSelected()
    {
        if (IsOpen && _vm.Selected is { } card)
            ActivateCard(card);
        else
            Dismiss("nothing selected");
    }

    private void OnCloseRequested(object sender, RoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not CardViewModel card)
            return;
        e.Handled = true;
        WindowActions.RequestClose(card);
    }

    private void OnHideAppRequested(object sender, RoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not CardViewModel { ProcessName.Length: > 0 } card)
            return;
        e.Handled = true;
        HideAppRequested?.Invoke(card.ProcessName);
    }

    private void MoveSelection(int delta)
    {
        _vm.MoveSelection(delta);
        ScrollSelectedIntoView();
    }

    private void ScrollSelectedIntoView()
    {
        if (_vm.Selected is { } card && CardList.ItemContainerGenerator.ContainerFromItem(card) is FrameworkElement container)
            container.BringIntoView();
    }

    private void OnGroupHeaderClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GroupHeaderViewModel header })
            _vm.ToggleGroup(header);
    }

    private void OnExposeClicked(object sender, RoutedEventArgs e) => ExposeRequested?.Invoke();

    /// <summary>
    /// Clicking the search box of an edge-opened sidebar (which never takes focus on its own) lets it
    /// take the keyboard so the user can type.
    /// </summary>
    private void OnSearchBoxClicked(object sender, MouseButtonEventArgs e)
    {
        if (!IsOpen || Mode == SidebarOpenMode.Hotkey)
            return;
        Mode = SidebarOpenMode.Hotkey;
        SetExStyle(WS_EX_NOACTIVATE, false);
        Activate();
        Focus();
        Log.Debug("Sidebar took keyboard focus for search.");
        KeyboardTaken?.Invoke();
    }

    private void OnCardMouseEnter(object sender, MouseEventArgs e)
    {
        if (!_hover.Allows() || sender is not FrameworkElement { DataContext: CardViewModel card })
            return;
        int index = _vm.Cards.IndexOf(card);
        if (index != _vm.SelectedIndex)
            _vm.SelectedIndex = index;
    }

    private void OnCardClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CardViewModel card })
            ActivateCard(card);
    }

#if DEBUG
    /// <summary>Same path as clicking the header's Exposé button.</summary>
    internal void ClickExposeButtonForTest() =>
        ExposeButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, ExposeButton));

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

            if (SystemParameters.ClientAreaAnimation && _settings.UseMotion && PlanZoom(card) is { } plan)
            {
                // Activation starts together with the animation, while this process still holds the
                // foreground (hiding the sidebar hands it to another app, after which Windows refuses).
                Dismiss("window selected");
                _zoom.Speed = _settings.AnimationSpeed;
                ActivationResult zoomed = await _zoom.PlayAsync(card.Hwnd, plan.Bounds, plan.From, plan.To,
                    () => WindowActivator.ActivateAsync(card.Hwnd));
                if (zoomed.Outcome == ActivationOutcome.Refused)
                {
                    Log.Warn($"Activation refused for 0x{card.Hwnd:X} ({card.ProcessName}): {zoomed.Detail}");
                    ActivationFailed?.Invoke(card.Title, zoomed);
                }
                return;
            }

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

    /// <summary>Start/end rectangles (screen device px) for the zoom transition, or null when it cannot be drawn.</summary>
    private (RECT Bounds, EdgeStage.Core.Thumbnails.PixelRect From, EdgeStage.Core.Thumbnails.PixelRect To)? PlanZoom(CardViewModel card)
    {
        if (_monitor is null || _thumbs.GetCardPreview(card) is not { } preview)
            return null;
        RECT target = ZoomOverlay.TargetRect(card.Hwnd);
        if (target.IsEmpty)
            return null;

        var origin = new POINT(0, 0);
        User32.ClientToScreen(_hwnd, ref origin);
        var from = new EdgeStage.Core.Thumbnails.PixelRect(
            preview.Rect.X + origin.X, preview.Rect.Y + origin.Y, preview.Rect.Width, preview.Rect.Height);

        // Same aspect ratio as the preview, centered in the window's rectangle (they differ only for minimized windows).
        var to = EdgeStage.Core.Thumbnails.ThumbnailGeometry.FitInside(
            new EdgeStage.Core.Thumbnails.PixelRect(target.Left, target.Top, target.Width, target.Height),
            preview.Source.Width, preview.Source.Height, maxScale: 100);

        RECT other = ZoomOverlay.MonitorBoundsOf(card.Hwnd, _monitor.Bounds);
        var bounds = new RECT(
            Math.Min(_monitor.Bounds.Left, other.Left), Math.Min(_monitor.Bounds.Top, other.Top),
            Math.Max(_monitor.Bounds.Right, other.Right), Math.Max(_monitor.Bounds.Bottom, other.Bottom));
        return (bounds, from, to);
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
