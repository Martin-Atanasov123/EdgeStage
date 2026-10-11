using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using EdgeStage.Core;
using EdgeStage.Core.Input;
using EdgeStage.Core.Interop;
using EdgeStage.Core.Layout;
using EdgeStage.Core.Settings;

namespace EdgeStage.UI;

/// <summary>Pages of the settings window.</summary>
internal enum SettingsPage
{
    Appearance,
    Sidebar,
    Expose,
    HiddenApps,
    General,
}

/// <summary>What the settings window needs from the application; implemented by the controller.</summary>
internal interface ISettingsHost
{
    AppSettings Settings { get; }
    HotkeyRegistrationResult HotkeyStatus { get; }
    HotkeyRegistrationResult ExposeHotkeyStatus { get; }
    StartupState StartupState { get; }

    /// <summary>Whether a shortcut is free for the sidebar or for Exposé (the shortcut's own value counts as free).</summary>
    HotkeyRegistrationResult ProbeHotkey(Hotkey hotkey, bool forExpose);
    HotkeyRegistrationResult UpdateSettings(AppSettings settings);
    void SetStartWithWindows(bool enabled);

    /// <summary>Releases the global shortcuts while the user records a new one, so the keys reach the recorder.</summary>
    void SuspendHotkey();
    void ResumeHotkey();

    /// <summary>A shortcut that is currently free, offered when the configured one is taken.</summary>
    Hotkey? SuggestFreeHotkey(bool forExpose);
}

/// <summary>
/// Compact settings window: one page per area. Every change applies and is saved at once (sliders
/// after a short pause), so there is no OK/Cancel.
/// </summary>
internal partial class SettingsWindow : Window
{
    private readonly ISettingsHost _host;
    private readonly DispatcherTimer _saveDelay;
    private readonly List<Action<AppSettings>> _loaders = [];
    private readonly Recorder _sidebarRecorder;
    private readonly Recorder _exposeRecorder;
    private readonly Dictionary<string, RadioButton> _swatches = new(StringComparer.OrdinalIgnoreCase);
    private RadioButton? _customSwatch;
    private AppSettings? _pending;
    private bool _loading;

    /// <summary>One keyboard-shortcut recorder (the sidebar's or Exposé's).</summary>
    private sealed class Recorder(Border box, TextBlock text, TextBlock status, Button suggest, bool forExpose)
    {
        public Border Box { get; } = box;
        public TextBlock Text { get; } = text;
        public TextBlock Status { get; } = status;
        public Button Suggest { get; } = suggest;
        public bool ForExpose { get; } = forExpose;
        public bool Recording { get; set; }
        public Hotkey? Suggestion { get; set; }
    }

