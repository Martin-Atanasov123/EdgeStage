using EdgeStage.Core.Input;

namespace EdgeStage.Core.Settings;

/// <summary>
/// User settings. Immutable; change with <c>with</c> expressions. "Start with Windows" is not
/// stored here: the registry Run key is its single source of truth (see StartupRegistration).
/// </summary>
public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public const int MinActivationDelayMs = 0;
    public const int MaxActivationDelayMs = 2000;
    public const int MinDismissDelayMs = 0;
    public const int MaxDismissDelayMs = 3000;
    public const int MaxExcludedApps = 200;

    public static AppSettings Default { get; } = new();

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

    /// <summary>Process names (no path, no ".exe", lower case) whose windows are never listed.</summary>
    public IReadOnlyList<string> ExcludedApps { get; init; } = [];

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

    /// <summary>Returns a copy with every value forced into its valid range.</summary>
    public AppSettings Normalized() => this with
    {
        ActivationDelayMs = Math.Clamp(ActivationDelayMs, MinActivationDelayMs, MaxActivationDelayMs),
        DismissDelayMs = Math.Clamp(DismissDelayMs, MinDismissDelayMs, MaxDismissDelayMs),
        Edge = Enum.IsDefined(Edge) ? Edge : ScreenEdge.Left,
        Hotkey = Hotkey.IsValid ? Hotkey : Hotkey.Default,
        ExcludedApps = (ExcludedApps ?? [])
            .Select(NormalizeAppName)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxExcludedApps)
            .ToArray(),
    };

    // Records compare collections by reference; settings must compare by content.
    public bool Equals(AppSettings? other) =>
        other is not null
        && Edge == other.Edge
        && EdgeActivationEnabled == other.EdgeActivationEnabled
        && ActivationDelayMs == other.ActivationDelayMs
        && DismissDelayMs == other.DismissDelayMs
        && Hotkey == other.Hotkey
        && HoldToSwitch == other.HoldToSwitch
        && OnlyPointerMonitor == other.OnlyPointerMonitor
        && FirstRunTipShown == other.FirstRunTipShown
        && ExcludedApps.SequenceEqual(other.ExcludedApps, StringComparer.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        HashCode.Combine(Edge, EdgeActivationEnabled, ActivationDelayMs, DismissDelayMs, Hotkey, HoldToSwitch, ExcludedApps.Count);

    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append($"Edge = {Edge}, EdgeActivationEnabled = {EdgeActivationEnabled}, ActivationDelayMs = {ActivationDelayMs}, " +
                       $"DismissDelayMs = {DismissDelayMs}, Hotkey = {Hotkey}, HoldToSwitch = {HoldToSwitch}, " +
                       $"OnlyPointerMonitor = {OnlyPointerMonitor}, ExcludedApps = [{string.Join(", ", ExcludedApps)}]");
        return true;
    }
}
