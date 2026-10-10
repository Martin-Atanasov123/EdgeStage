using System.Windows.Threading;
using EdgeStage.Core;
using EdgeStage.Core.Activation;
using EdgeStage.Core.Diagnostics;
using EdgeStage.Core.Input;
using EdgeStage.Core.Interop;
using EdgeStage.Core.Settings;
using EdgeStage.Core.Thumbnails;
using EdgeStage.Core.WindowTracking;
using EdgeStage.UI;
using Microsoft.Win32;
using static EdgeStage.Core.Interop.NativeConstants;

namespace EdgeStage.Hosting;

/// <summary>
/// Composition root and coordinator: owns the services and decides when the sidebar opens and
/// closes. Everything here runs on the WPF UI thread.
/// </summary>
internal sealed class EdgeStageController : IDisposable
{
    public const int SidebarHotkeyId = 1;
    public const int EscapeHotkeyId = 2;

    private readonly Dispatcher _dispatcher;
    private readonly SettingsStore _store;
    private readonly MessageWindow _messages = new();
    private readonly HotkeyService _hotkeys;
    private readonly WindowTracker _tracker = new();
    private readonly EdgeTriggerService _edge;
    private readonly SidebarWindow _sidebar;
    private MonitorLayout _layout;
    private bool _disposed;

    public EdgeStageController(SettingsStore store)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _store = store;
        (Settings, SettingsLoadOutcome outcome) = _store.Load();
        Log.Info($"Settings {outcome} ({store.FilePath}): {Settings}");

        _hotkeys = new HotkeyService(_messages.Handle);
        _hotkeys.Pressed += OnHotkeyPressed;
        _messages.Handler = OnMessage;

        _layout = MonitorLayout.Load();
        _edge = new EdgeTriggerService(new DispatcherSynchronizationContext(_dispatcher), _layout);
        _edge.Triggered += OnEdgeTriggered;

