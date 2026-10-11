using System.Windows.Media;
using Microsoft.Win32;

namespace EdgeStage.UI;

/// <summary>Reads the user's Windows accent color.</summary>
internal static class SystemAccent
{
    /// <summary>Entries of Windows' AccentPalette (8 RGBA colors, light to dark; 3 is the accent itself).</summary>
    public enum Shade
    {
        Light3 = 0,
        Light2 = 1,
        Light1 = 2,
        Accent = 3,
        Dark1 = 4,
        Dark2 = 5,
    }

    public static Color? Read(Shade shade = Shade.Light2)
    {
        try
        {
            using var accent = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            int i = (int)shade * 4;
            if (accent?.GetValue("AccentPalette") is byte[] palette && palette.Length >= i + 3)
                return Color.FromRgb(palette[i], palette[i + 1], palette[i + 2]);
        }
        catch (System.Security.SecurityException)
        {
        }
        return null;
    }
}
