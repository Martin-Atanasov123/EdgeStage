using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using EdgeStage.Core.Diagnostics;
using EdgeStage.Core.Input;
using EdgeStage.Core.Layout;

namespace EdgeStage.Core.Settings;

public enum SettingsLoadOutcome
{
    /// <summary>No file yet: defaults are used.</summary>
    Defaults,
    Loaded,
    /// <summary>An older schema was read and the file rewritten in the current one (the old file is kept as a backup).</summary>
    Migrated,
    /// <summary>The file was unreadable JSON; it was moved aside as .bak and defaults are used.</summary>
    RecoveredFromCorruption,
}

/// <summary>The on-disk shape written by this version (schema 2).</summary>
internal sealed class SettingsDocument
{
    public int SchemaVersion { get; set; }
    public SidebarSection Sidebar { get; set; } = new();
    public ExposeSection Expose { get; set; } = new();
    public AppearanceSection Appearance { get; set; } = new();
    public string[] ExcludedApps { get; set; } = [];
    public bool FirstRunTipShown { get; set; }

    internal sealed class SidebarSection
    {
        public string Hotkey { get; set; } = "";
        public bool HoldToSwitch { get; set; }
        public string Edge { get; set; } = "";
        public bool EdgeActivationEnabled { get; set; }
        public int ActivationDelayMs { get; set; }
        public int DismissDelayMs { get; set; }
        public bool EdgeInFullscreen { get; set; }
        public bool OnlyPointerMonitor { get; set; }
        public bool Search { get; set; }
        public bool Grouping { get; set; }
    }

    internal sealed class ExposeSection
    {
        public string Hotkey { get; set; } = "";
        public bool AllMonitors { get; set; }
        public string Density { get; set; } = "";
        public bool Grouping { get; set; }
        public bool ShowIcons { get; set; }
        public bool ShowTitles { get; set; }
        public bool Search { get; set; }
    }

    internal sealed class AppearanceSection
    {
        public string Theme { get; set; } = "";
        public string Accent { get; set; } = "";
        public string Layout { get; set; } = "";
        public string CardSize { get; set; } = "";
        public double CardSpacing { get; set; }
        public double SidebarWidth { get; set; }
        public double CornerRadius { get; set; }
        public bool Animations { get; set; }
        public int AnimationPercent { get; set; }
        public bool ReducedMotion { get; set; }
    }
}

