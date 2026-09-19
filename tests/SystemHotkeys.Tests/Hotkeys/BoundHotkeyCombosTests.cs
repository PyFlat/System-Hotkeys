using System.Text.Json;
using SystemHotkeys.Hotkeys;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using NUnit.Framework;
using Serilog;

namespace SystemHotkeys.Tests.Hotkeys;

[TestFixture]
public sealed class BoundHotkeyCombosTests
{
    private const string EventId = "hotkey-pressed";
    private const string ComboParameterName = "combo";
    private const string SuppressParameterName = "suppress";
    private const string DeviceParameterName = "deviceId";
    private const string ProfileParameterName = "profileId";
    private const string FolderParameterName = "folderId";

    private static readonly ILogger _logger = new LoggerConfiguration().CreateLogger();
    private static readonly string[] _ctrlOnly = ["Ctrl"];
    private static readonly DeckClient[] _noClients = [];

    [OneTimeTearDown]
    public void DisposeLogger() => (_logger as IDisposable)?.Dispose();

    private static BoundHotkeyCombos FromBindings(
        IReadOnlyList<DeckClient> connectedClients,
        params EventBinding[] bindings
    ) =>
        BoundHotkeyCombos.FromBindings(
            bindings,
            EventId,
            ComboParameterName,
            SuppressParameterName,
            DeviceParameterName,
            ProfileParameterName,
            FolderParameterName,
            connectedClients,
            _logger
        );

    private static BoundHotkeyCombos FromBindings(params EventBinding[] bindings) =>
        FromBindings(_noClients, bindings);

    // deviceId is what a Device-scoped binding matches on; clientId just distinguishes clients here.
    private static DeckClient Client(
        string clientId,
        string profileId,
        string folderId,
        string? deviceId = null
    ) =>
        new()
        {
            ClientId = clientId,
            DeviceId = deviceId,
            ProfileId = profileId,
            FolderId = folderId,
        };

    private static EventBindingValue Combo(
        IEnumerable<string> modifiers,
        string key,
        string @operator = "=="
    ) => new(JsonSerializer.SerializeToElement(new { modifiers, key }), @operator);

    private static EventBindingValue Bool(bool value, string @operator = "==") =>
        new(JsonSerializer.SerializeToElement(value), @operator);

    private static EventBindingValue Text(string value, string @operator = "==") =>
        new(JsonSerializer.SerializeToElement(value), @operator);

    private static EventBinding Binding(
        string eventId,
        EventBindingValue? combo = null,
        EventBindingValue? suppress = null,
        EventBindingValue? device = null,
        EventBindingValue? profile = null,
        EventBindingValue? folder = null
    )
    {
        var parameters = new Dictionary<string, EventBindingValue>();
        if (combo is not null)
        {
            parameters[ComboParameterName] = combo;
        }

        if (suppress is not null)
        {
            parameters[SuppressParameterName] = suppress;
        }

        if (device is not null)
        {
            parameters[DeviceParameterName] = device;
        }

        if (profile is not null)
        {
            parameters[ProfileParameterName] = profile;
        }

        if (folder is not null)
        {
            parameters[FolderParameterName] = folder;
        }

        return new EventBinding { EventId = eventId, Parameters = parameters };
    }

    [Test]
    public void No_bindings_suppresses_nothing()
    {
        var suppressed = FromBindings();

        Assert.That(suppressed.Count, Is.EqualTo(0));
    }

    [Test]
    public void A_binding_opted_into_suppression_is_contained()
    {
        var suppressed = FromBindings(Binding(EventId, Combo(["Ctrl", "Shift"], "F3"), Bool(true)));

        Assert.That(suppressed.Contains(new HotkeyCombo(["Ctrl", "Shift"], "F3")), Is.True);
    }

    [Test]
    public void Unset_scope_filters_sent_as_null_values_leave_the_binding_unscoped()
    {
        var nullFilter = new EventBindingValue(JsonSerializer.SerializeToElement<string?>(null), "==");
        var binding = Binding(
            EventId,
            Combo([], "NumpadMultiply"),
            Bool(true),
            nullFilter,
            nullFilter,
            nullFilter
        );

        Assert.That(FromBindings(binding).Contains(new HotkeyCombo([], "NumpadMultiply")), Is.True);
        Assert.That(
            BoundHotkeyCombos.AnyBindingIsScoped(
                [binding],
                EventId,
                DeviceParameterName,
                ProfileParameterName,
                FolderParameterName
            ),
            Is.False
        );
    }

