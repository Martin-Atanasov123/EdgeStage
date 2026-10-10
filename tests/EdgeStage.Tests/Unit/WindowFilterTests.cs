using EdgeStage.Core.WindowTracking;
using static EdgeStage.Core.Interop.NativeConstants;

namespace EdgeStage.Tests.Unit;

[TestClass]
public sealed class WindowFilterTests
{
    private const uint Self = 1000;
    private const long WS_CAPTION_AND_SYSMENU = 0x00CF0000L; // WS_OVERLAPPEDWINDOW

    private static WindowFacts App(
        string className = "Notepad",
        uint pid = 42,
        long style = WS_CAPTION_AND_SYSMENU,
        long exStyle = 0,
        bool visible = true,
        bool cloaked = false,
        nint owner = 0,
        bool rootOwnerVisible = false,
        int width = 800,
        int height = 600) =>
        new(0x1234, pid, className, style, exStyle, visible, cloaked, owner, rootOwnerVisible, width, height);

    [TestMethod]
    public void OrdinaryAppWindow_IsIncluded()
    {
        Assert.AreEqual(FilterVerdict.Included, WindowFilter.Evaluate(App(), Self));
    }

    [TestMethod]
    public void EmptyTitle_IsNotAFactor()
    {
        // WindowFacts carries no title at all: the filter is purely structural by design.
        Assert.IsTrue(WindowFilter.IsSwitchable(App(className: "SomeUntitledAppClass"), Self));
    }

    [TestMethod]
    public void InvisibleWindow_IsExcluded() =>
        Assert.AreEqual(FilterVerdict.Invisible, WindowFilter.Evaluate(App(visible: false), Self));

    [TestMethod]
    public void CloakedWindow_IsExcluded() =>
        Assert.AreEqual(FilterVerdict.Cloaked, WindowFilter.Evaluate(App(cloaked: true), Self));

    [TestMethod]
    public void ChildWindow_IsExcluded() =>
        Assert.AreEqual(FilterVerdict.ChildWindow, WindowFilter.Evaluate(App(style: WS_CHILD), Self));

    [TestMethod]
    public void OwnProcess_IsExcluded() =>
        Assert.AreEqual(FilterVerdict.OwnProcess, WindowFilter.Evaluate(App(pid: Self), Self));

    [TestMethod]
    [DataRow("Progman")]
    [DataRow("WorkerW")]
    [DataRow("Shell_TrayWnd")]
    [DataRow("Shell_SecondaryTrayWnd")]
    [DataRow("Windows.UI.Core.CoreWindow")]
    public void ShellSurfaces_AreExcluded(string className) =>
        Assert.AreEqual(FilterVerdict.ShellWindow, WindowFilter.Evaluate(App(className: className), Self));

    [TestMethod]
    public void ToolWindow_IsExcluded() =>
        Assert.AreEqual(FilterVerdict.ToolWindow, WindowFilter.Evaluate(App(exStyle: WS_EX_TOOLWINDOW), Self));

    [TestMethod]
    public void ToolWindowWithAppWindowStyle_IsIncluded() =>
        Assert.AreEqual(FilterVerdict.Included, WindowFilter.Evaluate(App(exStyle: WS_EX_TOOLWINDOW | WS_EX_APPWINDOW), Self));

    [TestMethod]
    public void NoActivateWindow_IsExcluded() =>
        Assert.AreEqual(FilterVerdict.NoActivate, WindowFilter.Evaluate(App(exStyle: WS_EX_NOACTIVATE), Self));

    [TestMethod]
    public void DialogOwnedByVisibleWindow_IsExcluded() =>
        Assert.AreEqual(FilterVerdict.OwnedWindow, WindowFilter.Evaluate(App(owner: 0x99, rootOwnerVisible: true), Self));

    [TestMethod]
    public void MainWindowOwnedByHiddenWindow_IsIncluded() =>
        // e.g. Delphi/VCL apps own their main form by an invisible application window.
        Assert.AreEqual(FilterVerdict.Included, WindowFilter.Evaluate(App(owner: 0x99, rootOwnerVisible: false), Self));

    [TestMethod]
    public void OwnedWindowWithAppWindowStyle_IsIncluded() =>
        Assert.AreEqual(FilterVerdict.Included, WindowFilter.Evaluate(App(owner: 0x99, rootOwnerVisible: true, exStyle: WS_EX_APPWINDOW), Self));

    [TestMethod]
    [DataRow(0, 600)]
    [DataRow(800, 0)]
    public void ZeroSizeWindow_IsExcluded(int w, int h) =>
        Assert.AreEqual(FilterVerdict.ZeroSize, WindowFilter.Evaluate(App(width: w, height: h), Self));

    [TestMethod]
    public void UwpFrameWindow_IsIncludedWhenNotCloaked() =>
        Assert.AreEqual(FilterVerdict.Included, WindowFilter.Evaluate(App(className: "ApplicationFrameWindow"), Self));

    [TestMethod]
    public void SuspendedUwpFrameWindow_IsExcludedBecauseCloaked() =>
        Assert.AreEqual(FilterVerdict.Cloaked, WindowFilter.Evaluate(App(className: "ApplicationFrameWindow", cloaked: true), Self));
}
