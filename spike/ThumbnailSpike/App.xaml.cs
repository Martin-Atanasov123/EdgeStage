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
            if (args.Length >= 3 && args[0] is "--source" or "--source-flip" or "--source-min")
            {
                var color = (Color)ColorConverter.ConvertFromString(args[2]);
                var window = new SourceWindow(args[1], color, 900, 500);
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
                else if (args[0] == "--source-min")
                {
                    await Task.Delay(800);
                    window.WindowState = WindowState.Minimized;
                }
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
