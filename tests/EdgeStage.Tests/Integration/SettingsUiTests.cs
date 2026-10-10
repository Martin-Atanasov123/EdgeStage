using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using EdgeStage.Core.Input;
using EdgeStage.Core.Settings;
using EdgeStage.UI;
using Microsoft.Win32;

namespace EdgeStage.Tests.Integration;

/// <summary>Fake application host recording what the UI asked for.</summary>
internal sealed class FakeHost : ITrayActions, ISettingsHost
{
    public AppSettings Settings { get; set; } = AppSettings.Default;
    public HotkeyRegistrationResult HotkeyStatus { get; set; } = new(HotkeyRegistrationStatus.Registered);
    public StartupState StartupState { get; set; } = StartupState.Disabled;
    public bool HotkeyAvailable => HotkeyStatus.Succeeded;
    public bool StartWithWindows => StartupState == StartupState.Enabled;
    public List<string> Calls { get; } = [];
    public Hotkey? Suggestion { get; set; }
    public Func<Hotkey, HotkeyRegistrationResult> Probe { get; set; } = _ => new(HotkeyRegistrationStatus.Registered);

    public void ShowSidebar() => Calls.Add("show");
    public void OpenSettings() => Calls.Add("settings");
    public void Exit() => Calls.Add("exit");
    public void SuspendHotkey() => Calls.Add("suspend");
    public void ResumeHotkey() => Calls.Add("resume");
    public Hotkey? SuggestFreeHotkey() => Suggestion;
    public HotkeyRegistrationResult ProbeHotkey(Hotkey hotkey) => Probe(hotkey);

    public void SetStartWithWindows(bool enabled)
    {
        Calls.Add($"startup:{enabled}");
        StartupState = enabled ? StartupState.Enabled : StartupState.Disabled;
    }

    void ITrayActions.UpdateSettings(AppSettings settings) => UpdateSettings(settings);

    public HotkeyRegistrationResult UpdateSettings(AppSettings settings)
    {
        Calls.Add("update");
        Settings = settings;
        return HotkeyStatus;
    }
}

