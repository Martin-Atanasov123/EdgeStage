using EdgeStage.Core.Input;
using EdgeStage.Core.Layout;
using EdgeStage.Core.Settings;

namespace EdgeStage.Tests.Unit;

/// <summary>Schema 2 settings: migration from EdgeStage 1.x, validation of the new values, tolerant reading.</summary>
[TestClass]
public sealed class SettingsMigrationTests
{
    private string _dir = "";
    private string FilePath => Path.Combine(_dir, "settings.json");

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "EdgeStageTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [TestMethod]
    public void RealV1File_IsMigrated_KeepingEveryValue_AndTheOriginal()
    {
        string v1 = Fixture("settings-v1-real.json");
        File.WriteAllText(FilePath, v1);
        var store = new SettingsStore(FilePath);

        var (settings, outcome) = store.Load();

        Assert.AreEqual(SettingsLoadOutcome.Migrated, outcome);
        Assert.AreEqual(ScreenEdge.Left, settings.Edge);
        Assert.IsTrue(settings.EdgeActivationEnabled);
        Assert.AreEqual(300, settings.ActivationDelayMs);
        Assert.AreEqual(400, settings.DismissDelayMs);
        Assert.AreEqual("Ctrl+Alt+Space", settings.Hotkey.ToString(), "the V1 hotkey is never changed");
        Assert.IsTrue(settings.FirstRunTipShown);
        // New V2 values start at their defaults.
        Assert.AreEqual(Hotkey.ExposeDefault, settings.ExposeHotkey);
        Assert.AreEqual(ThemeMode.Dark, settings.Theme);
        Assert.IsFalse(settings.EdgeInFullscreen);

        Assert.AreEqual(v1, File.ReadAllText(store.V1BackupPath), "original kept byte for byte");
        StringAssert.Contains(File.ReadAllText(FilePath), "\"schemaVersion\": 2");

        // The rewritten file loads as schema 2 with the same values.
        var (again, outcome2) = store.Load();
        Assert.AreEqual(SettingsLoadOutcome.Loaded, outcome2);
        Assert.AreEqual(settings, again);
    }

    [TestMethod]
    public void V1File_WithCustomValues_MigratesThemAll()
    {
        File.WriteAllText(FilePath, """
            {
              "schemaVersion": 1,
              "edge": "Right",
              "edgeActivationEnabled": false,
              "activationDelayMs": 150,
              "dismissDelayMs": 900,
              "hotkey": "Alt+`",
              "holdToSwitch": true,
              "onlyPointerMonitor": true,
              "excludedApps": [ "Spotify.exe", "discord" ],
              "firstRunTipShown": true
            }
            """);

        var (s, outcome) = new SettingsStore(FilePath).Load();

        Assert.AreEqual(SettingsLoadOutcome.Migrated, outcome);
        Assert.AreEqual(ScreenEdge.Right, s.Edge);
        Assert.IsFalse(s.EdgeActivationEnabled);
        Assert.AreEqual(150, s.ActivationDelayMs);
        Assert.AreEqual(900, s.DismissDelayMs);
        Assert.AreEqual("Alt+`", s.Hotkey.ToString());
        Assert.IsTrue(s.HoldToSwitch);
        Assert.IsTrue(s.OnlyPointerMonitor);
        CollectionAssert.AreEqual(new[] { "spotify", "discord" }, s.ExcludedApps.ToArray());
    }

    [TestMethod]
    public void FullV2Settings_RoundTrip()
    {
        var store = new SettingsStore(FilePath);
        var custom = AppSettings.Default with
        {
            SidebarSearch = false,
            SidebarGrouping = true,
            EdgeInFullscreen = true,
            ExposeHotkey = new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x45),
            ExposeAllMonitors = true,
            ExposeDensity = ExposeDensity.Dense,
            ExposeGrouping = true,
            ExposeShowIcons = false,
            ExposeShowTitles = false,
            ExposeSearch = false,
            Theme = ThemeMode.System,
            Accent = "#12AB34",
            Layout = LayoutDensity.Compact,
            CardSize = CardSize.Large,
            CardSpacing = 3.5,
            SidebarWidth = 300,
            CornerRadius = 4,
            Animations = false,
            AnimationPercent = 150,
            ReducedMotion = true,
            ExcludedApps = ["steam"],
        };

        store.Save(custom);
        var (loaded, outcome) = store.Load();

