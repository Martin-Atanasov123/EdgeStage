using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using EdgeStage.Core.Activation;
using EdgeStage.Core.Diagnostics;
using EdgeStage.Core.Interop;
using EdgeStage.Core.Thumbnails;
using static EdgeStage.Core.Interop.NativeConstants;

namespace EdgeStage.UI;

/// <summary>
/// The "zoom to window" transition. A click-through transparent window hosts ONE live DWM
/// thumbnail of the chosen window; the thumbnail's rectangle is interpolated every composition
/// frame from the card's preview to the window's real on-screen position, then faded out over
/// the (by then activated) real window. Because the thumbnail is live and sized exactly like the
/// window, the hand-over is invisible.
/// </summary>
internal sealed class ZoomOverlay : Window
{
    private const double GrowMs = 200;
    private const double FadeMs = 90;

    /// <summary>Animation speed multiplier from the settings (1 = default; 0.5 = twice as long).</summary>
    public double Speed { get; set; } = 1;

    private readonly nint _hwnd;

    public ZoomOverlay()
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
        Title = "EdgeStage Zoom";
        Width = 1;
        Height = 1;
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        long ex = User32.GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
        User32.SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (nint)(ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT));
    }

    /// <summary>Screen rectangle (device px) the window occupies, or where it will be restored to.</summary>
    public static RECT TargetRect(nint hwnd)
    {
        if (User32.IsIconic(hwnd))
        {
            var placement = new WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (User32.GetWindowPlacement(hwnd, ref placement))
            {
                bool maximized = placement.showCmd == 3 || (placement.flags & 0x2) != 0; // WPF_RESTORETOMAXIMIZED
                if (maximized)
                {
                    nint monitor = User32.MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                    var info = MONITORINFO.Create();
                    if (User32.GetMonitorInfo(monitor, ref info))
                        return info.rcWork;
                }
                return placement.rcNormalPosition;
            }
        }
        return User32.GetWindowRect(hwnd, out RECT r) ? r : default;
    }

    public static RECT MonitorBoundsOf(nint hwnd, RECT fallback)
    {
        nint monitor = User32.MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = MONITORINFO.Create();
        return User32.GetMonitorInfo(monitor, ref info) ? info.rcMonitor : fallback;
    }

    /// <param name="bounds">Screen area (device px) the overlay must cover: both the card's and the window's monitors.</param>
    /// <param name="startActivation">Called once, immediately: Windows only grants the foreground to the
    /// process that was just used, so it must not wait for the animation. The overlay sits above the
    /// window while it comes forward (and while a minimized one plays its restore animation).</param>
    public async Task<ActivationResult> PlayAsync(nint source, RECT bounds, PixelRect from, PixelRect to,
        Func<Task<ActivationResult>> startActivation)
    {
        ThumbnailRegistration? registration = DwmThumbnailService.TryRegister(_hwnd, source, out int hr);
        if (registration is null || registration.SourceSize.Width <= 0)
        {
            Log.Info($"Zoom skipped (DwmRegisterThumbnail 0x{hr:X8}).");
            registration?.Dispose();
            return await startActivation();
        }

        Task<ActivationResult> activation = startActivation();
        var full = new RECT(0, 0, registration.SourceSize.Width, registration.SourceSize.Height);

        void Place(PixelRect r, byte opacity)
        {
            var dest = new RECT((int)Math.Round(r.X - bounds.Left), (int)Math.Round(r.Y - bounds.Top),
                                (int)Math.Round(r.Right - bounds.Left), (int)Math.Round(r.Bottom - bounds.Top));
            registration.Apply(new ThumbnailPlacement(true, dest, full), opacity);
        }

        try
        {
            Place(from, 255);
            User32.SetWindowPos(_hwnd, HWND_TOPMOST, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            Show();

            await Frames(GrowMs / Speed, t => Place(Lerp(from, to, EaseOutQuart(t)), 255));

            // Normally already finished; never hold the screen for long if Windows is slow.
            if (await Task.WhenAny(activation, Task.Delay(700)) != activation)
                Log.Warn("Zoom: activation took longer than 700 ms.");

            await Frames(FadeMs / Speed, t => Place(to, (byte)Math.Round(255 * (1 - t))));
            return await activation;
        }
        finally
        {
            registration.Dispose();
            Hide();
        }
    }

    private static double EaseOutQuart(double t) => 1 - Math.Pow(1 - t, 4);

    private static PixelRect Lerp(PixelRect a, PixelRect b, double e) => new(
        a.X + (b.X - a.X) * e, a.Y + (b.Y - a.Y) * e,
        a.Width + (b.Width - a.Width) * e, a.Height + (b.Height - a.Height) * e);

    /// <summary>Runs <paramref name="step"/> once per composition frame for <paramref name="durationMs"/>, ending on t = 1.</summary>
    private static Task Frames(double durationMs, Action<double> step)
    {
        var done = new TaskCompletionSource();
        var clock = Stopwatch.StartNew();
        int frames = 0;
        void OnFrame(object? sender, EventArgs e)
        {
            double t = Math.Clamp(clock.Elapsed.TotalMilliseconds / durationMs, 0, 1);
            frames++;
            step(t);
            if (t < 1)
                return;
            CompositionTarget.Rendering -= OnFrame;
            Log.Debug($"Zoom phase: {frames} frames in {clock.Elapsed.TotalMilliseconds:F0} ms.");
            done.TrySetResult();
        }
        CompositionTarget.Rendering += OnFrame;
        return done.Task;
    }
}
