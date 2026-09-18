namespace SystemHotkeys.Hotkeys;

public sealed record HotkeyCombo(IReadOnlyList<string> Modifiers, string Key)
{
    private static readonly (int Left, int Right, string Name)[] _modifiers =
    [
        (VirtualKeys.LControl, VirtualKeys.RControl, "Ctrl"),
        (VirtualKeys.LShift, VirtualKeys.RShift, "Shift"),
        (VirtualKeys.LMenu, VirtualKeys.RMenu, "Alt"),
        (VirtualKeys.LWin, VirtualKeys.RWin, "Meta"),
    ];

    public string Text => string.Join('+', [.. Modifiers, Key]);

    public static HotkeyCombo? TryCreate(IReadOnlySet<int> heldKeys, int vkCode)
    {
        if (VirtualKeys.IsModifier(vkCode))
        {
            return null;
        }

        if (VirtualKeys.NameOf(vkCode) is not { } keyName)
        {
            return null;
        }

        var modifiers = _modifiers
            .Where(modifier =>
                heldKeys.Contains(modifier.Left) || heldKeys.Contains(modifier.Right)
            )
            .Select(modifier => modifier.Name)
            .ToArray();

        return new HotkeyCombo(modifiers, keyName);
    }
}
