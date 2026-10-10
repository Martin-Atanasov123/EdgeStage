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

    public static AppSettings Default { get; } = new();

    public ScreenEdge Edge { get; init; } = ScreenEdge.Left;
    public bool EdgeActivationEnabled { get; init; } = true;
    public int ActivationDelayMs { get; init; } = 300;
    public int DismissDelayMs { get; init; } = 400;
    public Hotkey Hotkey { get; init; } = Hotkey.Default;

    /// <summary>Set after the one-time "EdgeStage is running" tip has been shown.</summary>
    public bool FirstRunTipShown { get; init; }

    /// <summary>Returns a copy with every value forced into its valid range.</summary>
    public AppSettings Normalized() => this with
    {
        ActivationDelayMs = Math.Clamp(ActivationDelayMs, MinActivationDelayMs, MaxActivationDelayMs),
        DismissDelayMs = Math.Clamp(DismissDelayMs, MinDismissDelayMs, MaxDismissDelayMs),
        Edge = Enum.IsDefined(Edge) ? Edge : ScreenEdge.Left,
        Hotkey = Hotkey.IsValid ? Hotkey : Hotkey.Default,
    };
}
