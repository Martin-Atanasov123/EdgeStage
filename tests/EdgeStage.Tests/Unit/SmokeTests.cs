using EdgeStage.Core;

namespace EdgeStage.Tests.Unit;

[TestClass]
public sealed class SmokeTests
{
    [TestMethod]
    public void CoreAssemblyLoads_AndExposesPerUserPaths()
    {
        StringAssert.EndsWith(AppInfo.SettingsDirectory, "EdgeStage");
        StringAssert.EndsWith(AppInfo.LogDirectory, Path.Combine("EdgeStage", "logs"));
    }
}