        _sidebar = new SidebarWindow(_tracker);
        _sidebar.Dismissed += OnSidebarDismissed;
    }

    public AppSettings Settings { get; private set; }

    public HotkeyRegistrationResult HotkeyStatus { get; private set; }

    /// <summary>The sidebar hotkey could not be registered (e.g. another app owns it).</summary>
    public event Action<Hotkey, HotkeyRegistrationResult>? HotkeyRegistrationFailed;

    public event Action? ExitRequested;

    public bool IsSidebarOpen => _sidebar.IsOpen;

    public void Start()
    {
        LogEnvironment();
        _tracker.Start();
        ApplyHotkey();
        _edge.Configure(Settings.EdgeActivationEnabled, Settings.Edge, Settings.ActivationDelayMs);
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    // ---- sidebar ------------------------------------------------------------------------------------

    public void ShowSidebar(SidebarOpenMode mode, int? monitorIndex = null)
    {
        if (_sidebar.IsOpen || _layout.Monitors.Count == 0)
            return;

        int index = monitorIndex ?? (User32.GetCursorPos(out POINT p) ? _layout.IndexOf(p) : 0);
        index = Math.Clamp(index, 0, _layout.Monitors.Count - 1);

        _edge.Pause();
        if (mode == SidebarOpenMode.Edge)
        {
            // Esc must dismiss even though the edge-opened sidebar never takes keyboard focus.
            var esc = _hotkeys.RegisterTransientKey(EscapeHotkeyId, VK_ESCAPE);
            Log.Info(esc.Succeeded ? "Esc reserved while the edge-opened sidebar is visible." : $"Esc could not be reserved: {esc.Status}.");
        }
        _sidebar.Open(mode, _layout.Monitors[index], Settings);
    }

    public void ToggleSidebar()
    {
        if (_sidebar.IsOpen)
            _sidebar.Dismiss("hotkey pressed again");
        else
            ShowSidebar(SidebarOpenMode.Hotkey);
    }

    public void HideSidebar(string reason) => _sidebar.Dismiss(reason);

    private void OnSidebarDismissed(string reason)
    {
        _hotkeys.Unregister(EscapeHotkeyId);
        if (!_disposed)
            _edge.Resume();
    }

    private void OnEdgeTriggered(int monitorIndex, MonitorLayout layout)
    {
        if (!ReferenceEquals(layout, _layout))
            return; // monitors changed since the trigger was computed
        ShowSidebar(SidebarOpenMode.Edge, monitorIndex);
    }

    // ---- settings -----------------------------------------------------------------------------------

    /// <summary>Applies and persists new settings. Returns the hotkey registration result.</summary>
    public HotkeyRegistrationResult UpdateSettings(AppSettings settings)
    {
        settings = settings.Normalized();
        bool hotkeyChanged = settings.Hotkey != Settings.Hotkey;
        Settings = settings;
        _store.Save(settings);
        _edge.Configure(settings.EdgeActivationEnabled, settings.Edge, settings.ActivationDelayMs);
        if (hotkeyChanged || !HotkeyStatus.Succeeded)
            ApplyHotkey();
        Log.Info($"Settings updated: {settings}");
        return HotkeyStatus;
    }

    /// <summary>Checks whether <paramref name="hotkey"/> is available (our own current hotkey counts as available).</summary>
    public HotkeyRegistrationResult ProbeHotkey(Hotkey hotkey) =>
        hotkey == Settings.Hotkey && HotkeyStatus.Succeeded
            ? new HotkeyRegistrationResult(HotkeyRegistrationStatus.Registered)
            : _hotkeys.Probe(hotkey);

    private void ApplyHotkey()
    {
        HotkeyStatus = _hotkeys.Register(SidebarHotkeyId, Settings.Hotkey);
        if (HotkeyStatus.Succeeded)
        {
            Log.Info($"Hotkey {Settings.Hotkey} registered.");
        }
        else
        {
            Log.Warn($"Hotkey {Settings.Hotkey} not registered: {HotkeyStatus.Status} (error {HotkeyStatus.Win32Error}).");
            HotkeyRegistrationFailed?.Invoke(Settings.Hotkey, HotkeyStatus);
        }
    }

    // ---- messages ------------------------------------------------------------------------------------

    private bool OnMessage(uint message, nint wParam, nint lParam)
    {
        if (_hotkeys.HandleMessage(message, wParam))
            return true;
        if (message == SingleInstance.ShowSidebarMessage)
        {
            ShowSidebar(SidebarOpenMode.Hotkey);
            return true;
        }
        if (message == SingleInstance.ExitMessage)
        {
            ExitRequested?.Invoke();
            return true;
        }
#if DEBUG
        if (message == SingleInstance.TestActivateSelectedMessage)
        {
            _sidebar.ActivateSelectedForTest();
            return true;
        }
        if (message == SingleInstance.TestShowEdgeMessage)
        {
            ShowSidebar(SidebarOpenMode.Edge);
            return true;
        }
#endif
        return false;
    }

    private void OnHotkeyPressed(int id)
    {
        switch (id)
        {
            case SidebarHotkeyId:
                ToggleSidebar();
                break;
            case EscapeHotkeyId:
                _sidebar.Dismiss("escape");
                break;
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // Raised on a SystemEvents thread.
        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
                return;
            _layout = MonitorLayout.Load();
            _edge.UpdateLayout(_layout);
            _sidebar.Dismiss("display configuration changed");
            Log.Info($"Display configuration changed: {DescribeMonitors()}");
        });
    }

    // ---- diagnostics ---------------------------------------------------------------------------------

    private void LogEnvironment()
    {
        Log.Info($"{AppInfo.ProductName} {typeof(EdgeStageController).Assembly.GetName().Version} starting; " +
                 $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription}; " +
                 $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; " +
                 $"elevated={ProcessElevation.IsCurrentProcessElevated()}");
        Log.Info($"Monitors: {DescribeMonitors()}");
    }

    private string DescribeMonitors() => string.Join("; ", _layout.Monitors.Select(m =>
        $"{m.Bounds} work {m.WorkArea} {m.Dpi}dpi{(m.IsPrimary ? " primary" : "")}"));

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _sidebar.Dismiss("exit");
        _sidebar.Close();
        _edge.Dispose();
        _hotkeys.Dispose();
        _tracker.Dispose();
        _messages.Dispose();
        Log.Info($"Shutdown complete; live DWM thumbnail registrations: {DwmThumbnailService.LiveRegistrations}.");
    }
}
