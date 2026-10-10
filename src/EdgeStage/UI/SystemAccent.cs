using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace EdgeStage.UI;

/// <summary>Uses the user's Windows accent color (the light variant Windows itself uses on dark surfaces).</summary>
internal static class SystemAccent
{
    public static Color? Read()
    {
        try
        {
            // AccentPalette: 8 RGBA entries, light-to-dark; index 3 is the accent, index 1 its "Light 2" variant.
            using var accent = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            if (accent?.GetValue("AccentPalette") is byte[] { Length: >= 8 } palette)
                return Color.FromRgb(palette[4], palette[5], palette[6]);
        }
        catch (System.Security.SecurityException)
        {
        }
        return null;
    }

    /// <summary>Overrides the theme's accent resources before any window is created.</summary>
    public static void Apply(ResourceDictionary resources)
    {
        if (Read() is not Color color)
            return;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        resources["AccentColor"] = color;
        resources["AccentBrush"] = brush;
    }
}