        Assert.AreEqual(SettingsLoadOutcome.Loaded, outcome);
        Assert.AreEqual(custom, loaded);
        string json = File.ReadAllText(FilePath);
        StringAssert.Contains(json, "\"sidebar\"");
        StringAssert.Contains(json, "\"expose\"");
        StringAssert.Contains(json, "\"appearance\"");
    }

    [TestMethod]
    public void OutOfRangeAndInvalidV2Values_AreClampedOrReset()
    {
        File.WriteAllText(FilePath, """
            {
              "schemaVersion": 2,
              "expose": { "hotkey": "Nope+Q", "density": "Galactic" },
              "appearance": {
                "theme": "Neon", "accent": "not a color", "layout": "7", "cardSize": "Huge",
                "cardSpacing": 999, "sidebarWidth": 10, "cornerRadius": -3, "animationPercent": 5000
              }
            }
            """);

        var (s, outcome) = new SettingsStore(FilePath).Load();

        Assert.AreEqual(SettingsLoadOutcome.Loaded, outcome);
        Assert.AreEqual(Hotkey.ExposeDefault, s.ExposeHotkey);
        Assert.AreEqual(ExposeDensity.Auto, s.ExposeDensity);
        Assert.AreEqual(ThemeMode.Dark, s.Theme);
        Assert.AreEqual(Accents.System, s.Accent);
        Assert.AreEqual(LayoutDensity.Balanced, s.Layout, "numbers are not accepted as enum names");
        Assert.AreEqual(CardSize.Standard, s.CardSize);
        Assert.AreEqual(AppSettings.MaxCardSpacing, s.CardSpacing);
        Assert.AreEqual(CardMetrics.MinSidebarWidth, s.SidebarWidth);
        Assert.AreEqual(AppSettings.MinCornerRadius, s.CornerRadius);
        Assert.AreEqual(AppSettings.MaxAnimationPercent, s.AnimationPercent);
    }

    [TestMethod]
    public void MistypedValues_ResetOnlyThemselves()
    {
        File.WriteAllText(FilePath, """
            {
              "schemaVersion": 2,
              "sidebar": { "activationDelayMs": "fast", "dismissDelayMs": "650", "search": "false", "grouping": 1 },
              "appearance": { "cardSpacing": { "x": 1 }, "cornerRadius": 12 },
              "excludedApps": [ "steam", 42, null, "Discord.EXE" ]
            }
            """);

        var (s, outcome) = new SettingsStore(FilePath).Load();

        Assert.AreEqual(SettingsLoadOutcome.Loaded, outcome);
        Assert.AreEqual(AppSettings.Default.ActivationDelayMs, s.ActivationDelayMs);
        Assert.AreEqual(650, s.DismissDelayMs, "numbers written as text are understood");
        Assert.IsFalse(s.SidebarSearch);
        Assert.AreEqual(AppSettings.Default.SidebarGrouping, s.SidebarGrouping);
        Assert.AreEqual(AppSettings.Default.CardSpacing, s.CardSpacing);
        Assert.AreEqual(12, s.CornerRadius);
        CollectionAssert.AreEqual(new[] { "steam", "discord" }, s.ExcludedApps.ToArray());
    }

    [TestMethod]
    public void NonObjectRoot_IsTreatedAsCorrupt()
    {
        File.WriteAllText(FilePath, "[1, 2, 3]");

        var (s, outcome) = new SettingsStore(FilePath).Load();

        Assert.AreEqual(SettingsLoadOutcome.RecoveredFromCorruption, outcome);
        Assert.AreEqual(AppSettings.Default, s);
        Assert.IsTrue(File.Exists(FilePath + ".bak"));
    }

    [TestMethod]
    public void FileWithoutSchemaVersion_IsTreatedAsV1()
    {
        File.WriteAllText(FilePath, """{ "edge": "Right" }""");

        var (s, outcome) = new SettingsStore(FilePath).Load();

        Assert.AreEqual(SettingsLoadOutcome.Migrated, outcome);
        Assert.AreEqual(ScreenEdge.Right, s.Edge);
    }

    [TestMethod]
    [DataRow("system", "System")]
    [DataRow("BLUE", "Blue")]
    [DataRow("#a1b2c3", "#A1B2C3")]
    [DataRow("#12345", "System")]
    [DataRow("red-ish", "System")]
    [DataRow(null, "System")]
    public void Accent_IsNormalized(string? input, string expected) =>
        Assert.AreEqual(expected, Accents.Normalize(input));

    [TestMethod]
    public void Accent_ResolvesCuratedAndCustomColors()
    {
        Assert.IsTrue(Accents.TryResolve("Green", out uint green));
        Assert.AreEqual(0x30D158u, green);
        Assert.IsTrue(Accents.TryResolve("#000080", out uint navy));
        Assert.AreEqual(0x000080u, navy);
        Assert.IsFalse(Accents.TryResolve("System", out _), "the Windows accent is resolved by the app");
    }

    [TestMethod]
    public void LayoutPreset_SetsSpacing_AndCardMetricsFollowSettings()
    {
        var compact = AppSettings.Default.WithLayout(LayoutDensity.Compact);
        var spacious = AppSettings.Default.WithLayout(LayoutDensity.Spacious);

        Assert.IsTrue(compact.CardSpacing < AppSettings.Default.CardSpacing);
        Assert.IsTrue(spacious.CardSpacing > AppSettings.Default.CardSpacing);
        Assert.IsTrue(compact.CardMetrics.CardPadding < spacious.CardMetrics.CardPadding);
        Assert.AreEqual(CardMetrics.Default, AppSettings.Default.CardMetrics, "defaults keep the V1 sidebar geometry");

        var wide = AppSettings.Default with { SidebarWidth = 360 };
        Assert.IsTrue(wide.CardMetrics.CardHeight > AppSettings.Default.CardMetrics.CardHeight);
    }

    [TestMethod]
    public void Motion_FollowsAnimationsAndReducedMotion()
    {
        Assert.IsTrue(AppSettings.Default.UseMotion);
        Assert.IsFalse((AppSettings.Default with { ReducedMotion = true }).UseMotion);
        Assert.IsFalse((AppSettings.Default with { Animations = false }).UseMotion);
        Assert.AreEqual(0.5, (AppSettings.Default with { AnimationPercent = 200 }).AnimationSpeed, 1e-9);
    }
}
