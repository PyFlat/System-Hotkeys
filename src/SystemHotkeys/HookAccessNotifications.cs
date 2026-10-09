using MacroDeck.Localization;
using MacroDeck.Sdk.Notifications;
using SystemHotkeys.Hotkeys;

namespace SystemHotkeys;

/// <summary>
/// Tells the user which OS permission the keyboard hook is missing, and clears that again once granted.
/// </summary>
internal sealed class HookAccessNotifications
{
	private const string InputMonitoringKey = "input-monitoring-required";
	private const string AccessibilityKey = "accessibility-required";

	// UserNotificationRequest takes plain strings, so the plugin resolves its own text.
	private static readonly LocalizationResolver _resolver = CreateResolver();

	private readonly Lock _gate = new();
	private readonly HashSet<string> _shown = [];

	internal void Update(IUserNotifier notifier, KeyboardHookAccess access, bool suppressionRequested)
	{
		lock (_gate)
		{
			Toggle(
				notifier,
				InputMonitoringKey,
				access == KeyboardHookAccess.Denied,
				Strings.Notifications.InputMonitoringRequired.Title(),
				Strings.Notifications.InputMonitoringRequired.Message()
			);
			Toggle(
				notifier,
				AccessibilityKey,
				access == KeyboardHookAccess.ObserveOnly && suppressionRequested,
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
