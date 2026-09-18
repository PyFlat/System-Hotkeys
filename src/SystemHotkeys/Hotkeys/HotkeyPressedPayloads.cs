using MacroDeck.Sdk.Decks;

namespace SystemHotkeys.Hotkeys;

public static class HotkeyPressedPayloads
{
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> ForCombo(
        HotkeyCombo combo,
        IReadOnlyList<DeckClient> connectedClients,
        string comboParameterName,
        string deviceParameterName,
        string profileParameterName,
        string folderParameterName
    )
    {
        var comboValue = new { modifiers = combo.Modifiers, key = combo.Key };

        IReadOnlyDictionary<string, object?> PayloadFor(DeckClient? client) =>
            new Dictionary<string, object?>
            {
                [comboParameterName] = comboValue,
                [deviceParameterName] = client?.DeviceId,
                [profileParameterName] = client?.ProfileId,
                [folderParameterName] = client?.FolderId,
            };

        return connectedClients.Count == 0
            ? [PayloadFor(null)]
            : [.. connectedClients.Select(client => PayloadFor(client))];
    }
}
