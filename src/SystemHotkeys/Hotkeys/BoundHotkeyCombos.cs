using System.Text.Json;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using Serilog;

namespace SystemHotkeys.Hotkeys;

public sealed class BoundHotkeyCombos
{
    public static readonly BoundHotkeyCombos None = new([]);

    private readonly HashSet<(string Key, string Modifiers)> _combos;

    private BoundHotkeyCombos(HashSet<(string, string)> combos) => _combos = combos;

    public int Count => _combos.Count;

    public bool Contains(HotkeyCombo combo) =>
        _combos.Contains(NormalizedKey(combo.Modifiers, combo.Key));

    // True only when fanning out per client could change which trigger reacts.
    public static bool AnyBindingIsScoped(
        IReadOnlyList<EventBinding> bindings,
        string eventId,
        string deviceParameterName,
        string profileParameterName,
        string folderParameterName
    ) =>
        bindings.Any(binding =>
            string.Equals(binding.EventId, eventId, StringComparison.Ordinal)
            && (
                binding.Parameters.ContainsKey(deviceParameterName)
                || binding.Parameters.ContainsKey(profileParameterName)
                || binding.Parameters.ContainsKey(folderParameterName)
            )
        );

    public static BoundHotkeyCombos FromBindings(
        IReadOnlyList<EventBinding> bindings,
        string eventId,
        string comboParameterName,
        string suppressParameterName,
        string deviceParameterName,
        string profileParameterName,
        string folderParameterName,
        IReadOnlyList<DeckClient> connectedClients,
        ILogger logger
    )
    {
        var combos = new HashSet<(string, string)>();

        foreach (var binding in bindings)
        {
            if (!string.Equals(binding.EventId, eventId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!IsSuppressionRequested(binding, suppressParameterName))
            {
                continue;
            }

            if (
                !IsScopeSatisfied(
                    binding,
                    deviceParameterName,
                    profileParameterName,
                    folderParameterName,
                    connectedClients
                )
            )
            {
                continue;
            }

            if (TryReadCombo(binding, comboParameterName, out var key, out var modifiers))
            {
                combos.Add(NormalizedKey(modifiers, key));
            }
        }

        logger.Information("Suppressing {Count} bound hotkey combo(s).", combos.Count);
        return combos.Count == 0 ? None : new BoundHotkeyCombos(combos);
    }

    private static bool IsSuppressionRequested(
        EventBinding binding,
        string suppressParameterName
    ) =>
        binding.Parameters.TryGetValue(suppressParameterName, out var value)
        && value is { Operator: "==", Value.ValueKind: JsonValueKind.True };

    // No filters set means unscoped. Filters that are set must all match one connected client at once.
    private static bool IsScopeSatisfied(
        EventBinding binding,
        string deviceParameterName,
        string profileParameterName,
        string folderParameterName,
        IReadOnlyList<DeckClient> connectedClients
    )
    {
        var matchesDevice = FilterPredicate(binding, deviceParameterName, client => client.DeviceId);
        var matchesProfile = FilterPredicate(binding, profileParameterName, client => client.ProfileId);
        var matchesFolder = FilterPredicate(binding, folderParameterName, client => client.FolderId);

        if (matchesDevice is null && matchesProfile is null && matchesFolder is null)
        {
            return true;
        }

        return connectedClients.Any(client =>
            (matchesDevice?.Invoke(client) ?? true)
            && (matchesProfile?.Invoke(client) ?? true)
            && (matchesFolder?.Invoke(client) ?? true)
        );
    }

    // Null means unset (matches everyone). An unexpected shape or operator matches no one.
    private static Func<DeckClient, bool>? FilterPredicate(
        EventBinding binding,
        string parameterName,
        Func<DeckClient, string?> select
    )
    {
        if (!binding.Parameters.TryGetValue(parameterName, out var value))
        {
            return null;
        }

        return value is { Operator: "==", Value: { ValueKind: JsonValueKind.String } text }
            ? client => select(client) == text.GetString()
            : _ => false;
    }

    private static bool TryReadCombo(
        EventBinding binding,
        string comboParameterName,
        out string key,
        out IReadOnlyList<string> modifiers
    )
    {
        key = "";
        modifiers = [];

        if (
            !binding.Parameters.TryGetValue(comboParameterName, out var value)
            || value.Operator != "=="
            || value.Value is not { ValueKind: JsonValueKind.Object } combo
            || !combo.TryGetProperty("key", out var keyElement)
            || keyElement.ValueKind != JsonValueKind.String
            || keyElement.GetString() is not { Length: > 0 } combinedKey
        )
        {
            return false;
        }

        key = combinedKey;
        modifiers =
            combo.TryGetProperty("modifiers", out var modifiersElement)
            && modifiersElement.ValueKind == JsonValueKind.Array
                ? modifiersElement
                    .EnumerateArray()
                    .Where(m => m.ValueKind == JsonValueKind.String)
                    .Select(m => m.GetString()!)
                    .ToList()
                : [];
        return true;
    }

    private static (string Key, string Modifiers) NormalizedKey(
        IEnumerable<string> modifiers,
        string key
    ) =>
        (
            key.ToUpperInvariant(),
            string.Join(
                ',',
                modifiers.Select(m => m.ToUpperInvariant()).OrderBy(m => m, StringComparer.Ordinal)
            )
        );
}