[JsonSerializable(typeof(SettingsDocument))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
    /// <summary>
    /// Readable, hand-editable output: indented, camelCase. Relaxed escaping keeps "Ctrl+Alt+Space"
    /// literal; the file is never embedded in HTML.
    /// </summary>
    public static SettingsJsonContext Readable { get; } = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as human-readable JSON.
/// </summary>
/// <remarks>
/// Reading is deliberately forgiving: every value is read on its own, so one wrong or mistyped
/// value only resets that value; unknown fields are ignored; comments and trailing commas are
/// accepted. Schema 1 files (EdgeStage 1.x, flat) are read and rewritten as schema 2 (sections),
/// with the original kept next to it as settings.v1.json.
/// </remarks>
public sealed class SettingsStore
{
    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public SettingsStore(string filePath)
    {
        FilePath = filePath;
    }

    public static SettingsStore ForCurrentUser() => new(Path.Combine(AppInfo.SettingsDirectory, "settings.json"));

    public string FilePath { get; }

    /// <summary>Where a schema-1 file is kept after migration.</summary>
    public string V1BackupPath => Path.Combine(Path.GetDirectoryName(FilePath) ?? "", "settings.v1.json");

    public (AppSettings Settings, SettingsLoadOutcome Outcome) Load()
    {
        if (!File.Exists(FilePath))
            return (AppSettings.Default, SettingsLoadOutcome.Defaults);

        string text;
        JsonDocument doc;
        try
        {
            text = File.ReadAllText(FilePath);
            doc = JsonDocument.Parse(text, ReadOptions);
        }
        catch (JsonException ex)
        {
            BackUpCorrupt();
            Log.Warn($"Settings file is not valid JSON ({ex.Message}); saved a copy as {FilePath}.bak and using defaults.");
            return (AppSettings.Default, SettingsLoadOutcome.RecoveredFromCorruption);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                BackUpCorrupt();
                return (AppSettings.Default, SettingsLoadOutcome.RecoveredFromCorruption);
            }

            int schema = Int(doc.RootElement, "schemaVersion") ?? 1;
            AppSettings settings = Read(doc.RootElement);
            if (schema > AppSettings.CurrentSchemaVersion)
                Log.Info($"Settings were written by a newer EdgeStage (schema {schema}); loading the known fields.");
            if (schema >= AppSettings.CurrentSchemaVersion)
                return (settings, SettingsLoadOutcome.Loaded);

            // Older schema: keep the original, then rewrite in the current format.
            try
            {
                File.WriteAllText(V1BackupPath, text);
                Save(settings);
                Log.Info($"Settings migrated from schema {schema} to {AppSettings.CurrentSchemaVersion}; the original is kept as {V1BackupPath}.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Settings could not be rewritten in the new format ({ex.Message}); they are used as read.");
            }
            return (settings, SettingsLoadOutcome.Migrated);
        }
    }

    /// <summary>Writes atomically: a crash mid-save leaves either the old or the new file, never a torn one.</summary>
    public void Save(AppSettings settings)
    {
        settings = settings.Normalized();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temp = FilePath + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, ToDocument(settings), SettingsJsonContext.Readable.SettingsDocument);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temp, FilePath, overwrite: true);
    }

    private void BackUpCorrupt()
    {
        try
        {
            File.Copy(FilePath, FilePath + ".bak", overwrite: true);
        }
        catch (IOException copyError)
        {
            Log.Warn($"Could not back up corrupt settings: {copyError.Message}");
        }
    }

    internal static SettingsDocument ToDocument(AppSettings s) => new()
    {
        SchemaVersion = AppSettings.CurrentSchemaVersion,
        Sidebar = new()
        {
            Hotkey = s.Hotkey.ToString(),
            HoldToSwitch = s.HoldToSwitch,
            Edge = s.Edge.ToString(),
            EdgeActivationEnabled = s.EdgeActivationEnabled,
            ActivationDelayMs = s.ActivationDelayMs,
            DismissDelayMs = s.DismissDelayMs,
            EdgeInFullscreen = s.EdgeInFullscreen,
            OnlyPointerMonitor = s.OnlyPointerMonitor,
            Search = s.SidebarSearch,
            Grouping = s.SidebarGrouping,
        },
        Expose = new()
        {
            Hotkey = s.ExposeHotkey.ToString(),
            AllMonitors = s.ExposeAllMonitors,
            Density = s.ExposeDensity.ToString(),
            Grouping = s.ExposeGrouping,
            ShowIcons = s.ExposeShowIcons,
            ShowTitles = s.ExposeShowTitles,
            Search = s.ExposeSearch,
        },
        Appearance = new()
        {
            Theme = s.Theme.ToString(),
            Accent = s.Accent,
            Layout = s.Layout.ToString(),
            CardSize = s.CardSize.ToString(),
            CardSpacing = s.CardSpacing,
            SidebarWidth = s.SidebarWidth,
            CornerRadius = s.CornerRadius,
            Animations = s.Animations,
            AnimationPercent = s.AnimationPercent,
            ReducedMotion = s.ReducedMotion,
        },
        ExcludedApps = [.. s.ExcludedApps],
        FirstRunTipShown = s.FirstRunTipShown,
    };

    /// <summary>
    /// Reads schema 2 (sections) and schema 1 (flat) alike: each sidebar value is looked up in the
    /// "sidebar" section first and at the top level second, which is where EdgeStage 1.x kept it.
    /// </summary>
    internal static AppSettings Read(JsonElement root)
    {
        var d = AppSettings.Default;
        JsonElement? sidebar = Section(root, "sidebar");
        JsonElement? expose = Section(root, "expose");
        JsonElement? look = Section(root, "appearance");

        T Side<T>(Func<JsonElement, string, T?> read, string name, T fallback) where T : struct =>
            (sidebar is { } s ? read(s, name) : null) ?? read(root, name) ?? fallback;

        var settings = new AppSettings
        {
            Hotkey = ParseHotkey(Str(sidebar, "hotkey") ?? Str(root, "hotkey"), d.Hotkey),
            HoldToSwitch = Side(Bool, "holdToSwitch", d.HoldToSwitch),
            Edge = ParseEnum(Str(sidebar, "edge") ?? Str(root, "edge"), d.Edge),
            EdgeActivationEnabled = Side(Bool, "edgeActivationEnabled", d.EdgeActivationEnabled),
            ActivationDelayMs = Side(Int, "activationDelayMs", d.ActivationDelayMs),
            DismissDelayMs = Side(Int, "dismissDelayMs", d.DismissDelayMs),
            EdgeInFullscreen = Side(Bool, "edgeInFullscreen", d.EdgeInFullscreen),
            OnlyPointerMonitor = Side(Bool, "onlyPointerMonitor", d.OnlyPointerMonitor),
            SidebarSearch = (sidebar is { } sb ? Bool(sb, "search") : null) ?? d.SidebarSearch,
            SidebarGrouping = (sidebar is { } sg ? Bool(sg, "grouping") : null) ?? d.SidebarGrouping,

            ExposeHotkey = ParseHotkey(Str(expose, "hotkey"), d.ExposeHotkey),
            ExposeAllMonitors = Bool(expose, "allMonitors") ?? d.ExposeAllMonitors,
            ExposeDensity = ParseEnum(Str(expose, "density"), d.ExposeDensity),
            ExposeGrouping = Bool(expose, "grouping") ?? d.ExposeGrouping,
            ExposeShowIcons = Bool(expose, "showIcons") ?? d.ExposeShowIcons,
            ExposeShowTitles = Bool(expose, "showTitles") ?? d.ExposeShowTitles,
            ExposeSearch = Bool(expose, "search") ?? d.ExposeSearch,

            Theme = ParseEnum(Str(look, "theme"), d.Theme),
            Accent = Str(look, "accent") ?? d.Accent,
            Layout = ParseEnum(Str(look, "layout"), d.Layout),
            CardSize = ParseEnum(Str(look, "cardSize"), d.CardSize),
            CardSpacing = Num(look, "cardSpacing") ?? d.CardSpacing,
            SidebarWidth = Num(look, "sidebarWidth") ?? d.SidebarWidth,
            CornerRadius = Num(look, "cornerRadius") ?? d.CornerRadius,
            Animations = Bool(look, "animations") ?? d.Animations,
            AnimationPercent = Int(look, "animationPercent") ?? d.AnimationPercent,
            ReducedMotion = Bool(look, "reducedMotion") ?? d.ReducedMotion,

            ExcludedApps = AppNameList.From(Strings(root, "excludedApps")),
            FirstRunTipShown = Bool(root, "firstRunTipShown") ?? false,
        };
        return settings.Normalized();
    }

    // ---- tolerant value readers (property names are matched ignoring case) -----------------------

    private static JsonElement? Property(JsonElement? obj, string name)
    {
        if (obj is not { ValueKind: JsonValueKind.Object } o)
            return null;
        foreach (JsonProperty p in o.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p.Value;
        return null;
    }

    private static JsonElement? Section(JsonElement root, string name) =>
        Property(root, name) is { ValueKind: JsonValueKind.Object } section ? section : null;

    private static string? Str(JsonElement? obj, string name) =>
        Property(obj, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    private static bool? Bool(JsonElement obj, string name) => Bool((JsonElement?)obj, name);

    private static bool? Bool(JsonElement? obj, string name) => Property(obj, name) switch
    {
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.False } => false,
        { ValueKind: JsonValueKind.String } s when bool.TryParse(s.GetString(), out bool b) => b,
        _ => null,
    };

    private static int? Int(JsonElement obj, string name) => Int((JsonElement?)obj, name);

    private static int? Int(JsonElement? obj, string name)
    {
        double? n = Num(obj, name);
        return n is { } v && v >= int.MinValue && v <= int.MaxValue ? (int)Math.Round(v) : null;
    }

    private static double? Num(JsonElement? obj, string name) => Property(obj, name) switch
    {
        { ValueKind: JsonValueKind.Number } n when n.TryGetDouble(out double v) && double.IsFinite(v) => v,
        { ValueKind: JsonValueKind.String } s when double.TryParse(s.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) => v,
        _ => null,
    };

    private static IEnumerable<string> Strings(JsonElement obj, string name) =>
        Property(obj, name) is { ValueKind: JsonValueKind.Array } array
            ? array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString() ?? "").ToList()
            : [];

    private static T ParseEnum<T>(string? text, T fallback) where T : struct, Enum =>
        Enum.TryParse(text, ignoreCase: true, out T value) && Enum.IsDefined(value) && !int.TryParse(text, out _) ? value : fallback;

    private static Hotkey ParseHotkey(string? text, Hotkey fallback) =>
        Hotkey.TryParse(text, out Hotkey hotkey) ? hotkey : fallback;
}
