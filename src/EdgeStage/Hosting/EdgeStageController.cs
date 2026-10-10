using System.IO;
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
internal sealed class EdgeStageController : IDisposable, ITrayActions, ISettingsHost
{
    public const int SidebarHotkeyId = 1;
    public const int EscapeHotkeyId = 2;

    /// <summary>Offered (in this order) when the configured shortcut is taken; the first free one is suggested.</summary>
    private static readonly string[] FallbackHotkeys = ["Alt+`", "Ctrl+Shift+Space", "Ctrl+Alt+S", "Ctrl+Alt+Q", "Ctrl+Shift+`"];

    private readonly Dispatcher _dispatcher;
    private readonly SettingsStore _store;
    private readonly StartupRegistration _startup;
    private readonly MessageWindow _messages = new();
    private readonly HotkeyService _hotkeys;
    private readonly WindowTracker _tracker = new();
    private readonly EdgeTriggerService _edge;
    private readonly SidebarWindow _sidebar;
    private readonly TrayIcon _tray;
    private SettingsWindow? _settingsWindow;
    private MonitorLayout _layout;
    private bool _hotkeySuspended;
    private bool _sessionLocked;
    private bool _disposed;

    public EdgeStageController(SettingsStore store, StartupRegistration startup)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _store = store;
        _startup = startup;
        (Settings, SettingsLoadOutcome outcome) = _store.Load();
        Log.Info($"Settings {outcome} ({store.FilePath}): {Settings}");
        if (outcome == SettingsLoadOutcome.RecoveredFromCorruption)
            LoadWarning = "Your settings file could not be read, so defaults are in use. A copy was saved next to it as settings.json.bak.";

        _hotkeys = new HotkeyService(_messages.Handle);
        _hotkeys.Pressed += OnHotkeyPressed;
        _messages.Handler = OnMessage;

        _layout = MonitorLayout.Load();
        _edge = new EdgeTriggerService(new DispatcherSynchronizationContext(_dispatcher), _layout);
        _edge.Triggered += OnEdgeTriggered;

        _sidebar = new SidebarWindow(_tracker);
        _sidebar.Dismissed += OnSidebarDismissed;

