using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EdgeStage.Core.Diagnostics;
using EdgeStage.Core.Interop;
using EdgeStage.Core.Thumbnails;

namespace EdgeStage.UI;

/// <summary>
/// Keeps one DWM thumbnail per visible card and positions it over the card's thumbnail host.
/// DWM thumbnails are composited above WPF content and are not clipped by WPF, so placement and
/// scroll clipping are computed here (<see cref="ThumbnailGeometry"/>) in device pixels.
/// Registrations exist only while the sidebar is open.
/// </summary>
internal sealed class ThumbnailLayoutCoordinator
{
    private readonly Window _window;
    private readonly ItemsControl _items;
    private readonly ScrollViewer _scroll;
    private readonly Dictionary<CardViewModel, ThumbnailRegistration> _registrations = [];

    public ThumbnailLayoutCoordinator(Window window, ItemsControl items, ScrollViewer scroll)
    {
        _window = window;
        _items = items;
        _scroll = scroll;
    }

    /// <summary>Raised when DWM reports a card's source window gone (backstop for missed destroy events).</summary>
    public event Action<CardViewModel>? SourceLost;

    /// <summary>0–255 multiplier applied to every thumbnail (used by fade animations).</summary>
    public byte Opacity { get; set; } = 255;

    public int Count => _registrations.Count;

    /// <summary>Registers thumbnails for new cards and releases those of removed cards.</summary>
    public void Sync(IReadOnlyCollection<CardViewModel> cards, nint destination)
    {
        var wanted = new HashSet<CardViewModel>(cards);
        foreach (var (card, registration) in _registrations.Where(kv => !wanted.Contains(kv.Key)).ToList())
        {
            registration.Dispose();
            _registrations.Remove(card);
        }

        foreach (CardViewModel card in cards)
        {
            if (_registrations.ContainsKey(card) || card.PreviewUnavailable)
                continue;
            var registration = DwmThumbnailService.TryRegister(destination, card.Hwnd, out int hr);
            if (registration is null)
            {
                card.PreviewUnavailable = true;
                Log.Info($"No live preview for window 0x{card.Hwnd:X} ({card.ProcessName}): DwmRegisterThumbnail HRESULT 0x{hr:X8}.");
                continue;
            }
            _registrations[card] = registration;
        }
    }

    /// <summary>Places every thumbnail over its host element, clipped to the scroll viewport.</summary>
    public void Update()
    {
        if (_registrations.Count == 0 || !_window.IsVisible)
            return;

        double scale = VisualTreeHelper.GetDpi(_window).DpiScaleX;
        PixelRect viewport = ToDevice(_scroll, new Rect(0, 0, _scroll.ViewportWidth, _scroll.ViewportHeight), scale)
            .Intersect(new PixelRect(0, 0, _window.ActualWidth, _window.ActualHeight).Scale(scale));

        List<CardViewModel>? lost = null;
        foreach (var (card, registration) in _registrations)
        {
            ThumbnailPlacement placement = ThumbnailPlacement.Hidden;
            if (FindHost(card) is { IsVisible: true } host && host.ActualWidth > 0)
            {
                PixelRect hostRect = ToDevice(host, new Rect(0, 0, host.ActualWidth, host.ActualHeight), scale);
                placement = ThumbnailGeometry.Compute(hostRect, viewport, registration.SourceSize);
            }
            if (!registration.Apply(placement, Opacity))
                (lost ??= []).Add(card);
        }

        if (lost is not null)
            foreach (CardViewModel card in lost)
                SourceLost?.Invoke(card);
    }

    /// <summary>Re-reads source sizes (windows can be resized or restored while the sidebar is open).</summary>
    public bool RefreshSourceSizes()
    {
        bool changed = false;
        List<CardViewModel>? lost = null;
        foreach (var (card, registration) in _registrations)
        {
            SIZE before = registration.SourceSize;
            if (!registration.RefreshSourceSize())
                (lost ??= []).Add(card);
            else if (before.Width != registration.SourceSize.Width || before.Height != registration.SourceSize.Height)
                changed = true;
        }
        if (lost is not null)
            foreach (CardViewModel card in lost)
                SourceLost?.Invoke(card);
        return changed;
    }

    public void ReleaseAll()
    {
        foreach (ThumbnailRegistration registration in _registrations.Values)
            registration.Dispose();
        _registrations.Clear();
    }

    private PixelRect ToDevice(Visual element, Rect local, double scale)
    {
        Rect r = element.TransformToAncestor(_window).TransformBounds(local);
        return new PixelRect(r.X, r.Y, r.Width, r.Height).Scale(scale);
    }

    private FrameworkElement? FindHost(CardViewModel card)
    {
        if (_items.ItemContainerGenerator.ContainerFromItem(card) is not DependencyObject container)
            return null;
        return FindDescendant<PreviewCard>(container)?.ThumbnailHost;
    }

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
}
