using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
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
using EdgeStage.Core.Thumbnails;
using EdgeStage.Core.WindowTracking;
using static EdgeStage.Core.Interop.NativeConstants;

namespace EdgeStage.UI;

/// <summary>How Exposé lays out and filters its windows.</summary>
public sealed record ExposeOptions(
    bool AllMonitors = false,
    ExposeDensity Density = ExposeDensity.Auto,
    CardSize CardSize = CardSize.Standard,
    bool Grouping = false,
    bool ShowTitles = true,
    bool ShowIcons = true,
    bool Search = true,
    bool Animations = true,
    double AnimationScale = 1.0,
    bool Instant = false)
{
    public static ExposeOptions Default { get; } = new();

    public static ExposeOptions From(AppSettings s) => new(
        s.ExposeAllMonitors, s.ExposeDensity, s.CardSize, s.ExposeGrouping, s.ExposeShowTitles,
        s.ExposeShowIcons, s.ExposeSearch, s.UseMotion, s.AnimationSpeed, Instant: !s.Animations);
}

/// <summary>
/// Exposé: every window of the monitor (or of all monitors) at once, in an adaptive grid on a dimmed
/// full-monitor overlay. Opening, the previews fly from the windows' real positions into the grid;
/// closing, they fly back (the chosen window comes forward under the overlay at the same time).
/// Like the sidebar, everything costly exists only while it is on screen.
/// </summary>
public partial class ExposeWindow : Window
{
    private const double OpenDurationMs = 300;
    private const double CloseDurationMs = 230;
    private const double FadeDurationMs = 140;
    private const double HeaderHeight = 34;

    private static readonly TimeSpan SourceSizeInterval = TimeSpan.FromMilliseconds(500);

    private readonly WindowListViewModel _vm = new();
    private readonly WindowTracker _tracker;
    private readonly IconCache _icons = new();
    private readonly ThumbnailLayoutCoordinator _thumbs;
    private readonly CornerMaskWindow _mask;
    private readonly DispatcherTimer _sourceSizeTimer;
    private readonly nint _hwnd;

    private MonitorArea? _monitor;
    private AppSettings _settings = AppSettings.Default;
    private ExposeOptions _options = ExposeOptions.Default;
    private CardMetrics _metrics = CardMetrics.Default;
    private nint _previous;
    private ExposeGridPanel? _grid;
    private readonly HoverGuard _hover = new();

    // Animation: each card either flies (between its window's rectangle and its grid slot) or fades.
    private readonly Stopwatch _clock = new();
    private readonly Dictionary<CardViewModel, Flight> _flights = [];
    private bool _animating;
    private bool _opening;
    private double _durationMs;
    private int _frames;
    private bool _closing;

    /// <summary>Uniform scale + offset that maps a card's preview onto its window (DIPs, container coordinates).</summary>
    private sealed record Flight(FrameworkElement Container, double Scale, Vector Offset, bool Flies);

