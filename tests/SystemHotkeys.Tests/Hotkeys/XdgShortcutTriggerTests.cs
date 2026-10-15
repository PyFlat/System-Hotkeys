using MacroDeck.Sdk.Input;
using NUnit.Framework;
using SystemHotkeys.Hotkeys;
using SystemHotkeys.Hotkeys.Linux;

namespace SystemHotkeys.Tests.Hotkeys;

[TestFixture]
public sealed class XdgShortcutTriggerTests
{
    private static IEnumerable<KeyCode> NonModifierKeys() =>
        Enum.GetValues<KeyCode>()
            .Where(key => key != KeyCode.None && !KeyNames.TryGetModifier(key, out _));

    [TestCase(KeyModifier.Control | KeyModifier.Shift, KeyCode.F3, "CTRL+SHIFT+F3")]
    [TestCase(KeyModifier.None, KeyCode.F4, "F4")]
    [TestCase(KeyModifier.Meta | KeyModifier.Alt, KeyCode.A, "ALT+LOGO+a")]
    [TestCase(KeyModifier.Control, KeyCode.D7, "CTRL+7")]
    [TestCase(KeyModifier.None, KeyCode.Numpad5, "KP_5")]
    [TestCase(KeyModifier.None, KeyCode.MediaPlayPause, "XF86AudioPlay")]
    [TestCase(KeyModifier.Control | KeyModifier.Alt, KeyCode.Enter, "CTRL+ALT+Return")]
    public void A_combo_becomes_an_xdg_trigger(KeyModifier modifiers, KeyCode key, string expected)
    {
        Assert.That(XdgShortcutTrigger.TryCreate(modifiers, key, out var trigger), Is.True);
        Assert.That(trigger, Is.EqualTo(expected));
    }

    [Test]
    public void A_right_hand_modifier_names_the_same_modifier()
    {
        XdgShortcutTrigger.TryCreate(KeyModifier.RightControl | KeyModifier.RightMeta, KeyCode.Z, out var right);
        XdgShortcutTrigger.TryCreate(KeyModifier.Control | KeyModifier.Meta, KeyCode.Z, out var left);

        Assert.That(right, Is.EqualTo("CTRL+LOGO+z"));
        Assert.That(right, Is.EqualTo(left));
    }

    [Test]
    public void Every_key_but_a_modifier_has_a_keysym()
    {
        foreach (var key in NonModifierKeys())
        {
            Assert.That(XdgShortcutTrigger.TryCreate(KeyModifier.None, key, out _), Is.True, key.ToString());
        }
    }

    [Test]
    public void A_keysym_holds_only_letters_digits_and_underscores()
    {
        foreach (var key in NonModifierKeys())
        {
            XdgShortcutTrigger.TryCreate(KeyModifier.None, key, out var trigger);
            Assert.That(trigger, Does.Match("^[A-Za-z0-9_]+$"), key.ToString());
        }
    }

    [Test]
    public void A_modifier_alone_is_no_trigger() =>
        Assert.That(XdgShortcutTrigger.TryCreate(KeyModifier.None, KeyCode.LeftControl, out _), Is.False);
}
