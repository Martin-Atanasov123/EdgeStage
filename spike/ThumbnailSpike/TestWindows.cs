using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using EdgeStage.Core.Interop;
using static EdgeStage.Core.Interop.NativeConstants;

namespace ThumbnailSpike;

/// <summary>A normal app-style window filled with a solid, switchable color: the thumbnail source under test.</summary>
internal sealed class SourceWindow : Window
{
    private readonly Border _fill = new();

    public SourceWindow(string title, Color color, double left, double top, bool chromeless = false)
    {
        Title = title;
        Width = 480;
        Height = 320;
        Left = left;
        Top = top;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowActivated = false;
        if (chromeless)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
        }
        Content = _fill;
        SetColor(color);
    }

    /// <summary>Client-area rectangle in screen pixels.</summary>
    public RECT ClientScreenRect
    {
        get
        {
            User32.GetClientRect(Hwnd, out RECT client);
            var origin = new POINT(0, 0);
            User32.ClientToScreen(Hwnd, ref origin);
            return new RECT(origin.X, origin.Y, origin.X + client.Width, origin.Y + client.Height);
        }
    }

    public nint Hwnd => new WindowInteropHelper(this).EnsureHandle();

    public void SetColor(Color color) => _fill.Background = new SolidColorBrush(color);
}

/// <summary>The window configurations evaluated as thumbnail destinations.</summary>
internal enum HostConfig
{
    /// <summary>Opaque dark WPF window, rounded corners + dark mode via DWM attributes.</summary>
    Solid,
    /// <summary>WindowStyle=None, transparent WPF background, frame extended into client, Acrylic backdrop.</summary>
    AcrylicNoFrame,
    /// <summary>WindowChrome (glass frame -1, caption 0) keeping a native frame, Acrylic backdrop.</summary>
    AcrylicChrome,
    /// <summary>WPF AllowsTransparency=true (per-pixel alpha layered window via UpdateLayeredWindow).</summary>
    WpfAllowsTransparency,
    /// <summary>Solid host made WS_EX_LAYERED with SetLayeredWindowAttributes(LWA_ALPHA) for whole-window fades.</summary>
    LayeredAlpha,
}

/// <summary>A small destination window hosting thumbnails at a known placeholder position.</summary>
internal sealed class HostWindow : Window
{
    public static readonly Rect ThumbBoxDip = new(20, 20, 240, 150);
    public static readonly Rect BackdropProbeDip = new(20, 190, 260, 50);

    public HostConfig Config { get; }

    public HostWindow(HostConfig config, double left, double top)
    {
        Config = config;
        Title = $"Spike host ({config})";
        Width = 300;
        Height = 260;
        Left = left;
        Top = top;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;

        var canvas = new Canvas();
        var placeholder = new Border
        {
            Width = ThumbBoxDip.Width,
            Height = ThumbBoxDip.Height,
            Background = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)),
        };
        Canvas.SetLeft(placeholder, ThumbBoxDip.X);
        Canvas.SetTop(placeholder, ThumbBoxDip.Y);
        canvas.Children.Add(placeholder);
        Content = canvas;

        HostChrome.Configure(this, config);
        SourceInitialized += (_, _) => NativeSetupLog = HostChrome.ApplyNative(Hwnd, config);
    }

    public nint Hwnd => new WindowInteropHelper(this).Handle;

    public double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// <summary>HRESULTs from the DWM attribute calls, for the report.</summary>
    public string NativeSetupLog { get; private set; } = "";

    public void SetWindowAlpha(byte alpha) => User32.SetLayeredWindowAttributes(Hwnd, 0, alpha, LWA_ALPHA);

    /// <summary>Thumbnail destination rectangle in device pixels relative to the window.</summary>
    public RECT ThumbDestPx
    {
        get
        {
            double s = DpiScale;
            return new RECT(
                (int)Math.Round(ThumbBoxDip.X * s), (int)Math.Round(ThumbBoxDip.Y * s),
                (int)Math.Round(ThumbBoxDip.Right * s), (int)Math.Round(ThumbBoxDip.Bottom * s));
        }
    }

    /// <summary>Converts a window-relative device-pixel rectangle to screen coordinates (client origin).</summary>
    public RECT ToScreen(RECT local)
    {
        var origin = new POINT(0, 0);
        User32.ClientToScreen(Hwnd, ref origin);
        return new RECT(origin.X + local.Left, origin.Y + local.Top, origin.X + local.Right, origin.Y + local.Bottom);
    }

    public RECT BackdropProbeScreen
    {
        get
        {
            double s = DpiScale;
            return ToScreen(new RECT(
                (int)(BackdropProbeDip.X * s), (int)(BackdropProbeDip.Y * s),
                (int)(BackdropProbeDip.Right * s), (int)(BackdropProbeDip.Bottom * s)));
        }
    }
}
