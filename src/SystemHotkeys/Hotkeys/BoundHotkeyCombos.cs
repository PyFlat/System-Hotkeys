using System.Text.Json;
using MacroDeck.Sdk.Input;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using Serilog;

namespace SystemHotkeys.Hotkeys;

public sealed class BoundHotkeyCombos
{
    public static readonly BoundHotkeyCombos None = new([], []);

    private readonly HashSet<(string Key, string Modifiers)> _combos;

    private BoundHotkeyCombos(
        HashSet<(string Key, string Modifiers)> combos,
        IReadOnlyList<(KeyModifier Modifiers, KeyCode Key)> parsed
    )
    {
        _combos = combos;
        Parsed = parsed;
    }

    public int Count => _combos.Count;

    // Only the combos whose key and modifiers all have a known name.
    public IReadOnlyList<(KeyModifier Modifiers, KeyCode Key)> Parsed { get; }

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
                IsFilterSet(binding, deviceParameterName)
                || IsFilterSet(binding, profileParameterName)
                || IsFilterSet(binding, folderParameterName)
            )
        );

    private static bool IsFilterSet(EventBinding binding, string parameterName) =>
        binding.Parameters.TryGetValue(parameterName, out var value)
        && value is not { Operator: "==", Value: null or { ValueKind: JsonValueKind.Null } };

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
        var suppressed = Collect(
            bindings,
            eventId,
            comboParameterName,
            suppressParameterName,
            binding =>
                IsScopeSatisfied(
                    binding,
                    deviceParameterName,
                    profileParameterName,
                    folderParameterName,
                    connectedClients
                )
        );

        logger.Information("Suppressing {Count} bound hotkey combo(s).", suppressed.Count);
        return suppressed;
    }

    // Every combo some trigger asks to suppress, whether or not its scope applies right now.
    public static BoundHotkeyCombos RequestedAnywhere(
        IReadOnlyList<EventBinding> bindings,
        string eventId,
        string comboParameterName,
        string suppressParameterName
    ) => Collect(bindings, eventId, comboParameterName, suppressParameterName, _ => true);

    private static BoundHotkeyCombos Collect(
        IReadOnlyList<EventBinding> bindings,
        string eventId,
        string comboParameterName,
        string suppressParameterName,
        Func<EventBinding, bool> isInScope
    )
    {
        var combos = new HashSet<(string, string)>();
        var parsed = new List<(KeyModifier, KeyCode)>();

        foreach (var binding in bindings)
        {
            if (
                !string.Equals(binding.EventId, eventId, StringComparison.Ordinal)
                || !IsSuppressionRequested(binding, suppressParameterName)
                || !isInScope(binding)
                || !TryReadCombo(binding, comboParameterName, out var key, out var modifiers)
            )
            {
                continue;
            }

            if (combos.Add(NormalizedKey(modifiers, key)) && TryParse(modifiers, key, out var combo))
            {
                parsed.Add(combo);
            }
        }

        return combos.Count == 0 ? None : new BoundHotkeyCombos(combos, parsed);
    }

    private static bool TryParse(
        IReadOnlyList<string> modifiers,
        string key,
        out (KeyModifier Modifiers, KeyCode Key) combo
    )
    {
        combo = default;
        if (!KeyNames.TryParse(key, out var code))
        {
            return false;
        }

        var known = KeyModifier.None;
        foreach (var modifier in modifiers)
        {
            if (!KeyNames.TryParseModifier(modifier, out var resolved))
            {
                return false;
            }

            known |= resolved;
        }

        combo = (known, code);
        return true;
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
        var matchesDevice = FilterPredicate(
            binding,
            deviceParameterName,
            client => client.DeviceId
        );
        var matchesProfile = FilterPredicate(
            binding,
            profileParameterName,
            client => client.ProfileId
        );
        var matchesFolder = FilterPredicate(
            binding,
            folderParameterName,
            client => client.FolderId
        );

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
        if (!IsFilterSet(binding, parameterName))
        {
            return null;
        }

        var value = binding.Parameters[parameterName];
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

    // Same rule as the host's trigger matching: known spellings resolve, unknown ones compare as text.
    private static (string Key, string Modifiers) NormalizedKey(
        IEnumerable<string> modifiers,
        string key
    )
    {
        var known = KeyModifier.None;
        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var modifier in modifiers)
        {
            if (KeyNames.TryParseModifier(modifier, out var resolved))
            {
                known |= resolved;
            }
            else
            {
                unknown.Add(modifier.ToUpperInvariant());
            }
        }

        var keyName = KeyNames.TryParse(key, out var code) ? code.ToString() : key.ToUpperInvariant();
        return (keyName, $"{known}+{string.Join(',', unknown)}");
    }
}
