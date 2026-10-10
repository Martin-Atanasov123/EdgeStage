using System.Text;

namespace EdgeStage.Core.Input;

/// <summary>Modifier flags; numeric values match RegisterHotKey's MOD_* constants.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>A global keyboard shortcut: modifiers plus one Win32 virtual-key code. Text form: "Ctrl+Alt+Space".</summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, int VirtualKey)
{
    public const int VK_SPACE = 0x20;

    public static Hotkey Default => new(HotkeyModifiers.Control | HotkeyModifiers.Alt, VK_SPACE);

    private static readonly Dictionary<int, string> Names = BuildNames();
    private static readonly Dictionary<string, int> Codes = BuildCodes();

    /// <summary>
    /// A usable global shortcut has a real (non-modifier) key and at least one modifier; bare keys
    /// would steal ordinary typing. F13–F24 are allowed alone because no keyboard types with them.
    /// </summary>
    public bool IsValid =>
        Names.ContainsKey(VirtualKey)
        && !IsModifierKey(VirtualKey)
        && (Modifiers != HotkeyModifiers.None || VirtualKey is >= 0x7C and <= 0x87);

    public override string ToString() =>
        FormatModifiers(Modifiers) + (Names.TryGetValue(VirtualKey, out string? name) ? name : $"0x{VirtualKey:X2}");

    /// <summary>"Ctrl+Alt+" style prefix in canonical order (Ctrl, Alt, Shift, Win).</summary>
    public static string FormatModifiers(HotkeyModifiers modifiers)
    {
        var sb = new StringBuilder();
        if (modifiers.HasFlag(HotkeyModifiers.Control)) sb.Append("Ctrl+");
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) sb.Append("Alt+");
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) sb.Append("Shift+");
        if (modifiers.HasFlag(HotkeyModifiers.Win)) sb.Append("Win+");
        return sb.ToString();
    }

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Split on '+', but allow the key itself to be "+" (e.g. "Ctrl++").
        string trimmed = text.Trim();
        var parts = new List<string>();
        int start = 0;
        for (int i = 0; i < trimmed.Length; i++)
        {
            if (trimmed[i] == '+' && i > start)
            {
                parts.Add(trimmed[start..i].Trim());
                start = i + 1;
            }
        }
        parts.Add(trimmed[start..].Trim());

        var modifiers = HotkeyModifiers.None;
        int? key = null;
        foreach (string part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= HotkeyModifiers.Control; continue;
                case "alt": modifiers |= HotkeyModifiers.Alt; continue;
                case "shift": modifiers |= HotkeyModifiers.Shift; continue;
                case "win" or "windows" or "meta": modifiers |= HotkeyModifiers.Win; continue;
            }
            if (key is not null || !Codes.TryGetValue(part.ToLowerInvariant(), out int code))
                return false;
            key = code;
        }

        if (key is null)
            return false;
        hotkey = new Hotkey(modifiers, key.Value);
        return hotkey.IsValid;
    }

    public static bool IsModifierKey(int vk) =>
        vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    /// <summary>Display name for a virtual key, or null when EdgeStage does not support it as a hotkey key.</summary>
    public static string? KeyName(int vk) => Names.TryGetValue(vk, out string? name) ? name : null;

    private static Dictionary<int, string> BuildNames()
    {
        var names = new Dictionary<int, string>
        {
            [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x13] = "Pause",
            [0x20] = "Space", [0x21] = "PageUp", [0x22] = "PageDown", [0x23] = "End", [0x24] = "Home",
            [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down",
            [0x2C] = "PrintScreen", [0x2D] = "Insert", [0x2E] = "Delete",
            [0x6A] = "NumMultiply", [0x6B] = "NumAdd", [0x6D] = "NumSubtract", [0x6E] = "NumDecimal", [0x6F] = "NumDivide",
            [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/",
            [0xC0] = "`", [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'",
        };
        for (int c = 'A'; c <= 'Z'; c++) names[c] = ((char)c).ToString();
        for (int d = 0; d <= 9; d++) names['0' + d] = d.ToString();
        for (int f = 1; f <= 24; f++) names[0x70 + f - 1] = $"F{f}";
        for (int n = 0; n <= 9; n++) names[0x60 + n] = $"Num{n}";
        return names;
    }

    private static Dictionary<string, int> BuildCodes()
    {
        var codes = Names.ToDictionary(kv => kv.Value.ToLowerInvariant(), kv => kv.Key);
        // Friendly aliases accepted when parsing hand-edited settings.
        codes["backtick"] = 0xC0;
        codes["grave"] = 0xC0;
        codes["esc"] = 0x1B;
        codes["return"] = 0x0D;
        codes["pgup"] = 0x21;
        codes["pgdn"] = 0x22;
        codes["del"] = 0x2E;
        codes["ins"] = 0x2D;
        codes["plus"] = 0xBB;
        codes["+"] = 0xBB;
        codes["minus"] = 0xBD;
        return codes;
    }
}
