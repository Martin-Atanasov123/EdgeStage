using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using EdgeStage.Core.Interop;
using static EdgeStage.Core.Interop.NativeConstants;

namespace EdgeStage.UI;

/// <summary>
/// Rounds the corners of the live DWM previews.
/// </summary>
/// <remarks>
/// DWM thumbnails are composited above everything the sidebar window draws and cannot be masked.
/// So the corners are cut by a second, click-through transparent window that is OWNED by the
/// sidebar (owned windows always stay above their owner) and draws small "inverse corner" shapes
/// in the card's background color exactly on top of the previews' corners. The previews sit on the
/// card background (no dark tile), so the shapes are invisible except where they hide square preview pixels.
/// </remarks>
internal sealed class CornerMaskWindow : Window
{
    /// <summary>Default corner radius in DIPs; concentric with the 10 DIP card radius and 5 DIP padding.</summary>
    public const double DefaultRadius = 6;

    public static readonly Color Normal = Color.FromRgb(0x27, 0x27, 0x2A);
    public static readonly Color Hover = Color.FromRgb(0x32, 0x32, 0x36);

    /// <param name="Destination">Where the preview is drawn, in the sidebar window's device pixels.</param>
    public readonly record struct Item(RECT Destination, bool RoundTop, bool RoundBottom, bool Highlighted);

    private Geometry[] _corners = BuildCorners(DefaultRadius); // TL, TR, BL, BR
    private double _radius = DefaultRadius;
    private SolidColorBrush _normalBrush = Frozen(Normal);
    private SolidColorBrush _hoverBrush = Frozen(Hover);

    private readonly Canvas _canvas = new();
    private readonly List<Path> _paths = [];
    private readonly nint _hwnd;

    /// <param name="glass">Non-layered "glass" window (see <see cref="GlassWindow"/>): for full-screen
    /// owners such as Exposé, where a layered mask would hold screen-sized bitmaps.</param>
    public CornerMaskWindow(nint owner, bool glass = false)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = !glass;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Title = "EdgeStage Corners";
        Width = 1;
        Height = 1;
        Content = _canvas;

        var helper = new WindowInteropHelper(this) { Owner = owner };
        _hwnd = helper.EnsureHandle();
        long ex = User32.GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
        User32.SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (nint)(ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT));
        if (glass)
            GlassWindow.Apply(this, _hwnd, clickThrough: true); // WS_EX_TRANSPARENT passes clicks only for layered windows
    }

    /// <summary>Corner radius in DIPs (follows the card corner radius setting); 0 disables the mask.</summary>
    public double Radius
    {
        get => _radius;
        set
        {
            value = Math.Max(0, value);
            if (value == _radius)
                return;
            _radius = value;
            _corners = BuildCorners(Math.Max(value, 0.5));
        }
    }

    /// <summary>The card background colors the corner shapes must match.</summary>
    public void SetColors(Color normal, Color highlighted)
    {
        _normalBrush = Frozen(normal);
        _hoverBrush = Frozen(highlighted);
    }

    /// <summary>Covers exactly the sidebar window (screen device pixels).</summary>
    public void Place(RECT screen) =>
        User32.SetWindowPos(_hwnd, HWND_TOPMOST, screen.Left, screen.Top, screen.Width, screen.Height, SWP_NOACTIVATE);

    public void ShowMask()
    {
        if (!IsVisible)
            Show();
    }

    public void HideMask()
    {
        Apply([], 1, 0);
        if (IsVisible)
            Hide();
    }

    /// <param name="scale">Device pixels per DIP of the monitor the sidebar is on.</param>
    /// <param name="opacity">Follows the sidebar's fade so the corners never show while it is hidden.</param>
    public void Apply(IReadOnlyList<Item> items, double scale, double opacity)
    {
        // Window opacity works only for layered windows; a glass mask fades its content instead.
        if (AllowsTransparency)
            Opacity = Math.Clamp(opacity, 0, 1);
        else
            _canvas.Opacity = Math.Clamp(opacity, 0, 1);
        int needed = items.Count * 4;
        while (_paths.Count < needed)
        {
            var path = new Path { IsHitTestVisible = false, SnapsToDevicePixels = true };
            _paths.Add(path);
            _canvas.Children.Add(path);
        }

        double r = _radius;
        int used = 0;
        if (r <= 0)
            items = [];
        foreach (Item item in items)
        {
            double left = item.Destination.Left / scale, top = item.Destination.Top / scale;
            double right = item.Destination.Right / scale, bottom = item.Destination.Bottom / scale;
            Brush brush = item.Highlighted ? _hoverBrush : _normalBrush;

            Set(used++, 0, left, top, brush, item.RoundTop);
            Set(used++, 1, right - r, top, brush, item.RoundTop);
            Set(used++, 2, left, bottom - r, brush, item.RoundBottom);
            Set(used++, 3, right - r, bottom - r, brush, item.RoundBottom);
        }
        for (int i = used; i < _paths.Count; i++)
            _paths[i].Visibility = Visibility.Collapsed;
    }

    private void Set(int index, int corner, double x, double y, Brush brush, bool visible)
    {
        Path path = _paths[index];
        path.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible)
            return;
        path.Data = _corners[corner];
        path.Fill = brush;
        Canvas.SetLeft(path, x);
        Canvas.SetTop(path, y);
    }

    /// <summary>The area OUTSIDE a quarter circle, inside an r×r square, for each corner.</summary>
    private static Geometry[] BuildCorners(double r)
    {
        static Geometry G(string data)
        {
            var g = Geometry.Parse(data);
            g.Freeze();
            return g;
        }
        string s = r.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return
        [
            G($"M0,0 L{s},0 A{s},{s} 0 0 0 0,{s} Z"),       // top-left
            G($"M{s},0 L0,0 A{s},{s} 0 0 1 {s},{s} Z"),       // top-right
            G($"M0,{s} L0,0 A{s},{s} 0 0 0 {s},{s} Z"),       // bottom-left
            G($"M{s},{s} L{s},0 A{s},{s} 0 0 1 0,{s} Z"),     // bottom-right
        ];
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