    public ExposeWindow(WindowTracker tracker)
    {
        InitializeComponent();
        _tracker = tracker;
        DataContext = _vm;
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        _mask = new CornerMaskWindow(_hwnd);
        _thumbs = new ThumbnailLayoutCoordinator(this, CardList, Scroller, _mask);
        _thumbs.SourceLost += card => RemoveCard(card, "source window gone");
        _thumbs.CardOpacity = CardThumbnailOpacity;
        SetExStyle(WS_EX_TOOLWINDOW, true);

        _sourceSizeTimer = new DispatcherTimer(SourceSizeInterval, DispatcherPriority.Background, (_, _) =>
        {
            if (!_animating && _thumbs.RefreshSourceSizes())
                _thumbs.Update();
        }, Dispatcher);
        _sourceSizeTimer.Stop();

        CardList.LayoutUpdated += (_, _) =>
        {
            if (!_animating)
                _thumbs.Update();
        };
        Scroller.ScrollChanged += (_, _) =>
        {
            if (!_animating)
                _thumbs.Update();
        };
        _vm.Cards.CollectionChanged += OnCardsChanged;
        _vm.Items.CollectionChanged += (_, _) =>
        {
            if (IsOpen)
                ApplyGrid(); // group headers change the grid even when the cards do not
        };
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WindowListViewModel.Filter))
                ShowFilterText();
            else if (e.PropertyName == nameof(WindowListViewModel.SelectedIndex) && IsOpen && !_animating)
                _thumbs.Update();
        };
        Deactivated += (_, _) =>
        {
            // Another window took the foreground (Alt+Tab, a notification click…): get out of the way.
            if (IsOpen && !_closing)
            {
                // Never pull the user back: the window they switched to keeps the foreground.
                Log.Debug($"Exposé deactivated; foreground now belongs to {ForegroundProcessName()}.");
                BeginClose("deactivated", immediate: true, target: null);
            }
        };
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewTextInput += OnPreviewTextInput;
        AddHandler(PreviewCard.CloseRequestedEvent, new RoutedEventHandler(OnCloseRequested));
        AddHandler(PreviewCard.HideAppRequestedEvent, new RoutedEventHandler(OnHideAppRequested));
        Closed += (_, _) => _mask.Close();
        ApplyMetrics(CardMetrics.Default);
        // The corner shapes are drawn in the card color, which follows the theme.
        _mask.SetColors(ThemeManager.CardBackground, ThemeManager.CardHover);
        ThemeManager.Changed += OnThemeChanged;
        Closed += (_, _) => ThemeManager.Changed -= OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        _mask.SetColors(ThemeManager.CardBackground, ThemeManager.CardHover);
        if (IsOpen)
            _thumbs.Update();
    }

    public bool IsOpen { get; private set; }
    public int CardCount => _vm.Cards.Count;
    public int LiveThumbnailCount => _thumbs.Count;

    /// <summary>Raised once Exposé has closed and released its resources, with the reason.</summary>
    public event Action<string>? Dismissed;

    /// <summary>Windows refused the switch (card title, result).</summary>
    public event Action<string, ActivationResult>? ActivationFailed;

    /// <summary>The user chose "Never show this app" for the given process name.</summary>
    public event Action<string>? HideAppRequested;

    /// <summary>Card corner radius and padding follow the sidebar's appearance settings.</summary>
    public void ApplyMetrics(CardMetrics metrics)
    {
        _metrics = metrics;
        Resources["CardCornerRadius"] = new CornerRadius(metrics.CornerRadius);
        Resources["ThumbCornerRadius"] = new CornerRadius(metrics.ThumbCornerRadius);
        Resources["CardPaddingThickness"] = new Thickness(metrics.CardPadding);
        Resources["ExposeShowTitles"] = _options.ShowTitles;
        Resources["ExposeShowIcons"] = _options.ShowIcons;
        _mask.Radius = metrics.ThumbCornerRadius > 0 ? metrics.ThumbCornerRadius + 1 : 0;
    }

    // ---- open / close ----------------------------------------------------------------------------

    public void Open(MonitorArea monitor, AppSettings settings, ExposeOptions options)
    {
        if (IsOpen)
            return;
        if (_closing)
            FinishClose();

        _monitor = monitor;
        _settings = settings;
        _options = options;
        ApplyMetrics(settings.CardMetrics);
        SearchBox.Visibility = options.Search ? Visibility.Visible : Visibility.Hidden;

        // Remember where to return on Esc, before EdgeStage itself takes the foreground.
        _previous = User32.GetForegroundWindow();
        List<WindowInfo> windows = WindowEnumerator.Enumerate().Where(IsListed).ToList();
        _tracker.Mru.PruneWhere(User32.IsWindow);
        List<WindowInfo> ordered = _tracker.Mru.Order(windows, w => w.Hwnd, _previous);

        _vm.Grouping = options.Grouping;
        _vm.Load(ordered, _previous, ordered.Count > 0 ? 0 : -1);
        for (int i = 0; i < ordered.Count; i++)
            _icons.Apply(_vm.Cards[i], ordered[i]);
        ShowFilterText();
        Log.Debug($"Exposé listing {ordered.Count} windows ({(options.AllMonitors ? "all monitors" : "this monitor")}).");

        // Cover the whole monitor (taskbar included), in physical pixels.
        RECT b = monitor.Bounds;
        User32.SetWindowPos(_hwnd, HWND_TOPMOST, b.Left, b.Top, b.Width, b.Height, SWP_NOACTIVATE);
        _mask.Place(b);
        ApplyGrid();

        _tracker.WindowEvent += OnWindowEvent;
        _tracker.BeginDetailedTracking();

        Dim.Opacity = 0;
        SearchBox.Opacity = 0;
        CardList.Opacity = 0;   // revealed by the first animation frame, with the previews
        _thumbs.Opacity = 0;
        Root.IsHitTestVisible = false;
        IsOpen = true;
        Show();
        Activate();
        Focus();

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!IsOpen)
                return;
            ApplyGrid();
            UpdateLayout();
            _thumbs.Sync(_vm.Cards, _hwnd);
            _mask.ShowMask();
            CardList.Opacity = 1;
            _thumbs.Opacity = 255;
            BeginAnimation(opening: true);
            Log.Info($"Exposé opened on monitor {monitor.Bounds} @ {monitor.Dpi} dpi: {_vm.Cards.Count} windows, " +
                     $"{_thumbs.Count} live thumbnails, {_grid?.CardBoxes().Count ?? 0} cards laid out.");
        });
        _sourceSizeTimer.Start();
    }

    /// <summary>Closes Exposé and returns to the window that was active before it opened.</summary>
    public void Dismiss(string reason, bool immediate = false, bool returnToPrevious = true)
    {
        if (!IsOpen)
            return;
        nint previous = _previous;
        bool canReturn = returnToPrevious && previous != 0 && User32.IsWindow(previous) && previous != _hwnd;
        // Closing state first: activating the previous window deactivates this one synchronously.
        BeginClose(reason, immediate, target: null);
        if (canReturn)
            _ = ReturnToPreviousAsync(previous); // while this process still holds the foreground
    }

    private static async Task ReturnToPreviousAsync(nint previous)
    {
        ActivationResult result = await WindowActivator.ActivateAsync(previous);
        if (result.Outcome != ActivationOutcome.Activated)
            Log.Info($"Could not return to the previous window 0x{previous:X}: {result.Outcome} ({result.Detail}).");
    }

    private static string ForegroundProcessName()
    {
        nint fg = User32.GetForegroundWindow();
        if (fg == 0)
            return "nobody";
        User32.GetWindowThreadProcessId(fg, out uint pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return $"{process.ProcessName} (0x{fg:X})";
        }
        catch (ArgumentException)
        {
            return $"pid {pid}";
        }
    }

    private void BeginClose(string reason, bool immediate, CardViewModel? target)
    {
        IsOpen = false;
        _closing = true;
        _sourceSizeTimer.Stop();
        _tracker.WindowEvent -= OnWindowEvent;
        _tracker.EndDetailedTracking();
        Root.IsHitTestVisible = false;
        Log.Info($"Exposé dismissed: {reason}.");
        Dismissed?.Invoke(reason);

        if (immediate || !IsVisible)
        {
            FinishClose();
            return;
        }
        if (target is not null)
            _thumbs.BringToFront(target, _hwnd);
        BeginAnimation(opening: false, target);
    }

    private void FinishClose()
    {
        StopAnimation();
        _closing = false;
        _thumbs.ReleaseAll();
        ResetTransforms();
        Hide();
        _vm.Clear();
        _flights.Clear();
    }

    // ---- layout --------------------------------------------------------------------------------------

    private ExposeGridPanel? Grid => _grid ??= FindDescendant<ExposeGridPanel>(CardList);

    /// <summary>Picks the card size and column count for the current windows and monitor.</summary>
    private void ApplyGrid()
    {
        if (_monitor is null)
            return;
        double width = _monitor.Bounds.Width / _monitor.Scale;
        double height = _monitor.Bounds.Height / _monitor.Scale;
        double areaWidth = width - Scroller.Margin.Left - Scroller.Margin.Right;
        double areaHeight = height - Scroller.Margin.Top - Scroller.Margin.Bottom - (SearchBox.Height + SearchBox.Margin.Top + SearchBox.Margin.Bottom);

        (double min, double max, double spacing) = ExposeLayout.SizeRange(_options.Density, _options.CardSize);
        double border = CardMetrics.CardBorder;
        double chrome = 2 * border + 2 * _metrics.CardPadding + (_options.ShowTitles ? CardMetrics.TitleHeight + CardMetrics.TitleGap : 0);
        double headerHeight = _vm.Grouping ? HeaderHeight : 0;
        var input = new ExposeLayoutInput(areaWidth, areaHeight, _vm.VisibleGroupSizes(), chrome,
            _metrics.CardPadding + border, spacing, headerHeight, min, max);
        ExposeGrid grid = ExposeLayout.Compute(input);
        Grid?.Configure(grid, spacing, headerHeight, areaHeight);
        EmptyText.Visibility = _vm.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = _vm.HasFilter ? $"{_vm.Cards.Count} of {_vm.AllCards.Count()}" : "";
        Scroller.VerticalScrollBarVisibility = grid.NeedsScroll ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
    }

    private void ShowFilterText()
    {
        FilterText.Text = _vm.Filter;
        SearchPlaceholder.Visibility = _vm.HasFilter ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnCardsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!IsOpen)
            return;
        ApplyGrid();
        _thumbs.Sync(_vm.Cards, _hwnd);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (IsOpen && !_animating)
                _thumbs.Update();
        });
    }

    // ---- animation ----------------------------------------------------------------------------------

    /// <summary>
    /// Prepares the flights. Every card's preview is mapped (uniform scale + offset, applied as the
    /// container's render transform) onto its window's real screen rectangle; the DWM previews follow
    /// the transform, so at one end of the animation each preview covers its real window exactly.
    /// Minimized windows, windows on another monitor and cards scrolled out of view fade instead.
    /// </summary>
    private void BeginAnimation(bool opening, CardViewModel? target = null)
    {
        ResetTransforms();
        UpdateLayout();
        _flights.Clear();
        bool motion = _options.Animations && SystemParameters.ClientAreaAnimation && _monitor is not null;
        double scale = _monitor?.Scale ?? 1;
        RECT bounds = _monitor?.Bounds ?? default;
        Rect viewport = Scroller.TransformToAncestor(this).TransformBounds(new Rect(0, 0, Scroller.ViewportWidth, Scroller.ViewportHeight));

        foreach (CardViewModel card in _vm.Cards)
        {
            if (CardList.ItemContainerGenerator.ContainerFromItem(card) is not FrameworkElement container || container.ActualWidth <= 0)
                continue;
            bool flies = false;
            double s = 1;
            Vector offset = default;
            Rect slot = container.TransformToAncestor(this).TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
            bool closingTarget = !opening && card == target;
            if (motion && (!card.IsMinimized || closingTarget) && slot.IntersectsWith(viewport)
                && _thumbs.GetCardPreview(card) is { } preview)
            {
                RECT real = ZoomOverlay.TargetRect(card.Hwnd);
                bool onThisMonitor = real.Right > bounds.Left && real.Left < bounds.Right && real.Bottom > bounds.Top && real.Top < bounds.Bottom;
                if (!real.IsEmpty && (onThisMonitor || closingTarget) && preview.Rect.Width > 0)
                {
                    // Preview and window share an aspect ratio, so one scale maps one onto the other.
                    var previewDip = new Rect(preview.Rect.X / scale, preview.Rect.Y / scale, preview.Rect.Width / scale, preview.Rect.Height / scale);
                    var realDip = new Rect((real.Left - bounds.Left) / scale, (real.Top - bounds.Top) / scale, real.Width / scale, real.Height / scale);
                    s = realDip.Width / previewDip.Width;
                    Point q0 = new(previewDip.X - slot.X, previewDip.Y - slot.Y);
                    offset = new Vector(realDip.X - slot.X - s * q0.X, realDip.Y - slot.Y - s * q0.Y);
                    flies = true;
                }
            }
            _flights[card] = new Flight(container, s, offset, flies);
            container.RenderTransform = new MatrixTransform(Matrix.Identity);
        }

        _opening = opening;
        // Motion: flights. Reduced motion: a short fade. Animations off: (practically) at once.
        _durationMs = _options.Instant ? 1
            : (motion ? (opening ? OpenDurationMs : CloseDurationMs) : FadeDurationMs) / Math.Max(0.1, _options.AnimationScale);
        _frames = 0;
        _thumbs.ClipToViewport = false;
        _thumbs.MaskEnabled = false;
        _clock.Restart();
        if (!_animating)
        {
            _animating = true;
            CompositionTarget.Rendering += OnAnimationFrame;
        }
        ApplyFrame(opening ? 0 : 1);
    }

    private void OnAnimationFrame(object? sender, EventArgs e)
    {
        double elapsed = _clock.Elapsed.TotalMilliseconds;
        double t = Math.Clamp(elapsed / _durationMs, 0, 1);
        _frames++;
        // Opening: ease-out quart (quick start, soft landing in the grid). Closing: ease-in-out cubic,
        // so previews settle onto the real windows without a visible jolt.
        double p = _opening
            ? 1 - Math.Pow(1 - t, 4)
            : 1 - (t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2);
        ApplyFrame(_opening ? p : 1 - p);
        if (t < 1)
            return;

        StopAnimation();
        Log.Info($"Exposé {(_opening ? "open" : "close")} animation: {_frames} frames in {elapsed:F0} ms.");
        if (_opening)
        {
            ResetTransforms();
            _thumbs.ClipToViewport = true;
            _thumbs.MaskEnabled = true;
            Root.IsHitTestVisible = true;
            _hover.Arm();
            ScrollSelectedIntoView();
            _thumbs.Update();
        }
        else
        {
            FinishClose();
        }
    }

    /// <summary><paramref name="progress"/>: 0 = previews over the real windows, 1 = settled in the grid.</summary>
    private double _progress;

    private void ApplyFrame(double progress)
    {
        _progress = progress;
        Dim.Opacity = progress;
        SearchBox.Opacity = progress;
        foreach (Flight flight in _flights.Values)
        {
            double s = flight.Scale + (1 - flight.Scale) * progress;
            Vector o = flight.Offset * (1 - progress);
            ((MatrixTransform)flight.Container.RenderTransform).Matrix = new Matrix(s, 0, 0, s, o.X, o.Y);
            // The card's own chrome (frame, title) appears as it lands; the live preview is visible throughout.
            // Flying cards show their frame and title only near the grid, so no oversized "ghost" frame trails the preview.
            flight.Container.Opacity = flight.Flies ? Math.Clamp((progress - 0.4) / 0.6, 0, 1) : progress;
        }
        _thumbs.Update();
    }

    /// <summary>Flying previews stay opaque; fading cards fade their preview with the card.</summary>
    private double CardThumbnailOpacity(CardViewModel card) =>
        _animating && _flights.TryGetValue(card, out Flight? flight) && !flight.Flies ? _progress : 1;

    private void StopAnimation()
    {
        if (!_animating)
            return;
        _animating = false;
        CompositionTarget.Rendering -= OnAnimationFrame;
        _clock.Stop();
    }

    private void ResetTransforms()
    {
        foreach (Flight flight in _flights.Values)
        {
            flight.Container.RenderTransform = Transform.Identity;
            flight.Container.Opacity = 1;
        }
    }

    // ---- live updates ------------------------------------------------------------------------------

    private void OnWindowEvent(WindowEventKind kind, nint hwnd)
    {
        if (!IsOpen)
            return;
        CardViewModel? card = _vm.Find(hwnd);
        switch (kind)
        {
            case WindowEventKind.Shown or WindowEventKind.Uncloaked:
                if (card is null && WindowEnumerator.TryRead(hwnd) is { } info && IsListed(info))
                {
                    CardViewModel added = _vm.Append(info);
                    _icons.Apply(added, info);
                }
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
        }
    }

    private void RemoveCard(CardViewModel card, string reason)
    {
        Log.Debug($"Exposé card 0x{card.Hwnd:X} removed: {reason}.");
        _vm.Remove(card);
    }

    private bool IsListed(WindowInfo window)
    {
        if (!TestHooks.IsListed(window) || _settings.IsExcluded(window.ProcessName))
            return false;
        if (!_options.AllMonitors && _monitor is not null
            && User32.MonitorFromWindow(window.Hwnd, MONITOR_DEFAULTTONEAREST) != _monitor.Handle)
            return false;
        return true;
    }

    /// <summary>Applies changed settings to an open Exposé (e.g. an app was just hidden).</summary>
    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        if (!IsOpen)
            return;
        foreach (CardViewModel card in _vm.AllCards.Where(c => settings.IsExcluded(c.ProcessName)).ToList())
            RemoveCard(card, "app hidden");
    }

    // ---- input -------------------------------------------------------------------------------------

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsOpen)
            return;
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case Key.Escape:
                HandleEscape();
                break;
            case Key.Back when _vm.HasFilter:
                _vm.Filter = _vm.Filter[..^1];
                break;
            case Key.Left:
                Navigate(NavDirection.Left);
                break;
            case Key.Right:
                Navigate(NavDirection.Right);
                break;
            case Key.Up:
                Navigate(NavDirection.Up);
                break;
            case Key.Down:
                Navigate(NavDirection.Down);
                break;
            case Key.Tab:
                _vm.CycleSelection(shift ? -1 : 1);
                ScrollSelectedIntoView();
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
            case Key.PageDown:
                Navigate(NavDirection.Down);
                break;
            case Key.PageUp:
                Navigate(NavDirection.Up);
                break;
            case Key.Space when _vm.HasFilter:
                return; // part of the search text; handled in PreviewTextInput
            case Key.Enter or Key.Space:
                if (_vm.Selected is { } selected)
                    ActivateCard(selected);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>The first Esc clears the search, the next one closes and returns to the previous window.</summary>
    private void HandleEscape()
    {
        if (_vm.HasFilter)
            _vm.Filter = "";
        else
            Dismiss("escape");
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        string text = e.Text;
        if (!IsOpen || !_options.Search || string.IsNullOrEmpty(text) || text.Any(char.IsControl) || _vm.Filter.Length >= 60)
            return;
        if (text == " " && !_vm.HasFilter)
            return;
        _vm.Filter += text;
        e.Handled = true;
    }

    private void Navigate(NavDirection direction)
    {
        if (Grid is not { } grid)
            return;
        int next = GridNavigation.Next(grid.CardBoxes(), _vm.SelectedIndex, direction);
        if (next >= 0)
            _vm.SelectedIndex = next;
        ScrollSelectedIntoView();
    }

    private void ScrollSelectedIntoView()
    {
        if (_vm.Selected is { } card && CardList.ItemContainerGenerator.ContainerFromItem(card) is FrameworkElement container)
            container.BringIntoView();
    }

    private void OnCardMouseEnter(object sender, MouseEventArgs e)
    {
        if (_animating || !_hover.Allows() || sender is not FrameworkElement { DataContext: CardViewModel card })
            return;
        int index = _vm.Cards.IndexOf(card);
        if (index != _vm.SelectedIndex)
            _vm.SelectedIndex = index;
    }

    private void OnCardClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CardViewModel card })
        {
            e.Handled = true;
            ActivateCard(card);
        }
    }

    private void OnBackgroundClicked(object sender, MouseButtonEventArgs e) => Dismiss("clicked background");

    private void OnGroupHeaderClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GroupHeaderViewModel header })
        {
            e.Handled = true;
            _vm.ToggleGroup(header);
        }
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

