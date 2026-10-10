using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using EdgeStage.Core.Interop;
using EdgeStage.Core.Thumbnails;
using static EdgeStage.Core.Interop.NativeConstants;

namespace ThumbnailSpike;

/// <summary>
/// Interactive spike: a left-edge sidebar listing real top-level windows as rounded cards with
/// live DWM thumbnails inside a scrolling list. Click a card to activate that window.
/// </summary>
internal sealed class SidebarProbeWindow : Window
{
    private sealed class Card
    {
        public required SpikeWindow Window { get; init; }
        public required Border Host { get; init; }
        public required Border Root { get; init; }
        public SafeThumbnailHandle? Thumb { get; set; }
        public SIZE SourceSize { get; set; }
        public ThumbnailPlacement Placement { get; set; }
    }

    // ---- Automation surface used by RealWindowProbe -------------------------------------------

    public nint WindowHandle => Hwnd;
    public ScrollViewer Scroller => _scroll;
    public IReadOnlyList<(SpikeWindow Window, ThumbnailPlacement Placement, bool Registered)> Snapshot() =>
        _cards.Select(c => (c.Window, c.Placement, c.Thumb is not null)).ToList();
    public void RebuildNow() => Rebuild();
    public void UpdateNow() => UpdateThumbnails();
    public int RegisteredCount => _cards.Count(c => c.Thumb is not null);
    public (int Frames, double AvgMs, double MaxMs) LastFade { get; private set; }
    public Task RunFade() => FadeTest();
    public void BringCardIntoView(int index) => _cards[index].Root.BringIntoView();

    private readonly HostConfig _config;
    private readonly List<Card> _cards = [];
    private readonly StackPanel _list = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _status = new() { Foreground = Brushes.Gainsboro, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(8, 4, 8, 4) };
    private byte _opacity = 255;

    public SidebarProbeWindow(HostConfig config)
    {
        _config = config;
        Title = $"EdgeStage spike probe ({config})";
        Rect wa = SystemParameters.WorkArea;
        Left = wa.Left + 12;
        Top = wa.Top + 12;
        Width = 280;
        Height = wa.Height - 24;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        HostChrome.Configure(this, config);

        var buttons = new WrapPanel { Margin = new Thickness(6) };
        buttons.Children.Add(MakeButton("Refresh", (_, _) => Rebuild()));
        buttons.Children.Add(MakeButton("Fade", async (_, _) => await FadeTest()));
        buttons.Children.Add(MakeButton("Leak×1000", (_, _) => LeakTest()));
        buttons.Children.Add(MakeButton("Close", (_, _) => Close()));

        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list };
        _scroll.ScrollChanged += (_, _) => UpdateThumbnails();

