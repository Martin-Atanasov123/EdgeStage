using System.Globalization;
using EdgeStage.Core.Input;
using EdgeStage.Core.Layout;

namespace EdgeStage.Core.Settings;

/// <summary>Which displays Exposé appears on and which windows each shows.</summary>
public enum ExposeMonitors
{
    /// <summary>On every display at once, each showing the windows on that display (like macOS).</summary>
    EveryDisplay,
    /// <summary>Only on the display under the pointer, with that display's windows.</summary>
    ThisDisplay,
    /// <summary>Only on the display under the pointer, with the windows of every display.</summary>
    AllOnOneDisplay,
}

public enum ThemeMode
{
    Dark,
    Light,
    /// <summary>Follows the Windows app mode (light or dark).</summary>
    System,
}

/// <summary>
/// User settings. Immutable; change with <c>with</c> expressions. "Start with Windows" is not
/// stored here: the registry Run key is its single source of truth (see StartupRegistration).
/// Every value is validated by <see cref="Normalized"/>, which loading and saving always apply.
/// </summary>
public sealed record AppSettings
{
    /// <summary>1 = EdgeStage 1.x (flat file); 2 = V2 (sections: sidebar, expose, appearance).</summary>
    public const int CurrentSchemaVersion = 2;

    public const int MinActivationDelayMs = 0;
    public const int MaxActivationDelayMs = 2000;
    public const int MinDismissDelayMs = 0;
    public const int MaxDismissDelayMs = 3000;
    public const int MaxExcludedApps = 200;
    public const double MinCardSpacing = 0;
    public const double MaxCardSpacing = 24;
    public const double MinCornerRadius = 0;
    public const double MaxCornerRadius = 20;
    public const int MinAnimationPercent = 50;
    public const int MaxAnimationPercent = 200;

    public static AppSettings Default { get; } = new();

    // ---- sidebar ----------------------------------------------------------------------------------

    public ScreenEdge Edge { get; init; } = ScreenEdge.Left;
    public bool EdgeActivationEnabled { get; init; } = true;
    public int ActivationDelayMs { get; init; } = 300;
    public int DismissDelayMs { get; init; } = 400;
    public Hotkey Hotkey { get; init; } = Hotkey.Default;

    /// <summary>
    /// Alt+Tab-style use of the shortcut: while its modifier keys stay held, pressing the key again
    /// moves the selection, and releasing the modifiers switches to the selected window.
    /// </summary>
    public bool HoldToSwitch { get; init; }

    /// <summary>List only windows that are on the monitor the sidebar opens on.</summary>
    public bool OnlyPointerMonitor { get; init; }

    /// <summary>Let the screen edge open the sidebar even while a full-screen app is in front (off: games and videos are never interrupted).</summary>
    public bool EdgeInFullscreen { get; init; }

    /// <summary>Type-to-search in the sidebar.</summary>
    public bool SidebarSearch { get; init; } = true;

    /// <summary>Sidebar order: false = most recently used, true = grouped by app (collapsible).</summary>
    public bool SidebarGrouping { get; init; }

    // ---- Exposé -----------------------------------------------------------------------------------

    public Hotkey ExposeHotkey { get; init; } = Hotkey.ExposeDefault;

    /// <summary>Exposé on every display (default), on this display only, or every window on one display.</summary>
    public ExposeMonitors ExposeMonitors { get; init; } = ExposeMonitors.EveryDisplay;

    public ExposeDensity ExposeDensity { get; init; } = ExposeDensity.Auto;

    /// <summary>Group Exposé's windows by app by default.</summary>
    public bool ExposeGrouping { get; init; }

    public bool ExposeShowIcons { get; init; } = true;
    public bool ExposeShowTitles { get; init; } = true;
    public bool ExposeSearch { get; init; } = true;

    // ---- appearance -------------------------------------------------------------------------------

    public ThemeMode Theme { get; init; } = ThemeMode.Dark;

    /// <summary>"System", a curated name (see <see cref="Accents"/>) or a custom "#RRGGBB".</summary>
    public string Accent { get; init; } = Accents.System;

    public LayoutDensity Layout { get; init; } = LayoutDensity.Balanced;
    public CardSize CardSize { get; init; } = CardSize.Standard;

    /// <summary>Gap between cards, DIPs.</summary>
    public double CardSpacing { get; init; } = 6;

    /// <summary>Sidebar width, DIPs.</summary>
    public double SidebarWidth { get; init; } = CardMetrics.Default.SidebarWidth;

    /// <summary>Card corner radius, DIPs (0 = square).</summary>
    public double CornerRadius { get; init; } = 10;

    public bool Animations { get; init; } = true;

    /// <summary>Animation duration relative to the default (100 %); larger is slower.</summary>
    public int AnimationPercent { get; init; } = 100;

    /// <summary>Replace motion (slides, zoom, flights) with short fades.</summary>
    public bool ReducedMotion { get; init; }

    // ---- shared -----------------------------------------------------------------------------------

    /// <summary>Process names (no path, no ".exe", lower case) whose windows are never listed, in either view.</summary>
    public AppNameList ExcludedApps { get; init; } = AppNameList.Empty;

    /// <summary>Set after the one-time "EdgeStage is running" tip has been shown.</summary>
    public bool FirstRunTipShown { get; init; }

