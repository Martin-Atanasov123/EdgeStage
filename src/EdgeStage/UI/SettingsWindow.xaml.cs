using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using EdgeStage.Core;
using EdgeStage.Core.Input;
using EdgeStage.Core.Interop;
using EdgeStage.Core.Settings;

namespace EdgeStage.UI;

/// <summary>What the settings window needs from the application; implemented by the controller.</summary>
internal interface ISettingsHost
{
    AppSettings Settings { get; }
    HotkeyRegistrationResult HotkeyStatus { get; }
    StartupState StartupState { get; }
    HotkeyRegistrationResult ProbeHotkey(Hotkey hotkey);
    HotkeyRegistrationResult UpdateSettings(AppSettings settings);
    void SetStartWithWindows(bool enabled);

    /// <summary>Releases the global shortcut while the user records a new one, so the keys reach the recorder.</summary>
    void SuspendHotkey();
    void ResumeHotkey();

    /// <summary>A shortcut that is currently free, offered when the configured one is taken.</summary>
    Hotkey? SuggestFreeHotkey();
}

internal partial class SettingsWindow : Window
{
    private readonly ISettingsHost _host;
    private readonly DispatcherTimer _saveDelay;
    private AppSettings? _pending;
    private bool _loading;
    private bool _recording;
    private Hotkey? _suggestion;