        var dock = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Top);
        DockPanel.SetDock(_status, Dock.Top);
        dock.Children.Add(buttons);
        dock.Children.Add(_status);
        dock.Children.Add(_scroll);
        Content = dock;

        SourceInitialized += (_, _) => HostChrome.ApplyNative(Hwnd, _config);
        Loaded += (_, _) => Rebuild();
        SizeChanged += (_, _) => UpdateThumbnails();
        Closed += (_, _) => ReleaseAll();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    private nint Hwnd => new WindowInteropHelper(this).Handle;

    private static Button MakeButton(string text, RoutedEventHandler onClick)
    {
        var b = new Button { Content = text, Margin = new Thickness(2), Padding = new Thickness(6, 2, 6, 2) };
        b.Click += onClick;
        return b;
    }

    private void Rebuild()
    {
        ReleaseAll();
        _list.Children.Clear();
        _cards.Clear();

        foreach (SpikeWindow w in SpikeWindowList.Enumerate())
        {
            var host = new Border { Height = 138, Background = new SolidColorBrush(Color.FromRgb(0x7A, 0x4A, 0x10)), CornerRadius = new CornerRadius(4) };
            var caption = new TextBlock
            {
                Text = (w.IsMinimized ? "[min] " : "") + (string.IsNullOrEmpty(w.Title) ? $"({w.ClassName})" : w.Title),
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(2, 6, 2, 0),
                FontSize = 12,
            };
            var stack = new StackPanel();
            stack.Children.Add(host);
            stack.Children.Add(caption);
            var root = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)),
                Padding = new Thickness(6),
                Margin = new Thickness(8, 4, 8, 4),
                Child = stack,
                Cursor = Cursors.Hand,
            };
            var card = new Card { Window = w, Host = host, Root = root };
            root.MouseLeftButtonUp += (_, _) => Activate(card);
            root.MouseEnter += (_, _) => root.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
            root.MouseLeave += (_, _) => root.Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));

            int hr = Dwmapi.DwmRegisterThumbnail(Hwnd, w.Hwnd, out SafeThumbnailHandle thumb);
            if (hr >= 0)
            {
                card.Thumb = thumb;
                Dwmapi.DwmQueryThumbnailSourceSize(thumb, out SIZE size);
                card.SourceSize = size;
            }
            else
            {
                thumb.Dispose();
                caption.Text += $"  [register HR 0x{hr:X8}]";
            }

            _cards.Add(card);
            _list.Children.Add(root);
        }

        _status.Text = $"{_cards.Count} windows, {_cards.Count(c => c.Thumb is not null)} thumbnails registered. {Counters()}";
        Dispatcher.BeginInvoke(UpdateThumbnails, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Maps each card's host element to device pixels and clips against the scroll viewport.</summary>
    private void UpdateThumbnails()
    {
        if (!IsLoaded || _cards.Count == 0)
            return;

        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Rect viewportDip = _scroll.TransformToAncestor(this).TransformBounds(new Rect(0, 0, _scroll.ViewportWidth, _scroll.ViewportHeight));
        PixelRect viewport = new PixelRect(viewportDip.X, viewportDip.Y, viewportDip.Width, viewportDip.Height).Scale(dpi);

        foreach (Card card in _cards)
        {
            if (card.Thumb is null)
                continue;

            // Source size can change (window resized/minimized) — re-query each layout pass in the spike.
            Dwmapi.DwmQueryThumbnailSourceSize(card.Thumb, out SIZE size);
            card.SourceSize = size;

            Rect hostDip = card.Host.TransformToAncestor(this).TransformBounds(new Rect(card.Host.RenderSize));
            PixelRect host = new PixelRect(hostDip.X + 4, hostDip.Y + 4, hostDip.Width - 8, hostDip.Height - 8).Scale(dpi);
            ThumbnailPlacement placement = ThumbnailGeometry.Compute(host, viewport, size);
            card.Placement = placement;

            var props = new DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = DWM_TNP_VISIBLE | DWM_TNP_RECTDESTINATION | DWM_TNP_RECTSOURCE | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY,
                fVisible = placement.IsVisible ? 1 : 0,
                rcDestination = placement.Destination,
                rcSource = placement.Source,
                opacity = _opacity,
                fSourceClientAreaOnly = 0,
            };
            Dwmapi.DwmUpdateThumbnailProperties(card.Thumb, props);
        }
    }

    private void Activate(Card card)
    {
        nint target = card.Window.Hwnd;
        if (!User32.IsWindow(target))
        {
            _status.Text = "Window no longer exists.";
            Rebuild();
            return;
        }

        bool wasIconic = User32.IsIconic(target);
        if (wasIconic)
            User32.ShowWindow(target, SW_RESTORE);
        bool set = User32.SetForegroundWindow(target);
        Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(250);
            bool ok = User32.GetForegroundWindow() == target;
            _status.Text = $"Activate '{card.Window.Title}': iconic={wasIconic}, SetForegroundWindow={set}, foreground now={ok}. {Counters()}";
        });
    }

    private async Task FadeTest()
    {
        var frameTimes = new List<double>();
        var clock = Stopwatch.StartNew();
        var done = new TaskCompletionSource();
        void OnRendering(object? s, EventArgs e)
        {
            double t = clock.Elapsed.TotalMilliseconds;
            double phase = Math.Min(1.0, t / 360.0); // 180 ms out + 180 ms in
            _opacity = (byte)(255 * Math.Abs(1 - 2 * phase));
            _list.Opacity = _opacity / 255.0;
            var sw = Stopwatch.StartNew();
            UpdateThumbnails();
            frameTimes.Add(sw.Elapsed.TotalMilliseconds);
            if (phase >= 1.0) done.TrySetResult();
        }
        CompositionTarget.Rendering += OnRendering;
        await done.Task;
        CompositionTarget.Rendering -= OnRendering;
        _opacity = 255;
        _list.Opacity = 1;
        UpdateThumbnails();
        LastFade = (frameTimes.Count, frameTimes.Average(), frameTimes.Max());
        _status.Text = $"Fade: {frameTimes.Count} frames in 360 ms, thumbnail update cost avg {frameTimes.Average():F2} ms, max {frameTimes.Max():F2} ms.";
    }

    private void LeakTest()
    {
        Card? card = _cards.FirstOrDefault(c => c.Thumb is not null);
        if (card is null)
            return;
        string before = Counters();
        int failures = 0;
        for (int i = 0; i < 1000; i++)
        {
            if (Dwmapi.DwmRegisterThumbnail(Hwnd, card.Window.Hwnd, out SafeThumbnailHandle t) < 0) failures++;
            t.Dispose();
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        _status.Text = $"Leak×1000 on '{card.Window.Title}': failures={failures}. Before: {before}. After: {Counters()}";
    }

    private static string Counters()
    {
        var p = Process.GetCurrentProcess();
        p.Refresh();
        return $"handles {p.HandleCount}, GDI {User32.GetGuiResources(Kernel32.CurrentProcess, GR_GDIOBJECTS)}, USER {User32.GetGuiResources(Kernel32.CurrentProcess, GR_USEROBJECTS)}";
    }

    private void ReleaseAll()
    {
        foreach (Card c in _cards)
        {
            c.Thumb?.Dispose();
            c.Thumb = null;
        }
    }
}