    public SettingsWindow(ISettingsHost host)
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        _host = host;
        _saveDelay = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) => FlushPending(), Dispatcher);
        _saveDelay.Stop();
        VersionText.Text = $"Version {AppInfo.Version}";

        _sidebarRecorder = new Recorder(HotkeyBox, HotkeyText, HotkeyStatus, SuggestButton, forExpose: false);
        _exposeRecorder = new Recorder(ExposeHotkeyBox, ExposeHotkeyText, ExposeHotkeyStatus, ExposeSuggestButton, forExpose: true);
        foreach (Recorder r in new[] { _sidebarRecorder, _exposeRecorder })
            WireRecorder(r);

        BindControls();
        BuildAccentSwatches();

        SourceInitialized += (_, _) => ApplyTitleBarTheme();
        ThemeManager.Changed += OnThemeChanged;
        Closed += (_, _) =>
        {
            ThemeManager.Changed -= OnThemeChanged;
            FlushPending();
            if (_sidebarRecorder.Recording || _exposeRecorder.Recording)
                _host.ResumeHotkey();
        };
        NavAppearance.IsChecked = true;
        LoadFromHost();
    }

    /// <summary>Re-reads every value (e.g. after the tray menu changed something).</summary>
    public void LoadFromHost()
    {
        _loading = true;
        AppSettings s = _host.Settings;
        foreach (Action<AppSettings> load in _loaders)
            load(s);
        SelectAccent(s.Accent);
        AnimationDurationRow.IsEnabled = s.Animations && !s.ReducedMotion;
        RefreshHiddenApps();
        LoadStartupState();
        RefreshHotkey(_sidebarRecorder);
        RefreshHotkey(_exposeRecorder);
        _loading = false;
    }

    /// <summary>Shows one page.</summary>
    public void ShowPage(SettingsPage page)
    {
        RadioButton nav = page switch
        {
            SettingsPage.Sidebar => NavSidebar,
            SettingsPage.Expose => NavExpose,
            SettingsPage.HiddenApps => NavHiddenApps,
            SettingsPage.General => NavGeneral,
            _ => NavAppearance,
        };
        nav.IsChecked = true;
    }

    private void OnNavigate(object sender, RoutedEventArgs e)
    {
        if (PageAppearance is null)
            return; // during InitializeComponent
        PageAppearance.Visibility = NavAppearance.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageSidebar.Visibility = NavSidebar.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageExpose.Visibility = NavExpose.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageHiddenApps.Visibility = NavHiddenApps.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageGeneral.Visibility = NavGeneral.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageScroller.ScrollToTop();
        ResetConfirm.Visibility = Visibility.Collapsed;
    }

    // ---- control ↔ setting bindings ------------------------------------------------------------------
    // Each control writes only its own value, so e.g. a 1500 ms delay set in the file survives a
    // slider whose range ends at 1000 until that slider is actually moved.

    private void BindControls()
    {
        // Appearance
        Choice(ThemeDark, Core.Settings.ThemeMode.Dark, s => s.Theme, (s, v) => s with { Theme = v });
        Choice(ThemeLight, Core.Settings.ThemeMode.Light, s => s.Theme, (s, v) => s with { Theme = v });
        Choice(ThemeSystem, Core.Settings.ThemeMode.System, s => s.Theme, (s, v) => s with { Theme = v });
        Choice(LayoutCompact, LayoutDensity.Compact, s => s.Layout, (s, v) => s.WithLayout(v));
        Choice(LayoutBalanced, LayoutDensity.Balanced, s => s.Layout, (s, v) => s.WithLayout(v));
        Choice(LayoutSpacious, LayoutDensity.Spacious, s => s.Layout, (s, v) => s.WithLayout(v));
        Choice(SizeSmall, CardSize.Small, s => s.CardSize, (s, v) => s with { CardSize = v });
        Choice(SizeStandard, CardSize.Standard, s => s.CardSize, (s, v) => s with { CardSize = v });
        Choice(SizeLarge, CardSize.Large, s => s.CardSize, (s, v) => s with { CardSize = v });
        Range(SidebarWidth, SidebarWidthText, v => $"{v:0} px", s => s.SidebarWidth, (s, v) => s with { SidebarWidth = v });
        Range(CardSpacing, CardSpacingText, v => $"{v:0} px", s => s.CardSpacing, (s, v) => s with { CardSpacing = v });
        Range(CornerRadiusSlider, CornerRadiusText, v => $"{v:0} px", s => s.CornerRadius, (s, v) => s with { CornerRadius = v });
        Toggle(AnimationsEnabled, s => s.Animations, (s, on) => s with { Animations = on });
        Range(AnimationDuration, AnimationDurationText, v => $"{v:0} %", s => s.AnimationPercent, (s, v) => s with { AnimationPercent = (int)Math.Round(v) });
        Toggle(ReducedMotion, s => s.ReducedMotion, (s, on) => s with { ReducedMotion = on });

        // Sidebar
        Toggle(HoldToSwitch, s => s.HoldToSwitch, (s, on) => s with { HoldToSwitch = on });
        Toggle(EdgeEnabled, s => s.EdgeActivationEnabled, (s, on) => s with { EdgeActivationEnabled = on });
        Choice(EdgeLeft, ScreenEdge.Left, s => s.Edge, (s, v) => s with { Edge = v });
        Choice(EdgeRight, ScreenEdge.Right, s => s.Edge, (s, v) => s with { Edge = v });
        Range(ActivationDelay, ActivationDelayText, v => $"{v:0} ms", s => s.ActivationDelayMs, (s, v) => s with { ActivationDelayMs = (int)Math.Round(v) },
            shown: s => $"{s.ActivationDelayMs} ms");
        Range(DismissDelay, DismissDelayText, v => $"{v:0} ms", s => s.DismissDelayMs, (s, v) => s with { DismissDelayMs = (int)Math.Round(v) },
            shown: s => $"{s.DismissDelayMs} ms");
        Toggle(EdgeInFullscreen, s => s.EdgeInFullscreen, (s, on) => s with { EdgeInFullscreen = on });
        Choice(OrderRecent, false, s => s.SidebarGrouping, (s, v) => s with { SidebarGrouping = v });
        Choice(OrderByApp, true, s => s.SidebarGrouping, (s, v) => s with { SidebarGrouping = v });
        Toggle(SidebarSearch, s => s.SidebarSearch, (s, on) => s with { SidebarSearch = on });
        Toggle(OnlyMonitor, s => s.OnlyPointerMonitor, (s, on) => s with { OnlyPointerMonitor = on });

        // Exposé
        Choice(ExposeThisMonitor, false, s => s.ExposeAllMonitors, (s, v) => s with { ExposeAllMonitors = v });
        Choice(ExposeAllMonitors, true, s => s.ExposeAllMonitors, (s, v) => s with { ExposeAllMonitors = v });
        Choice(DensitySpacious, ExposeDensity.Spacious, s => s.ExposeDensity, (s, v) => s with { ExposeDensity = v });
        Choice(DensityAuto, ExposeDensity.Auto, s => s.ExposeDensity, (s, v) => s with { ExposeDensity = v });
        Choice(DensityDense, ExposeDensity.Dense, s => s.ExposeDensity, (s, v) => s with { ExposeDensity = v });
        Toggle(ExposeGrouping, s => s.ExposeGrouping, (s, on) => s with { ExposeGrouping = on });
        Toggle(ExposeTitles, s => s.ExposeShowTitles, (s, on) => s with { ExposeShowTitles = on });
        Toggle(ExposeIcons, s => s.ExposeShowIcons, (s, on) => s with { ExposeShowIcons = on });
        Toggle(ExposeSearch, s => s.ExposeSearch, (s, on) => s with { ExposeSearch = on });
    }

    private void Toggle(CheckBox box, Func<AppSettings, bool> get, Func<AppSettings, bool, AppSettings> set)
    {
        _loaders.Add(s => box.IsChecked = get(s));
        RoutedEventHandler changed = (_, _) =>
        {
            if (_loading)
                return;
            Apply(set(Current, box.IsChecked == true));
            AnimationDurationRow.IsEnabled = AnimationsEnabled.IsChecked == true && ReducedMotion.IsChecked != true;
        };
        box.Checked += changed;
        box.Unchecked += changed;
    }

    private void Choice<T>(RadioButton button, T value, Func<AppSettings, T> get, Func<AppSettings, T, AppSettings> set)
    {
        _loaders.Add(s => button.IsChecked = EqualityComparer<T>.Default.Equals(get(s), value));
        button.Checked += (_, _) =>
        {
            if (_loading)
                return;
            Apply(set(Current, value));
            if (typeof(T) == typeof(LayoutDensity))
                Reload(); // a layout preset also moves the spacing slider
        };
    }

    private void Range(Slider slider, TextBlock label, Func<double, string> format, Func<AppSettings, double> get,
        Func<AppSettings, double, AppSettings> set, Func<AppSettings, string>? shown = null)
    {
        _loaders.Add(s =>
        {
            slider.Value = Math.Clamp(get(s), slider.Minimum, slider.Maximum);
            label.Text = shown?.Invoke(s) ?? format(slider.Value);
        });
        slider.ValueChanged += (_, e) =>
        {
            if (_loading)
                return;
            label.Text = format(e.NewValue);
            Apply(set(Current, e.NewValue), debounce: true);
        };
    }

    private void Reload()
    {
        FlushPending();
        LoadFromHost();
    }

    // ---- accent swatches ------------------------------------------------------------------------------

    private void BuildAccentSwatches()
    {
        AddSwatch(Accents.System, "Windows accent color", ThemeManager.ResolveAccent(Accents.System, ThemeManager.IsDark));
        foreach (var (name, hex) in Accents.Curated)
            AddSwatch(name, name, (Color)ColorConverter.ConvertFromString(hex));

        // "Custom": a multicolor swatch that reveals the #RRGGBB field.
        var custom = new RadioButton
        {
            Style = (Style)FindResource("Swatch"),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(0xFF, 0x45, 0x3A), 0), new(Color.FromRgb(0xFF, 0xD6, 0x0A), 0.33),
                    new(Color.FromRgb(0x30, 0xD1, 0x58), 0.66), new(Color.FromRgb(0x0A, 0x84, 0xFF), 1),
                }, 45),
            ToolTip = "Custom color",
        };
        AutomationProperties.SetName(custom, "Custom accent color");
        custom.Checked += (_, _) =>
        {
            CustomAccentRow.Visibility = Visibility.Visible;
            if (_loading)
                return;
            if (Accents.TryParseHex(CustomAccent.Text.Trim(), out _))
                Apply(Current with { Accent = CustomAccent.Text.Trim() });
            else
                CustomAccent.Focus();
        };
        custom.Unchecked += (_, _) => CustomAccentRow.Visibility = Visibility.Collapsed;
        _customSwatch = custom;
        AccentSwatches.Children.Add(custom);
    }

    private void AddSwatch(string accent, string label, Color color)
    {
        var swatch = new RadioButton { Style = (Style)FindResource("Swatch"), Background = new SolidColorBrush(color), ToolTip = label, Tag = accent };
        AutomationProperties.SetName(swatch, $"Accent color: {label}");
        swatch.Checked += (_, _) =>
        {
            if (!_loading)
                Apply(Current with { Accent = accent });
        };
        _swatches[accent] = swatch;
        AccentSwatches.Children.Add(swatch);
    }

    private void SelectAccent(string accent)
    {
        if (_swatches.TryGetValue(accent, out RadioButton? swatch))
        {
            swatch.IsChecked = true;
            return;
        }
        CustomAccent.Text = accent;
        if (_customSwatch is not null)
            _customSwatch.IsChecked = true;
    }

    private void OnCustomAccentChanged(object sender, TextChangedEventArgs e)
    {
        string text = CustomAccent.Text.Trim();
        bool valid = Accents.TryParseHex(text, out _);
        CustomAccentHint.Text = valid || text.Length == 0 ? "#RRGGBB, e.g. #3A86FF" : "Use # and six hex digits, e.g. #3A86FF";
        if (!_loading && valid && _customSwatch?.IsChecked == true)
            Apply(Current with { Accent = text }, debounce: true);
    }

    private void OnThemeChanged()
    {
        ApplyTitleBarTheme();
        if (_swatches.TryGetValue(Accents.System, out RadioButton? system))
            system.Background = new SolidColorBrush(ThemeManager.ResolveAccent(Accents.System, ThemeManager.IsDark));
        RefreshHotkey(_sidebarRecorder);
        RefreshHotkey(_exposeRecorder);
    }

    private void ApplyTitleBarTheme()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != 0)
            Dwmapi.SetIntAttribute(hwnd, NativeConstants.DWMWA_USE_IMMERSIVE_DARK_MODE, ThemeManager.IsDark ? 1 : 0);
    }

    // ---- hidden apps -------------------------------------------------------------------------------

    private void RefreshHiddenApps()
    {
        HiddenAppsList.Children.Clear();
        ShowAllApps.IsEnabled = _host.Settings.ExcludedApps.Count > 0;
        NoHiddenApps.Visibility = _host.Settings.ExcludedApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
            Apply(Current with { ExcludedApps = [.. _host.Settings.ExcludedApps, name] });
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
        Apply(Current with { ExcludedApps = AppNameList.Empty });
        RefreshHiddenApps();
    }

    private void OnRemoveHiddenApp(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string app })
            return;
        Apply(Current with { ExcludedApps = _host.Settings.ExcludedApps.Where(a => !string.Equals(a, app, StringComparison.OrdinalIgnoreCase)).ToArray() });
        RefreshHiddenApps();
    }

    // ---- keyboard shortcut recorders -------------------------------------------------------------

    private void WireRecorder(Recorder r)
    {
        r.Box.MouseLeftButtonDown += (_, e) =>
        {
            r.Box.Focus();
            e.Handled = true;
        };
        r.Box.GotKeyboardFocus += (_, _) =>
        {
            if (r.Recording)
                return;
            r.Recording = true;
            _host.SuspendHotkey();
            RefreshHotkey(r);
        };
        r.Box.LostKeyboardFocus += (_, _) => EndRecording(r);
        r.Box.PreviewKeyDown += (_, e) => OnRecorderKeyDown(r, e);
        r.Suggest.Click += (_, _) =>
        {
            if (r.Suggestion is not { } suggestion)
                return;
            _host.UpdateSettings(r.ForExpose ? _host.Settings with { ExposeHotkey = suggestion } : _host.Settings with { Hotkey = suggestion });
            RefreshHotkey(r);
        };
    }

    private void RefreshHotkey(Recorder r, string? message = null, bool warning = false)
    {
        Hotkey current = r.ForExpose ? _host.Settings.ExposeHotkey : _host.Settings.Hotkey;
        r.Text.Text = r.Recording ? "Press a shortcut…" : current.ToString();
        r.Box.BorderBrush = (Brush)FindResource(r.Recording ? "AccentBrush" : "ControlBorderBrush");

        r.Suggestion = null;
        if (message is null && !r.Recording)
        {
            HotkeyRegistrationResult status = r.ForExpose ? _host.ExposeHotkeyStatus : _host.HotkeyStatus;
            if (status.Succeeded)
            {
                message = "Active.";
            }
            else
            {
                warning = true;
                message = status.Status switch
                {
                    HotkeyRegistrationStatus.Conflict => "Another app is already using this shortcut, so it does not work right now.",
                    HotkeyRegistrationStatus.Duplicate => "This is the same as the other EdgeStage shortcut. Choose a different one.",
                    _ => $"Windows did not accept this shortcut (error {status.Win32Error}).",
                };
                r.Suggestion = _host.SuggestFreeHotkey(r.ForExpose);
            }
        }

        r.Status.Text = message ?? "Hold Ctrl, Alt, Shift or Win and press a key. Esc cancels.";
        r.Status.Foreground = (Brush)FindResource(warning ? "WarningTextBrush" : "SecondaryTextBrush");
        r.Suggest.Visibility = r.Suggestion is null ? Visibility.Collapsed : Visibility.Visible;
        r.Suggest.Content = r.Suggestion is { } s ? $"Use {s}" : null;
    }

    private void EndRecording(Recorder r, string? message = null, bool warning = false)
    {
        if (!r.Recording)
            return;
        r.Recording = false;
        _host.ResumeHotkey();
        RefreshHotkey(r, message, warning);
        // Park focus on the window itself. Otherwise WPF remembers the shortcut box as the focused
        // element and puts focus back into it when the window is re-activated, which would silently
        // start recording again (and suspend the global shortcut).
        FocusManager.SetFocusedElement(this, this);
        if (r.Box.IsKeyboardFocusWithin)
            Keyboard.Focus(this);
    }

    private void OnRecorderKeyDown(Recorder r, KeyEventArgs e)
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
            EndRecording(r);
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            r.Text.Text = modifiers == HotkeyModifiers.None ? "Press a shortcut…" : Hotkey.FormatModifiers(modifiers) + "…";
            return;
        }

        var hotkey = new Hotkey(modifiers, KeyInterop.VirtualKeyFromKey(key));
        if (!hotkey.IsValid)
        {
            RefreshHotkey(r, "Use at least one of Ctrl, Alt, Shift or Win together with a letter, number or function key.", warning: true);
            r.Text.Text = "Press a shortcut…";
            return;
        }

        HotkeyRegistrationResult probe = _host.ProbeHotkey(hotkey, r.ForExpose);
        if (!probe.Succeeded)
        {
            RefreshHotkey(r, probe.Status switch
            {
                HotkeyRegistrationStatus.Conflict => $"{hotkey} is already used by another app. Try a different combination.",
                HotkeyRegistrationStatus.Duplicate => $"{hotkey} is already EdgeStage's {(r.ForExpose ? "sidebar" : "Exposé")} shortcut.",
                _ => $"Windows did not accept {hotkey}.",
            }, warning: true);
            r.Text.Text = "Press a shortcut…";
            return;
        }

        _host.UpdateSettings(r.ForExpose ? _host.Settings with { ExposeHotkey = hotkey } : _host.Settings with { Hotkey = hotkey });
        EndRecording(r);
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

    // ---- reset ---------------------------------------------------------------------------------------

    private void OnResetClicked(object sender, RoutedEventArgs e)
    {
        ResetDone.Visibility = Visibility.Collapsed;
        ResetConfirm.Visibility = Visibility.Visible;
        ResetNo.Focus();
    }

    private void OnResetCancelled(object sender, RoutedEventArgs e) => ResetConfirm.Visibility = Visibility.Collapsed;

    private void OnResetConfirmed(object sender, RoutedEventArgs e)
    {
        ResetConfirm.Visibility = Visibility.Collapsed;
        _saveDelay.Stop();
        _pending = null;
        AppSettings current = _host.Settings;
        _host.UpdateSettings(ResetToDefaults(current));
        LoadFromHost();
        ResetDone.Visibility = Visibility.Visible;
    }

    /// <summary>Defaults for everything except what the user curated elsewhere (hidden apps) and one-time flags.</summary>
    internal static AppSettings ResetToDefaults(AppSettings current) => AppSettings.Default with
    {
        ExcludedApps = current.ExcludedApps,
        FirstRunTipShown = current.FirstRunTipShown,
    };

    // ---- saving ---------------------------------------------------------------------------------------

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

    // ---- general ---------------------------------------------------------------------------------------

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
