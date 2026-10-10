using Microsoft.Win32;

namespace EdgeStage.Core.Settings;

/// <summary>Per-user registry access needed for "Start with Windows" (abstracted for tests).</summary>
public interface IStartupRegistry
{
    string? GetRunValue(string name);
    void SetRunValue(string name, string value);
    void DeleteRunValue(string name);

    /// <summary>Raw StartupApproved\Run value written by Task Manager's Startup page, or null.</summary>
    byte[]? GetApprovedValue(string name);
    void DeleteApprovedValue(string name);
}

public enum StartupState
{
    Disabled,
    Enabled,
    /// <summary>Registered, but pointing at a different EdgeStage executable (moved or another copy).</summary>
    EnabledElsewhere,
    /// <summary>Registered, but switched off by the user in Task Manager → Startup apps.</summary>
    DisabledInTaskManager,
}

/// <summary>
/// Opt-in "Start with Windows" through HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// Per-user, no administrator rights, fully reversible.
/// </summary>
public sealed class StartupRegistration
{
    public const string ValueName = "EdgeStage";
    public const string AutostartArgument = "--autostart";

    private readonly IStartupRegistry _registry;
    private readonly string _executablePath;

    public StartupRegistration(IStartupRegistry registry, string executablePath)
    {
        _registry = registry;
        _executablePath = executablePath;
    }

    public static StartupRegistration ForCurrentUser() =>
        new(new WindowsStartupRegistry(), Environment.ProcessPath ?? throw new InvalidOperationException("Unknown process path."));

    public string CommandLine => $"\"{_executablePath}\" {AutostartArgument}";

    public StartupState GetState()
    {
        string? value = _registry.GetRunValue(ValueName);
        if (string.IsNullOrWhiteSpace(value))
            return StartupState.Disabled;

        // Task Manager marks disabled entries with an odd first byte (0x03/0x01), enabled with 0x02.
        byte[]? approved = _registry.GetApprovedValue(ValueName);
        if (approved is { Length: > 0 } && (approved[0] & 1) == 1)
            return StartupState.DisabledInTaskManager;

        return string.Equals(ExtractPath(value), _executablePath, StringComparison.OrdinalIgnoreCase)
            ? StartupState.Enabled
            : StartupState.EnabledElsewhere;
    }

    public bool IsEnabled => GetState() == StartupState.Enabled;

    /// <summary>Registers this executable. Explicitly re-enabling also clears a Task Manager "disabled" mark.</summary>
    public void Enable()
    {
        _registry.SetRunValue(ValueName, CommandLine);
        byte[]? approved = _registry.GetApprovedValue(ValueName);
        if (approved is { Length: > 0 } && (approved[0] & 1) == 1)
            _registry.DeleteApprovedValue(ValueName);
    }

    public void Disable() => _registry.DeleteRunValue(ValueName);

    internal static string ExtractPath(string commandLine)
    {
        string trimmed = commandLine.Trim();
        if (trimmed.StartsWith('"'))
        {
            int end = trimmed.IndexOf('"', 1);
            return end > 1 ? trimmed[1..end] : trimmed.Trim('"');
        }
        int space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed[..space] : trimmed;
    }
}

/// <summary>The real HKCU implementation. Key paths are injectable so tests can use a scratch key.</summary>
public sealed class WindowsStartupRegistry : IStartupRegistry
{
    public const string DefaultRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string DefaultApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private readonly string RunKey;
    private readonly string ApprovedKey;

    public WindowsStartupRegistry(string runKey = DefaultRunKey, string approvedKey = DefaultApprovedKey)
    {
        RunKey = runKey;
        ApprovedKey = approvedKey;
    }

    public string? GetRunValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(name) as string;
    }

    public void SetRunValue(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void DeleteRunValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public byte[]? GetApprovedValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        return key?.GetValue(name) as byte[];
    }

    public void DeleteApprovedValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