    public SettingsWindow(ISettingsHost host)
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 40; // scrolls on small screens
        _host = host;
        _saveDelay = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) => FlushPending(), Dispatcher);
        _saveDelay.Stop();
        SourceInitialized += (_, _) =>
            Dwmapi.SetIntAttribute(new WindowInteropHelper(this).Handle, NativeConstants.DWMWA_USE_IMMERSIVE_DARK_MODE, 1);
        Closed += (_, _) =>
        {
            FlushPending();
            if (_recording)
                _host.ResumeHotkey();
        };
        LoadFromHost();
    }

    /// <summary>Re-reads every value (e.g. after the tray menu changed something).</summary>
    public void LoadFromHost()
    {
        _loading = true;
        AppSettings s = _host.Settings;
        EdgeEnabled.IsChecked = s.EdgeActivationEnabled;
        EdgeLeft.IsChecked = s.Edge == ScreenEdge.Left;
        EdgeRight.IsChecked = s.Edge == ScreenEdge.Right;
        ActivationDelay.Value = Math.Min(s.ActivationDelayMs, ActivationDelay.Maximum);
        ActivationDelayText.Text = $"{s.ActivationDelayMs} ms";
        DismissDelay.Value = Math.Min(s.DismissDelayMs, DismissDelay.Maximum);
        DismissDelayText.Text = $"{s.DismissDelayMs} ms";
        HoldToSwitch.IsChecked = s.HoldToSwitch;
        OnlyMonitor.IsChecked = s.OnlyPointerMonitor;
        RefreshHiddenApps();
        LoadStartupState();
        RefreshHotkey();
        _loading = false;
    }

    // ---- behavior and hidden apps -------------------------------------------------------------------

    private void OnBehaviorToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        Apply(_host.Settings with
        {
            HoldToSwitch = HoldToSwitch.IsChecked == true,
            OnlyPointerMonitor = OnlyMonitor.IsChecked == true,
        });
    }

    private void RefreshHiddenApps()
    {
        HiddenAppsList.Children.Clear();
        ShowAllApps.IsEnabled = _host.Settings.ExcludedApps.Count > 0;
        foreach (string app in _host.Settings.ExcludedApps)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = app, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var remove = new Button
            {
                Content = "Show again",
                Style = (Style)FindResource("DarkButton"),
                Padding = new Thickness(10, 3, 10, 3),
                Tag = app,
            };
            AutomationProperties.SetName(remove, $"Show {app} again");
            remove.Click += OnRemoveHiddenApp;
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            HiddenAppsList.Children.Add(row);
        }
    }

    private void OnAddHiddenApp(object sender, RoutedEventArgs e)
    {
        string name = AppSettings.NormalizeAppName(NewApp.Text);
        if (name.Length == 0)
            return;
        if (!_host.Settings.ExcludedApps.Contains(name, StringComparer.OrdinalIgnoreCase))
            Apply(_host.Settings with { ExcludedApps = [.. _host.Settings.ExcludedApps, name] });
        NewApp.Clear();
        RefreshHiddenApps();
    }

    private void OnNewAppKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        OnAddHiddenApp(sender, e);
        e.Handled = true;
    }

    private void OnShowAllApps(object sender, RoutedEventArgs e)
    {
        if (_host.Settings.ExcludedApps.Count == 0)
            return;
        Apply(_host.Settings with { ExcludedApps = AppNameList.Empty });
        RefreshHiddenApps();
    }

    private void OnRemoveHiddenApp(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string app })
            return;
        Apply(_host.Settings with { ExcludedApps = _host.Settings.ExcludedApps.Where(a => !string.Equals(a, app, StringComparison.OrdinalIgnoreCase)).ToArray() });
        RefreshHiddenApps();
    }

    // ---- keyboard shortcut recorder -------------------------------------------------------------

    private void RefreshHotkey(string? message = null, bool warning = false)
    {
        HotkeyText.Text = _recording ? "Press a shortcut…" : _host.Settings.Hotkey.ToString();
        HotkeyBox.BorderBrush = _recording
            ? (Brush)FindResource("AccentBrush")
            : (Brush)FindResource("ControlBorderBrush");

        _suggestion = null;
        if (message is null && !_recording)
        {
            HotkeyRegistrationResult status = _host.HotkeyStatus;
            if (status.Succeeded)
            {
                message = "Active.";
            }
            else
            {
                warning = true;
                message = status.Status == HotkeyRegistrationStatus.Conflict
                    ? "Another app is already using this shortcut, so it does not work right now."
                    : $"Windows did not accept this shortcut (error {status.Win32Error}).";
                _suggestion = _host.SuggestFreeHotkey();
            }
        }

        HotkeyStatus.Text = message ?? "Hold Ctrl, Alt, Shift or Win and press a key. Esc cancels.";
        HotkeyStatus.Foreground = (Brush)FindResource(warning ? "WarningTextBrush" : "SecondaryTextBrush");
        SuggestButton.Visibility = _suggestion is null ? Visibility.Collapsed : Visibility.Visible;
        SuggestButton.Content = _suggestion is { } s ? $"Use {s}" : null;
    }

    private void OnHotkeyBoxClicked(object sender, MouseButtonEventArgs e)
    {
        HotkeyBox.Focus();
        e.Handled = true;
    }

    private void OnHotkeyBoxFocused(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_recording)
            return;
        _recording = true;
        _host.SuspendHotkey();
        RefreshHotkey();
    }

    private void OnHotkeyBoxBlurred(object sender, KeyboardFocusChangedEventArgs e) => EndRecording();

    private void EndRecording(string? message = null, bool warning = false)
    {
        if (!_recording)
            return;
        _recording = false;
        _host.ResumeHotkey();
        RefreshHotkey(message, warning);
        // Park focus on the window itself. Otherwise WPF remembers the shortcut box as the focused
        // element and puts focus back into it when the window is re-activated, which would silently
        // start recording again (and suspend the global shortcut).
        FocusManager.SetFocusedElement(this, this);
        if (HotkeyBox.IsKeyboardFocusWithin)
            Keyboard.Focus(this);
    }

    private void OnHotkeyBoxKeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key switch
        {
            Key.System => e.SystemKey,          // keys pressed with Alt
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };
        HotkeyModifiers modifiers = ToHotkeyModifiers(Keyboard.Modifiers);

        if (key == Key.Tab && modifiers == HotkeyModifiers.None)
            return; // let Tab move focus
        e.Handled = true;

        if (key == Key.Escape && modifiers == HotkeyModifiers.None)
        {
            EndRecording();
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            HotkeyText.Text = modifiers == HotkeyModifiers.None ? "Press a shortcut…" : Hotkey.FormatModifiers(modifiers) + "…";
            return;
        }

        var hotkey = new Hotkey(modifiers, KeyInterop.VirtualKeyFromKey(key));
        if (!hotkey.IsValid)
        {
            RefreshHotkey("Use at least one of Ctrl, Alt, Shift or Win together with a letter, number or function key.", warning: true);
            HotkeyText.Text = "Press a shortcut…";
            return;
        }

        HotkeyRegistrationResult probe = _host.ProbeHotkey(hotkey);
        if (!probe.Succeeded)
        {
            RefreshHotkey(probe.Status == HotkeyRegistrationStatus.Conflict
                ? $"{hotkey} is already used by another app. Try a different combination."
                : $"Windows did not accept {hotkey}.", warning: true);
            HotkeyText.Text = "Press a shortcut…";
            return;
        }

        _host.UpdateSettings(_host.Settings with { Hotkey = hotkey });
        EndRecording();
    }

    private void OnUseSuggestion(object sender, RoutedEventArgs e)
    {
        if (_suggestion is not { } suggestion)
            return;
        _host.UpdateSettings(_host.Settings with { Hotkey = suggestion });
        RefreshHotkey();
    }

    private static HotkeyModifiers ToHotkeyModifiers(ModifierKeys keys)
    {
        var result = HotkeyModifiers.None;
        if (keys.HasFlag(ModifierKeys.Control)) result |= HotkeyModifiers.Control;
        if (keys.HasFlag(ModifierKeys.Alt)) result |= HotkeyModifiers.Alt;
        if (keys.HasFlag(ModifierKeys.Shift)) result |= HotkeyModifiers.Shift;
        if (keys.HasFlag(ModifierKeys.Windows)) result |= HotkeyModifiers.Win;
        return result;
    }

    // ---- other settings ------------------------------------------------------------------------------

    private void OnEdgeToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
            Apply(_host.Settings with { EdgeActivationEnabled = EdgeEnabled.IsChecked == true });
    }

    private void OnEdgeSideChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading)
            Apply(_host.Settings with { Edge = EdgeRight.IsChecked == true ? ScreenEdge.Right : ScreenEdge.Left });
    }

    private void OnActivationDelayChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading)
            return;
        int ms = (int)Math.Round(e.NewValue);
        ActivationDelayText.Text = $"{ms} ms";
        Apply(Current with { ActivationDelayMs = ms }, debounce: true);
    }

    private void OnDismissDelayChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading)
            return;
        int ms = (int)Math.Round(e.NewValue);
        DismissDelayText.Text = $"{ms} ms";
        Apply(Current with { DismissDelayMs = ms }, debounce: true);
    }

    private AppSettings Current => _pending ?? _host.Settings;

    /// <summary>Applies immediately, or after a short pause for sliders so dragging does not rewrite the file on every tick.</summary>
    private void Apply(AppSettings settings, bool debounce = false)
    {
        _pending = settings;
        _saveDelay.Stop();
        if (debounce)
            _saveDelay.Start();
        else
            FlushPending();
    }

    private void FlushPending()
    {
        _saveDelay.Stop();
        if (_pending is null)
            return;
        AppSettings settings = _pending;
        _pending = null;
        _host.UpdateSettings(settings);
    }

    private void LoadStartupState()
    {
        StartupState state = _host.StartupState;
        StartWithWindows.IsChecked = state == StartupState.Enabled;
        StartupHint.Text = state switch
        {
            StartupState.EnabledElsewhere => "Registered for another copy of EdgeStage. Turn on to start this copy instead.",
            StartupState.DisabledInTaskManager => "Disabled in Task Manager › Startup apps. Turn on to re-enable it.",
            _ => "Starts EdgeStage in the background when you sign in.",
        };
    }

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        _host.SetStartWithWindows(StartWithWindows.IsChecked == true);
        _loading = true;
        LoadStartupState();
        _loading = false;
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppInfo.LogDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppInfo.LogDirectory}\"") { UseShellExecute = true });
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
