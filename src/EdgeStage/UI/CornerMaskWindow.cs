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
    /// <summary>Corner radius in DIPs; concentric with the 10 DIP card radius and 5 DIP padding.</summary>
    public const double Radius = 6;

    public static readonly Color Normal = Color.FromRgb(0x27, 0x27, 0x2A);
    public static readonly Color Hover = Color.FromRgb(0x32, 0x32, 0x36);

    /// <param name="Destination">Where the preview is drawn, in the sidebar window's device pixels.</param>
    public readonly record struct Item(RECT Destination, bool RoundTop, bool RoundBottom, bool Highlighted);

    private static readonly Geometry[] Corners = BuildCorners(Radius); // TL, TR, BL, BR
    private static readonly SolidColorBrush NormalBrush = Frozen(Normal);
    private static readonly SolidColorBrush HoverBrush = Frozen(Hover);

    private readonly Canvas _canvas = new();
    private readonly List<Path> _paths = [];
    private readonly nint _hwnd;

    public CornerMaskWindow(nint owner)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
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
        Opacity = Math.Clamp(opacity, 0, 1);
        int needed = items.Count * 4;
        while (_paths.Count < needed)
        {
            var path = new Path { IsHitTestVisible = false, SnapsToDevicePixels = true };
            _paths.Add(path);
            _canvas.Children.Add(path);
        }

        double r = Radius;
        int used = 0;
        foreach (Item item in items)
        {
            double left = item.Destination.Left / scale, top = item.Destination.Top / scale;
            double right = item.Destination.Right / scale, bottom = item.Destination.Bottom / scale;
            Brush brush = item.Highlighted ? HoverBrush : NormalBrush;

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
        path.Data = Corners[corner];
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
