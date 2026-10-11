using System.Windows;
using System.Windows.Media;
using EdgeStage.Core.Diagnostics;
using EdgeStage.Core.Settings;
using Microsoft.Win32;
using ThemeMode = EdgeStage.Core.Settings.ThemeMode;

namespace EdgeStage.UI;

/// <summary>
/// Applies the color theme (dark, light or following Windows) and the accent color by replacing
/// the brush resources every view looks up with DynamicResource, so open windows recolor at once.
/// </summary>
internal static class ThemeManager
{
    // ARGB values per resource key. Dark keeps EdgeStage 1.x's look exactly.
    private static readonly Dictionary<string, uint> Dark = new()
    {
        ["SidebarBackgroundBrush"] = 0xEB1B1B1D,
        ["SidebarBorderBrush"] = 0x33FFFFFF,
        ["CardBackgroundBrush"] = 0xFF27272A,
        ["CardHoverBrush"] = 0xFF323236,
        ["CardBorderBrush"] = 0x14FFFFFF,
        ["CardBorderHoverBrush"] = 0xB3FFFFFF,
        ["FilterBarBrush"] = 0x26FFFFFF,
        ["ExposeDimBrush"] = 0xB30A0A0C,
        ["GroupHeaderBrush"] = 0x14FFFFFF,
        ["ThumbPlaceholderBrush"] = 0xFF161618,
        ["PrimaryTextBrush"] = 0xF2FFFFFF,
        ["SecondaryTextBrush"] = 0x99FFFFFF,
        ["WarningTextBrush"] = 0xFFFFB86B,
        ["BadgeBackgroundBrush"] = 0x1FFFFFFF,
        ["SurfaceBrush"] = 0xFF202023,
        ["PopupBackgroundBrush"] = 0xFF2B2B2F,
        ["PopupBorderBrush"] = 0x26FFFFFF,
        ["HighlightBrush"] = 0x1AFFFFFF,
        ["ControlBrush"] = 0xFF35353A,
        ["ControlBorderBrush"] = 0x33FFFFFF,
        ["ToggleOffBorderBrush"] = 0x99FFFFFF,
        ["ToggleKnobBrush"] = 0xCCFFFFFF,
        ["ToggleKnobOnBrush"] = 0xFF1B1B1D,
        ["ButtonHoverBrush"] = 0xFF404046,
        ["ButtonPressedBrush"] = 0x33FFFFFF,
        ["SliderTrackBrush"] = 0x4DFFFFFF,
        ["SliderThumbBrush"] = 0xFF45454A,
        ["SliderThumbBorderBrush"] = 0x40FFFFFF,
        ["ScrollThumbBrush"] = 0x55FFFFFF,
        ["NavSelectedBrush"] = 0x1FFFFFFF,
    };

    private static readonly Dictionary<string, uint> Light = new()
    {
        ["SidebarBackgroundBrush"] = 0xEBF3F3F5,
        ["SidebarBorderBrush"] = 0x1F000000,
        ["CardBackgroundBrush"] = 0xFFFFFFFF,
        ["CardHoverBrush"] = 0xFFEDEDF0,
        ["CardBorderBrush"] = 0x14000000,
        ["CardBorderHoverBrush"] = 0x73000000,
        ["FilterBarBrush"] = 0x12000000,
        ["ExposeDimBrush"] = 0xB8EEEEF1,
        ["GroupHeaderBrush"] = 0x0F000000,
        ["ThumbPlaceholderBrush"] = 0xFFE4E4E8,
        ["PrimaryTextBrush"] = 0xE6000000,
        ["SecondaryTextBrush"] = 0x8C000000,
        ["WarningTextBrush"] = 0xFFB45309,
        ["BadgeBackgroundBrush"] = 0x14000000,
        ["SurfaceBrush"] = 0xFFF3F3F5,
        ["PopupBackgroundBrush"] = 0xFFFFFFFF,
        ["PopupBorderBrush"] = 0x1F000000,
        ["HighlightBrush"] = 0x0F000000,
        ["ControlBrush"] = 0xFFFFFFFF,
        ["ControlBorderBrush"] = 0x29000000,
        ["ToggleOffBorderBrush"] = 0x8C000000,
        ["ToggleKnobBrush"] = 0xA6000000,
        ["ToggleKnobOnBrush"] = 0xFFFFFFFF,
        ["ButtonHoverBrush"] = 0xFFE9E9EC,
        ["ButtonPressedBrush"] = 0x1F000000,
        ["SliderTrackBrush"] = 0x33000000,
        ["SliderThumbBrush"] = 0xFFFFFFFF,
        ["SliderThumbBorderBrush"] = 0x40000000,
        ["ScrollThumbBrush"] = 0x45000000,
        ["NavSelectedBrush"] = 0x14000000,
    };