        _tray = new TrayIcon(_messages.Handle, AppInfo.ProductName);
        _tray.Invoked += () => ShowSidebar(SidebarOpenMode.Hotkey);
        _tray.ContextMenuRequested += () => TrayMenu.Show(this);
        _tray.BalloonClicked += OpenSettings;
    }

    public AppSettings Settings { get; private set; }

    public HotkeyRegistrationResult HotkeyStatus { get; private set; }

    /// <summary>Set when settings had to be reset; shown once at startup.</summary>
    public string? LoadWarning { get; }

    public event Action? ExitRequested;

    public bool IsSidebarOpen => _sidebar.IsOpen;

    public void Start(bool autostart)
    {
        LogEnvironment();
        _tracker.Start();
        ApplyHotkey();
        _edge.Configure(Settings.EdgeActivationEnabled, Settings.Edge, Settings.ActivationDelayMs);
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        UpdateTrayTip();

        if (!HotkeyStatus.Succeeded)
            ReportHotkeyProblem(quiet: autostart);
        else if (!Settings.FirstRunTipShown)
            _tray.ShowBalloon("EdgeStage is running",
                $"Press {Settings.Hotkey} or rest the pointer at the {EdgeName(Settings.Edge)} screen edge to switch windows.");

        if (LoadWarning is not null)
            _tray.ShowBalloon("EdgeStage settings were reset", LoadWarning, warning: true);

        if (!Settings.FirstRunTipShown)
            Persist(Settings with { FirstRunTipShown = true });
    }

    // ---- sidebar ------------------------------------------------------------------------------------

    public void ShowSidebar(SidebarOpenMode mode, int? monitorIndex = null)
    {
        if (_sidebar.IsOpen || _sessionLocked || _layout.Monitors.Count == 0)
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

    public void HideSidebar(string reason, bool immediate = false) => _sidebar.Dismiss(reason, immediate);

    private void OnSidebarDismissed(string reason)
    {
        _hotkeys.Unregister(EscapeHotkeyId);
        if (!_disposed && !_sessionLocked)
            _edge.Resume();
    }

    private void OnEdgeTriggered(int monitorIndex, MonitorLayout layout)
    {
        if (!ReferenceEquals(layout, _layout))
            return; // monitors changed since the trigger was computed
        ShowSidebar(SidebarOpenMode.Edge, monitorIndex);
    }

    // ---- settings (ITrayActions / ISettingsHost) ------------------------------------------------------

    void ITrayActions.ShowSidebar() => ShowSidebar(SidebarOpenMode.Hotkey);

    void ITrayActions.UpdateSettings(AppSettings settings) => UpdateSettings(settings);

    public bool HotkeyAvailable => HotkeyStatus.Succeeded;

    public bool StartWithWindows => _startup.GetState() == StartupState.Enabled;

    public StartupState StartupState => _startup.GetState();

    /// <summary>Applies and persists new settings. Returns the hotkey registration result.</summary>
    public HotkeyRegistrationResult UpdateSettings(AppSettings settings)
    {
        settings = settings.Normalized();
        bool hotkeyChanged = settings.Hotkey != Settings.Hotkey;
        bool edgeChanged = settings.Edge != Settings.Edge;
        Persist(settings);
        _edge.Configure(settings.EdgeActivationEnabled, settings.Edge, settings.ActivationDelayMs);
        if (edgeChanged)
            _sidebar.Dismiss("edge changed", immediate: true);
        if (hotkeyChanged || !HotkeyStatus.Succeeded)
            ApplyHotkey();
        UpdateTrayTip();
        _settingsWindow?.LoadFromHost();
        return HotkeyStatus;
    }

    private void Persist(AppSettings settings)
    {
        Settings = settings.Normalized();
        try
        {
            _store.Save(Settings);
            Log.Info($"Settings saved: {Settings}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Settings could not be saved", ex);
            _tray.ShowBalloon("EdgeStage", "Settings could not be saved: " + ex.Message, warning: true);
        }
    }

    public void SetStartWithWindows(bool enabled)
    {
        try
        {
            if (enabled)
                _startup.Enable();
            else
                _startup.Disable();
            Log.Info($"Start with Windows {(enabled ? "enabled" : "disabled")}: {_startup.GetState()} ({_startup.CommandLine}).");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Error("Could not change the Start with Windows setting", ex);
            _tray.ShowBalloon("EdgeStage", "Could not change \"Start with Windows\": " + ex.Message, warning: true);
        }
        _settingsWindow?.LoadFromHost();
    }

    public void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.LoadFromHost();
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void Exit() => ExitRequested?.Invoke();

    /// <summary>Checks whether <paramref name="hotkey"/> is available (our own current hotkey counts as available).</summary>
    public HotkeyRegistrationResult ProbeHotkey(Hotkey hotkey) =>
        hotkey == Settings.Hotkey && HotkeyStatus.Succeeded && !_hotkeySuspended
            ? new HotkeyRegistrationResult(HotkeyRegistrationStatus.Registered)
            : _hotkeys.Probe(hotkey);

    public void SuspendHotkey()
    {
        _hotkeySuspended = true;
        _hotkeys.Unregister(SidebarHotkeyId);
    }

    public void ResumeHotkey()
    {
        _hotkeySuspended = false;
        ApplyHotkey();
        UpdateTrayTip();
    }

    public Hotkey? SuggestFreeHotkey()
    {
        foreach (string text in FallbackHotkeys)
            if (Hotkey.TryParse(text, out Hotkey candidate) && candidate != Settings.Hotkey && _hotkeys.Probe(candidate).Succeeded)
                return candidate;
        return null;
    }

    private void ApplyHotkey()
    {
        if (_hotkeySuspended)
            return;
        HotkeyStatus = _hotkeys.Register(SidebarHotkeyId, Settings.Hotkey);
        if (HotkeyStatus.Succeeded)
            Log.Info($"Hotkey {Settings.Hotkey} registered.");
        else
            Log.Warn($"Hotkey {Settings.Hotkey} not registered: {HotkeyStatus.Status} (error {HotkeyStatus.Win32Error}).");
    }

    /// <summary>
    /// Explains a hotkey that could not be registered. At sign-in (autostart) only a quiet tray
    /// notification is shown; a manual start shows a window with a direct way to fix it.
    /// </summary>
    private void ReportHotkeyProblem(bool quiet)
    {
        string reason = HotkeyStatus.Status == HotkeyRegistrationStatus.Conflict
            ? "another app is already using it"
            : $"Windows rejected it (error {HotkeyStatus.Win32Error})";
        string text = $"{Settings.Hotkey} could not be registered because {reason}. " +
                      "The screen edge still works. Choose a different shortcut in settings.";
        if (quiet)
        {
            _tray.ShowBalloon("EdgeStage shortcut unavailable", text, warning: true);
            return;
        }
        new NoticeWindow($"Shortcut {Settings.Hotkey} is not available", text, "Choose a shortcut…", OpenSettings).Show();
    }

    private void UpdateTrayTip()
    {
        string hotkey = HotkeyStatus.Succeeded ? Settings.Hotkey.ToString() : $"{Settings.Hotkey} unavailable";
        string edge = Settings.EdgeActivationEnabled ? $"{EdgeName(Settings.Edge)} edge" : "edge off";
        _tray.SetTip($"{AppInfo.ProductName} — {hotkey} · {edge}");
    }

    private static string EdgeName(ScreenEdge edge) => edge == ScreenEdge.Left ? "left" : "right";

    // ---- messages and system events -------------------------------------------------------------------

    private bool OnMessage(uint message, nint wParam, nint lParam)
    {
        if (_tray.HandleMessage(message, wParam, lParam))
            return true;
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
        if (message == SingleInstance.TestOpenSettingsMessage)
        {
            OpenSettings();
            return true;
        }
        if (message == SingleInstance.TestTrayMenuMessage)
        {
            TrayMenu.Show(this, new System.Windows.Point(800, 80));
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
            _sidebar.Dismiss("display configuration changed", immediate: true);
            Log.Info($"Display configuration changed: {DescribeMonitors()}");
        });
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
                return;
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect:
                    _sessionLocked = true;
                    _sidebar.Dismiss("session locked", immediate: true);
                    _edge.Pause();
                    Log.Info($"Session {e.Reason}: sidebar hidden, edge polling paused.");
                    break;
                case SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect:
                    _sessionLocked = false;
                    _layout = MonitorLayout.Load(); // monitors may have changed while locked (docking, RDP)
                    _edge.UpdateLayout(_layout);
                    _edge.Resume();
                    if (!HotkeyStatus.Succeeded)
                        ApplyHotkey(); // the app that held the shortcut may have exited
                    UpdateTrayTip();
                    Log.Info($"Session {e.Reason}: edge polling resumed; hotkey {(HotkeyStatus.Succeeded ? "active" : "unavailable")}.");
                    break;
            }
        });
    }

    // ---- diagnostics ---------------------------------------------------------------------------------

    private void LogEnvironment()
    {
        Log.Info($"{AppInfo.ProductName} {typeof(EdgeStageController).Assembly.GetName().Version} starting; " +
                 $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription}; " +
                 $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; " +
                 $"elevated={ProcessElevation.IsCurrentProcessElevated()}; system DPI {User32.GetDpiForSystem()}");
        Log.Info($"Monitors: {DescribeMonitors()}");
        Log.Info($"Start with Windows: {_startup.GetState()}; tray icon {(_tray.IsVisible ? "added" : "NOT added")}.");
    }

    private string DescribeMonitors() => string.Join("; ", _layout.Monitors.Select(m =>
        $"{m.Bounds} work {m.WorkArea} {m.Dpi}dpi{(m.IsPrimary ? " primary" : "")}"));

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _settingsWindow?.Close();
        TrayMenu.CloseOpenMenu();
        _sidebar.Dismiss("exit", immediate: true);
        _sidebar.Close();
        _edge.Dispose();
        _hotkeys.Dispose();
        _tracker.Dispose();
        _tray.Dispose();
        _messages.Dispose();
        Log.Info($"Shutdown complete; live DWM thumbnail registrations: {DwmThumbnailService.LiveRegistrations}.");
    }
}
