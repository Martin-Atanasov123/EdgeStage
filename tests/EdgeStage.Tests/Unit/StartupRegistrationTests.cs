using EdgeStage.Core.Settings;

namespace EdgeStage.Tests.Unit;

[TestClass]
public sealed class StartupRegistrationTests
{
    private sealed class FakeRegistry : IStartupRegistry
    {
        public Dictionary<string, string> Run { get; } = [];
        public Dictionary<string, byte[]> Approved { get; } = [];
        public string? GetRunValue(string name) => Run.GetValueOrDefault(name);
        public void SetRunValue(string name, string value) => Run[name] = value;
        public void DeleteRunValue(string name) => Run.Remove(name);
        public byte[]? GetApprovedValue(string name) => Approved.GetValueOrDefault(name);
        public void DeleteApprovedValue(string name) => Approved.Remove(name);
    }

    private const string Exe = @"C:\Apps\EdgeStage\EdgeStage.exe";

    [TestMethod]
    public void NotRegistered_IsDisabled()
    {
        var reg = new StartupRegistration(new FakeRegistry(), Exe);
        Assert.AreEqual(StartupState.Disabled, reg.GetState());
    }

    [TestMethod]
    public void Enable_WritesQuotedPathWithAutostartFlag()
    {
        var fake = new FakeRegistry();
        var reg = new StartupRegistration(fake, Exe);

        reg.Enable();

        Assert.AreEqual($"\"{Exe}\" --autostart", fake.Run["EdgeStage"]);
        Assert.AreEqual(StartupState.Enabled, reg.GetState());
        Assert.IsTrue(reg.IsEnabled);
    }

    [TestMethod]
    public void Disable_RemovesTheValue_AndIsReversible()
    {
        var fake = new FakeRegistry();
        var reg = new StartupRegistration(fake, Exe);
        reg.Enable();

        reg.Disable();
        Assert.AreEqual(StartupState.Disabled, reg.GetState());

        reg.Enable();
        Assert.AreEqual(StartupState.Enabled, reg.GetState());
    }

    [TestMethod]
    public void RegisteredForAnotherCopy_IsReportedAsElsewhere()
    {
        var fake = new FakeRegistry();
        fake.Run["EdgeStage"] = "\"D:\\Old\\EdgeStage.exe\" --autostart";

        Assert.AreEqual(StartupState.EnabledElsewhere, new StartupRegistration(fake, Exe).GetState());
    }

    [TestMethod]
    public void PathComparison_IgnoresCase()
    {
        var fake = new FakeRegistry();
        fake.Run["EdgeStage"] = $"\"{Exe.ToUpperInvariant()}\" --autostart";

        Assert.AreEqual(StartupState.Enabled, new StartupRegistration(fake, Exe).GetState());
    }

    [TestMethod]
    public void DisabledInTaskManager_IsDetected_AndEnableClearsIt()
    {
        var fake = new FakeRegistry();
        var reg = new StartupRegistration(fake, Exe);
        reg.Enable();
        fake.Approved["EdgeStage"] = [0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        Assert.AreEqual(StartupState.DisabledInTaskManager, reg.GetState());

        reg.Enable();
        Assert.AreEqual(StartupState.Enabled, reg.GetState());
        Assert.IsFalse(fake.Approved.ContainsKey("EdgeStage"));
    }

    [TestMethod]
    public void EnabledMarkInTaskManager_IsRespected()
    {
        var fake = new FakeRegistry();
        var reg = new StartupRegistration(fake, Exe);
        reg.Enable();
        fake.Approved["EdgeStage"] = [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        Assert.AreEqual(StartupState.Enabled, reg.GetState());
    }

    [TestMethod]
    [DataRow("\"C:\\A B\\E.exe\" --autostart", "C:\\A B\\E.exe")]
    [DataRow("C:\\A\\E.exe --autostart", "C:\\A\\E.exe")]
    [DataRow("C:\\A\\E.exe", "C:\\A\\E.exe")]
    public void ExtractPath_HandlesQuotedAndUnquotedCommands(string command, string expected) =>
        Assert.AreEqual(expected, StartupRegistration.ExtractPath(command));
}
