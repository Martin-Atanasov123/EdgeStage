namespace EdgeStage.Core;

/// <summary>Product-wide constants and well-known per-user locations.</summary>
public static class AppInfo
{
    public const string ProductName = "EdgeStage";

    /// <summary>"major.minor.patch" of the running application.</summary>
    public static string Version =>
        (System.Reflection.Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly).GetName().Version?.ToString(3) ?? "?";

    /// <summary>Roaming settings folder: %APPDATA%\EdgeStage.</summary>
    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ProductName);

    /// <summary>Local log folder: %LOCALAPPDATA%\EdgeStage\logs.</summary>
    public static string LogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductName, "logs");
}
