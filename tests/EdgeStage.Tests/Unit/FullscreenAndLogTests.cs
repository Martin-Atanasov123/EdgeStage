using EdgeStage.Core.Diagnostics;
using EdgeStage.Core.Input;
using EdgeStage.Core.Interop;

namespace EdgeStage.Tests.Unit;

[TestClass]
public sealed class FullscreenDetectorTests
{
    private static readonly RECT Monitor = new(0, 0, 1920, 1080);
    private const long WS_POPUP = 0x80000000L;
    private const long WS_OVERLAPPEDWINDOW = 0x00CF0000L;

    [TestMethod]
    public void BorderlessWindowCoveringMonitor_IsFullscreen() =>
        Assert.IsTrue(FullscreenDetector.IsFullscreen(new RECT(0, 0, 1920, 1080), WS_POPUP, Monitor));

    [TestMethod]
    public void MaximizedCaptionedWindow_IsNotFullscreen_EvenWithAutoHideTaskbar() =>
        // Maximized windows extend 8 px past each edge and keep their caption.
        Assert.IsFalse(FullscreenDetector.IsFullscreen(new RECT(-8, -8, 1928, 1088), WS_OVERLAPPEDWINDOW, Monitor));

    [TestMethod]
    public void WindowSmallerThanMonitor_IsNotFullscreen() =>
        Assert.IsFalse(FullscreenDetector.IsFullscreen(new RECT(0, 0, 1920, 1032), WS_POPUP, Monitor));

    [TestMethod]
    public void FullscreenOnSecondaryMonitorWithNegativeCoordinates() =>
        Assert.IsTrue(FullscreenDetector.IsFullscreen(new RECT(-2560, 0, 0, 1440), WS_POPUP, new RECT(-2560, 0, 0, 1440)));
}

[TestClass]
[DoNotParallelize] // Log is process-wide static state
public sealed class LogTests
{
    private string _dir = "";

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "EdgeStageTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Log.Shutdown();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [TestMethod]
    public void Initialize_KeepsOnlyNewestFiles()
    {
        for (int day = 1; day <= 9; day++)
            File.WriteAllText(Path.Combine(_dir, $"edgestage-202601{day:00}.log"), "x");

        Log.Initialize(_dir, retain: 5);
        Log.Info("hello");

        var files = Directory.GetFiles(_dir, "edgestage-*.log").Select(Path.GetFileName).Order().ToList();
        Assert.IsTrue(files.Count <= 5, string.Join(", ", files));
        CollectionAssert.Contains(files, "edgestage-20260109.log");
        CollectionAssert.DoesNotContain(files, "edgestage-20260101.log");
    }

    [TestMethod]
    public void Messages_AreWrittenWithLevel()
    {
        Log.Initialize(_dir);
        Log.Warn("hotkey conflict");
        string file = Log.CurrentFile!;
        Log.Shutdown();

        string text = File.ReadAllText(file);
        StringAssert.Contains(text, "[WARN ]");
        StringAssert.Contains(text, "hotkey conflict");
    }

    [TestMethod]
    public void BeforeInitialize_LoggingIsSafe()
    {
        Log.Shutdown();
        Log.Error("no file yet", new InvalidOperationException("x"));
    }
}
