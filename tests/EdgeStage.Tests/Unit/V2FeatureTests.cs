using EdgeStage.Core.Settings;
using EdgeStage.Core.WindowTracking;
using EdgeStage.UI;

namespace EdgeStage.Tests.Unit;

[TestClass]
public sealed class ExcludedAppsTests
{
    [TestMethod]
    [DataRow("Spotify.exe", "spotify")]
    [DataRow("  SPOTIFY  ", "spotify")]
    [DataRow(@"C:\Program Files\App\Notes App.EXE", "notes app")]
    [DataRow("\"discord.exe\"", "discord")]
    [DataRow("", "")]
    [DataRow(null, "")]
    public void NormalizeAppName(string? input, string expected) =>
        Assert.AreEqual(expected, AppSettings.NormalizeAppName(input));

    [TestMethod]
    public void IsExcluded_IsCaseInsensitive_AndIgnoresExtension()
    {
        var s = (AppSettings.Default with { ExcludedApps = ["spotify", "discord"] }).Normalized();

        Assert.IsTrue(s.IsExcluded("Spotify"));
        Assert.IsTrue(s.IsExcluded("DISCORD.exe"));
        Assert.IsFalse(s.IsExcluded("notepad"));
        Assert.IsFalse(s.IsExcluded(""));
        Assert.IsFalse(s.IsExcluded(null));
    }

    [TestMethod]
    public void Normalized_DedupesCleansAndCaps()
    {
        var many = Enumerable.Range(0, 500).Select(i => $"app{i}").ToList();
        many.AddRange(["APP1", " app2.exe ", "", "   "]);

        var s = (AppSettings.Default with { ExcludedApps = many }).Normalized();

        Assert.AreEqual(AppSettings.MaxExcludedApps, s.ExcludedApps.Count);
        Assert.AreEqual(s.ExcludedApps.Count, s.ExcludedApps.Distinct().Count());
        Assert.IsTrue(s.ExcludedApps.All(a => a == a.ToLowerInvariant() && a.Length > 0));
    }

