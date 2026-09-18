using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Capabilities.Events;
using MacroDeck.Plugin.Testing;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using NUnit.Framework;

namespace SystemHotkeys.Tests;

[TestFixture]
public sealed class PluginIntegrationTests
{
    private static readonly string[] _configurationParameters =
        ["combo", "suppress", "deviceId", "profileId", "folderId"];
    private static readonly string[] _payloadParameters = ["combo", "deviceId", "profileId", "folderId"];
    private static readonly string[] _ctrlShift = ["Ctrl", "Shift"];

    private static PluginTestHarness CreateHarness() =>
        PluginTestHarness.Create(builder =>
            builder
                .UseLocalization(Strings.LocalizationCatalog)
                .RegisterIntegration<PluginIntegration>()
        );

    [Test]
    public async Task The_plugin_builds_and_initializes()
    {
        await using var harness = CreateHarness();

        Assert.DoesNotThrowAsync(harness.InitializeIntegrationsAsync);
    }

    [Test]
    public async Task The_event_declares_combo_suppress_client_profile_and_folder_configuration_and_matching_payload_parameters()
    {
        await using var harness = CreateHarness();
        await harness.InitializeIntegrationsAsync();

        var catalog = (await harness.Events.DescribeAsync()).DataAs<EventCatalogPayload>();
        var hotkeyPressed = catalog!.Events.Single(e => e.LocalId == "hotkey-pressed");

        Assert.That(
            hotkeyPressed.ConfigurationParameters.Select(p => p.Name),
            Is.EquivalentTo(_configurationParameters)
        );
        Assert.That(
            hotkeyPressed.ConfigurationParameters.Single(p => p.Name == "combo").Type,
            Is.EqualTo("KeyboardCombo")
        );
        var suppress = hotkeyPressed.ConfigurationParameters.Single(p => p.Name == "suppress");
        Assert.That(suppress.Type, Is.EqualTo("Boolean"));
        Assert.That(suppress.LiteralOnly, Is.True);
        foreach (var scopeParameter in new[] { "deviceId", "profileId", "folderId" })
        {
            var scope = hotkeyPressed.ConfigurationParameters.Single(p => p.Name == scopeParameter);
            Assert.That(scope.Type, Is.EqualTo("DynamicChoice"));
            Assert.That(scope.Required, Is.False);
        }
        Assert.That(
            hotkeyPressed.PayloadParameters.Select(p => p.Name),
            Is.EquivalentTo(_payloadParameters)
        );
        Assert.That(
            hotkeyPressed.PayloadParameters.Single(p => p.Name == "combo").Type,
            Is.EqualTo("KeyboardCombo")
        );
        Assert.That(catalog.HasDynamicEventOptions, Is.True);
    }

    [Test]
    public async Task The_device_parameter_defers_to_the_host_s_own_named_device_list()
    {
        await using var harness = CreateHarness();
        await harness.InitializeIntegrationsAsync();

        var catalog = (await harness.Events.DescribeAsync()).DataAs<EventCatalogPayload>();
        var hotkeyPressed = catalog!.Events.Single(e => e.LocalId == "hotkey-pressed");

        // Device options come from the host's own "macrodeck.devices" source, not GetEventOptionsAsync.
        foreach (var list in new[] { hotkeyPressed.ConfigurationParameters, hotkeyPressed.PayloadParameters })
        {
            var device = list.Single(p => p.Name == "deviceId");
            Assert.That(device.OptionsSourceId, Is.EqualTo("macrodeck.devices"));
        }
    }

    [Test]
    public async Task The_profile_parameter_offers_the_deck_s_configured_profiles_as_dynamic_options()
    {
        await using var harness = CreateHarness();
        harness.Context.Deck.SeedProfiles(
            new DeckProfile { Id = "streaming", Label = "Streaming" },
            new DeckProfile { Id = "gaming", Label = "Gaming" }
        );
        await harness.InitializeIntegrationsAsync();

        var response = await harness.Events.GetOptionsAsync(
            new EventOptionsArguments { EventId = "hotkey-pressed", ParameterName = "profileId" }
        );
        var options = response.DataAs<DynamicOptionsResultDto>();

        Assert.That(
            options!.Options.Select(o => o.Value),
            Is.EquivalentTo(["streaming", "gaming"])
        );
    }

    [Test]
    public async Task The_folder_parameter_offers_the_deck_s_configured_folders_as_dynamic_options()
    {
        await using var harness = CreateHarness();
        harness.Context.Deck.SeedFolders(
            new DeckFolder { Id = "folder-a", Label = "Folder A" },
            new DeckFolder { Id = "folder-b", Label = "Folder B" }
        );
        await harness.InitializeIntegrationsAsync();

        var response = await harness.Events.GetOptionsAsync(
            new EventOptionsArguments { EventId = "hotkey-pressed", ParameterName = "folderId" }
        );
        var options = response.DataAs<DynamicOptionsResultDto>();

        Assert.That(
            options!.Options.Select(o => o.Value),
            Is.EquivalentTo(["folder-a", "folder-b"])
        );
    }

    [Test]
    public async Task No_actions_are_declared()
    {
        await using var harness = CreateHarness();
        await harness.InitializeIntegrationsAsync();

        var describe = (await harness.Actions.DescribeAsync()).DataAs<ActionCatalogPayload>();

        Assert.That(describe!.Actions, Is.Empty);
    }

    [Test]
    public async Task A_binding_pushed_after_initialization_is_accepted_without_throwing()
    {
        await using var harness = CreateHarness();
        await harness.InitializeIntegrationsAsync();

        Assert.DoesNotThrow(
            () =>
                harness.Context.Events.SetBindings(
                    new EventBinding
                    {
                        EventId = "hotkey-pressed",
                        Parameters = new Dictionary<string, EventBindingValue>
                        {
                            ["combo"] = new EventBindingValue(
                                JsonSerializer.SerializeToElement(
                                    new { modifiers = _ctrlShift, key = "F3" }
                                ),
                                "=="
                            ),
                            ["suppress"] = new EventBindingValue(
                                JsonSerializer.SerializeToElement(true),
                                "=="
                            ),
                        },
                    }
                )
        );
    }
}

/// <summary>Guards the localization wiring, not wording.</summary>
[TestFixture]
public sealed class LocalizationTests
{
    [Test]
    public void The_catalog_is_scoped_to_the_plugin_id()
    {
        Assert.That(
            Strings.LocalizationCatalog.Scope,
            Is.EqualTo("plugin:com.pyflat.system-hotkeys")
        );
    }

    [Test]
    public void English_is_the_default_culture()
    {
        Assert.That(Strings.LocalizationCatalog.DefaultCulture, Is.EqualTo("en"));
        Assert.That(Strings.LocalizationCatalog.Cultures, Does.Contain("en"));
    }

    [Test]
    public void The_event_strings_come_from_the_catalog()
    {
        Assert.That(
            Strings.LocalizationCatalog.KeysOf("en"),
            Does.Contain("Events.HotkeyPressed.Name")
        );
    }

    [Test]
    public void Every_key_the_default_culture_declares_resolves_to_text()
    {
        foreach (var key in Strings.LocalizationCatalog.KeysOf("en"))
        {
            Assert.That(
                Strings.LocalizationCatalog.TryGetTemplate("en", key, out var text),
                Is.True
            );
            Assert.That(text, Is.Not.Empty);
        }
    }
}
