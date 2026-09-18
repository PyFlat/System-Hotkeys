using System.Globalization;

namespace SystemHotkeys.Hotkeys;

/// <summary>
/// Virtual-key code to combo-editor key-token mapping. Not exhaustive: an unmapped code is simply never
/// recognized as a combo's trailing key.
/// </summary>
public static class VirtualKeys
{
    public const int LShift = 0xA0;
    public const int RShift = 0xA1;
    public const int LControl = 0xA2;
    public const int RControl = 0xA3;
    public const int LMenu = 0xA4;
    public const int RMenu = 0xA5;
    public const int LWin = 0x5B;
    public const int RWin = 0x5C;

    // Generic (non-side-specific) modifier codes some callers still report.
    private const int Shift = 0x10;
    private const int Control = 0x11;
    private const int Menu = 0x12;

    private static readonly HashSet<int> _modifierCodes =
    [
        LShift,
        RShift,
        LControl,
        RControl,
        LMenu,
        RMenu,
        LWin,
        RWin,
        Shift,
        Control,
        Menu,
    ];

    private static readonly IReadOnlyDictionary<int, string> _names = BuildNames();

    public static bool IsModifier(int vkCode) => _modifierCodes.Contains(vkCode);

    public static string? NameOf(int vkCode) => _names.GetValueOrDefault(vkCode);

    private static Dictionary<int, string> BuildNames()
    {
        var names = new Dictionary<int, string>();

        for (var letter = 0; letter < 26; letter++)
        {
            names[0x41 + letter] = ((char)('A' + letter)).ToString();
        }

        for (var digit = 0; digit < 10; digit++)
        {
            names[0x30 + digit] = digit.ToString(CultureInfo.InvariantCulture);
        }

        for (var f = 0; f < 24; f++)
        {
            names[0x70 + f] = $"F{f + 1}";
        }

        names[0x08] = "Backspace";
        names[0x09] = "Tab";
        names[0x0D] = "Enter";
        names[0x13] = "Pause";
        names[0x14] = "CapsLock";
        names[0x1B] = "Escape";
        names[0x20] = "Space";
        names[0x21] = "PageUp";
        names[0x22] = "PageDown";
        names[0x23] = "End";
        names[0x24] = "Home";
        names[0x25] = "ArrowLeft";
        names[0x26] = "ArrowUp";
        names[0x27] = "ArrowRight";
        names[0x28] = "ArrowDown";
        names[0x2C] = "PrintScreen";
        names[0x2D] = "Insert";
        names[0x2E] = "Delete";
        names[0x5D] = "ContextMenu";

        for (var digit = 0; digit < 10; digit++)
        {
            names[0x60 + digit] = $"Numpad{digit}";
        }

        names[0x6A] = "NumpadMultiply";
        names[0x6B] = "NumpadAdd";
        names[0x6D] = "NumpadSubtract";
        names[0x6E] = "NumpadDecimal";
        names[0x6F] = "NumpadDivide";

        names[0x91] = "ScrollLock";

        names[0xA6] = "BrowserBack";
        names[0xA7] = "BrowserForward";
        names[0xA8] = "BrowserRefresh";
        names[0xA9] = "BrowserStop";
        names[0xAA] = "BrowserSearch";
        names[0xAB] = "BrowserFavorites";
        names[0xAC] = "BrowserHome";
        names[0xAD] = "AudioVolumeMute";
        names[0xAE] = "AudioVolumeDown";
        names[0xAF] = "AudioVolumeUp";
        names[0xB0] = "MediaTrackNext";
        names[0xB1] = "MediaTrackPrevious";
        names[0xB2] = "MediaStop";
        names[0xB3] = "MediaPlayPause";
        names[0xB4] = "LaunchMail";
        names[0xB5] = "LaunchMediaPlayer";
        names[0xB6] = "LaunchApplication1";
        names[0xB7] = "LaunchApplication2";

        names[0xBA] = ";";
        names[0xBB] = "=";
        names[0xBC] = ",";
        names[0xBD] = "-";
        names[0xBE] = ".";
        names[0xBF] = "/";
        names[0xC0] = "`";
        names[0xDB] = "[";
        names[0xDC] = "\\";
        names[0xDD] = "]";
        names[0xDE] = "'";

        // layout-dependent ISO key next to left Shift on most non-US keyboards
        names[0xE2] = "<";

        return names;
    }
}
