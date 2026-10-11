using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using EdgeStage.Core.Settings;
using EdgeStage.UI;
using ThemeMode = EdgeStage.Core.Settings.ThemeMode;

namespace EdgeStage.Tests.Unit;

[TestClass]
public sealed class ThemeTests
{
    private static Color BrushColor(ResourceDictionary r, string key) => ((SolidColorBrush)r[key]).Color;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EdgeStage.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    [TestMethod]
    public void DarkTheme_KeepsTheV1Colors()
    {
        var r = new ResourceDictionary();
        ThemeManager.Apply(r, AppSettings.Default with { Accent = "Blue" });

        Assert.IsTrue(ThemeManager.IsDark);
        Assert.AreEqual(Color.FromRgb(0x27, 0x27, 0x2A), BrushColor(r, "CardBackgroundBrush"));
        Assert.AreEqual(Color.FromArgb(0xEB, 0x1B, 0x1B, 0x1D), BrushColor(r, "SidebarBackgroundBrush"));
        Assert.AreEqual(ThemeManager.CardBackground, BrushColor(r, "CardBackgroundBrush"), "corner mask uses the card color");
    }

    [TestMethod]
    public void LightTheme_UsesLightSurfacesAndDarkText()
    {
        var r = new ResourceDictionary();
        ThemeManager.Apply(r, AppSettings.Default with { Theme = ThemeMode.Light, Accent = "Blue" });

        Assert.IsFalse(ThemeManager.IsDark);
        Assert.AreEqual(Colors.White, BrushColor(r, "CardBackgroundBrush"));
        Color text = BrushColor(r, "PrimaryTextBrush");
        Assert.IsTrue(text.R < 0x40 && text.G < 0x40 && text.B < 0x40, "dark text on light surfaces");

        ThemeManager.Apply(r, AppSettings.Default); // leave the shared state dark for other tests
    }

    [TestMethod]
    public void CuratedAndCustomAccents_ColorTheAccentResources()
    {
        var r = new ResourceDictionary();

        ThemeManager.Apply(r, AppSettings.Default with { Accent = "Green" });
        Assert.AreEqual(Color.FromRgb(0x30, 0xD1, 0x58), BrushColor(r, "AccentBrush"));
        Color active = BrushColor(r, "CardBorderActiveBrush");
        Assert.AreEqual((0x30, 0xD1, 0x58), (active.R, active.G, active.B));
        Assert.IsTrue(active.A < 0xFF, "the selection ring is a translucent accent");

        ThemeManager.Apply(r, AppSettings.Default with { Accent = "#123456" });
        Assert.AreEqual(Color.FromRgb(0x12, 0x34, 0x56), BrushColor(r, "AccentBrush"));
        Assert.AreEqual(Colors.White, BrushColor(r, "OnAccentBrush"), "light text on a dark accent");

        ThemeManager.Apply(r, AppSettings.Default with { Accent = "Yellow" });
        Assert.AreNotEqual(Colors.White, BrushColor(r, "OnAccentBrush"), "dark text on a light accent");
    }

    [TestMethod]
    public void BothThemes_DefineEveryBrushTheViewsUse()
    {
        string ui = Path.Combine(RepoRoot(), "src", "EdgeStage");
        var used = Directory.EnumerateFiles(ui, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"\{DynamicResource (\w+Brush)\}").Select(m => m.Groups[1].Value))
            .ToHashSet();
        Assert.IsTrue(used.Count > 15, $"found {used.Count} brush keys");

        foreach (ThemeMode mode in new[] { ThemeMode.Dark, ThemeMode.Light })
        {
            var r = new ResourceDictionary();
            ThemeManager.Apply(r, AppSettings.Default with { Theme = mode });
            var missing = used.Where(k => !r.Contains(k)).ToList();
            Assert.AreEqual(0, missing.Count, $"{mode} theme lacks: {string.Join(", ", missing)}");
        }
        ThemeManager.Apply(new ResourceDictionary(), AppSettings.Default);
    }

    [TestMethod]
    public void ResetToDefaults_KeepsHiddenAppsAndOneTimeFlags_ResetsEverythingElse()
    {
        var custom = AppSettings.Default with
        {
            Theme = ThemeMode.Light,
            Hotkey = new EdgeStage.Core.Input.Hotkey(EdgeStage.Core.Input.HotkeyModifiers.Alt, 0xC0),
            SidebarWidth = 360,
            ExposeGrouping = true,
            ExcludedApps = ["steam"],
            FirstRunTipShown = true,
        };

        AppSettings reset = SettingsWindow.ResetToDefaults(custom);

        Assert.AreEqual(AppSettings.Default with { ExcludedApps = ["steam"], FirstRunTipShown = true }, reset);
    }
}