    /// <summary>Canonical form of a process name for the exclusion list; empty when unusable.</summary>
    public static string NormalizeAppName(string? name)
    {
        string n = (name ?? "").Trim().Trim('"');
        if (n.Contains('\\') || n.Contains('/'))
            n = Path.GetFileName(n);
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            n = n[..^4];
        return n.Trim().ToLowerInvariant();
    }

    public bool IsExcluded(string? processName)
    {
        string n = NormalizeAppName(processName);
        return n.Length > 0 && ExcludedApps.Contains(n, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Card geometry for the sidebar from the appearance settings.</summary>
    public CardMetrics CardMetrics => new(
        SidebarWidth,
        CardMetrics.PaddingFor(Layout),
        CardSpacing,
        ListPadding: Layout switch { LayoutDensity.Compact => 6, LayoutDensity.Spacious => 12, _ => 8 },
        CornerRadius);

    /// <summary>The settings a layout preset stands for (spacing follows the preset; everything else is kept).</summary>
    public AppSettings WithLayout(LayoutDensity layout) => this with
    {
        Layout = layout,
        CardSpacing = CardMetrics.SpacingFor(layout),
    };

    /// <summary>Whether motion is wanted at all (setting on and reduced motion off).</summary>
    public bool UseMotion => Animations && !ReducedMotion;

    /// <summary>Multiplier for animation speed: 0.5 = twice as long (200 %), 2 = half as long (50 %).</summary>
    public double AnimationSpeed => 100.0 / AnimationPercent;

    /// <summary>Returns a copy with every value forced into its valid range.</summary>
    public AppSettings Normalized() => this with
    {
        ActivationDelayMs = Math.Clamp(ActivationDelayMs, MinActivationDelayMs, MaxActivationDelayMs),
        DismissDelayMs = Math.Clamp(DismissDelayMs, MinDismissDelayMs, MaxDismissDelayMs),
        Edge = Enum.IsDefined(Edge) ? Edge : ScreenEdge.Left,
        Hotkey = Hotkey.IsValid ? Hotkey : Hotkey.Default,
        ExposeHotkey = ExposeHotkey.IsValid ? ExposeHotkey : Hotkey.ExposeDefault,
        ExposeDensity = Enum.IsDefined(ExposeDensity) ? ExposeDensity : ExposeDensity.Auto,
        ExposeMonitors = Enum.IsDefined(ExposeMonitors) ? ExposeMonitors : ExposeMonitors.EveryDisplay,
        Theme = Enum.IsDefined(Theme) ? Theme : ThemeMode.Dark,
        Accent = Accents.Normalize(Accent),
        Layout = Enum.IsDefined(Layout) ? Layout : LayoutDensity.Balanced,
        CardSize = Enum.IsDefined(CardSize) ? CardSize : CardSize.Standard,
        CardSpacing = ClampFinite(CardSpacing, MinCardSpacing, MaxCardSpacing, 6),
        SidebarWidth = ClampFinite(SidebarWidth, CardMetrics.MinSidebarWidth, CardMetrics.MaxSidebarWidth, CardMetrics.Default.SidebarWidth),
        CornerRadius = ClampFinite(CornerRadius, MinCornerRadius, MaxCornerRadius, 10),
        AnimationPercent = Math.Clamp(AnimationPercent, MinAnimationPercent, MaxAnimationPercent),
        ExcludedApps = AppNameList.From((ExcludedApps ?? AppNameList.Empty)
            .Select(NormalizeAppName)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxExcludedApps)),
    };

    private static double ClampFinite(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Round(Math.Clamp(value, min, max), 1) : fallback;
}

/// <summary>Curated accent colors (plus the Windows accent and custom "#RRGGBB" values).</summary>
public static class Accents
{
    public const string System = "System";

    /// <summary>Name → color, in the order shown in settings.</summary>
    public static IReadOnlyList<(string Name, string Hex)> Curated { get; } =
    [
        ("Blue", "#0A84FF"),
        ("Purple", "#BF5AF2"),
        ("Pink", "#FF375F"),
        ("Red", "#FF453A"),
        ("Orange", "#FF9F0A"),
        ("Yellow", "#FFD60A"),
        ("Green", "#30D158"),
        ("Teal", "#40C8E0"),
        ("Graphite", "#8E8E93"),
    ];

    /// <summary>"System", a curated name (canonical casing) or "#RRGGBB" (upper case); anything else becomes "System".</summary>
    public static string Normalize(string? accent)
    {
        string a = (accent ?? "").Trim();
        if (a.Equals(System, StringComparison.OrdinalIgnoreCase) || a.Length == 0)
            return System;
        foreach (var (name, _) in Curated)
            if (a.Equals(name, StringComparison.OrdinalIgnoreCase))
                return name;
        return TryParseHex(a, out _) ? a.ToUpperInvariant() : System;
    }

    /// <summary>The RGB value of a curated name or "#RRGGBB"; false for "System" (resolved by the app).</summary>
    public static bool TryResolve(string? accent, out uint rgb)
    {
        string a = Normalize(accent);
        foreach (var (name, hex) in Curated)
            if (a == name)
                return TryParseHex(hex, out rgb);
        return TryParseHex(a, out rgb);
    }

    public static bool TryParseHex(string? text, out uint rgb)
    {
        rgb = 0;
        return text is { Length: 7 } && text[0] == '#'
               && uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb);
    }
}
