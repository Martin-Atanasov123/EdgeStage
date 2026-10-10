using EdgeStage.Core.WindowTracking;

namespace EdgeStage.UI;

/// <summary>
/// Automated-verification hooks. In DEBUG builds only, the environment variable
/// <c>EDGESTAGE_TEST_ONLY_PROCESS</c> limits the sidebar to windows of one process name, so
/// screenshots taken by tests contain only synthetic test windows and never the user's own
/// windows. Release builds compile this to a no-op.
/// </summary>
internal static class TestHooks
{
#if DEBUG
    private static readonly string? OnlyProcess = Environment.GetEnvironmentVariable("EDGESTAGE_TEST_ONLY_PROCESS");
#endif

    public static bool IsListed(WindowInfo window)
    {
#if DEBUG
        return string.IsNullOrEmpty(OnlyProcess)
            || string.Equals(window.ProcessName, OnlyProcess, StringComparison.OrdinalIgnoreCase);
#else
        return true;
#endif
    }
}
