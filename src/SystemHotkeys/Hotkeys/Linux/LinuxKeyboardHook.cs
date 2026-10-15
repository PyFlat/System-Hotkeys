using System.Runtime.Versioning;
using MacroDeck.Sdk.Input;
using Serilog;
using static SystemHotkeys.Hotkeys.Linux.NativeMethods;

namespace SystemHotkeys.Hotkeys.Linux;

// Reading needs the input group. Never swallows: a suppressed combo is left to the desktop's own shortcuts.
[SupportedOSPlatform("linux")]
internal sealed unsafe class LinuxKeyboardHook(ILogger logger) : KeyboardHook(logger)
{
	private const string InputDirectory = "/dev/input";
	private const int PollTimeoutMilliseconds = 500;
	private const int ReadBatch = 64;
	private static readonly TimeSpan _rescanInterval = TimeSpan.FromSeconds(2);

	private readonly List<EvdevKeyboard> _keyboards = [];
	private readonly HashSet<string> _ignoredPaths = [];

	private volatile bool _stopping;
	private volatile Thread? _thread;

	internal override void Start()
	{
		if (_thread is not null)
		{
			return;
		}

		_stopping = false;
		_thread = new Thread(Run)
		{
			IsBackground = true,
			Name = "SystemHotkeys.KeyboardHook",
		};
		_thread.Start();
	}

	public override void Dispose()
	{
		_stopping = true;
		_thread?.Join(TimeSpan.FromSeconds(2));
		_thread = null;
	}

	private void Run()
	{
		try
		{
			var nextScan = 0L;
			while (!_stopping)
			{
				if (Environment.TickCount64 >= nextScan)
				{
					Rescan();
					nextScan = Environment.TickCount64 + (long)_rescanInterval.TotalMilliseconds;
				}

				PollOnce();
			}
		}
		catch (Exception exception)
		{
			Logger.Error(exception, "The Linux keyboard hook stopped; no hotkey will fire until Macro Deck restarts.");
			ReportAccess(KeyboardHookAccess.Pending);
		}
		finally
		{
			foreach (var keyboard in _keyboards)
			{
				keyboard.Dispose();
			}

			_keyboards.Clear();
			_ignoredPaths.Clear();
		}
	}

	private void Rescan()
	{
		string[] paths;
		try
		{
			paths = Directory.GetFiles(InputDirectory, "event*");
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			paths = [];
		}

		_ignoredPaths.IntersectWith(paths);

		var permissionDenied = false;
		foreach (var path in paths)
		{
			if (_ignoredPaths.Contains(path) || _keyboards.Exists(keyboard => keyboard.Path == path))
			{
				continue;
			}

			if (EvdevKeyboard.TryOpen(path, out var error) is { } keyboard)
			{
				_keyboards.Add(keyboard);
				Logger.Information("Reading keyboard {Name} ({Path}).", keyboard.Name, keyboard.Path);
			}
			else if (error is EACCES or EPERM)
			{
				permissionDenied = true;
			}
			else if (error == 0)
			{
				_ignoredPaths.Add(path);
			}
		}

		var access = _keyboards.Count == 0 ? KeyboardHookAccess.Denied : KeyboardHookAccess.ObserveOnly;
		if (access != Access)
		{
			LogAccess(access, permissionDenied);
		}

		ReportAccess(access);
	}

	private void PollOnce()
	{
		var count = _keyboards.Count;
		if (count == 0)
		{
			Thread.Sleep(PollTimeoutMilliseconds);
			return;
		}

		var fds = stackalloc PollFd[count];
		for (var i = 0; i < count; i++)
		{
			fds[i] = new PollFd { Fd = _keyboards[i].Fd, Events = POLLIN };
		}

		if (Poll(fds, (nuint)count, PollTimeoutMilliseconds) <= 0)
		{
			return;
		}

		for (var i = count - 1; i >= 0; i--)
		{
			var returned = fds[i].ReturnedEvents;
			var gone = (returned & (POLLERR | POLLHUP | POLLNVAL)) != 0;
			if (!gone && (returned & POLLIN) != 0)
			{
				gone = !ReadKeyboard(_keyboards[i]);
			}

			if (gone)
			{
				Logger.Information("Keyboard {Name} disconnected.", _keyboards[i].Name);
				_keyboards[i].Dispose();
				_keyboards.RemoveAt(i);
			}
		}
	}

	private bool ReadKeyboard(EvdevKeyboard keyboard)
	{
		Span<InputEvent> events = stackalloc InputEvent[ReadBatch];
		int count;
		while ((count = keyboard.Read(events)) > 0)
		{
			foreach (ref readonly var inputEvent in events[..count])
			{
				HandleEvent(keyboard, inputEvent);
			}
		}

		return count == 0;
	}

	private void HandleEvent(EvdevKeyboard keyboard, in InputEvent inputEvent)
	{
		if (inputEvent.Type == EV_SYN && inputEvent.Code == SYN_DROPPED)
		{
			keyboard.IsResyncing = true;
			return;
		}

		if (keyboard.IsResyncing)
		{
			keyboard.IsResyncing = inputEvent.Type != EV_SYN || inputEvent.Code != SYN_REPORT;
			return;
		}

		if (inputEvent.Type != EV_KEY || NativeKeys.FromLinux(inputEvent.Code) is not (var key and not KeyCode.None))
		{
			return;
		}

		if (inputEvent.Value == 0)
		{
			OnKeyUp(key);
		}
		else
		{
			OnKeyDown(key);
		}
	}

	private void LogAccess(KeyboardHookAccess access, bool permissionDenied)
	{
		if (access == KeyboardHookAccess.ObserveOnly)
		{
			Logger.Information("Reading {Count} keyboard(s) from {Directory}.", _keyboards.Count, InputDirectory);
			return;
		}

		Logger.Warning(
			permissionDenied
				? "Could not read any keyboard in {Directory}: the user running Macro Deck is not in the input "
					+ "group. Retrying every {Seconds} seconds."
				: "Found no keyboard in {Directory}. Retrying every {Seconds} seconds.",
			InputDirectory,
			_rescanInterval.TotalSeconds
		);
	}
}
