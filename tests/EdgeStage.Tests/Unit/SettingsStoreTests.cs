using EdgeStage.Core.Input;
using EdgeStage.Core.Settings;

namespace EdgeStage.Tests.Unit;

[TestClass]
public sealed class SettingsStoreTests
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

    [TestMethod]
    public void MissingFile_ReturnsDefaults()
    {
        var (settings, outcome) = new SettingsStore(FilePath).Load();

        Assert.AreEqual(SettingsLoadOutcome.Defaults, outcome);
        Assert.AreEqual(AppSettings.Default, settings);
    }

    [TestMethod]
    public void Defaults_MatchTheAgreedSpec()
    {
        var d = AppSettings.Default;
        Assert.AreEqual(ScreenEdge.Left, d.Edge);
        Assert.IsTrue(d.EdgeActivationEnabled);
        Assert.AreEqual(300, d.ActivationDelayMs);
        Assert.AreEqual(400, d.DismissDelayMs);
        Assert.AreEqual("Ctrl+Alt+Space", d.Hotkey.ToString());
    }

    [TestMethod]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new SettingsStore(FilePath);
        var custom = AppSettings.Default with
        {
            Edge = ScreenEdge.Right,
            EdgeActivationEnabled = false,
            ActivationDelayMs = 150,
            DismissDelayMs = 900,
            Hotkey = new Hotkey(HotkeyModifiers.Alt, 0xC0),
            FirstRunTipShown = true,
        };

        store.Save(custom);
        var (loaded, outcome) = store.Load();

        Assert.AreEqual(SettingsLoadOutcome.Loaded, outcome);
        Assert.AreEqual(custom, loaded);
        Assert.IsFalse(File.Exists(FilePath + ".tmp"), "temporary file must not be left behind");
    }

    [TestMethod]
    public void SavedFile_IsReadableJson()
    {
        new SettingsStore(FilePath).Save(AppSettings.Default);
        string json = File.ReadAllText(FilePath);

        StringAssert.Contains(json, "\"hotkey\": \"Ctrl+Alt+Space\"");
        StringAssert.Contains(json, "\"edge\": \"Left\"");
        StringAssert.Contains(json, "\"schemaVersion\": 1");
    }

    [TestMethod]
    public void OutOfRangeValues_AreClamped()
    {
        File.WriteAllText(FilePath, """{ "activationDelayMs": 99999, "dismissDelayMs": -5 }""");

        var (settings, _) = new SettingsStore(FilePath).Load();

        Assert.AreEqual(AppSettings.MaxActivationDelayMs, settings.ActivationDelayMs);
        Assert.AreEqual(AppSettings.MinDismissDelayMs, settings.DismissDelayMs);
    }

    [TestMethod]
    public void OneBadValue_ResetsOnlyThatValue()
    {
        File.WriteAllText(FilePath, """{ "edge": "Top", "hotkey": "Ctrl+Nope", "activationDelayMs": 500 }""");

        var (settings, outcome) = new SettingsStore(FilePath).Load();

        Assert.AreEqual(SettingsLoadOutcome.Loaded, outcome);
        Assert.AreEqual(ScreenEdge.Left, settings.Edge);
        Assert.AreEqual(Hotkey.Default, settings.Hotkey);
        Assert.AreEqual(500, settings.ActivationDelayMs);
    }

    [TestMethod]
    public void CorruptJson_IsBackedUp_AndDefaultsUsed()
    {
        File.WriteAllText(FilePath, "{ this is not json");

        var (settings, outcome) = new SettingsStore(FilePath).Load();

        Assert.AreEqual(SettingsLoadOutcome.RecoveredFromCorruption, outcome);
        Assert.AreEqual(AppSettings.Default, settings);
        Assert.AreEqual("{ this is not json", File.ReadAllText(FilePath + ".bak"));
    }

    [TestMethod]
    public void NewerSchema_LoadsKnownFields_IgnoringUnknownOnes()
    {
        File.WriteAllText(FilePath, """
            {
              "schemaVersion": 7,
              "edge": "Right",
              "someFutureOption": { "nested": true },
              "dismissDelayMs": 800
            }
            """);

        var (settings, outcome) = new SettingsStore(FilePath).Load();

        Assert.AreEqual(SettingsLoadOutcome.Loaded, outcome);
        Assert.AreEqual(ScreenEdge.Right, settings.Edge);
        Assert.AreEqual(800, settings.DismissDelayMs);
    }

    [TestMethod]
    public void CommentsAndTrailingCommas_AreTolerated_ForHandEditedFiles()
    {
        File.WriteAllText(FilePath, """
            {
              // user note
              "edge": "Right",
            }
            """);

        var (settings, outcome) = new SettingsStore(FilePath).Load();

        Assert.AreEqual(SettingsLoadOutcome.Loaded, outcome);
        Assert.AreEqual(ScreenEdge.Right, settings.Edge);
    }

    [TestMethod]
    public void Save_CreatesMissingDirectory()
    {
        string nested = Path.Combine(_dir, "a", "b", "settings.json");

        new SettingsStore(nested).Save(AppSettings.Default);

        Assert.IsTrue(File.Exists(nested));
    }
}
