using System.Runtime.Versioning;
using Serilog;

namespace SystemHotkeys.Hotkeys.MacOS;

// An active tap needs Accessibility, a listen-only one Input Monitoring.
[SupportedOSPlatform("macos")]
internal sealed class MacKeyboardHook(ILogger logger) : KeyboardHook(logger)
{
	private const ulong KeyboardEvents =
		(1UL << (int)NativeMethods.kCGEventKeyDown)
		| (1UL << (int)NativeMethods.kCGEventKeyUp)
		| (1UL << (int)NativeMethods.kCGEventFlagsChanged);

	// An active tap whose Accessibility was revoked freezes all input until it is removed.
	private const double RunLoopSliceSeconds = 0.5;
	private static readonly TimeSpan _retryInterval = TimeSpan.FromSeconds(5);

	// Reused across Start/Dispose cycles.
	private readonly ManualResetEventSlim _stopping = new();

	private NativeMethods.CGEventTapCallBack? _callback;

	private volatile Thread? _thread;
	private volatile nint _runLoop;
	private nint _tap;
	private nint _source;
	private bool _tapIsActive;
	private bool _listenAccessRequested;

	internal override void Start()
	{
		if (_thread is not null)
		{
			return;
		}

		_stopping.Reset();
		_thread = new Thread(RunEventTaps)
		{
			IsBackground = true,
			Name = "SystemHotkeys.KeyboardHook",
		};
		_thread.Start();
	}

	public override void Dispose()
	{
		_stopping.Set();
		if (_runLoop != 0)
		{
			NativeMethods.CFRunLoopStop(_runLoop);
		}

		_thread?.Join(TimeSpan.FromSeconds(2));
		_thread = null;
	}

	private void RunEventTaps()
	{
		_runLoop = NativeMethods.CFRunLoopGetCurrent();
		_callback = OnEvent;

		try
		{
			while (!_stopping.IsSet)
			{
				var access = InstallTap();
				if (access != Access)
				{
					LogAccess(access);
				}

				ReportAccess(access);

				if (access == KeyboardHookAccess.Denied)
				{
					_stopping.Wait(_retryInterval);
					continue;
				}

				RunUntilStoppedOrAccessibilityChanges();
				RemoveTap();
			}
		}
		finally
		{
			RemoveTap();
			_runLoop = 0;
		}
	}

	private KeyboardHookAccess InstallTap()
	{
		_tap = CreateTap(NativeMethods.kCGEventTapOptionDefault);
		_tapIsActive = _tap != 0;

		if (_tap == 0)
		{
			_tap = CreateTap(NativeMethods.kCGEventTapOptionListenOnly);
		}

		if (_tap == 0)
		{
			if (!_listenAccessRequested)
			{
				// Lists Macro Deck under Input Monitoring.
				_listenAccessRequested = true;
				NativeMethods.CGRequestListenEventAccess();
			}

			return KeyboardHookAccess.Denied;
		}

		_source = NativeMethods.CFMachPortCreateRunLoopSource(0, _tap, 0);
		NativeMethods.CFRunLoopAddSource(_runLoop, _source, NativeMethods.DefaultRunLoopMode);
		NativeMethods.CGEventTapEnable(_tap, true);
		return _tapIsActive ? KeyboardHookAccess.Full : KeyboardHookAccess.ObserveOnly;
	}

	private nint CreateTap(uint options, ulong eventsOfInterest = KeyboardEvents) =>
		NativeMethods.CGEventTapCreate(
			NativeMethods.kCGSessionEventTap,
			NativeMethods.kCGHeadInsertEventTap,
			options,
			eventsOfInterest,
			_callback!,
			0
		);

	// AXIsProcessTrusted stays false in a running process after Accessibility is granted.
	private bool CanCreateActiveTap()
	{
		var probe = CreateTap(NativeMethods.kCGEventTapOptionDefault, 1UL << (int)NativeMethods.kCGEventNull);
		if (probe == 0)
		{
			return false;
		}

		NativeMethods.CGEventTapEnable(probe, false);
		NativeMethods.CFMachPortInvalidate(probe);
		NativeMethods.CFRelease(probe);
		return true;
	}

