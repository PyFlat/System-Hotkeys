using System.Runtime.InteropServices;
using Serilog;
using SystemHotkeys.Hotkeys.MacOS;
using SystemHotkeys.Hotkeys.Windows;

namespace SystemHotkeys.Hotkeys;

// Platform backends feed this Windows virtual-key codes.
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

	// True when the key must not reach other applications.
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

	protected bool OnKeyUp(int vkCode)
	{
		lock (_gate)
		{
			_heldKeys.Remove(vkCode);
			return _swallowedKeys.Remove(vkCode);
		}
	}

	// For a platform that reports modifier state rather than modifier key presses.
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
	// Also a hook that failed for a reason no permission fixes.
	Pending,
	Denied,
	ObserveOnly,
	Full,
}
