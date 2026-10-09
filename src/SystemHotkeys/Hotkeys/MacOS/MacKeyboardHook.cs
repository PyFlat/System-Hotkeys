using System.Runtime.Versioning;
using Serilog;

namespace SystemHotkeys.Hotkeys.MacOS;

/// <summary>
/// A Quartz event tap on its own CFRunLoop thread. An active tap (able to swallow a key) needs the
/// Accessibility permission; without it the hook falls back to a listen-only tap, which needs Input
/// Monitoring. With neither it keeps retrying, so granting a permission takes effect without a restart.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacKeyboardHook(ILogger logger) : KeyboardHook(logger)
{
	private const ulong KeyboardEvents =
		(1UL << (int)NativeMethods.kCGEventKeyDown)
		| (1UL << (int)NativeMethods.kCGEventKeyUp)
		| (1UL << (int)NativeMethods.kCGEventFlagsChanged);

	private const double RunLoopSliceSeconds = 1;
	private static readonly TimeSpan _retryInterval = TimeSpan.FromSeconds(5);

	// Never disposed: it is reused across Start/Dispose cycles and never allocates a kernel wait handle.
	private readonly ManualResetEventSlim _stopping = new();

	private NativeMethods.CGEventTapCallBack? _callback;

	private volatile Thread? _thread;
	private volatile nint _runLoop;
	private nint _tap;
	private nint _source;
	private bool _upgradeWhenTrusted;
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

				RunUntilStoppedOrUpgradable();
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
		var access = KeyboardHookAccess.Full;

		if (_tap == 0)
		{
			_tap = CreateTap(NativeMethods.kCGEventTapOptionListenOnly);
			access = KeyboardHookAccess.ObserveOnly;
		}

		if (_tap == 0)
		{
			if (!_listenAccessRequested)
			{
				// Shows the system prompt once and lists Macro Deck under Input Monitoring.
				_listenAccessRequested = true;
				NativeMethods.CGRequestListenEventAccess();
			}

			return KeyboardHookAccess.Denied;
		}

		// Granting Accessibility later should upgrade the tap, but if it is already granted and the active
		// tap still failed, re-checking would only rebuild the same listen-only tap every slice.
		_upgradeWhenTrusted =
			access == KeyboardHookAccess.ObserveOnly && !NativeMethods.AXIsProcessTrusted();

		_source = NativeMethods.CFMachPortCreateRunLoopSource(0, _tap, 0);
		NativeMethods.CFRunLoopAddSource(_runLoop, _source, NativeMethods.DefaultRunLoopMode);
		NativeMethods.CGEventTapEnable(_tap, true);
		return access;
	}

	private nint CreateTap(uint options) =>
		NativeMethods.CGEventTapCreate(
			NativeMethods.kCGSessionEventTap,
			NativeMethods.kCGHeadInsertEventTap,
			options,
			KeyboardEvents,
			_callback!,
			0
		);

	// Runs in short slices because CFRunLoopStop is lost when it arrives before the loop is running.
	private void RunUntilStoppedOrUpgradable()
	{
		while (!_stopping.IsSet)
		{
			NativeMethods.CFRunLoopRunInMode(NativeMethods.DefaultRunLoopMode, RunLoopSliceSeconds, false);

			if (_upgradeWhenTrusted && NativeMethods.AXIsProcessTrusted())
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
				// macOS turns off a tap whose callback was too slow; turn it back on rather than go deaf.
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

	/// <returns><see langword="true"/> when the event must be swallowed.</returns>
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

		// A fresh press of a key still marked held means its key-up was never seen (Secure Event Input
		// hides keys from every tap), so drop the stale state or this press would count as a repeat.
		if (NativeMethods.CGEventGetIntegerValueField(cgEvent, NativeMethods.kCGKeyboardEventAutorepeat) == 0)
		{
			OnKeyUp(vkCode);
		}

		return OnKeyDown(vkCode);
	}

	// Modifier state is read from every event's flags rather than tracked from key presses, so a modifier
	// released while the tap could not see it never sticks.
	private void SyncModifiers(nint cgEvent)
	{
		var flags = NativeMethods.CGEventGetFlags(cgEvent);
		foreach (var (vkCode, flagMask) in MacKeyCodes.ModifierFlags)
		{
			SetModifierHeld(vkCode, (flags & flagMask) != 0);
		}
	}
}
