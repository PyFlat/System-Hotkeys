using MacroDeck.Localization;
using MacroDeck.Sdk.Notifications;
using SystemHotkeys.Hotkeys;

namespace SystemHotkeys;

internal sealed class HookAccessNotifications
{
	private const string KeyboardAccessKey = "keyboard-access-required";
	private const string AccessibilityKey = "accessibility-required";

	private static readonly bool _isLinux = OperatingSystem.IsLinux();

	// UserNotificationRequest takes plain strings.
	private static readonly LocalizationResolver _resolver = CreateResolver();

	private readonly Lock _gate = new();
	private readonly HashSet<string> _shown = [];

	internal void Update(IUserNotifier notifier, KeyboardHookAccess access, bool suppressionRequested)
	{
		lock (_gate)
		{
			Toggle(
				notifier,
				KeyboardAccessKey,
				access == KeyboardHookAccess.Denied,
				Strings.Notifications.KeyboardAccessRequired.Title(),
				_isLinux
					? Strings.Notifications.KeyboardAccessRequired.Message.Linux()
					: Strings.Notifications.KeyboardAccessRequired.Message.MacOs()
			);
			Toggle(
				notifier,
				AccessibilityKey,
				// Linux never swallows; the suppress toggle's description says what to do instead.
				!_isLinux && access == KeyboardHookAccess.ObserveOnly && suppressionRequested,
				Strings.Notifications.AccessibilityRequired.Title(),
				Strings.Notifications.AccessibilityRequired.Message()
			);
		}
	}

	internal void Reset()
	{
		lock (_gate)
		{
			_shown.Clear();
		}
	}

	private void Toggle(
		IUserNotifier notifier,
		string key,
		bool visible,
		LocalizedText title,
		LocalizedText message
	)
	{
		if (!visible)
		{
			if (_shown.Remove(key))
			{
				notifier.Dismiss(key);
			}

			return;
		}

		if (_shown.Add(key))
		{
			notifier.Notify(
				new UserNotificationRequest
				{
					Key = key,
					Level = UserNotificationLevel.Warning,
					Title = _resolver.Resolve(title, null) ?? string.Empty,
					Message = _resolver.Resolve(message, null),
				}
			);
		}
	}

	private static LocalizationResolver CreateResolver()
	{
		var catalogs = new LocalizationCatalogRegistry();
		catalogs.Register(Strings.LocalizationCatalog);
		return new LocalizationResolver(catalogs);
	}
}