    [Test]
    public void A_binding_that_did_not_opt_in_is_not_contained()
    {
        var suppressed = FromBindings(
            Binding(EventId, Combo(["Ctrl", "Shift"], "F3"), Bool(false))
        );

        Assert.That(suppressed.Contains(new HotkeyCombo(["Ctrl", "Shift"], "F3")), Is.False);
    }

    [Test]
    public void A_binding_with_no_suppress_parameter_at_all_is_not_contained()
    {
        // Parameter absent (not false) must also read as "do not suppress".
        var suppressed = FromBindings(Binding(EventId, Combo([], "F13")));

        Assert.That(suppressed.Contains(new HotkeyCombo([], "F13")), Is.False);
    }

    [Test]
    public void A_binding_for_a_different_event_is_ignored()
    {
        var suppressed = FromBindings(
            Binding("some-other-event", Combo(["Ctrl"], "F3"), Bool(true))
        );

        Assert.That(suppressed.Count, Is.EqualTo(0));
    }

    [TestCase("!=")]
    [TestCase("isNotEmpty")]
    public void A_suppress_parameter_under_any_operator_but_equals_is_not_contained(
        string @operator
    )
    {
        var suppressed = FromBindings(
            Binding(EventId, Combo(["Ctrl"], "F3"), Bool(true, @operator))
        );

        Assert.That(suppressed.Contains(new HotkeyCombo(["Ctrl"], "F3")), Is.False);
    }

    [TestCase("!=")]
    [TestCase("isAvailable")]
    public void A_combo_parameter_under_any_operator_but_equals_is_not_contained(string @operator)
    {
        var suppressed = FromBindings(
            Binding(EventId, Combo(["Ctrl"], "F3", @operator), Bool(true))
        );

        Assert.That(suppressed.Contains(new HotkeyCombo(["Ctrl"], "F3")), Is.False);
    }

    [Test]
    public void A_combo_that_is_a_variable_reference_rather_than_a_structured_value_is_skipped_without_throwing()
    {
        var reference = new EventBindingValue(
            JsonSerializer.SerializeToElement("$var:some_variable"),
            "=="
        );

        Assert.DoesNotThrow(() => FromBindings(Binding(EventId, reference, Bool(true))));
        Assert.That(FromBindings(Binding(EventId, reference, Bool(true))).Count, Is.EqualTo(0));
    }

    [Test]
    public void A_combo_missing_its_key_property_is_skipped_without_throwing()
    {
        var malformed = new EventBindingValue(
            JsonSerializer.SerializeToElement(new { modifiers = _ctrlOnly }),
            "=="
        );

        Assert.DoesNotThrow(() => FromBindings(Binding(EventId, malformed, Bool(true))));
    }

    [Test]
    public void A_state_operator_carrying_no_value_is_skipped_without_throwing()
    {
        var stateValue = new EventBindingValue(null, "isEmpty");

        Assert.DoesNotThrow(() => FromBindings(Binding(EventId, stateValue, Bool(true))));
    }

    [Test]
    public void Matching_is_case_and_modifier_order_insensitive()
    {
        var suppressed = FromBindings(Binding(EventId, Combo(["shift", "CTRL"], "f3"), Bool(true)));

        Assert.That(suppressed.Contains(new HotkeyCombo(["Ctrl", "Shift"], "F3")), Is.True);
    }

    [Test]
    public void A_combo_with_extra_or_missing_modifiers_is_not_contained()
    {
        var suppressed = FromBindings(Binding(EventId, Combo(["Ctrl", "Shift"], "F3"), Bool(true)));

        Assert.That(suppressed.Contains(new HotkeyCombo(["Ctrl"], "F3")), Is.False);
        Assert.That(suppressed.Contains(new HotkeyCombo(["Ctrl", "Shift", "Alt"], "F3")), Is.False);
    }

