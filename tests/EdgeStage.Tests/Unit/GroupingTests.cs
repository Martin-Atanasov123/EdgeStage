using EdgeStage.Core.WindowTracking;
using EdgeStage.UI;

namespace EdgeStage.Tests.Unit;

/// <summary>Optional grouping by app in the shared window list (sidebar and Exposé).</summary>
[TestClass]
public sealed class GroupingTests
{
    private static WindowInfo Win(nint hwnd, string title, string exe) =>
        new(hwnd, 1, title, "Cls", $@"C:\Apps\{exe}.exe", false);

    /// <summary>MRU order: chrome, word, chrome, notepad, word.</summary>
    private static WindowListViewModel Loaded(bool grouping = true)
    {
        var vm = new WindowListViewModel { Grouping = grouping };
        vm.Load(
        [
            Win(1, "Docs - Chrome", "chrome"),
            Win(2, "Report.docx - Word", "winword"),
            Win(3, "Mail - Chrome", "chrome"),
            Win(4, "todo.txt - Notepad", "notepad"),
            Win(5, "Letter.docx - Word", "winword"),
        ], foreground: 1, initialSelection: 0);
        return vm;
    }

    private static List<string> Shape(WindowListViewModel vm) => vm.Items.Select(i => i switch
    {
        GroupHeaderViewModel h => $"[{h.AppName}:{h.Count}{(h.IsCollapsed ? " collapsed" : "")}]",
        CardViewModel c => c.Hwnd.ToString(),
        _ => "?",
    }).ToList();

    [TestMethod]
    public void Groups_FollowMostRecentUse_AndKeepMruInside()
    {
        var vm = Loaded();

        CollectionAssert.AreEqual(
            new[] { "[chrome:2]", "1", "3", "[winword:2]", "2", "5", "[notepad:1]", "4" },
            Shape(vm));
        CollectionAssert.AreEqual(new nint[] { 1, 3, 2, 5, 4 }, vm.Cards.Select(c => c.Hwnd).ToList(),
            "selectable cards follow the display order");
        CollectionAssert.AreEqual(new[] { 2, 2, 1 }, vm.VisibleGroupSizes().ToList());
        Assert.AreEqual(3, vm.GroupCount);
    }

    [TestMethod]
    public void Ungrouped_IsPlainMru()
    {
        var vm = Loaded(grouping: false);

        CollectionAssert.AreEqual(new[] { "1", "2", "3", "4", "5" }, Shape(vm));
        Assert.AreEqual(0, vm.GroupCount);
        CollectionAssert.AreEqual(new[] { 5 }, vm.VisibleGroupSizes().ToList());
    }

    [TestMethod]
    public void CollapsingAGroup_HidesItsCards_ButNotItsHeader()
    {
        var vm = Loaded();
        var word = (GroupHeaderViewModel)vm.Items.First(i => i is GroupHeaderViewModel { AppName: "winword" });

        vm.ToggleGroup(word);

        CollectionAssert.AreEqual(
            new[] { "[chrome:2]", "1", "3", "[winword:2 collapsed]", "[notepad:1]", "4" },
            Shape(vm));
        CollectionAssert.AreEqual(new[] { 2, 0, 1 }, vm.VisibleGroupSizes().ToList());

        vm.ToggleGroup(word);
        Assert.AreEqual(5, vm.Cards.Count);
    }

    [TestMethod]
    public void Search_ShowsMatchesEvenInsideCollapsedGroups_AndDropsEmptyGroups()
    {
        var vm = Loaded();
        vm.ToggleGroup((GroupHeaderViewModel)vm.Items.First(i => i is GroupHeaderViewModel { AppName: "winword" }));

        vm.Filter = "docx";

        CollectionAssert.AreEqual(new[] { "[winword:2 collapsed]", "2", "5" }, Shape(vm));
        Assert.AreEqual(2, vm.Selected?.Hwnd ?? 0, "the first match is selected");
    }

    [TestMethod]
    public void Selection_SurvivesTurningGroupingOnAndOff()
    {
        var vm = Loaded(grouping: false);
        vm.SelectedIndex = 3; // hwnd 4 (notepad)

        vm.Grouping = true;
        Assert.AreEqual(4, vm.Selected?.Hwnd ?? 0);

        vm.Grouping = false;
        Assert.AreEqual(4, vm.Selected?.Hwnd ?? 0);
    }

    [TestMethod]
    public void NewWindow_JoinsItsAppGroup_ClosedLastWindow_RemovesTheGroup()
    {
        var vm = Loaded();

        vm.Append(Win(6, "Inbox - Chrome", "chrome"));
        CollectionAssert.AreEqual(
            new[] { "[chrome:3]", "1", "3", "6", "[winword:2]", "2", "5", "[notepad:1]", "4" },
            Shape(vm));

        vm.Remove(vm.Find(4)!);
        Assert.IsFalse(Shape(vm).Any(s => s.StartsWith("[notepad", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ArrowSelection_MovesThroughCards_SkippingHeaders()
    {
        var vm = Loaded();
        vm.SelectedIndex = 1; // hwnd 3, last chrome card

        vm.MoveSelection(1);

        Assert.AreEqual(2, vm.Selected?.Hwnd ?? 0, "next card is the first Word window, not the Word header");
    }
}
