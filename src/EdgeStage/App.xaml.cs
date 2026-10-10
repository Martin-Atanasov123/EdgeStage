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
        if (HasArg("--test-activate-selected") || HasArg("--test-show-edge"))
        {
            SingleInstance.Signal(HasArg("--test-show-edge") ? SingleInstance.TestShowEdgeMessage : SingleInstance.TestActivateSelectedMessage);
            Shutdown(0);
            return;
        }
#endif

        if (!SingleInstance.TryAcquire())
        {
            SingleInstance.Signal(SingleInstance.ShowSidebarMessage);
            Shutdown(0);
            return;
        }

        Log.Initialize(AppInfo.LogDirectory);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => { Log.Error("Unobserved task exception", args.Exception); args.SetObserved(); };

        _controller = new EdgeStageController(SettingsStore.ForCurrentUser());
        _controller.ExitRequested += () =>
        {
            _controller.HideSidebar("exit");
            Shutdown(0);
        };
        _controller.HotkeyRegistrationFailed += OnHotkeyRegistrationFailed;
        _controller.Start();

        if (HasArg("--show"))
            _controller.ShowSidebar(SidebarOpenMode.Hotkey);
    }

    private static void OnHotkeyRegistrationFailed(Hotkey hotkey, HotkeyRegistrationResult result)
    {
        string reason = result.Status == HotkeyRegistrationStatus.Conflict
            ? "another application is already using it"
            : $"Windows rejected it (error {result.Win32Error})";
        new NoticeWindow(
            $"Shortcut {hotkey} is not available",
            $"It could not be registered because {reason}. EdgeStage is still running and opens from the " +
            "screen edge. Choose a different shortcut in EdgeStage settings.").Show();
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
