using SystemHotkeys.Hotkeys;
using MacroDeck.Sdk.Decks;
using NUnit.Framework;

namespace SystemHotkeys.Tests.Hotkeys;

[TestFixture]
public sealed class HotkeyPressedPayloadsTests
{
    private const string ComboParameterName = "combo";
    private const string DeviceParameterName = "deviceId";
    private const string ProfileParameterName = "profileId";
    private const string FolderParameterName = "folderId";

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ForCombo(
        HotkeyCombo combo,
        params DeckClient[] connectedClients
    ) =>
        HotkeyPressedPayloads.ForCombo(
            combo,
            connectedClients,
            ComboParameterName,
            DeviceParameterName,
            ProfileParameterName,
            FolderParameterName
        );

    [Test]
    public void With_no_connected_client_it_publishes_one_occurrence_with_no_scope_at_all()
    {
        var payloads = ForCombo(new HotkeyCombo(["Ctrl"], "F3"));

        Assert.That(payloads, Has.Count.EqualTo(1));
        Assert.That(payloads[0][DeviceParameterName], Is.Null);
        Assert.That(payloads[0][ProfileParameterName], Is.Null);
        Assert.That(payloads[0][FolderParameterName], Is.Null);
    }

    [Test]
    public void With_one_connected_client_it_publishes_one_occurrence_carrying_that_client_s_state()
    {
        var payloads = ForCombo(
            new HotkeyCombo(["Ctrl"], "F3"),
            new DeckClient
            {
                ClientId = "c1",
                DeviceId = "my-phone",
                ProfileId = "streaming",
                FolderId = "scenes",
            }
        );

        Assert.That(payloads, Has.Count.EqualTo(1));
        Assert.That(payloads[0][DeviceParameterName], Is.EqualTo("my-phone"));
        Assert.That(payloads[0][ProfileParameterName], Is.EqualTo("streaming"));
        Assert.That(payloads[0][FolderParameterName], Is.EqualTo("scenes"));
    }

    [Test]
    public void A_client_with_no_paired_device_carries_no_device_id()
    {
        var payloads = ForCombo(
            new HotkeyCombo(["Ctrl"], "F3"),
            new DeckClient
            {
                ClientId = "c1",
                ProfileId = "streaming",
                FolderId = "scenes",
            }
        );

        Assert.That(payloads[0][DeviceParameterName], Is.Null);
    }

    [Test]
    public void With_several_connected_clients_it_publishes_one_occurrence_per_client()
    {
        var payloads = ForCombo(
            new HotkeyCombo(["Ctrl"], "F3"),
            new DeckClient
            {
                ClientId = "c1",
                DeviceId = "my-phone",
                ProfileId = "main",
                FolderId = "folder-a",
            },
            new DeckClient
            {
                ClientId = "c2",
                DeviceId = "my-tablet",
                ProfileId = "main",
                FolderId = "folder-b",
            }
        );

        Assert.That(payloads, Has.Count.EqualTo(2));
        Assert.That(
            payloads.Select(p => p[DeviceParameterName]),
            Is.EquivalentTo(["my-phone", "my-tablet"])
        );
        Assert.That(
            payloads.Select(p => p[FolderParameterName]),
            Is.EquivalentTo(["folder-a", "folder-b"])
        );
    }

    [Test]
    public void Every_occurrence_carries_the_same_combo_value()
    {
        var combo = new HotkeyCombo(["Ctrl", "Shift"], "F3");

        var payloads = ForCombo(
            combo,
            new DeckClient
            {
                ClientId = "c1",
                ProfileId = "main",
                FolderId = "folder-a",
            },
            new DeckClient
            {
                ClientId = "c2",
                ProfileId = "main",
                FolderId = "folder-b",
            }
        );

        // Must be the same object, not two separately built copies.
        Assert.That(payloads[0][ComboParameterName], Is.SameAs(payloads[1][ComboParameterName]));

        // The anonymous combo type is internal to the plugin assembly; read it back via reflection.
        var comboValue = payloads[0][ComboParameterName]!;
        var comboType = comboValue.GetType();
        Assert.That(comboType.GetProperty("key")!.GetValue(comboValue), Is.EqualTo("F3"));
        Assert.That(
            comboType.GetProperty("modifiers")!.GetValue(comboValue),
            Is.EqualTo(combo.Modifiers)
        );
    }
}
