using System.Runtime.InteropServices;
using MacroDeck.Sdk.Input;
using Serilog;
using SystemHotkeys.Hotkeys.Linux;
using SystemHotkeys.Hotkeys.MacOS;
using SystemHotkeys.Hotkeys.Windows;

namespace SystemHotkeys.Hotkeys;

internal abstract class KeyboardHook : IDisposable
{
	private readonly Lock _gate = new();
	private readonly HashSet<KeyCode> _heldKeys = [];
	private readonly HashSet<KeyCode> _swallowedKeys = [];
	private KeyModifier _heldModifiers;

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

		if (OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
		{
			return new LinuxKeyboardHook(logger);
		}

		return new UnsupportedKeyboardHook(logger);
	}

	internal void SetSuppressed(BoundHotkeyCombos suppressed) => _suppressed = suppressed;

	// For a backend that hands suppression to the desktop, which cannot follow a trigger's scope.
	internal virtual void SetRequestedSuppression(BoundHotkeyCombos requested) { }

	internal virtual Task<bool> OpenShortcutSettingsAsync() => Task.FromResult(false);

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

	protected void Publish(HotkeyCombo combo)
	{
		Logger.Debug("Detected hotkey {Combo}.", combo.Text);
		HotkeyPressed?.Invoke(combo);
	}

	protected virtual bool IsDeliveredElsewhere(HotkeyCombo combo) => false;

	// True when the key must not reach other applications.
	protected bool OnKeyDown(KeyCode key)
	{
		if (KeyNames.TryGetModifier(key, out var modifier))
		{
			SetModifierHeld(modifier, true);
			return false;
		}

		KeyModifier modifiers;
		lock (_gate)
		{
			if (!_heldKeys.Add(key))
			{
				return _swallowedKeys.Contains(key);
			}

			modifiers = _heldModifiers;
		}

		var combo = HotkeyCombo.Create(modifiers, key);
		if (!IsDeliveredElsewhere(combo))
		{
			Publish(combo);
		}

		if (!_suppressed.Contains(combo))
		{
			return false;
		}

		if (_access == KeyboardHookAccess.Full)
		{
			Logger.Information(
				"Suppressing hotkey {Combo}: keeping it from reaching other applications.",
				combo.Text
			);
		}

		lock (_gate)
		{
			_swallowedKeys.Add(key);
		}

		return true;
	}

	protected bool OnKeyUp(KeyCode key)
	{
		if (KeyNames.TryGetModifier(key, out var modifier))
		{
			SetModifierHeld(modifier, false);
			return false;
		}

		lock (_gate)
		{
			_heldKeys.Remove(key);
			return _swallowedKeys.Remove(key);
		}
	}

	// For a platform that reports modifier state rather than modifier key presses.
	protected void SetModifierHeld(KeyModifier modifier, bool held)
	{
		lock (_gate)
		{
			_heldModifiers = held ? _heldModifiers | modifier : _heldModifiers & ~modifier;
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
