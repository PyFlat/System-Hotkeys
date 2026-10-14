using MacroDeck.Sdk.Input;

namespace SystemHotkeys.Hotkeys;

public sealed record HotkeyCombo(IReadOnlyList<string> Modifiers, string Key)
{
    private static readonly (KeyModifier Right, KeyModifier Either)[] _sides =
    [
        (KeyModifier.RightControl, KeyModifier.Control),
        (KeyModifier.RightShift, KeyModifier.Shift),
        (KeyModifier.RightAlt, KeyModifier.Alt),
        (KeyModifier.RightMeta, KeyModifier.Meta),
    ];

    public string Text => string.Join('+', [.. Modifiers, Key]);

    // Event trigger fields never store right-hand modifier names, so either side counts as the generic one.
    public static HotkeyCombo Create(KeyModifier heldModifiers, KeyCode key)
    {
        var generic = heldModifiers;
        foreach (var (right, either) in _sides)
        {
            if (generic.HasFlag(right))
            {
                generic = (generic & ~right) | either;
            }
        }

        return new HotkeyCombo(KeyNames.ToModifierNames(generic), KeyNames.ToName(key));
    }
}