	// CFRunLoopStop is lost when it arrives before the loop runs.
	private void RunUntilStoppedOrAccessibilityChanges()
	{
		while (!_stopping.IsSet)
		{
			NativeMethods.CFRunLoopRunInMode(NativeMethods.DefaultRunLoopMode, RunLoopSliceSeconds, false);

			if (_tapIsActive != CanCreateActiveTap())
			{
				return;
			}
		}
	}

	private void RemoveTap()
	{
		if (_source != 0)
		{
			NativeMethods.CFRunLoopRemoveSource(_runLoop, _source, NativeMethods.DefaultRunLoopMode);
			NativeMethods.CFRelease(_source);
			_source = 0;
		}

		if (_tap != 0)
		{
			NativeMethods.CGEventTapEnable(_tap, false);
			NativeMethods.CFMachPortInvalidate(_tap);
			NativeMethods.CFRelease(_tap);
			_tap = 0;
		}
	}

	private void LogAccess(KeyboardHookAccess access)
	{
		switch (access)
		{
			case KeyboardHookAccess.Full:
				Logger.Information("Global keyboard event tap installed.");
				break;
			case KeyboardHookAccess.ObserveOnly:
				Logger.Warning(
					"Global keyboard event tap installed listen-only: Macro Deck has no Accessibility "
						+ "permission, so a suppressed hotkey still reaches other applications."
				);
				break;
			case KeyboardHookAccess.Denied:
				Logger.Warning(
					"Could not install the global keyboard event tap: Macro Deck has neither the Input "
						+ "Monitoring nor the Accessibility permission. Retrying every {Seconds} seconds.",
					_retryInterval.TotalSeconds
				);
				break;
		}
	}

	private nint OnEvent(nint proxy, uint type, nint cgEvent, nint userInfo)
	{
		switch (type)
		{
			case NativeMethods.kCGEventTapDisabledByTimeout:
			case NativeMethods.kCGEventTapDisabledByUserInput:
				if (_tapIsActive && !CanCreateActiveTap())
				{
					// Accessibility was revoked: swap to listen-only.
					NativeMethods.CFRunLoopStop(_runLoop);
					return cgEvent;
				}

				NativeMethods.CGEventTapEnable(_tap, true);
				return cgEvent;

			case NativeMethods.kCGEventFlagsChanged:
				SyncModifiers(cgEvent);
				return cgEvent;

			case NativeMethods.kCGEventKeyDown:
			case NativeMethods.kCGEventKeyUp:
				SyncModifiers(cgEvent);
				return HandleKey(type, cgEvent) ? 0 : cgEvent;

			default:
				return cgEvent;
		}
	}

	private bool HandleKey(uint type, nint cgEvent)
	{
		var keyCode = (int)
			NativeMethods.CGEventGetIntegerValueField(cgEvent, NativeMethods.kCGKeyboardEventKeycode);
		if (MacKeyCodes.ToVirtualKey(keyCode) is not { } vkCode)
		{
			return false;
		}

		if (type == NativeMethods.kCGEventKeyUp)
		{
			return OnKeyUp(vkCode);
		}

		// Secure Event Input hides key-ups, so a fresh press may find the key still held.
		if (NativeMethods.CGEventGetIntegerValueField(cgEvent, NativeMethods.kCGKeyboardEventAutorepeat) == 0)
		{
			OnKeyUp(vkCode);
		}

		return OnKeyDown(vkCode);
	}

	private void SyncModifiers(nint cgEvent)
	{
		var flags = NativeMethods.CGEventGetFlags(cgEvent);
		foreach (var (vkCode, flagMask) in MacKeyCodes.ModifierFlags)
		{
			SetModifierHeld(vkCode, (flags & flagMask) != 0);
		}
	}
}
