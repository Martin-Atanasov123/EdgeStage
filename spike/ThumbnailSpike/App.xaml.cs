using System.IO;
using System.Windows;
using System.Windows.Media;

namespace ThumbnailSpike;

/// <summary>
/// Spike entry point.
///   (no args)                 interactive sidebar probe listing real windows
///   --host &lt;HostConfig&gt;       interactive probe using a specific window configuration
///   --auto &lt;report.md&gt;        run the automated experiments and write a report, then exit
///   --source &lt;title&gt; &lt;#RRGGBB&gt; show a solid-color source window (used as an out-of-process source)
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string[] args = e.Args;

        try
        {
            if (args.Length >= 3 && args[0] is "--source" or "--source-flip" or "--source-min" or "--source-retitle" or "--source-stubborn")
            {
                var color = (Color)ColorConverter.ConvertFromString(args[2]);
                // Optional placement: <x> <y> <width> <height> after the color.
                bool placed = args.Length >= 7;
                var window = placed
                    ? new SourceWindow(args[1], color, double.Parse(args[3]), double.Parse(args[4]))
                    : new SourceWindow(args[1], color, 900, 500);
                if (placed)
                {
                    window.Width = double.Parse(args[5]);
                    window.Height = double.Parse(args[6]);
                }
                window.Closed += (_, _) => Shutdown();
                window.Show();
                if (args[0] == "--source-flip")
                {
                    // Out-of-process content that changes continuously: proves the thumbnail is live.
                    var other = Color.FromRgb(0, 255, 255);
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                    bool flip = false;
                    timer.Tick += (_, _) => window.SetColor((flip = !flip) ? other : color);
                    timer.Start();
                }
                else if (args[0] == "--source-retitle")
                {
                    // Title that keeps changing (like a terminal or a browser tab) and grows very long.
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                    int step = 0;
                    timer.Tick += (_, _) => window.Title = $"{args[1]} — step {++step} — " + new string('x', 20 * (step % 4));
                    timer.Start();
                }
                else if (args[0] == "--source-stubborn")
                {
                    // Ignores close requests, like an app showing "Save changes?" (ended by the test script).
                    window.Closing += (_, e) => e.Cancel = true;
                }
                else if (args[0] == "--source-min")
                {
                    await Task.Delay(800);
                    window.WindowState = WindowState.Minimized;
                }
                return;
            }

            if (args.Length >= 3 && args[0] == "--capture-window")
            {
                // --capture-window <exact title> <out prefix> [frames] [intervalMs]
                int frames = args.Length >= 4 ? int.Parse(args[3]) : 1;
                int interval = args.Length >= 5 ? int.Parse(args[4]) : 250;
                File.WriteAllText(args[2] + ".txt", await Capture.WindowFrames(args[1], args[2], frames, interval));
                Shutdown(0);
                return;
            }

            if (args.Length >= 6 && args[0] == "--capture-rect")
            {
                // --capture-rect <x> <y> <width> <height> <out prefix> [frames] [intervalMs]
                int frames = args.Length >= 7 ? int.Parse(args[6]) : 1;
                int interval = args.Length >= 8 ? int.Parse(args[7]) : 250;
                File.WriteAllText(args[5] + ".txt", await Capture.RectFrames(int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]), args[5], frames, interval));
                Shutdown(0);
                return;
            }

            if (args.Length >= 5 && args[0] == "--backdrop")
            {
                // --backdrop <left> <top> <width> <height>: opaque neutral surface so screenshots of
                // the translucent sidebar show nothing of the user's own windows behind it.
                var backdrop = new Window
                {
                    WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
                    Left = double.Parse(args[1]), Top = double.Parse(args[2]), Width = double.Parse(args[3]), Height = double.Parse(args[4]),
                    Background = new LinearGradientBrush(Color.FromRgb(0x3A, 0x4A, 0x6A), Color.FromRgb(0x6A, 0x4A, 0x5A), 45),
                    Title = "EdgeStage Spike Backdrop", ShowActivated = false, Topmost = true,
                };
                // Tool window: never listed by EdgeStage (or Alt+Tab).
                backdrop.SourceInitialized += (_, _) =>
                {
                    nint h = new System.Windows.Interop.WindowInteropHelper(backdrop).Handle;
                    long ex = EdgeStage.Core.Interop.User32.GetWindowLongPtr(h, EdgeStage.Core.Interop.NativeConstants.GWL_EXSTYLE);
                    EdgeStage.Core.Interop.User32.SetWindowLongPtr(h, EdgeStage.Core.Interop.NativeConstants.GWL_EXSTYLE,
                        (nint)(ex | EdgeStage.Core.Interop.NativeConstants.WS_EX_TOOLWINDOW));
                };
                backdrop.Closed += (_, _) => Shutdown();
                backdrop.Show();
                return;
            }

            if (args.Length >= 2 && args[0] == "--realprobe")
            {
                var report = await new RealWindowProbe().RunAsync();
                File.WriteAllText(args[1], report);
                Shutdown(0);
                return;
            }

            if (args.Length >= 2 && args[0] is "--auto" or "--followup")
            {
                var runner = new AutoExperiments();
                var report = args[0] == "--auto" ? await runner.RunAsync() : await runner.RunFollowUpsAsync();
                File.WriteAllText(args[1], report);
                Shutdown(0);
                return;
            }

            var config = HostConfig.AcrylicNoFrame;
            if (args.Length >= 2 && args[0] == "--host")
                config = Enum.Parse<HostConfig>(args[1], ignoreCase: true);

            var probe = new SidebarProbeWindow(config);
            probe.Closed += (_, _) => Shutdown();
            probe.Show();
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "spike-crash.txt"), ex.ToString());
            Shutdown(1);
        }
    }
}
