using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using EdgeStage.Core.Diagnostics;
using EdgeStage.Core.Input;

namespace EdgeStage.Core.Settings;

public enum SettingsLoadOutcome
{
    /// <summary>No file yet: defaults are used.</summary>
    Defaults,
    Loaded,
    /// <summary>The file was unreadable JSON; it was moved aside as .bak and defaults are used.</summary>
    RecoveredFromCorruption,
}

/// <summary>
/// The on-disk shape. Every field is optional and loosely typed so that one bad or unknown value
/// only resets that value, never the whole file, and files written by newer versions still load.
/// </summary>
internal sealed class SettingsDocument
{
    public int? SchemaVersion { get; set; }
    public string? Edge { get; set; }
    public bool? EdgeActivationEnabled { get; set; }
    public int? ActivationDelayMs { get; set; }
    public int? DismissDelayMs { get; set; }
    public string? Hotkey { get; set; }
    public bool? FirstRunTipShown { get; set; }
}

[JsonSerializable(typeof(SettingsDocument))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
    /// <summary>
    /// Readable, hand-editable output: indented, camelCase, comments and trailing commas accepted.
    /// Relaxed escaping keeps "Ctrl+Alt+Space" literal; the file is never embedded in HTML.
    /// </summary>
    public static SettingsJsonContext Readable { get; } = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}

/// <summary>Loads and saves <see cref="AppSettings"/> as human-readable JSON.</summary>
public sealed class SettingsStore
{
    public SettingsStore(string filePath)
    {
        FilePath = filePath;
    }

    public static SettingsStore ForCurrentUser() => new(Path.Combine(AppInfo.SettingsDirectory, "settings.json"));

    public string FilePath { get; }

    public (AppSettings Settings, SettingsLoadOutcome Outcome) Load()
    {
        if (!File.Exists(FilePath))
            return (AppSettings.Default, SettingsLoadOutcome.Defaults);

        SettingsDocument? doc;
        try
        {
            using var stream = File.OpenRead(FilePath);
            doc = JsonSerializer.Deserialize(stream, SettingsJsonContext.Readable.SettingsDocument);
        }
        catch (JsonException ex)
        {
            string backup = FilePath + ".bak";
            try
            {
                File.Copy(FilePath, backup, overwrite: true);
            }
            catch (IOException copyError)
            {
                Log.Warn($"Could not back up corrupt settings: {copyError.Message}");
            }
            Log.Warn($"Settings file is not valid JSON ({ex.Message}); saved a copy as {backup} and using defaults.");
            return (AppSettings.Default, SettingsLoadOutcome.RecoveredFromCorruption);
        }

        if (doc is null)
            return (AppSettings.Default, SettingsLoadOutcome.RecoveredFromCorruption);

        if (doc.SchemaVersion > AppSettings.CurrentSchemaVersion)
            Log.Info($"Settings were written by a newer EdgeStage (schema {doc.SchemaVersion}); loading the known fields.");

        return (FromDocument(doc), SettingsLoadOutcome.Loaded);
    }

    /// <summary>Writes atomically: a crash mid-save leaves either the old or the new file, never a torn one.</summary>
    public void Save(AppSettings settings)
    {
        settings = settings.Normalized();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temp = FilePath + ".tmp";
        var doc = new SettingsDocument
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            Edge = settings.Edge.ToString(),
            EdgeActivationEnabled = settings.EdgeActivationEnabled,
            ActivationDelayMs = settings.ActivationDelayMs,
            DismissDelayMs = settings.DismissDelayMs,
            Hotkey = settings.Hotkey.ToString(),
            FirstRunTipShown = settings.FirstRunTipShown,
        };
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, doc, SettingsJsonContext.Readable.SettingsDocument);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temp, FilePath, overwrite: true);
    }

    internal static AppSettings FromDocument(SettingsDocument doc)
    {
        var d = AppSettings.Default;
        var settings = new AppSettings
        {
            Edge = Enum.TryParse(doc.Edge, ignoreCase: true, out ScreenEdge edge) && Enum.IsDefined(edge) ? edge : d.Edge,
            EdgeActivationEnabled = doc.EdgeActivationEnabled ?? d.EdgeActivationEnabled,
            ActivationDelayMs = doc.ActivationDelayMs ?? d.ActivationDelayMs,
            DismissDelayMs = doc.DismissDelayMs ?? d.DismissDelayMs,
            Hotkey = Hotkey.TryParse(doc.Hotkey, out Hotkey hotkey) ? hotkey : d.Hotkey,
            FirstRunTipShown = doc.FirstRunTipShown ?? false,
        };
        return settings.Normalized();
    }
}
