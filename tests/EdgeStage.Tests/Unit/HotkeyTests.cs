using EdgeStage.Core.Input;

namespace EdgeStage.Tests.Unit;

[TestClass]
public sealed class HotkeyTests
{
    [TestMethod]
    public void Default_IsCtrlAltSpace()
    {
        Assert.AreEqual("Ctrl+Alt+Space", Hotkey.Default.ToString());
        Assert.IsTrue(Hotkey.Default.IsValid);
    }

    [TestMethod]
    [DataRow("Ctrl+Alt+Space")]
    [DataRow("Alt+`")]
    [DataRow("Alt+Win+W")]
    [DataRow("Ctrl+Shift+F12")]
    [DataRow("Ctrl+Alt+Num5")]
    [DataRow("Ctrl+Alt+PageDown")]
    [DataRow("Ctrl+Alt+Shift+Win+Z")]
    [DataRow("F13")]
    public void CanonicalText_RoundTrips(string text)
    {
        Assert.IsTrue(Hotkey.TryParse(text, out var hotkey), text);
        Assert.AreEqual(text, hotkey.ToString());
    }

    [TestMethod]
    [DataRow("alt+ctrl+space", "Ctrl+Alt+Space")]
    [DataRow(" control + alt + SPACE ", "Ctrl+Alt+Space")]
    [DataRow("Alt+Backtick", "Alt+`")]
    [DataRow("Windows+Shift+Del", "Shift+Win+Delete")]
    [DataRow("Ctrl++", "Ctrl+=")]
    public void Parse_IsLenient_AndNormalizes(string input, string expected)
    {
        Assert.IsTrue(Hotkey.TryParse(input, out var hotkey), input);
        Assert.AreEqual(expected, hotkey.ToString());
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("Space")]          // bare key would steal normal typing
    [DataRow("Ctrl+Alt")]       // no key
    [DataRow("Ctrl+Alt+Q+W")]   // two keys
    [DataRow("Ctrl+Hyper+Q")]   // unknown token
    [DataRow("Ctrl+Alt+Shift")] // modifiers only
    public void Invalid_IsRejected(string? text)
    {
        Assert.IsFalse(Hotkey.TryParse(text, out _));
    }

    [TestMethod]
    public void ModifierFlags_MatchRegisterHotKeyConstants()
    {
        Assert.AreEqual(0x1, (int)HotkeyModifiers.Alt);
        Assert.AreEqual(0x2, (int)HotkeyModifiers.Control);
        Assert.AreEqual(0x4, (int)HotkeyModifiers.Shift);
        Assert.AreEqual(0x8, (int)HotkeyModifiers.Win);
    }

    [TestMethod]
    public void ModifierKeyAsMainKey_IsInvalid()
    {
        Assert.IsFalse(new Hotkey(HotkeyModifiers.Control, 0x12 /* VK_MENU */).IsValid);
    }
}
