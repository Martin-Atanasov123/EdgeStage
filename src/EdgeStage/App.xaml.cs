using System.Windows;
using System.Windows.Threading;
using EdgeStage.Core;
using EdgeStage.Core.Diagnostics;
using EdgeStage.Core.Input;
using EdgeStage.Core.Settings;
using EdgeStage.Hosting;
using EdgeStage.UI;

namespace EdgeStage;

/// <summary>
/// Command line:
///   (none)        start (or, if already running, show the sidebar)
///   --show        start if needed and show the sidebar
///   --autostart   used by the "Start with Windows" registry entry
///   --exit        ask the running instance to exit
/// </summary>
public partial class App : Application
{
    private EdgeStageController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool HasArg(string name) => e.Args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

        if (HasArg("--exit"))
        {
            SingleInstance.Signal(SingleInstance.ExitMessage);
            Shutdown(0);
            return;
        }

#if DEBUG
        uint? testMessage = HasArg("--test-show-edge") ? SingleInstance.TestShowEdgeMessage
            : HasArg("--test-activate-selected") ? SingleInstance.TestActivateSelectedMessage
            : HasArg("--test-open-settings") ? SingleInstance.TestOpenSettingsMessage
            : HasArg("--test-tray-menu") ? SingleInstance.TestTrayMenuMessage
            : null;
        if (testMessage is uint message)
        {
            SingleInstance.Signal(message);
            Shutdown(0);
            return;
        }
        // --test-cmd <name>: e.g. --test-cmd ExposeShow (see SingleInstance.TestCommand).
        int cmdIndex = Array.FindIndex(e.Args, a => string.Equals(a, "--test-cmd", StringComparison.OrdinalIgnoreCase));
        if (cmdIndex >= 0 && cmdIndex + 1 < e.Args.Length
            && Enum.TryParse(e.Args[cmdIndex + 1], ignoreCase: true, out SingleInstance.TestCommand command))
        {
            SingleInstance.Signal(SingleInstance.TestCommandMessage, (nint)command);
            Shutdown(0);
            return;
        }
#endif

        if (!SingleInstance.TryAcquire())
        {
            SingleInstance.Signal(HasArg("--expose") ? SingleInstance.ToggleExposeMessage : SingleInstance.ShowSidebarMessage);
            Shutdown(0);
            return;
        }

        Log.Initialize(AppInfo.LogDirectory);
        // Optional diagnostics: EDGESTAGE_LOG_LEVEL=Debug writes detailed traces to the same log.
        if (Enum.TryParse(Environment.GetEnvironmentVariable("EDGESTAGE_LOG_LEVEL"), ignoreCase: true, out LogLevel level))
            Log.MinimumLevel = level;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => { Log.Error("Unobserved task exception", args.Exception); args.SetObserved(); };

        _controller = new EdgeStageController(SettingsStore.ForCurrentUser(), StartupRegistration.ForCurrentUser());
        _controller.ExitRequested += () =>
        {
            _controller.HideSidebar("exit", immediate: true);
            Shutdown(0);
        };
        _controller.Start(autostart: HasArg("--autostart"));

        if (HasArg("--show"))
            _controller.ShowSidebar(SidebarOpenMode.Hotkey);
        else if (HasArg("--expose"))
            _controller.ShowExpose();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A switcher utility should keep running; the failure is recorded for diagnosis.
        Log.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        SingleInstance.Release();
        Log.Info("Exited.");
        Log.Shutdown();
        base.OnExit(e);
    }
}
