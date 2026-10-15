using System.Runtime.Versioning;
using Tmds.DBus.Protocol;

namespace SystemHotkeys.Hotkeys.Linux;

// org.freedesktop.portal.GlobalShortcuts: the desktop takes over a combo and reports its presses.
[SupportedOSPlatform("linux")]
internal sealed class GlobalShortcutsPortal : IDisposable
{
	private const string Service = "org.freedesktop.portal.Desktop";
	private const string DesktopPath = "/org/freedesktop/portal/desktop";
	private const string Interface = "org.freedesktop.portal.GlobalShortcuts";
	private const string RequestInterface = "org.freedesktop.portal.Request";
	private const string SessionInterface = "org.freedesktop.portal.Session";
	private const string PropertiesInterface = "org.freedesktop.DBus.Properties";

	// KDE files an unidentified app's shortcuts under this token, so a fixed one keeps the user's answers.
	private const string SessionToken = "system_hotkeys";

	private const uint ResponseSuccess = 0;
	private const uint ResponseCancelled = 1;

	private readonly DBusConnection _connection;
	private readonly string _requestPathPrefix;
	private readonly List<IDisposable> _subscriptions = [];
	private volatile string? _session;
	private int _requestCount;

	private GlobalShortcutsPortal(DBusConnection connection, uint version)
	{
		_connection = connection;
		Version = version;
		_requestPathPrefix =
			$"{DesktopPath}/request/{connection.UniqueName!.TrimStart(':').Replace('.', '_')}/";
	}

	internal event Action<string>? Activated;

	internal event Action<IReadOnlyDictionary<string, string>>? ShortcutsChanged;

	internal uint Version { get; }

	// Null when there is no session bus or the desktop's portal has no GlobalShortcuts.
	internal static async Task<GlobalShortcutsPortal?> TryConnectAsync(Action<string> reportUnavailable)
	{
		if (DBusAddress.Session is not { } address)
		{
			reportUnavailable("no D-Bus session bus address is set (DBUS_SESSION_BUS_ADDRESS)");
			return null;
		}

		var connection = new DBusConnection(address);
		try
		{
			await connection.ConnectAsync();
			var version = await ReadVersionAsync(connection);
			var portal = new GlobalShortcutsPortal(connection, version);
			await portal.SubscribeAsync();
			return portal;
		}
		catch (DBusErrorReplyException exception)
		{
			connection.Dispose();
			reportUnavailable($"the desktop's portal has no global shortcuts ({exception.ErrorName})");
			return null;
		}
		catch (Exception exception) when (exception is DBusExceptionBase or IOException)
		{
			connection.Dispose();
			reportUnavailable($"the D-Bus session bus cannot be reached ({exception.Message})");
			return null;
		}
	}