    [Test]
    public void Multiple_opted_in_bindings_are_all_contained()
    {
        var suppressed = FromBindings(
            Binding(EventId, Combo([], "+"), Bool(true)),
            Binding(EventId, Combo(["Alt"], "*"), Bool(true)),
            Binding(EventId, Combo(["Ctrl", "Shift"], "F3"), Bool(false))
        );

        Assert.That(suppressed.Count, Is.EqualTo(2));
        Assert.That(suppressed.Contains(new HotkeyCombo([], "+")), Is.True);
        Assert.That(suppressed.Contains(new HotkeyCombo(["Alt"], "*")), Is.True);
        Assert.That(suppressed.Contains(new HotkeyCombo(["Ctrl", "Shift"], "F3")), Is.False);
    }

    [Test]
    public void A_binding_with_no_scope_filters_is_contained_regardless_of_connected_clients()
    {
        var suppressed = FromBindings(
            [Client("c1", "streaming", "scenes")],
            Binding(EventId, Combo(["Ctrl"], "F3"), Bool(true))
        );

        Assert.That(suppressed.Contains(new HotkeyCombo(["Ctrl"], "F3")), Is.True);
    }

    [Test]
    public void A_binding_scoped_to_a_profile_some_client_currently_has_open_is_contained()
    {
        var suppressed = FromBindings(
            [Client("c1", "streaming", "scenes")],
            Binding(EventId, Combo(["Ctrl"], "F3"), Bool(true), profile: Text("streaming"))
        );

        Assert.That(suppressed.Contains(new HotkeyCombo(["Ctrl"], "F3")), Is.True);
    }

    [Test]
    public void A_binding_scoped_to_a_profile_no_connected_client_has_open_is_not_contained()
    {
        var suppressed = FromBindings(
            [Client("c1", "gaming", "scenes")],
            Binding(EventId, Combo(["Ctrl"], "F3"), Bool(true), profile: Text("streaming"))
        );

        Assert.That(suppressed.Contains(new HotkeyCombo(["Ctrl"], "F3")), Is.False);
    }

    [Test]
    public void A_binding_scoped_to_a_profile_is_not_contained_when_no_client_is_connected()
    {
        var suppressed = FromBindings(
            _noClients,
            Binding(EventId, Combo(["Ctrl"], "F3"), Bool(true), profile: Text("streaming"))
        );

        Assert.That(suppressed.Contains(new HotkeyCombo(["Ctrl"], "F3")), Is.False);
    }

    [Test]
    public void A_binding_scoped_to_a_folder_some_client_currently_has_open_is_contained()
    {
        var suppressed = FromBindings(
            [Client("c1", "main", "folder-b")],
            Binding(EventId, Combo(["Ctrl", "Right"], "F3"), Bool(true), folder: Text("folder-b"))
        );

        Assert.That(suppressed.Contains(new HotkeyCombo(["Ctrl", "Right"], "F3")), Is.True);
    }

    [Test]
    public void Only_the_binding_scoped_to_the_currently_open_folder_is_contained()
    {
        // Same combo bound once per folder; only the currently open folder's copy should suppress.
        var combo = new HotkeyCombo(["Ctrl"], "Right");
        var connected = new[] { Client("c1", "main", "folder-b") };

        var suppressed = FromBindings(
            connected,
            Binding(EventId, Combo(["Ctrl"], "Right"), Bool(true), folder: Text("folder-a")),
            Binding(EventId, Combo(["Ctrl"], "Right"), Bool(true), folder: Text("folder-b")),
            Binding(EventId, Combo(["Ctrl"], "Right"), Bool(true), folder: Text("folder-c"))
        );

        Assert.That(suppressed.Contains(combo), Is.True);

        var onlyFolderAAndC = FromBindings(
            connected,
            Binding(EventId, Combo(["Ctrl"], "Left"), Bool(true), folder: Text("folder-a")),
            Binding(EventId, Combo(["Ctrl"], "Left"), Bool(true), folder: Text("folder-c"))
        );
        Assert.That(onlyFolderAAndC.Contains(new HotkeyCombo(["Ctrl"], "Left")), Is.False);
    }

