using System.Runtime.Versioning;
using System.Threading.Channels;
using MacroDeck.Sdk.Input;
using Serilog;
using Tmds.DBus.Protocol;
using static SystemHotkeys.Hotkeys.Linux.NativeMethods;

namespace SystemHotkeys.Hotkeys.Linux;

// Reads evdev (needs the input group) and hands suppressed combos to the desktop's GlobalShortcuts portal.
[SupportedOSPlatform("linux")]
internal sealed class LinuxKeyboardHook(ILogger logger) : KeyboardHook(logger)
{
	private const string InputDirectory = "/dev/input";
	private const int PollTimeoutMilliseconds = 500;
	private const int ReadBatch = 64;
	private const string ShortcutIdPrefix = "system-hotkeys.";
	private static readonly TimeSpan _rescanInterval = TimeSpan.FromSeconds(2);

	// Lets a burst of binding changes end in a single rebind, and so a single dialog.
	private static readonly TimeSpan _rebindDelay = TimeSpan.FromSeconds(1);

	private readonly List<EvdevKeyboard> _keyboards = [];
	private readonly HashSet<string> _ignoredPaths = [];
	private readonly Lock _accessGate = new();
	private readonly Channel<bool> _requestedChanged = Channel.CreateBounded<bool>(
		new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite }
	);

	private volatile bool _stopping;
	private volatile Thread? _thread;
	private volatile bool _keyboardsReadable;
	private volatile bool _readerFailed;

	private CancellationTokenSource? _portalStopping;
	private Task? _portalTask;
	private volatile GlobalShortcutsPortal? _portal;
	private volatile IReadOnlyList<RequestedShortcut> _requested = [];
	private volatile int _requestedCount;
	private volatile Dictionary<string, string> _triggerDescriptions = [];
	private volatile Dictionary<string, HotkeyCombo> _delivered = [];
	private volatile HashSet<string> _deliveredTexts = [];

	internal override void Start()
	{
		if (_thread is not null)
		{
			return;
		}

		_stopping = false;
		_readerFailed = false;
		_thread = new Thread(Run)
		{
			IsBackground = true,
			Name = "SystemHotkeys.KeyboardHook",
		};
		_thread.Start();

		_portalStopping = new CancellationTokenSource();
		var stopping = _portalStopping.Token;
		_portalTask = Task.Run(() => RunPortalAsync(stopping));
	}

	public override void Dispose()
	{
		_stopping = true;
		_portalStopping?.Cancel();
		_thread?.Join(TimeSpan.FromSeconds(2));
		_thread = null;

		// RunPortalAsync handles its own failures, so the wait only bounds how long shutdown takes.
		_portalTask?.Wait(TimeSpan.FromSeconds(2));
		_portalTask = null;
		_portalStopping?.Dispose();
		_portalStopping = null;
	}

	internal override void SetRequestedSuppression(BoundHotkeyCombos requested)
	{
		var shortcuts = new List<RequestedShortcut>(requested.Parsed.Count);
		foreach (var (modifiers, key) in requested.Parsed)
		{
			if (XdgShortcutTrigger.TryCreate(modifiers, key, out var trigger))
			{
				shortcuts.Add(new RequestedShortcut(ShortcutIdPrefix + trigger, HotkeyCombo.Create(modifiers, key), trigger));
			}
		}

		var unchanged =
			requested.Count == _requestedCount
			&& shortcuts
				.Select(shortcut => shortcut.Id)
				.ToHashSet(StringComparer.Ordinal)
				.SetEquals(_requested.Select(shortcut => shortcut.Id));
		if (unchanged)
		{
			return;
		}

		_requested = shortcuts;
		_requestedCount = requested.Count;
		UpdateAccess();
		_requestedChanged.Writer.TryWrite(true);
	}

	internal override async Task<bool> OpenShortcutSettingsAsync()
	{
		if (_portal is not { } portal)
		{
			return false;
		}

		try
		{
			return await portal.ConfigureAsync();
		}
		catch (DBusExceptionBase exception)
		{
			Logger.Warning(exception, "Could not open the desktop's shortcut settings.");
			return false;
		}
	}

	protected override bool IsDeliveredElsewhere(HotkeyCombo combo) => _deliveredTexts.Contains(combo.Text);

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
			_readerFailed = true;
			UpdateAccess();
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

		var readable = _keyboards.Count > 0;
		if (readable != _keyboardsReadable || Access == KeyboardHookAccess.Pending)
		{
			LogReadable(readable, permissionDenied);
		}

		_keyboardsReadable = readable;
		UpdateAccess();
	}

	// Full means every combo a trigger asks to suppress is the desktop's to swallow.
	private void UpdateAccess()
	{
		lock (_accessGate)
		{
			ReportAccess(
				_readerFailed ? KeyboardHookAccess.Pending
				: !_keyboardsReadable ? KeyboardHookAccess.Denied
				: _delivered.Count >= _requestedCount ? KeyboardHookAccess.Full
				: KeyboardHookAccess.ObserveOnly
			);
		}
	}

	private async Task RunPortalAsync(CancellationToken stopping)
	{
		GlobalShortcutsPortal? portal = null;
		var unavailable = false;
		try
		{
			var first = true;
			while (first || await _requestedChanged.Reader.WaitToReadAsync(stopping))
			{
				if (!first)
				{
					await Task.Delay(_rebindDelay, stopping);
				}

				first = false;
				while (_requestedChanged.Reader.TryRead(out _)) { }

				var requested = _requested;
				if (portal is null)
				{
					// Nothing asks for suppression yet, so leave D-Bus alone.
					if (requested.Count == 0 || unavailable)
					{
						continue;
					}

					portal = await GlobalShortcutsPortal.TryConnectAsync(reason =>
						Logger.Warning(
							"Your desktop cannot take over suppressed hotkeys: {Reason}. They still fire, but reach "
								+ "other applications too.",
							reason
						)
					);
					if (portal is null)
					{
						unavailable = true;
						continue;
					}

					portal.Activated += OnShortcutActivated;
					portal.ShortcutsChanged += OnShortcutsChanged;
					_portal = portal;
				}

				var triggers = await portal.BindAsync(
					[.. requested.Select(shortcut => (shortcut.Id, Describe(shortcut.Combo), shortcut.Trigger))],
					stopping
				);
				ApplyTriggerDescriptions(requested, triggers, replace: true);
			}
		}
		catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
		catch (Exception exception) when (exception is DBusExceptionBase or InvalidOperationException or IOException)
		{
			Logger.Warning(
				exception,
				"The global shortcuts portal stopped working; suppressed hotkeys reach other applications until "
					+ "Macro Deck restarts."
			);
		}
		finally
		{
			_portal = null;
			portal?.Dispose();
			ApplyTriggerDescriptions([], new Dictionary<string, string>(), replace: true);
		}
	}

	private static string Describe(HotkeyCombo combo) => PlainText.Resolve(Strings.Shortcuts.Description(combo.Text));

	private void OnShortcutActivated(string id)
	{
		if (_delivered.TryGetValue(id, out var combo))
		{
			Publish(combo);
		}
	}

	private void OnShortcutsChanged(IReadOnlyDictionary<string, string> changed) =>
		ApplyTriggerDescriptions(_requested, changed, replace: false);

	// A shortcut the user declined or left without a key comes back with an empty description.
	private void ApplyTriggerDescriptions(
		IReadOnlyList<RequestedShortcut> requested,
		IReadOnlyDictionary<string, string> triggers,
		bool replace
	)
	{
		var descriptions = replace
			? new Dictionary<string, string>(StringComparer.Ordinal)
			: new Dictionary<string, string>(_triggerDescriptions, StringComparer.Ordinal);
		foreach (var (id, description) in triggers)
		{
			descriptions[id] = description;
		}

		var delivered = new Dictionary<string, HotkeyCombo>(StringComparer.Ordinal);
		foreach (var shortcut in requested)
		{
			if (descriptions.TryGetValue(shortcut.Id, out var description) && description.Length > 0)
			{
				delivered[shortcut.Id] = shortcut.Combo;
			}
		}

		_triggerDescriptions = descriptions;
		_delivered = delivered;
		_deliveredTexts = [.. delivered.Values.Select(combo => combo.Text)];
		if (requested.Count > 0)
		{
			Logger.Information(
				"Your desktop takes over {Delivered} of {Requested} suppressed hotkey(s): {Combos}.",
				delivered.Count,
				_requestedCount,
				string.Join(", ", requested.Select(shortcut => $"{shortcut.Combo.Text} as {DescribeTrigger(descriptions, shortcut.Id)}"))
			);
		}

		UpdateAccess();
	}

	private static string DescribeTrigger(Dictionary<string, string> descriptions, string id) =>
		descriptions.TryGetValue(id, out var description) && description.Length > 0 ? description : "nothing";

	private unsafe void PollOnce()
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

	private void LogReadable(bool readable, bool permissionDenied)
	{
		if (readable)
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

	private sealed record RequestedShortcut(string Id, HotkeyCombo Combo, string Trigger);
}
