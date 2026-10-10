using System.Runtime.InteropServices;
using EdgeStage.Core.Interop;

namespace EdgeStage.Tests.Unit;

[TestClass]
public sealed class IcoAndInteropLayoutTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EdgeStage.slnx")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "repository root not found");
        return Path.Combine([dir.FullName, .. parts]);
    }

    [TestMethod]
    public void AppIcon_ContainsAllExpectedSizes()
    {
        byte[] ico = File.ReadAllBytes(RepoFile("src", "EdgeStage", "Assets", "EdgeStage.ico"));

        var sizes = IcoReader.ReadEntries(ico).Select(e => e.Size).Order().ToList();

        CollectionAssert.AreEqual(new[] { 16, 20, 24, 32, 40, 48, 64, 256 }, sizes);
    }

    [TestMethod]
    [DataRow(16, 16)]
    [DataRow(18, 20)]   // 112 % → next size up, scaled down
    [DataRow(24, 24)]
    [DataRow(32, 32)]
    [DataRow(300, 256)] // beyond the largest → largest
    public void SelectBest_PrefersExactOrNextLarger(int wanted, int expected)
    {
        var entries = new[] { 16, 20, 24, 32, 40, 48, 64, 256 }.Select(s => new IcoReader.Entry(s, 100, 10)).ToList();
        Assert.AreEqual(expected, IcoReader.SelectBest(entries, wanted)!.Value.Size);
    }

    [TestMethod]
    public void ReadEntries_RejectsNonIconData()
    {
        Assert.AreEqual(0, IcoReader.ReadEntries("not an icon"u8).Count);
        Assert.AreEqual(0, IcoReader.ReadEntries([]).Count);
    }

    [TestMethod]
    public void CreateIcon_ProducesAnHicon()
    {
        byte[] ico = File.ReadAllBytes(RepoFile("src", "EdgeStage", "Assets", "EdgeStage.ico"));
        nint icon = IcoReader.CreateIcon(ico, 24);
        Assert.AreNotEqual(0, icon);
        Assert.IsTrue(User32.DestroyIcon(icon));
    }

    [TestMethod]
    public unsafe void NativeStructSizes_MatchWindowsSdk()
    {
        // Values for 64-bit Windows (sizeof in C).
        Assert.IsTrue(Environment.Is64BitProcess, "tests run as x64");
        Assert.AreEqual(976, sizeof(NOTIFYICONDATAW));
        Assert.AreEqual(48, Marshal.SizeOf<DWM_THUMBNAIL_PROPERTIES>());
        Assert.AreEqual(40, Marshal.SizeOf<MONITORINFO>());
        Assert.AreEqual(48, Marshal.SizeOf<MSG>());
        Assert.AreEqual(32, Marshal.SizeOf<MSLLHOOKSTRUCT>());
    }
}