	// Shortcut id to the desktop's description of its trigger; empty when the user gave it none.
	internal async Task<IReadOnlyDictionary<string, string>> BindAsync(
		IReadOnlyList<(string Id, string Description, string Trigger)> shortcuts,
		CancellationToken cancellationToken
	)
	{
		// A session binds only once, so a changed set needs a fresh one.
		await CloseSessionAsync();
		if (shortcuts.Count == 0)
		{
			return new Dictionary<string, string>();
		}

		var (created, createResults) = await RequestAsync(
			token =>
			{
				using var writer = _connection.GetMessageWriter();
				writer.WriteMethodCallHeader(Service, DesktopPath, Interface, "CreateSession", "a{sv}", MessageFlags.None);
				writer.WriteDictionary(
					new Dictionary<string, VariantValue>
					{
						["handle_token"] = VariantValue.String(token),
						["session_handle_token"] = VariantValue.String(SessionToken),
					}
				);
				return writer.CreateMessage();
			},
			cancellationToken
		);
		if (created != ResponseSuccess || !createResults.TryGetValue("session_handle", out var handle))
		{
			throw new InvalidOperationException($"The portal refused to create a session (response {created}).");
		}

		var session = Unwrap(handle).Type == VariantValueType.ObjectPath
			? Unwrap(handle).GetObjectPathAsString()
			: Unwrap(handle).GetString();
		_session = session;

		var (bound, bindResults) = await RequestAsync(
			token =>
			{
				using var writer = _connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					Service,
					DesktopPath,
					Interface,
					"BindShortcuts",
					"oa(sa{sv})sa{sv}",
					MessageFlags.None
				);
				writer.WriteObjectPath(session);
				var array = writer.WriteArrayStart(DBusType.Struct);
				foreach (var (id, description, trigger) in shortcuts)
				{
					writer.WriteStructureStart();
					writer.WriteString(id);
					writer.WriteDictionary(
						new Dictionary<string, VariantValue>
						{
							["description"] = VariantValue.String(description),
							["preferred_trigger"] = VariantValue.String(trigger),
						}
					);
				}

				writer.WriteArrayEnd(array);
				writer.WriteString("");
				writer.WriteDictionary(
					new Dictionary<string, VariantValue> { ["handle_token"] = VariantValue.String(token) }
				);
				return writer.CreateMessage();
			},
			cancellationToken
		);

		if (bound == ResponseCancelled)
		{
			return new Dictionary<string, string>();
		}

		if (bound != ResponseSuccess)
		{
			throw new InvalidOperationException($"The portal could not bind the shortcuts (response {bound}).");
		}

