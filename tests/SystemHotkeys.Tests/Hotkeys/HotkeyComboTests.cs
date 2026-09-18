using SystemHotkeys.Hotkeys;
using NUnit.Framework;

namespace SystemHotkeys.Tests.Hotkeys;

[TestFixture]
public sealed class HotkeyComboTests
{
    private static readonly string[] _ctrl = ["Ctrl"];
    private static readonly string[] _ctrlShift = ["Ctrl", "Shift"];
    private static readonly string[] _allFourModifiersInOrder = ["Ctrl", "Shift", "Alt", "Meta"];

    [Test]
    public void A_modifier_and_key_produce_the_expected_parts()
    {
        var combo = HotkeyCombo.TryCreate(
            Held(VirtualKeys.LControl),
            0x70 /* F1 */
        );

        Assert.That(combo?.Modifiers, Is.EqualTo(_ctrl));
        Assert.That(combo?.Key, Is.EqualTo("F1"));
    }

    [Test]
    public void Modifiers_always_appear_in_the_same_order_regardless_of_press_order()
    {
        var shiftThenCtrl = HotkeyCombo.TryCreate(
            Held(VirtualKeys.LShift, VirtualKeys.LControl),
            0x41 /* A */
        );
        var ctrlThenShift = HotkeyCombo.TryCreate(
            Held(VirtualKeys.LControl, VirtualKeys.LShift),
            0x41 /* A */
        );

        Assert.That(shiftThenCtrl?.Modifiers, Is.EqualTo(_ctrlShift));
        Assert.That(ctrlThenShift?.Modifiers, Is.EqualTo(shiftThenCtrl?.Modifiers));
    }

    [Test]
    public void All_four_modifiers_keep_the_editors_record_order()
    {
        var combo = HotkeyCombo.TryCreate(
            Held(VirtualKeys.LWin, VirtualKeys.LMenu, VirtualKeys.LShift, VirtualKeys.LControl),
            0x41
        );

        Assert.That(combo?.Modifiers, Is.EqualTo(_allFourModifiersInOrder));
    }

    [Test]
    public void The_left_and_right_variant_of_a_modifier_produce_the_same_name()
    {
        var left = HotkeyCombo.TryCreate(Held(VirtualKeys.LControl), 0x41);
        var right = HotkeyCombo.TryCreate(Held(VirtualKeys.RControl), 0x41);

        Assert.That(left?.Modifiers, Is.EqualTo(right?.Modifiers));
    }

    [Test]
    public void A_key_with_no_modifier_held_still_forms_a_combo()
    {
        var combo = HotkeyCombo.TryCreate(
            Held(),
            0x41 /* A */
        );

        Assert.That(combo?.Modifiers, Is.Empty);
        Assert.That(combo?.Key, Is.EqualTo("A"));
    }

    [Test]
    public void A_modifier_pressed_as_the_trailing_key_is_not_a_combo()
    {
        var combo = HotkeyCombo.TryCreate(
            Held(VirtualKeys.LControl, VirtualKeys.LShift),
            VirtualKeys.LShift
        );

        Assert.That(combo, Is.Null);
    }

    [Test]
    public void An_unmapped_trailing_key_is_not_a_combo()
    {
        var combo = HotkeyCombo.TryCreate(
            Held(VirtualKeys.LControl),
            0x07 /* unmapped */
        );

        Assert.That(combo, Is.Null);
    }

    [Test]
    public void A_numpad_key_uses_its_own_token_distinct_from_the_identically_printed_main_row_key()
    {
        // Numpad and main-row minus print the same glyph but the editor names them differently.
        Assert.That(
            HotkeyCombo
                .TryCreate(
                    Held(VirtualKeys.LControl),
                    0x6D /* numpad - */
                )
                ?.Key,
            Is.EqualTo("NumpadSubtract")
        );
        Assert.That(
            HotkeyCombo
                .TryCreate(
                    Held(VirtualKeys.LControl),
                    0xBD /* main-row - */
                )
                ?.Key,
            Is.EqualTo("-")
        );
        Assert.That(
            HotkeyCombo
                .TryCreate(
                    Held(VirtualKeys.LControl),
                    0x6B /* numpad + */
                )
                ?.Key,
            Is.EqualTo("NumpadAdd")
        );
    }

    private static HashSet<int> Held(params int[] vkCodes) => [.. vkCodes];
}