    [TestMethod]
    public void SettingsEquality_ComparesListContents()
    {
        var a = AppSettings.Default with { ExcludedApps = ["x", "y"] };
        var b = AppSettings.Default with { ExcludedApps = ["X", "y"] };
        var c = AppSettings.Default with { ExcludedApps = ["x"] };

        Assert.AreEqual(a, b);
        Assert.AreNotEqual(a, c);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    [TestMethod]
    public void Store_RoundTripsV2Settings()
    {
        string dir = Path.Combine(Path.GetTempPath(), "EdgeStageTests", Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(Path.Combine(dir, "settings.json"));
        var custom = AppSettings.Default with
        {
            HoldToSwitch = true,
            OnlyPointerMonitor = true,
            ExcludedApps = ["spotify", "steam"],
        };

        store.Save(custom);
        var (loaded, outcome) = store.Load();

        Assert.AreEqual(SettingsLoadOutcome.Loaded, outcome);
        Assert.AreEqual(custom, loaded);
        StringAssert.Contains(File.ReadAllText(store.FilePath), "\"excludedApps\"");
        Directory.Delete(dir, recursive: true);
    }

    [TestMethod]
    public void OldSettingsFile_WithoutV2Fields_LoadsWithDefaults()
    {
        string dir = Path.Combine(Path.GetTempPath(), "EdgeStageTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, """{ "schemaVersion": 1, "edge": "Right", "activationDelayMs": 450 }""");

        var (s, _) = new SettingsStore(path).Load();

        Assert.IsFalse(s.HoldToSwitch);
        Assert.IsFalse(s.OnlyPointerMonitor);
        Assert.AreEqual(0, s.ExcludedApps.Count);
        Assert.AreEqual(450, s.ActivationDelayMs);
        Directory.Delete(dir, recursive: true);
    }
}

[TestClass]
public sealed class TypeToFilterTests
{
    private static WindowInfo Win(nint hwnd, string title, string exe = "app") =>
        new(hwnd, 1, title, "Cls", $@"C:\Apps\{exe}.exe", false);

    private static SidebarViewModel Loaded()
    {
        var vm = new SidebarViewModel();
        vm.Load(
        [
            Win(1, "Inbox - Mail", "outlook"),
            Win(2, "Quarterly report.xlsx", "excel"),
            Win(3, "notes.txt - Notepad", "notepad"),
            Win(4, "Quarterly planning - Chrome", "chrome"),
        ], foreground: 1, initialSelection: 1);
        return vm;
    }

    [TestMethod]
    public void Filter_MatchesTitleOrAppName_CaseInsensitively()
    {
        var vm = Loaded();

        vm.Filter = "QUARTERLY";
        CollectionAssert.AreEqual(new nint[] { 2, 4 }, vm.Cards.Select(c => c.Hwnd).ToList());

        vm.Filter = "notepad";   // app name and title both contain it
        CollectionAssert.AreEqual(new nint[] { 3 }, vm.Cards.Select(c => c.Hwnd).ToList());

        vm.Filter = "excel";     // only the process name matches
        CollectionAssert.AreEqual(new nint[] { 2 }, vm.Cards.Select(c => c.Hwnd).ToList());
    }

    [TestMethod]
    public void ClearingFilter_RestoresAllCardsInOriginalOrder()
    {
        var vm = Loaded();
        vm.Filter = "chrome";
        vm.Filter = "";

        CollectionAssert.AreEqual(new nint[] { 1, 2, 3, 4 }, vm.Cards.Select(c => c.Hwnd).ToList());
        Assert.IsFalse(vm.HasFilter);
    }

    [TestMethod]
    public void Filter_SelectsFirstMatch_OrKeepsSelectionIfStillShown()
    {
        var vm = Loaded();               // card 2 selected
        vm.Filter = "quarterly";         // 2 still matches → stays selected
        Assert.AreEqual((nint)2, vm.Selected!.Hwnd);

        vm.Filter = "chrome";            // 2 gone → first match
        Assert.AreEqual((nint)4, vm.Selected!.Hwnd);
    }

    [TestMethod]
    public void NoMatches_ShowsEmptyState_AndNoSelection()
    {
        var vm = Loaded();
        vm.Filter = "zzz";

        Assert.IsTrue(vm.IsEmpty);
        Assert.AreEqual(-1, vm.SelectedIndex);
        Assert.AreEqual("No matching windows", vm.EmptyMessage);
    }

    [TestMethod]
    public void FilterBar_AddsHeight_OnlyWhileFiltering()
    {
        var vm = Loaded();
        double plain = vm.ContentHeightDip;

        vm.Filter = "quarterly";   // 2 of 4 cards
        double expected = SidebarViewModel.FilterBarHeight + 8 + 2 * 186 + 2 + 2;
        Assert.AreEqual(expected, vm.ContentHeightDip, 1e-9);

        vm.Filter = "";
        Assert.AreEqual(plain, vm.ContentHeightDip, 1e-9);
    }

    [TestMethod]
    public void WindowOpeningWhileFiltered_IsHiddenUnlessItMatches_AndFindable()
    {
        var vm = Loaded();
        vm.Filter = "notepad";

        var other = vm.Append(Win(9, "Calculator", "calc"));
        var match = vm.Append(Win(10, "todo - Notepad", "notepad"));

        CollectionAssert.AreEqual(new nint[] { 3, 10 }, vm.Cards.Select(c => c.Hwnd).ToList());
        Assert.AreSame(other, vm.Find(9), "hidden cards must still be found for window events");
        Assert.AreSame(match, vm.Find(10));
    }

    [TestMethod]
    public void RemovingHiddenCard_DoesNotDisturbSelection()
    {
        var vm = Loaded();
        vm.Filter = "quarterly";
        var selected = vm.Selected;

        vm.Remove(vm.Find(3)!);

        Assert.AreSame(selected, vm.Selected);
        Assert.IsNull(vm.Find(3));
    }

    [TestMethod]
    public void CycleSelection_WrapsAroundBothEnds()
    {
        var vm = Loaded();
        vm.SelectedIndex = 3;
        vm.CycleSelection(1);
        Assert.AreEqual(0, vm.SelectedIndex);

        vm.CycleSelection(-1);
        Assert.AreEqual(3, vm.SelectedIndex);
    }
}
