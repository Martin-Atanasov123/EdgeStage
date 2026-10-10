using EdgeStage.Core.WindowTracking;
using EdgeStage.UI;

namespace EdgeStage.Tests.Unit;

[TestClass]
public sealed class SidebarViewModelTests
{
    private static WindowInfo Win(nint hwnd, string title = "", bool minimized = false) =>
        new(hwnd, 1, title, "Cls", @"C:\Apps\app.exe", minimized);

    private static SidebarViewModel Loaded(int count, nint foreground = 1, int selection = 1)
    {
        var vm = new SidebarViewModel();
        vm.Load(Enumerable.Range(1, count).Select(i => Win(i, $"W{i}")), foreground, selection);
        return vm;
    }

    [TestMethod]
    public void Load_MarksForegroundActive_AndAppliesInitialSelection()
    {
        var vm = Loaded(3, foreground: 1, selection: 1);

        Assert.IsTrue(vm.Cards[0].IsActive);
        Assert.IsFalse(vm.Cards[1].IsActive);
        Assert.AreEqual(1, vm.SelectedIndex);
        Assert.IsTrue(vm.Cards[1].IsSelected);
        Assert.AreEqual(1, vm.Cards.Count(c => c.IsSelected));
    }

    [TestMethod]
    public void SingleWindow_HotkeySelectionClampsToFirstCard()
    {
        var vm = Loaded(1, selection: 1);
        Assert.AreEqual(0, vm.SelectedIndex);
    }

    [TestMethod]
    public void MoveSelection_ClampsAtBothEnds()
    {
        var vm = Loaded(3, selection: 1);

        vm.MoveSelection(5);
        Assert.AreEqual(2, vm.SelectedIndex);
        vm.MoveSelection(-10);
        Assert.AreEqual(0, vm.SelectedIndex);
    }

    [TestMethod]
    public void MoveSelection_FromNoSelection_StartsAtTheEndInThatDirection()
    {
        var vm = Loaded(3, selection: -1);
        vm.MoveSelection(1);
        Assert.AreEqual(0, vm.SelectedIndex);

        vm.SelectedIndex = -1;
        vm.MoveSelection(-1);
        Assert.AreEqual(2, vm.SelectedIndex);
    }

    [TestMethod]
    public void RemovingAnotherCard_KeepsTheSameCardSelected()
    {
        var vm = Loaded(4, selection: 2);
        CardViewModel selected = vm.Selected!;

        vm.Remove(vm.Cards[0]);

        Assert.AreSame(selected, vm.Selected);
        Assert.AreEqual(1, vm.SelectedIndex);
    }

    [TestMethod]
    public void RemovingTheSelectedCard_SelectsItsNeighbor()
    {
        var vm = Loaded(3, selection: 2);

        vm.Remove(vm.Cards[2]);

        Assert.AreEqual(1, vm.SelectedIndex);
        Assert.AreEqual((nint)2, vm.Selected!.Hwnd);
    }

    [TestMethod]
    public void RemovingLastCard_LeavesEmptyState()
    {
        var vm = Loaded(1, selection: 0);

        vm.Remove(vm.Cards[0]);

        Assert.IsTrue(vm.IsEmpty);
        Assert.AreEqual(-1, vm.SelectedIndex);
        Assert.IsNull(vm.Selected);
    }

    [TestMethod]
    public void Append_AddsAtTheEnd_WithoutReorderingExistingCards()
    {
        var vm = Loaded(2);

        vm.Append(Win(99, "new"));

        CollectionAssert.AreEqual(new nint[] { 1, 2, 99 }, vm.Cards.Select(c => c.Hwnd).ToList());
    }

    [TestMethod]
    public void ContentHeight_MatchesCardPitchPlusPadding()
    {
        // 8 top padding + n × (180 card + 6 spacing) + 2 bottom (8 − 6) + 2 border
        Assert.AreEqual(8 + 4 * 186 + 2 + 2, Loaded(4).ContentHeightDip, 1e-9);
        Assert.AreEqual(SidebarViewModel.EmptyHeight, new SidebarViewModel().ContentHeightDip, 1e-9);
    }

    [TestMethod]
    public void SetActive_MovesTheActiveMarker()
    {
        var vm = Loaded(3, foreground: 1);

        vm.SetActive(3);

        Assert.IsFalse(vm.Cards[0].IsActive);
        Assert.IsTrue(vm.Cards[2].IsActive);
    }

    [TestMethod]
    public void UntitledWindow_FallsBackToProcessName()
    {
        var vm = new SidebarViewModel();
        vm.Load([Win(5, title: "")], foreground: 0, initialSelection: -1);

        Assert.AreEqual("app", vm.Cards[0].Title);
    }

    [TestMethod]
    public void MinimizedState_IsCarriedIntoTheCard()
    {
        var vm = new SidebarViewModel();
        vm.Load([Win(5, "x", minimized: true)], 0, -1);
        Assert.IsTrue(vm.Cards[0].IsMinimized);
    }
}