    /// <summary>EdgeStage 1.x accent (used when the Windows accent cannot be read).</summary>
    private static readonly Color FallbackAccentDark = Color.FromRgb(0x60, 0xCD, 0xFF);
    private static readonly Color FallbackAccentLight = Color.FromRgb(0x00, 0x5F, 0xB8);

    private static ResourceDictionary? s_resources;
    private static AppSettings s_settings = AppSettings.Default;
    private static bool s_listening;

    public static bool IsDark { get; private set; } = true;
    public static Color CardBackground { get; private set; } = Color.FromRgb(0x27, 0x27, 0x2A);
    public static Color CardHover { get; private set; } = Color.FromRgb(0x32, 0x32, 0x36);
    public static Color Accent { get; private set; } = FallbackAccentDark;

    /// <summary>Raised after the theme or accent changed (views refresh colors drawn in code).</summary>
    public static event Action? Changed;

    /// <summary>Applies the theme for <paramref name="settings"/> to <paramref name="resources"/> (the application's).</summary>
    public static void Apply(ResourceDictionary resources, AppSettings settings)
    {
        s_resources = resources;
        s_settings = settings;
        bool dark = settings.Theme switch
        {
            ThemeMode.Light => false,
            ThemeMode.System => !WindowsUsesLightTheme(),
            _ => true,
        };
        Color accent = ResolveAccent(settings.Accent, dark);
        Write(resources, dark ? Dark : Light, accent, dark);

        bool changed = dark != IsDark || accent != Accent;
        IsDark = dark;
        Accent = accent;
        CardBackground = ToColor((dark ? Dark : Light)["CardBackgroundBrush"]);
        CardHover = ToColor((dark ? Dark : Light)["CardHoverBrush"]);
        EnsureListening();
        if (changed)
            Log.Info($"Theme: {(dark ? "dark" : "light")} ({settings.Theme}), accent {accent} ({settings.Accent}).");
        Changed?.Invoke();
    }

    /// <summary>The accent for a setting: curated or custom colors as chosen, "System" = the Windows accent.</summary>
    public static Color ResolveAccent(string accentSetting, bool dark)
    {
        if (Accents.TryResolve(accentSetting, out uint rgb))
            return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        // Windows itself uses a lighter shade of the accent on dark surfaces and a darker one on light.
        return SystemAccent.Read(dark ? SystemAccent.Shade.Light2 : SystemAccent.Shade.Dark1)
               ?? (dark ? FallbackAccentDark : FallbackAccentLight);
    }

    private static void Write(ResourceDictionary resources, Dictionary<string, uint> palette, Color accent, bool dark)
    {
        foreach (var (key, argb) in palette)
            resources[key] = Frozen(ToColor(argb));

        resources["AccentColor"] = accent;
        resources["AccentBrush"] = Frozen(accent);
        resources["CardBorderActiveBrush"] = Frozen(Color.FromArgb(dark ? (byte)0x80 : (byte)0xB0, accent.R, accent.G, accent.B));
        resources["SidebarBackgroundColor"] = ToColor(palette["SidebarBackgroundBrush"]);
        // Text on the accent (selected segments, primary buttons) must stay readable for light accents.
        double luminance = (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255;
        resources["OnAccentBrush"] = Frozen(luminance > 0.6 ? Color.FromRgb(0x10, 0x10, 0x12) : Colors.White);
    }

    private static bool WindowsUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>Follows changes of the Windows app mode and accent while the theme or accent is "System".</summary>
    private static void EnsureListening()
    {
        if (s_listening)
            return;
        s_listening = true;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle))
                return;
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (s_resources is not null && (s_settings.Theme == ThemeMode.System || s_settings.Accent == Accents.System))
                    Apply(s_resources, s_settings);
            });
        };
    }

    private static Color ToColor(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
