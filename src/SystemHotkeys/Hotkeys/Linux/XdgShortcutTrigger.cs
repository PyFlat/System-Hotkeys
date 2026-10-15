using System.Diagnostics.CodeAnalysis;
using MacroDeck.Sdk.Input;

namespace SystemHotkeys.Hotkeys.Linux;

// The portal's trigger format (xkbcommon names), which the SDK has no names for.
public static class XdgShortcutTrigger
{
	private static readonly (KeyModifier Modifiers, string Name)[] _modifiers =
	[
		(KeyModifier.Control | KeyModifier.RightControl, "CTRL"),
		(KeyModifier.Alt | KeyModifier.RightAlt, "ALT"),
		(KeyModifier.Shift | KeyModifier.RightShift, "SHIFT"),
		(KeyModifier.Meta | KeyModifier.RightMeta, "LOGO"),
	];

	private static readonly Dictionary<KeyCode, string> _keysyms = CreateKeysyms();

	public static bool TryCreate(KeyModifier modifiers, KeyCode key, [NotNullWhen(true)] out string? trigger)
	{
		trigger = null;
		if (!_keysyms.TryGetValue(key, out var keysym))
		{
			return false;
		}

		var parts = new List<string>(5);
		foreach (var (mask, name) in _modifiers)
		{
			if ((modifiers & mask) != 0)
			{
				parts.Add(name);
			}
		}

		parts.Add(keysym);
		trigger = string.Join('+', parts);
		return true;
	}

	private static Dictionary<KeyCode, string> CreateKeysyms()
	{
		var keysyms = new Dictionary<KeyCode, string>
		{
			[KeyCode.Enter] = "Return",
			[KeyCode.Escape] = "Escape",
			[KeyCode.Backspace] = "BackSpace",
			[KeyCode.Tab] = "Tab",
			[KeyCode.Space] = "space",
			[KeyCode.CapsLock] = "Caps_Lock",
			[KeyCode.Insert] = "Insert",
			[KeyCode.Delete] = "Delete",
			[KeyCode.Home] = "Home",
			[KeyCode.End] = "End",
			[KeyCode.PageUp] = "Page_Up",
			[KeyCode.PageDown] = "Page_Down",
			[KeyCode.PrintScreen] = "Print",
			[KeyCode.ScrollLock] = "Scroll_Lock",
			[KeyCode.Pause] = "Pause",
			[KeyCode.ArrowUp] = "Up",
			[KeyCode.ArrowDown] = "Down",
			[KeyCode.ArrowLeft] = "Left",
			[KeyCode.ArrowRight] = "Right",
			[KeyCode.NumLock] = "Num_Lock",
			[KeyCode.NumpadAdd] = "KP_Add",
			[KeyCode.NumpadSubtract] = "KP_Subtract",
			[KeyCode.NumpadMultiply] = "KP_Multiply",
			[KeyCode.NumpadDivide] = "KP_Divide",
			[KeyCode.NumpadDecimal] = "KP_Decimal",
			[KeyCode.NumpadEnter] = "KP_Enter",
			[KeyCode.Semicolon] = "semicolon",
			[KeyCode.Equal] = "equal",
			[KeyCode.Comma] = "comma",
			[KeyCode.Minus] = "minus",
			[KeyCode.Period] = "period",
			[KeyCode.Slash] = "slash",
			[KeyCode.Backquote] = "grave",
			[KeyCode.BracketLeft] = "bracketleft",
			[KeyCode.Backslash] = "backslash",
			[KeyCode.BracketRight] = "bracketright",
			[KeyCode.Quote] = "apostrophe",
			[KeyCode.IntlBackslash] = "less",
			[KeyCode.MediaPlayPause] = "XF86AudioPlay",
			[KeyCode.MediaStop] = "XF86AudioStop",
			[KeyCode.MediaTrackNext] = "XF86AudioNext",
			[KeyCode.MediaTrackPrevious] = "XF86AudioPrev",
			[KeyCode.AudioVolumeUp] = "XF86AudioRaiseVolume",
			[KeyCode.AudioVolumeDown] = "XF86AudioLowerVolume",
			[KeyCode.AudioVolumeMute] = "XF86AudioMute",
		};

		for (var letter = KeyCode.A; letter <= KeyCode.Z; letter++)
		{
			keysyms[letter] = ((char)('a' + (letter - KeyCode.A))).ToString();
		}

		for (var digit = KeyCode.D0; digit <= KeyCode.D9; digit++)
		{
			keysyms[digit] = ((char)('0' + (digit - KeyCode.D0))).ToString();
		}

		for (var function = KeyCode.F1; function <= KeyCode.F24; function++)
		{
			keysyms[function] = $"F{function - KeyCode.F1 + 1}";
		}

		for (var numpad = KeyCode.Numpad0; numpad <= KeyCode.Numpad9; numpad++)
		{
			keysyms[numpad] = $"KP_{numpad - KeyCode.Numpad0}";
		}

		return keysyms;
	}
}