    [Test]
    public void A_binding_scoped_to_a_specific_device_ignores_a_matching_folder_on_a_different_device()
    {
        var suppressed = FromBindings(
            [Client("c1", "main", "folder-b", deviceId: "other-device")],
            Binding(
                EventId,
                Combo(["Ctrl"], "F3"),
                Bool(true),
                device: Text("my-device"),
                folder: Text("folder-b")
            )
        );

        Assert.That(suppressed.Contains(new HotkeyCombo(["Ctrl"], "F3")), Is.False);
    }

    [Test]
    public void A_binding_scoped_to_a_device_and_folder_together_requires_both_on_the_same_device()
    {
        var suppressed = FromBindings(
            [
                Client("c1", "main", "folder-a", deviceId: "my-device"),
                Client("c2", "main", "folder-b", deviceId: "other-device"),
            ],
            Binding(
                EventId,
                Combo(["Ctrl"], "F3"),
                Bool(true),
                device: Text("my-device"),
                folder: Text("folder-b")
            )
        );

        // folder-b is open on the other device, not my-device; must not match by mixing state.
        Assert.That(suppressed.Contains(new HotkeyCombo(["Ctrl"], "F3")), Is.False);
    }

    [Test]
    public void A_binding_scoped_to_a_device_a_connected_client_currently_has_paired_is_contained()
    {
        var suppressed = FromBindings(
            [Client("c1", "main", "folder-a", deviceId: "my-phone")],
            Binding(EventId, Combo(["Ctrl"], "F3"), Bool(true), device: Text("my-phone"))
        );

        Assert.That(suppressed.Contains(new HotkeyCombo(["Ctrl"], "F3")), Is.True);
    }

    [Test]
    public void A_binding_scoped_to_a_device_is_not_contained_for_a_client_with_no_paired_device()
    {
        var suppressed = FromBindings(
            [Client("c1", "main", "folder-a")],
            Binding(EventId, Combo(["Ctrl"], "F3"), Bool(true), device: Text("my-phone"))
        );

        Assert.That(suppressed.Contains(new HotkeyCombo(["Ctrl"], "F3")), Is.False);
    }

    [TestCase("!=")]
    [TestCase("isNotEmpty")]
    public void A_scope_filter_under_any_operator_but_equals_is_not_contained(string @operator)
    {
        var suppressed = FromBindings(
            [Client("c1", "streaming", "scenes")],
            Binding(EventId, Combo(["Ctrl"], "F3"), Bool(true), profile: Text("streaming", @operator))
        );

        Assert.That(suppressed.Contains(new HotkeyCombo(["Ctrl"], "F3")), Is.False);
    }

    [Test]
    public void No_bound_trigger_using_device_profile_or_folder_is_reported_as_unscoped()
    {
        var isScoped = BoundHotkeyCombos.AnyBindingIsScoped(
            [Binding(EventId, Combo(["Ctrl"], "F3"), Bool(true))],
            EventId,
            DeviceParameterName,
            ProfileParameterName,
            FolderParameterName
        );

        Assert.That(isScoped, Is.False);
    }

    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    [TestCase(false, false, true)]
    public void A_bound_trigger_using_any_one_of_device_profile_or_folder_is_reported_as_scoped(
        bool device,
        bool profile,
        bool folder
    )
    {
        var isScoped = BoundHotkeyCombos.AnyBindingIsScoped(
            [
                Binding(
                    EventId,
                    Combo(["Ctrl"], "F3"),
                    Bool(true),
                    device: device ? Text("my-phone") : null,
                    profile: profile ? Text("streaming") : null,
                    folder: folder ? Text("scenes") : null
                ),
            ],
            EventId,
            DeviceParameterName,
            ProfileParameterName,
            FolderParameterName
        );

        Assert.That(isScoped, Is.True);
    }

    [Test]
    public void A_scoped_binding_for_a_different_event_does_not_count()
    {
        var isScoped = BoundHotkeyCombos.AnyBindingIsScoped(
            [Binding("some-other-event", Combo(["Ctrl"], "F3"), Bool(true), profile: Text("streaming"))],
            EventId,
            DeviceParameterName,
            ProfileParameterName,
            FolderParameterName
        );

        Assert.That(isScoped, Is.False);
    }
}