		return bindResults.TryGetValue("shortcuts", out var list)
			? ReadShortcuts(Unwrap(list))
			: new Dictionary<string, string>();
	}

	internal async Task<bool> ConfigureAsync()
	{
		if (Version < 2 || _session is not { } session)
		{
			return false;
		}

		await _connection.CallMethodAsync(CreateConfigureCall(session));
		return true;
	}

	private MessageBuffer CreateConfigureCall(string session)
	{
		using var writer = _connection.GetMessageWriter();
		writer.WriteMethodCallHeader(Service, DesktopPath, Interface, "ConfigureShortcuts", "osa{sv}", MessageFlags.None);
		writer.WriteObjectPath(session);
		writer.WriteString("");
		writer.WriteDictionary(new Dictionary<string, VariantValue>());
		return writer.CreateMessage();
	}

	// Closing the connection also ends the session, which releases every combo it took over.
	public void Dispose()
	{
		foreach (var subscription in _subscriptions)
		{
			subscription.Dispose();
		}

		_connection.Dispose();
	}

	private static Task<uint> ReadVersionAsync(DBusConnection connection)
	{
		using var writer = connection.GetMessageWriter();
		writer.WriteMethodCallHeader(Service, DesktopPath, PropertiesInterface, "Get", "ss", MessageFlags.None);
		writer.WriteString(Interface);
		writer.WriteString("version");
		return connection.CallMethodAsync(
			writer.CreateMessage(),
			static (message, _) => Unwrap(message.GetBodyReader().ReadVariantValue()).GetUInt32(),
			null
		);
	}

	private async Task SubscribeAsync()
	{
		_subscriptions.Add(
			await _connection.AddMatchAsync(
				new MatchRule { Type = MessageType.Signal, Path = DesktopPath, Interface = Interface, Member = "Activated" },
				static (message, _) =>
				{
					var reader = message.GetBodyReader();
					return (Session: reader.ReadObjectPathAsString(), Id: reader.ReadString());
				},
				notification =>
				{
					if (notification.HasValue && notification.Value.Session == _session)
					{
						Activated?.Invoke(notification.Value.Id);
					}
				},
				false,
				ObserverFlags.None,
				null
			)
		);

		_subscriptions.Add(
			await _connection.AddMatchAsync(
				new MatchRule { Type = MessageType.Signal, Path = DesktopPath, Interface = Interface, Member = "ShortcutsChanged" },
				static (message, _) =>
				{
					var reader = message.GetBodyReader();
					var session = reader.ReadObjectPathAsString();
					var shortcuts = new Dictionary<string, string>(StringComparer.Ordinal);
					var end = reader.ReadArrayStart(DBusType.Struct);
					while (reader.HasNext(end))
					{
						reader.AlignStruct();
						var id = reader.ReadString();
						var properties = reader.ReadDictionaryOfStringToVariantValue();
						shortcuts[id] = TriggerDescription(properties);
					}

					return (Session: session, Shortcuts: shortcuts);
				},
				notification =>
				{
					if (notification.HasValue && notification.Value.Session == _session)
					{
						ShortcutsChanged?.Invoke(notification.Value.Shortcuts);
					}
				},
				false,
				ObserverFlags.None,
				null
			)
		);
	}

	// Subscribes to the Response before calling, or a fast reply would arrive unheard.
	private async Task<(uint Response, Dictionary<string, VariantValue> Results)> RequestAsync(
		Func<string, MessageBuffer> createCall,
		CancellationToken cancellationToken
	)
	{
		var token = $"system_hotkeys_{Interlocked.Increment(ref _requestCount)}";
		var response = new TaskCompletionSource<(uint, Dictionary<string, VariantValue>)>(
			TaskCreationOptions.RunContinuationsAsynchronously
		);

		using var subscription = await _connection.AddMatchAsync(
			new MatchRule
			{
				Type = MessageType.Signal,
				Path = _requestPathPrefix + token,
				Interface = RequestInterface,
				Member = "Response",
			},
			static (message, _) =>
			{
				var reader = message.GetBodyReader();
				return (reader.ReadUInt32(), reader.ReadDictionaryOfStringToVariantValue());
			},
			notification =>
			{
				if (notification.HasValue)
				{
					response.TrySetResult(notification.Value);
				}
				else if (notification.Exception is { } exception)
				{
					response.TrySetException(exception);
				}
			},
			false,
			ObserverFlags.None,
			null
		);

		await _connection.CallMethodAsync(createCall(token));
		using var cancellation = cancellationToken.Register(() => response.TrySetCanceled(cancellationToken));
		return await response.Task;
	}

	private async Task CloseSessionAsync()
	{
		if (_session is not { } session)
		{
			return;
		}

		_session = null;
		try
		{
			await _connection.CallMethodAsync(CreateCloseCall(session));
		}
		catch (DBusErrorReplyException)
		{
			// The portal already closed it.
		}
	}

	private MessageBuffer CreateCloseCall(string session)
	{
		using var writer = _connection.GetMessageWriter();
		writer.WriteMethodCallHeader(Service, session, SessionInterface, "Close", null, MessageFlags.None);
		return writer.CreateMessage();
	}

	private static Dictionary<string, string> ReadShortcuts(VariantValue list)
	{
		var shortcuts = new Dictionary<string, string>(StringComparer.Ordinal);
		for (var index = 0; index < list.Count; index++)
		{
			var shortcut = Unwrap(list.GetItem(index));
			var properties = new Dictionary<string, VariantValue>(StringComparer.Ordinal);
			var dictionary = Unwrap(shortcut.GetItem(1));
			for (var entry = 0; entry < dictionary.Count; entry++)
			{
				var (key, value) = dictionary.GetDictionaryEntry(entry);
				properties[Unwrap(key).GetString()] = value;
			}

			shortcuts[Unwrap(shortcut.GetItem(0)).GetString()] = TriggerDescription(properties);
		}

		return shortcuts;
	}

	private static string TriggerDescription(Dictionary<string, VariantValue> properties) =>
		properties.TryGetValue("trigger_description", out var value) && Unwrap(value) is { Type: VariantValueType.String } text
			? text.GetString()
			: "";

	private static VariantValue Unwrap(VariantValue value) =>
		value.Type == VariantValueType.Variant ? Unwrap(value.GetVariantValue()) : value;
}