#if DEBUG
    internal void ActivateSelectedForTest()
    {
        if (_vm.Selected is { } card)
            ActivateCard(card);
    }

    internal void SelectForTest(string key)
    {
        switch (key)
        {
            case "right": Navigate(NavDirection.Right); break;
            case "left": Navigate(NavDirection.Left); break;
            case "up": Navigate(NavDirection.Up); break;
            case "down": Navigate(NavDirection.Down); break;
        }
    }

    internal void TypeForTest(string text)
    {
        if (IsOpen && _options.Search)
            _vm.Filter += text;
    }

    internal void EscapeKeyForTest() => HandleEscape();

    internal void ToggleGroupingForTest() => _vm.Grouping = !_vm.Grouping;

    internal string DescribeForTest() =>
        $"open={IsOpen} cards={_vm.Cards.Count} groups={_vm.GroupCount} filter='{_vm.Filter}' thumbs={_thumbs.Count} selected={_vm.SelectedIndex} " +
        $"boxes=[{string.Join("; ", Grid?.CardBoxes().Select(b => $"{b.X:F0},{b.Y:F0} {b.Width:F0}x{b.Height:F0}") ?? [])}]";
#endif

    /// <summary>
    /// The chosen window is activated at once (this process still holds the foreground); the
    /// previews fly back to their windows meanwhile and the overlay disappears at the end.
    /// </summary>
    private async void ActivateCard(CardViewModel card)
    {
        if (!IsOpen || _animating)
            return;
        card.Notice = null;
        // Closing state first: the activation deactivates this window synchronously.
        BeginClose("window selected", immediate: false, target: card);
        Task<ActivationResult> activation = WindowActivator.ActivateAsync(card.Hwnd);
        try
        {
            ActivationResult result = await activation;
            if (result.Outcome == ActivationOutcome.Refused)
            {
                Log.Warn($"Activation refused for 0x{card.Hwnd:X} ({card.ProcessName}): {result.Detail}");
                ActivationFailed?.Invoke(card.Title, result);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Activation of 0x{card.Hwnd:X} failed", ex);
        }
    }

    // ---- helpers -----------------------------------------------------------------------------------

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                return match;
            if (FindDescendant<T>(child) is T nested)
                return nested;
        }
        return null;
    }

    private void SetExStyle(long flag, bool enabled)
    {
        long ex = User32.GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
        long updated = enabled ? ex | flag : ex & ~flag;
        if (updated != ex)
            User32.SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (nint)updated);
    }
}