/// <summary>
/// Drives the real tray menu and settings window objects against a fake host. Interaction is
/// raised programmatically (no synthesized OS input), so this covers the UI logic, not the
/// physical click path.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
public sealed class SettingsUiTests
{
    private static void EnsureApplicationResources()
    {
        if (Application.Current is null)
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (Application.Current!.Resources.MergedDictionaries.Count == 0)
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/EdgeStage;component/Themes/Dark.xaml"),
            });
    }

    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    private static MenuItem Find(ItemsControl menu, string headerStart) =>
        menu.Items.OfType<MenuItem>().First(i => ((string)i.Header).StartsWith(headerStart, StringComparison.Ordinal));

    // ---- tray menu ---------------------------------------------------------------------------------

    [STATestMethod]
    public void TrayMenu_ReflectsState_AndRoutesClicks()
    {
        EnsureApplicationResources();
        var host = new FakeHost
        {
            Settings = AppSettings.Default with { Edge = ScreenEdge.Right, ActivationDelayMs = 500 },
            HotkeyStatus = new(HotkeyRegistrationStatus.Conflict, 1409),
            StartupState = StartupState.Enabled,
        };

        ContextMenu menu = TrayMenu.Build(host);

        Assert.IsTrue(Find(menu, "Edge activation").IsChecked);
        Assert.IsTrue(Find(menu, "Start with Windows").IsChecked);
        StringAssert.Contains((string)Find(menu, "Shortcut").Header, "(unavailable)");
        var position = Find(menu, "Position");
        Assert.IsFalse(Find(position, "Left").IsChecked);
        Assert.IsTrue(Find(position, "Right").IsChecked);
        Assert.IsTrue(Find(Find(menu, "Activation delay"), "500 ms").IsChecked);

        Click(Find(menu, "Show sidebar"));
        Click(Find(menu, "Edge activation"));
        Click(Find(position, "Left"));
        Click(Find(Find(menu, "Activation delay"), "150 ms"));
        Click(Find(menu, "Start with Windows"));
        Click(Find(menu, "Settings"));
        Click(Find(menu, "Exit"));

        Assert.IsFalse(host.Settings.EdgeActivationEnabled);
        Assert.AreEqual(ScreenEdge.Left, host.Settings.Edge);
        Assert.AreEqual(150, host.Settings.ActivationDelayMs);
        Assert.AreEqual(StartupState.Disabled, host.StartupState);
        CollectionAssert.IsSubsetOf(new[] { "show", "startup:False", "settings", "exit" }, host.Calls);
    }

    [STATestMethod]
    public void TrayMenu_CustomDelay_IsShownAsChecked()
    {
        EnsureApplicationResources();
        var host = new FakeHost { Settings = AppSettings.Default with { ActivationDelayMs = 650 } };

        var delay = Find(TrayMenu.Build(host), "Activation delay");

        var custom = delay.Items.OfType<MenuItem>().Last();
        Assert.IsTrue(custom.IsChecked);
        StringAssert.Contains((string)custom.Header, "650 ms");
        Assert.IsFalse(delay.Items.OfType<MenuItem>().Take(3).Any(i => i.IsChecked));
    }

    // ---- settings window ---------------------------------------------------------------------------

    private static SettingsWindow OpenSettings(FakeHost host)
    {
        EnsureApplicationResources();
        var window = new SettingsWindow(host) { ShowActivated = false, Left = -2000, Top = -2000, WindowStartupLocation = WindowStartupLocation.Manual };
        window.Show();
        Pump(100);
        return window;
    }

    [STATestMethod]
    public void Settings_LoadsCurrentValues()
    {
        var host = new FakeHost { Settings = AppSettings.Default with { Edge = ScreenEdge.Right, EdgeActivationEnabled = false, ActivationDelayMs = 450, DismissDelayMs = 900 } };
        var window = OpenSettings(host);
        try
        {
            Assert.AreEqual(false, window.EdgeEnabled.IsChecked);
            Assert.AreEqual(true, window.EdgeRight.IsChecked);
            Assert.AreEqual(450, window.ActivationDelay.Value, 0.01);
            Assert.AreEqual("450 ms", window.ActivationDelayText.Text);
            Assert.AreEqual(900, window.DismissDelay.Value, 0.01);
            Assert.AreEqual("Ctrl+Alt+Space", window.HotkeyText.Text);
            Assert.AreEqual("Active.", window.HotkeyStatus.Text);
            Assert.AreEqual(0, host.Calls.Count(c => c == "update"), "loading must not write settings");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void Settings_TogglesAndEdgeApplyImmediately()
    {
        var host = new FakeHost();
        var window = OpenSettings(host);
        try
        {
            window.EdgeEnabled.IsChecked = false;
            Assert.IsFalse(host.Settings.EdgeActivationEnabled);

            window.EdgeRight.IsChecked = true;
            Assert.AreEqual(ScreenEdge.Right, host.Settings.Edge);

            window.StartWithWindows.IsChecked = true;
            CollectionAssert.Contains(host.Calls, "startup:True");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void Settings_SliderChangesAreDebounced_AndFlushedOnClose()
    {
        var host = new FakeHost();
        var window = OpenSettings(host);

        window.ActivationDelay.Value = 100;
        window.ActivationDelay.Value = 200;
        window.ActivationDelay.Value = 250;
        Assert.AreEqual(0, host.Calls.Count(c => c == "update"), "dragging should not save on every tick");
        Assert.AreEqual("250 ms", window.ActivationDelayText.Text);

        window.DismissDelay.Value = 1200;
        window.Close();

        Assert.AreEqual(250, host.Settings.ActivationDelayMs);
        Assert.AreEqual(1200, host.Settings.DismissDelayMs);
        Assert.AreEqual(1, host.Calls.Count(c => c == "update"), "pending slider changes are written once");
    }

    [STATestMethod]
    public void Settings_ConflictingHotkey_ShowsWarningAndSuggestion()
    {
        var host = new FakeHost
        {
            HotkeyStatus = new(HotkeyRegistrationStatus.Conflict, 1409),
            Suggestion = new Hotkey(HotkeyModifiers.Alt, 0xC0),
        };
        var window = OpenSettings(host);
        try
        {
            StringAssert.Contains(window.HotkeyStatus.Text, "Another app");
            Assert.AreEqual(Visibility.Visible, window.SuggestButton.Visibility);
            Assert.AreEqual("Use Alt+`", window.SuggestButton.Content);

            window.SuggestButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.AreEqual("Alt+`", host.Settings.Hotkey.ToString());
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void Settings_RecordingSuspendsTheGlobalHotkey_AndResumesAfterwards()
    {
        var host = new FakeHost();
        var window = OpenSettings(host);
        try
        {
            window.Activate();
            Pump(50);
            if (!window.HotkeyBox.Focus())
                Assert.Inconclusive("The test runner window could not take keyboard focus.");
            Pump(50);
            Assert.AreEqual("Press a shortcut…", window.HotkeyText.Text);
            CollectionAssert.Contains(host.Calls, "suspend");

            Keyboard.ClearFocus();
            window.Focus();
            Pump(50);
            CollectionAssert.Contains(host.Calls, "resume");
            Assert.AreEqual("Ctrl+Alt+Space", window.HotkeyText.Text);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void Settings_ClosingWhileRecording_ResumesHotkey()
    {
        var host = new FakeHost();
        var window = OpenSettings(host);
        window.Activate();
        Pump(50);
        if (!window.HotkeyBox.Focus())
            Assert.Inconclusive("The test runner window could not take keyboard focus.");
        Pump(50);

        window.Close();

        Assert.AreEqual(host.Calls.Count(c => c == "suspend"), host.Calls.Count(c => c == "resume"));
    }

    // ---- start with Windows against the real registry (scratch key) ------------------------------------

    [TestMethod]
    public void StartupRegistration_WritesAndRemovesRealRegistryValue_InScratchKey()
    {
        string scratch = $@"Software\EdgeStage.Tests\{Guid.NewGuid():N}";
        try
        {
            var registry = new WindowsStartupRegistry($@"{scratch}\Run", $@"{scratch}\Approved");
            var startup = new StartupRegistration(registry, @"C:\Apps\EdgeStage\EdgeStage.exe");

            Assert.AreEqual(StartupState.Disabled, startup.GetState());
            startup.Enable();
            using (var key = Registry.CurrentUser.OpenSubKey($@"{scratch}\Run"))
                Assert.AreEqual("\"C:\\Apps\\EdgeStage\\EdgeStage.exe\" --autostart", key?.GetValue("EdgeStage"));
            Assert.AreEqual(StartupState.Enabled, startup.GetState());

            startup.Disable();
            Assert.AreEqual(StartupState.Disabled, startup.GetState());
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\EdgeStage.Tests", throwOnMissingSubKey: false);
        }
    }
}
