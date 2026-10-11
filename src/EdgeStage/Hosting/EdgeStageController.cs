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
    public const int ExposeHotkeyId = 3;

    /// <summary>Offered (in this order) when the configured shortcut is taken; the first free one is suggested.</summary>
    private static readonly string[] FallbackHotkeys = ["Alt+`", "Ctrl+Shift+Space", "Ctrl+Alt+S", "Ctrl+Alt+Q", "Ctrl+Shift+`"];
    private static readonly string[] FallbackExposeHotkeys = ["Ctrl+Alt+Down", "Ctrl+Shift+Up", "Ctrl+Alt+Tab", "Alt+Shift+`", "Ctrl+Alt+F1"];

    private readonly Dispatcher _dispatcher;
    private readonly SettingsStore _store;
    private readonly StartupRegistration _startup;
    private readonly MessageWindow _messages = new();
    private readonly HotkeyService _hotkeys;
    private readonly WindowTracker _tracker = new();
    private readonly EdgeTriggerService _edge;
    private readonly SidebarWindow _sidebar;
    private readonly ExposeWindow _expose;
    private readonly TrayIcon _tray;
    private SettingsWindow? _settingsWindow;
    private MonitorLayout _layout;
    private int _sidebarMonitor;
    private readonly DispatcherTimer _holdWatcher;
    private bool _hotkeySuspended;
    private SettingsPage _balloonPage = SettingsPage.Sidebar;
    private bool _sessionLocked;
    private bool _disposed;

    public EdgeStageController(SettingsStore store, StartupRegistration startup)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _store = store;
        _startup = startup;
        (Settings, SettingsLoadOutcome outcome) = _store.Load();
        Log.Info($"Settings {outcome} ({store.FilePath}): {Settings}");
        if (outcome == SettingsLoadOutcome.Migrated)
            Log.Info($"Settings from EdgeStage 1.x were carried over (original kept as {store.V1BackupPath}).");
        if (outcome == SettingsLoadOutcome.RecoveredFromCorruption)
            LoadWarning = "Your settings file could not be read, so defaults are in use. A copy was saved next to it as settings.json.bak.";

        // Colors first: every window created below picks them up.
        if (System.Windows.Application.Current is { } app)
            ThemeManager.Apply(app.Resources, Settings);

        _hotkeys = new HotkeyService(_messages.Handle);
        _hotkeys.Pressed += OnHotkeyPressed;
        _messages.Handler = OnMessage;

        _layout = MonitorLayout.Load();
        _edge = new EdgeTriggerService(new DispatcherSynchronizationContext(_dispatcher), _layout);
        _edge.Triggered += OnEdgeTriggered;

        _sidebar = new SidebarWindow(_tracker);
        _sidebar.Dismissed += OnSidebarDismissed;
        _sidebar.KeyboardTaken += () => _hotkeys.Unregister(EscapeHotkeyId);
        _sidebar.HideAppRequested += app =>
        {
            Log.Info($"Hiding app '{app}' from the sidebar.");
            UpdateSettings(Settings with { ExcludedApps = [.. Settings.ExcludedApps, app] });
        };
        _expose = new ExposeWindow(_tracker);
        _expose.Dismissed += OnExposeDismissed;
        _expose.HideAppRequested += app =>
        {
            Log.Info($"Hiding app '{app}' (from Exposé).");
            UpdateSettings(Settings with { ExcludedApps = [.. Settings.ExcludedApps, app] });
        };
        _sidebar.ExposeRequested += () =>
        {
            _sidebar.Dismiss("opening Exposé", immediate: true);
            ShowExpose(_sidebarMonitor); // same monitor as the sidebar
        };
        _holdWatcher = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Input, OnHoldTick, _dispatcher);
        _holdWatcher.Stop();

        _tray = new TrayIcon(_messages.Handle, AppInfo.ProductName);
        _tray.Invoked += () => ShowSidebar(SidebarOpenMode.Hotkey);
        _tray.ContextMenuRequested += () => TrayMenu.Show(this);
        _tray.BalloonClicked += () => OpenSettings(_balloonPage);
        _sidebar.ActivationFailed += OnActivationFailed;
        _expose.ActivationFailed += OnActivationFailed;
    }

    public AppSettings Settings { get; private set; }

    public HotkeyRegistrationResult HotkeyStatus { get; private set; }

    public HotkeyRegistrationResult ExposeHotkeyStatus { get; private set; }

    /// <summary>Set when settings had to be reset; shown once at startup.</summary>
    public string? LoadWarning { get; }

    public event Action? ExitRequested;

    public bool IsSidebarOpen => _sidebar.IsOpen;

    public void Start(bool autostart)
    {
        LogEnvironment();
        _tracker.Start();
        ApplyHotkey();
        ApplyExposeHotkey();
        _edge.Configure(Settings.EdgeActivationEnabled, Settings.Edge, Settings.ActivationDelayMs, Settings.EdgeInFullscreen);
        _sidebar.ApplySettings(Settings);
        _expose.ApplySettings(Settings);
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        UpdateTrayTip();

        if (!HotkeyStatus.Succeeded)
            ReportHotkeyProblem(quiet: autostart);
        else if (!ExposeHotkeyStatus.Succeeded)
            ReportExposeHotkeyProblem();
        else if (!Settings.FirstRunTipShown)
            _tray.ShowBalloon("EdgeStage is running",
                $"Press {Settings.Hotkey} or rest the pointer at the {EdgeName(Settings.Edge)} screen edge to switch windows. " +
                $"{Settings.ExposeHotkey} shows all windows at once (Exposé).");

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
        _sidebarMonitor = index;

        _edge.Pause();
        if (mode == SidebarOpenMode.Edge)
        {
            // Esc must dismiss even though the edge-opened sidebar never takes keyboard focus.
            var esc = _hotkeys.RegisterTransientKey(EscapeHotkeyId, VK_ESCAPE);
            Log.Info(esc.Succeeded ? "Esc reserved while the edge-opened sidebar is visible." : $"Esc could not be reserved: {esc.Status}.");
        }
        _sidebar.Open(mode, _layout.Monitors[index], Settings);
    }

    private void OnActivationFailed(string title, ActivationResult result) =>
        _tray.ShowBalloon($"Couldn't switch to {title}",
            result.TargetElevated ? "That window runs as administrator, which Windows does not let EdgeStage switch to."
                                  : "Windows did not allow the switch.", warning: true);

    // ---- Exposé ----------------------------------------------------------------------------------------

    public bool IsExposeOpen => _expose.IsOpen;

    /// <summary>Opens Exposé on the pointer's monitor (or the given one).</summary>
    public void ShowExpose(int? monitorIndex = null)
    {
        if (_expose.IsOpen || _sessionLocked || _layout.Monitors.Count == 0)
            return;
        if (_sidebar.IsOpen)
            _sidebar.Dismiss("opening Exposé", immediate: true);
        int index = monitorIndex ?? (User32.GetCursorPos(out POINT p) ? _layout.IndexOf(p) : 0);
        index = Math.Clamp(index, 0, _layout.Monitors.Count - 1);
        _edge.Pause();
        ExposeOptions options = ExposeOptions.From(Settings);
#if DEBUG
        // Slow motion for animation captures in validation scripts.
        if (double.TryParse(Environment.GetEnvironmentVariable("EDGESTAGE_TEST_ANIMATION_SCALE"),
                System.Globalization.CultureInfo.InvariantCulture, out double animationScale))
            options = options with { AnimationScale = animationScale };
#endif
        _expose.Open(_layout.Monitors[index], Settings, options);
    }

    public void ToggleExpose()
    {
        if (_expose.IsOpen)
            _expose.Dismiss("hotkey pressed again");
        else
            ShowExpose();
    }

    private void OnExposeDismissed(string reason)
    {
        if (!_disposed && !_sessionLocked && !_sidebar.IsOpen)
            _edge.Resume();
    }

    /// <summary>Hold-to-switch needs at least one modifier to hold.</summary>
    private bool HoldMode => Settings.HoldToSwitch && Settings.Hotkey.Modifiers != HotkeyModifiers.None;

    public void ToggleSidebar()
    {
        if (_expose.IsOpen)
            _expose.Dismiss("switching to the sidebar", immediate: true, returnToPrevious: false);
        if (_sidebar.IsOpen)
        {
            if (HoldMode && _sidebar.Mode == SidebarOpenMode.Hotkey)
            {
                // Alt+Tab-style: each further press steps forward (Shift steps back, if Shift is not part of the shortcut).
                bool back = !Settings.Hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift) && IsDown(0x10);
                _sidebar.CycleSelection(back ? -1 : 1);
                StartHoldWatcher();
            }
            else
            {
                _sidebar.Dismiss("hotkey pressed again");
            }
            return;
        }

        ShowSidebar(SidebarOpenMode.Hotkey);
        if (HoldMode && _sidebar.IsOpen)
            StartHoldWatcher();
    }

    private void StartHoldWatcher()
    {
        if (!_holdWatcher.IsEnabled)
            _holdWatcher.Start();
    }

    /// <summary>Releasing every modifier of the shortcut switches to the selected window.</summary>
    private void OnHoldTick(object? sender, EventArgs e)
    {
        if (!_sidebar.IsOpen)
        {
            _holdWatcher.Stop();
            return;
        }
        HotkeyModifiers m = Settings.Hotkey.Modifiers;
        bool held = (m.HasFlag(HotkeyModifiers.Control) && IsDown(0x11))
                    || (m.HasFlag(HotkeyModifiers.Alt) && IsDown(0x12))
                    || (m.HasFlag(HotkeyModifiers.Shift) && IsDown(0x10))
                    || (m.HasFlag(HotkeyModifiers.Win) && (IsDown(0x5B) || IsDown(0x5C)));
        if (held)
            return;
        _holdWatcher.Stop();
        Log.Info("Hold-to-switch: modifiers released, switching to the selected window.");
        _sidebar.ActivateSelected();
    }

    private static bool IsDown(int virtualKey) => (User32.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

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

    void ITrayActions.ShowExpose() => ShowExpose();

    public bool ExposeHotkeyAvailable => ExposeHotkeyStatus.Succeeded;

    public void OpenHiddenApps() => OpenSettings(SettingsPage.HiddenApps);

    void ITrayActions.UpdateSettings(AppSettings settings) => UpdateSettings(settings);

    public bool HotkeyAvailable => HotkeyStatus.Succeeded;

    public bool StartWithWindows => _startup.GetState() == StartupState.Enabled;

    public StartupState StartupState => _startup.GetState();

    /// <summary>Applies and persists new settings. Returns the hotkey registration result.</summary>
    public HotkeyRegistrationResult UpdateSettings(AppSettings settings)
    {
        settings = settings.Normalized();
        bool themeChanged = settings.Theme != Settings.Theme || settings.Accent != Settings.Accent;
        bool hotkeyChanged = settings.Hotkey != Settings.Hotkey;
        bool exposeHotkeyChanged = settings.ExposeHotkey != Settings.ExposeHotkey;
        bool edgeChanged = settings.Edge != Settings.Edge;
        Persist(settings);
        if (themeChanged && System.Windows.Application.Current is { } app)
            ThemeManager.Apply(app.Resources, Settings);
        _sidebar.ApplySettings(Settings);
        _expose.ApplySettings(Settings);
        _edge.Configure(settings.EdgeActivationEnabled, settings.Edge, settings.ActivationDelayMs, settings.EdgeInFullscreen);
        if (edgeChanged)
            _sidebar.Dismiss("edge changed", immediate: true);
        if (hotkeyChanged || !HotkeyStatus.Succeeded)
            ApplyHotkey();
        if (exposeHotkeyChanged || hotkeyChanged || !ExposeHotkeyStatus.Succeeded)
            ApplyExposeHotkey();
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

    public void OpenSettings() => OpenSettings(SettingsPage.Appearance);

    public void OpenSettings(SettingsPage page)
    {
        if (_settingsWindow is not { IsLoaded: true })
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        _settingsWindow.LoadFromHost();
        _settingsWindow.ShowPage(page);
        _settingsWindow.Activate();
    }

    public void Exit() => ExitRequested?.Invoke();

    /// <summary>Checks whether <paramref name="hotkey"/> is available (our own current hotkey counts as available).</summary>
    public HotkeyRegistrationResult ProbeHotkey(Hotkey hotkey) => ProbeHotkey(hotkey, forExpose: false);

    /// <summary>Checks whether <paramref name="hotkey"/> is available for the sidebar or for Exposé (the shortcut's own current value counts as available).</summary>
    public HotkeyRegistrationResult ProbeHotkey(Hotkey hotkey, bool forExpose)
    {
        Hotkey own = forExpose ? Settings.ExposeHotkey : Settings.Hotkey;
        Hotkey other = forExpose ? Settings.Hotkey : Settings.ExposeHotkey;
        bool ownRegistered = forExpose ? ExposeHotkeyStatus.Succeeded : HotkeyStatus.Succeeded;
        if (hotkey == other)
            return new HotkeyRegistrationResult(HotkeyRegistrationStatus.Duplicate);
        if (hotkey == own && ownRegistered && !_hotkeySuspended)
            return new HotkeyRegistrationResult(HotkeyRegistrationStatus.Registered);
        return _hotkeys.Probe(hotkey);
    }

    /// <summary>Releases both global shortcuts while the user records a new one, so the keys reach the recorder.</summary>
    public void SuspendHotkey()
    {
        _hotkeySuspended = true;
        _hotkeys.Unregister(SidebarHotkeyId);
        _hotkeys.Unregister(ExposeHotkeyId);
    }

    public void ResumeHotkey()
    {
        _hotkeySuspended = false;
        ApplyHotkey();
        ApplyExposeHotkey();
        UpdateTrayTip();
    }

    public Hotkey? SuggestFreeHotkey() => SuggestFreeHotkey(forExpose: false);

    public Hotkey? SuggestFreeHotkey(bool forExpose)
    {
        foreach (string text in forExpose ? FallbackExposeHotkeys : FallbackHotkeys)
            if (Hotkey.TryParse(text, out Hotkey candidate) && candidate != Settings.Hotkey
                && candidate != Settings.ExposeHotkey && _hotkeys.Probe(candidate).Succeeded)
                return candidate;
        return null;
    }

    /// <summary>Registers the Exposé shortcut. A conflict is reported, never resolved by picking another key.</summary>
    private void ApplyExposeHotkey()
    {
        if (_hotkeySuspended)
            return;
        if (Settings.ExposeHotkey == Settings.Hotkey)
        {
            _hotkeys.Unregister(ExposeHotkeyId);
            ExposeHotkeyStatus = new HotkeyRegistrationResult(HotkeyRegistrationStatus.Duplicate);
            Log.Warn($"Exposé shortcut {Settings.ExposeHotkey} is the same as the sidebar shortcut; not registered.");
            return;
        }
        ExposeHotkeyStatus = _hotkeys.Register(ExposeHotkeyId, Settings.ExposeHotkey);
        if (ExposeHotkeyStatus.Succeeded)
            Log.Info($"Exposé hotkey {Settings.ExposeHotkey} registered.");
        else
            Log.Warn($"Exposé hotkey {Settings.ExposeHotkey} not registered: {ExposeHotkeyStatus.Status} (error {ExposeHotkeyStatus.Win32Error}).");
    }

    /// <summary>Exposé is optional, so its shortcut problem is a quiet tray notification only.</summary>
    private void ReportExposeHotkeyProblem()
    {
        _balloonPage = SettingsPage.Expose;
        _tray.ShowBalloon("Exposé shortcut unavailable",
            $"{Settings.ExposeHotkey} is {(ExposeHotkeyStatus.Status == HotkeyRegistrationStatus.Duplicate ? "the same as the sidebar shortcut" : "used by another app")}. " +
            "Exposé still opens from the tray and the sidebar. Choose a different shortcut in settings.", warning: true);
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
            _balloonPage = SettingsPage.Sidebar;
            _tray.ShowBalloon("EdgeStage shortcut unavailable", text, warning: true);
            return;
        }
        new NoticeWindow($"Shortcut {Settings.Hotkey} is not available", text, "Choose a shortcut…", () => OpenSettings(SettingsPage.Sidebar)).Show();
    }

    private void UpdateTrayTip()
    {
        string hotkey = HotkeyStatus.Succeeded ? Settings.Hotkey.ToString() : $"{Settings.Hotkey} unavailable";
        string edge = Settings.EdgeActivationEnabled ? $"{EdgeName(Settings.Edge)} edge" : "edge off";
        string expose = ExposeHotkeyStatus.Succeeded ? $"Exposé {Settings.ExposeHotkey}" : "Exposé (no shortcut)";
        _tray.SetTip($"{AppInfo.ProductName} — {hotkey} · {edge} · {expose}");
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
        if (message == SingleInstance.ToggleExposeMessage)
        {
            ToggleExpose();
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
        if (message == SingleInstance.TestCommandMessage)
        {
            RunTestCommand((SingleInstance.TestCommand)(int)wParam);
            return true;
        }
#endif
        return false;
    }

#if DEBUG
    /// <summary>Tests draw over a backdrop on the primary monitor, wherever the pointer is.</summary>
    private int PrimaryMonitorIndex => Math.Max(0, _layout.Monitors.ToList().FindIndex(m => m.IsPrimary));

    private void RunTestCommand(SingleInstance.TestCommand command)
    {
        Log.Info($"Test command {command}.");
        switch (command)
        {
            case SingleInstance.TestCommand.ExposeShow: ShowExpose(PrimaryMonitorIndex); break;
            case SingleInstance.TestCommand.ExposeActivateSelected: _expose.ActivateSelectedForTest(); break;
            case SingleInstance.TestCommand.ExposeEscape: _expose.Dismiss("escape (test)"); break;
            case SingleInstance.TestCommand.ExposeRight: _expose.SelectForTest("right"); break;
            case SingleInstance.TestCommand.ExposeLeft: _expose.SelectForTest("left"); break;
            case SingleInstance.TestCommand.ExposeUp: _expose.SelectForTest("up"); break;
            case SingleInstance.TestCommand.ExposeDown: _expose.SelectForTest("down"); break;
            case SingleInstance.TestCommand.ExposeDescribe: Log.Info("Exposé state: " + _expose.DescribeForTest()); break;
            case SingleInstance.TestCommand.ExposeTypeSearch: _expose.TypeForTest("REPORT"); break;
            case SingleInstance.TestCommand.ExposeEscapeKey: _expose.EscapeKeyForTest(); break;
            case SingleInstance.TestCommand.ExposeToggleGrouping: _expose.ToggleGroupingForTest(); break;
            case SingleInstance.TestCommand.SidebarExposeButton:
                ShowSidebar(SidebarOpenMode.Hotkey, PrimaryMonitorIndex);
                _dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _sidebar.ClickExposeButtonForTest());
                break;
        }
    }
#endif

    private void OnHotkeyPressed(int id)
    {
        switch (id)
        {
            case SidebarHotkeyId:
                ToggleSidebar();
                break;
            case ExposeHotkeyId:
                ToggleExpose();
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
            _expose.Dismiss("display configuration changed", immediate: true);
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
                    _expose.Dismiss("session locked", immediate: true);
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
        _holdWatcher.Stop();
        _settingsWindow?.Close();
        TrayMenu.CloseOpenMenu();
        _sidebar.Dismiss("exit", immediate: true);
        _sidebar.Close();
        _expose.Dismiss("exit", immediate: true);
        _expose.Close();
        _edge.Dispose();
        _hotkeys.Dispose();
        _tracker.Dispose();
        _tray.Dispose();
        _messages.Dispose();
        Log.Info($"Shutdown complete; live DWM thumbnail registrations: {DwmThumbnailService.LiveRegistrations}.");
    }
}
