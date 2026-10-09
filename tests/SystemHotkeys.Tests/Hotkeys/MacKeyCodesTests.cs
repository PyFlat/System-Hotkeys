using NUnit.Framework;
using SystemHotkeys.Hotkeys;
using SystemHotkeys.Hotkeys.MacOS;

namespace SystemHotkeys.Tests.Hotkeys;

[TestFixture]
public sealed class MacKeyCodesTests
{
    [TestCase(0x00, "A")]
    [TestCase(0x06, "Z")]
    [TestCase(0x10, "Y")]
    [TestCase(0x2E, "M")]
    [TestCase(0x1D, "0")]
    [TestCase(0x12, "1")]
    [TestCase(0x16, "6")]
    [TestCase(0x17, "5")]
    [TestCase(0x7A, "F1")]
    [TestCase(0x63, "F3")]
    [TestCase(0x6F, "F12")]
    [TestCase(0x5A, "F20")]
    [TestCase(0x24, "Enter")]
    [TestCase(0x4C, "Enter")]
    [TestCase(0x33, "Backspace")]
    [TestCase(0x75, "Delete")]
    [TestCase(0x35, "Escape")]
    [TestCase(0x31, "Space")]
    [TestCase(0x7E, "ArrowUp")]
    [TestCase(0x7B, "ArrowLeft")]
    [TestCase(0x52, "Numpad0")]
    [TestCase(0x5C, "Numpad9")]
    [TestCase(0x45, "NumpadAdd")]
    [TestCase(0x4E, "NumpadSubtract")]
    [TestCase(0x43, "NumpadMultiply")]
    [TestCase(0x4B, "NumpadDivide")]
    [TestCase(0x41, "NumpadDecimal")]
    [TestCase(0x1B, "-")]
    [TestCase(0x2A, "\\")]
    [TestCase(0x32, "`")]
    [TestCase(0x48, "AudioVolumeUp")]
    public void Mac_key_codes_name_the_same_key_as_on_windows(int keyCode, string expected)
    {
        var vkCode = MacKeyCodes.ToVirtualKey(keyCode);

        Assert.That(vkCode, Is.Not.Null);
        Assert.That(VirtualKeys.NameOf(vkCode!.Value), Is.EqualTo(expected));
    }

    [Test]
    public void Every_mapped_mac_key_code_has_an_editor_key_name()
    {
        for (var keyCode = 0; keyCode <= 0x7F; keyCode++)
        {
            if (MacKeyCodes.ToVirtualKey(keyCode) is { } vkCode)
            {
                Assert.That(VirtualKeys.NameOf(vkCode), Is.Not.Null, $"kVK 0x{keyCode:X2}");
            }
        }
    }

    [TestCase(0x37)]
    [TestCase(0x38)]
    [TestCase(0x3A)]
    [TestCase(0x3B)]
    [TestCase(0x39)]
    [TestCase(0x3F)]
    public void Modifier_caps_lock_and_fn_key_codes_are_never_a_trailing_key(int keyCode)
    {
        Assert.That(MacKeyCodes.ToVirtualKey(keyCode), Is.Null);
    }

    [Test]
    public void Every_modifier_flag_names_a_distinct_modifier_key()
    {
        Assert.Multiple(() =>
        {
            var virtualKeys = MacKeyCodes.ModifierFlags.Select(m => m.VirtualKey).ToList();

            Assert.That(virtualKeys, Is.All.Matches<int>(VirtualKeys.IsModifier));
            Assert.That(virtualKeys, Is.Unique);
            Assert.That(MacKeyCodes.ModifierFlags.Select(m => m.FlagMask), Is.Unique);
        });
    }

    [Test]
    public void Command_and_option_read_back_as_meta_and_alt()
    {
        var command = MacKeyCodes.ModifierFlags.Single(m => m.FlagMask == 0x08).VirtualKey;
        var option = MacKeyCodes.ModifierFlags.Single(m => m.FlagMask == 0x20).VirtualKey;

        var combo = HotkeyCombo.TryCreate(new HashSet<int> { command, option, 'K' }, 'K');

        Assert.That(combo?.Text, Is.EqualTo("Alt+Meta+K"));
    }
}
