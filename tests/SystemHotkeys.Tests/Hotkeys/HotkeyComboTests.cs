using MacroDeck.Sdk.Input;
using NUnit.Framework;
using SystemHotkeys.Hotkeys;

namespace SystemHotkeys.Tests.Hotkeys;

[TestFixture]
public sealed class HotkeyComboTests
{
    private static readonly string[] _ctrl = ["Ctrl"];
    private static readonly string[] _allFourModifiersInOrder = ["Ctrl", "Shift", "Alt", "Meta"];

    [Test]
    public void A_modifier_and_key_produce_the_editors_names()
    {
        var combo = HotkeyCombo.Create(KeyModifier.Control, KeyCode.F1);

        Assert.That(combo.Modifiers, Is.EqualTo(_ctrl));
        Assert.That(combo.Key, Is.EqualTo("F1"));
    }

    [Test]
    public void All_four_modifiers_keep_the_editors_record_order()
    {
        var combo = HotkeyCombo.Create(
            KeyModifier.Meta | KeyModifier.Alt | KeyModifier.Shift | KeyModifier.Control,
            KeyCode.A
        );

        Assert.That(combo.Modifiers, Is.EqualTo(_allFourModifiersInOrder));
    }

    [Test]
    public void A_right_hand_modifier_is_named_like_the_left_one()
    {
        var right = HotkeyCombo.Create(KeyModifier.RightControl | KeyModifier.RightMeta, KeyCode.A);
        var left = HotkeyCombo.Create(KeyModifier.Control | KeyModifier.Meta, KeyCode.A);

        Assert.That(right.Text, Is.EqualTo("Ctrl+Meta+A"));
        Assert.That(right.Text, Is.EqualTo(left.Text));
    }

    [Test]
    public void A_key_with_no_modifier_held_still_forms_a_combo()
    {
        var combo = HotkeyCombo.Create(KeyModifier.None, KeyCode.A);

        Assert.That(combo.Modifiers, Is.Empty);
        Assert.That(combo.Text, Is.EqualTo("A"));
    }

    [TestCase(KeyCode.D3, "3")]
    [TestCase(KeyCode.Minus, "Minus")]
    [TestCase(KeyCode.IntlBackslash, "IntlBackslash")]
    [TestCase(KeyCode.NumpadSubtract, "NumpadSubtract")]
    [TestCase(KeyCode.NumpadEnter, "NumpadEnter")]
    public void Keys_use_the_name_the_editor_stores(KeyCode key, string expected)
    {
        Assert.That(HotkeyCombo.Create(KeyModifier.None, key).Key, Is.EqualTo(expected));
    }

    [Test]
    public void A_windows_punctuation_key_is_named_by_its_scan_code_position()
    {
        // VK_OEM_4 with the scan code of the key right of 0, the German ß key.
        var key = NativeKeys.FromWindows(0xDB, 0x0C, isExtended: false);

        Assert.That(HotkeyCombo.Create(KeyModifier.Control, key).Text, Is.EqualTo("Ctrl+Minus"));
    }
}
