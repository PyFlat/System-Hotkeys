using System.Runtime.InteropServices;
using Serilog;
using SystemHotkeys.Hotkeys.MacOS;
using SystemHotkeys.Hotkeys.Windows;

namespace SystemHotkeys.Hotkeys;

/// <summary>
/// The platform-neutral half of the global keyboard hook: tracks held keys, detects a completed combo and
/// decides which key events a suppressed combo swallows. A platform backend feeds it virtual-key codes.
/// </summary>
internal abstract class KeyboardHook : IDisposable
{
	private readonly Lock _gate = new();
	private readonly HashSet<int> _heldKeys = [];
	private readonly HashSet<int> _swallowedKeys = [];

	private volatile BoundHotkeyCombos _suppressed = BoundHotkeyCombos.None;
	private volatile KeyboardHookAccess _access = KeyboardHookAccess.Pending;

	protected KeyboardHook(ILogger logger) => Logger = logger.ForContext(GetType());

	internal event Action<HotkeyCombo>? HotkeyPressed;

	internal event Action<KeyboardHookAccess>? AccessChanged;

	internal KeyboardHookAccess Access => _access;

	protected ILogger Logger { get; }

	internal static KeyboardHook Create(ILogger logger)
	{
		if (OperatingSystem.IsWindows())
		{
			return new WindowsKeyboardHook(logger);
		}

		if (OperatingSystem.IsMacOS())
		{
			return new MacKeyboardHook(logger);
		}

		return new UnsupportedKeyboardHook(logger);
	}

	internal void SetSuppressed(BoundHotkeyCombos suppressed) => _suppressed = suppressed;

	internal abstract void Start();

	public abstract void Dispose();

	protected void ReportAccess(KeyboardHookAccess access)
	{
		if (_access == access)
		{
			return;
		}

		_access = access;
		AccessChanged?.Invoke(access);
	}

	/// <returns><see langword="true"/> when the key-down must be kept from other applications.</returns>
	protected bool OnKeyDown(int vkCode)
	{
		bool isNewPress;
		HashSet<int> snapshot;
		lock (_gate)
		{
			isNewPress = _heldKeys.Add(vkCode);
			snapshot = [.. _heldKeys];

			if (!isNewPress)
			{
				return _swallowedKeys.Contains(vkCode);
			}
		}

		if (HotkeyCombo.TryCreate(snapshot, vkCode) is not { } combo)
		{
			return false;
		}

		Logger.Debug("Detected hotkey {Combo}.", combo.Text);
		HotkeyPressed?.Invoke(combo);

		if (!_suppressed.Contains(combo))
		{
			return false;
		}

		Logger.Information(
			"Suppressing hotkey {Combo}: keeping it from reaching other applications.",
			combo.Text
		);

		lock (_gate)
		{
			_swallowedKeys.Add(vkCode);
		}

		return true;
	}

	/// <returns><see langword="true"/> when the key-up must be kept from other applications.</returns>
	protected bool OnKeyUp(int vkCode)
	{
		lock (_gate)
		{
			_heldKeys.Remove(vkCode);
			return _swallowedKeys.Remove(vkCode);
		}
	}

	/// <summary>
	/// Records a modifier's state without running combo detection, for a platform that reports modifier
	/// state rather than modifier key presses. A modifier never completes a combo and is never swallowed.
	/// </summary>
	protected void SetModifierHeld(int vkCode, bool held)
	{
		lock (_gate)
		{
			if (held)
			{
				_heldKeys.Add(vkCode);
			}
			else
			{
				_heldKeys.Remove(vkCode);
			}
		}
	}

	private sealed class UnsupportedKeyboardHook(ILogger logger) : KeyboardHook(logger)
	{
		internal override void Start() =>
			Logger.Warning(
				"Global hotkeys are not supported on {Platform}; no hotkey will fire.",
				RuntimeInformation.OSDescription
			);

		public override void Dispose() { }
	}
}

internal enum KeyboardHookAccess
{
	/// <summary>Not started yet, or failed for a reason the user cannot fix with a permission.</summary>
	Pending,

	/// <summary>The OS refused the hook until the user grants a permission.</summary>
	Denied,

	/// <summary>Hotkeys are detected, but a suppressed combo still reaches other applications.</summary>
	ObserveOnly,

	Full,
}
