using SystemHotkeys.Hotkeys;
using NUnit.Framework;

namespace SystemHotkeys.Tests.Hotkeys;

[TestFixture]
public sealed class VirtualKeysTests
{
    [TestCase(0x41, "A")]
    [TestCase(0x5A, "Z")]
    [TestCase(0x30, "0")]
    [TestCase(0x39, "9")]
    [TestCase(0x70, "F1")]
    [TestCase(0x87, "F24")]
    [TestCase(0x20, "Space")]
    [TestCase(0x1B, "Escape")]
    [TestCase(0x26, "ArrowUp")]
    [TestCase(0x60, "Numpad0")]
    [TestCase(0x69, "Numpad9")]
    [TestCase(0x6A, "NumpadMultiply")]
    [TestCase(0x6B, "NumpadAdd")]
    [TestCase(0x6D, "NumpadSubtract")]
    [TestCase(0x6E, "NumpadDecimal")]
    [TestCase(0x6F, "NumpadDivide")]
    [TestCase(0xBD, "-")]
    [TestCase(0xB3, "MediaPlayPause")]
    [TestCase(0x14, "CapsLock")]
    [TestCase(0x5D, "ContextMenu")]
    [TestCase(0xA6, "BrowserBack")]
    [TestCase(0xB6, "LaunchApplication1")]
    [TestCase(0xE2, "<")]
    public void Known_virtual_keys_map_to_the_editors_key_token(int vkCode, string expected)
    {
        Assert.That(VirtualKeys.NameOf(vkCode), Is.EqualTo(expected));
    }

    [Test]
    public void An_unmapped_virtual_key_has_no_name()
    {
        Assert.That(VirtualKeys.NameOf(0x07), Is.Null);
    }

    [TestCase(0xA0)] // left shift
    [TestCase(0xA1)] // right shift
    [TestCase(0xA2)] // left control
    [TestCase(0xA3)] // right control
    [TestCase(0xA4)] // left alt
    [TestCase(0xA5)] // right alt
    [TestCase(0x5B)] // left win
    [TestCase(0x5C)] // right win
    public void Every_modifier_key_reports_itself_as_a_modifier(int vkCode)
    {
        Assert.That(VirtualKeys.IsModifier(vkCode), Is.True);
    }

    [Test]
    public void An_ordinary_key_does_not_report_itself_as_a_modifier()
    {
        Assert.That(VirtualKeys.IsModifier(0x41), Is.False);
    }
}
