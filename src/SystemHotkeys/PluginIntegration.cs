using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using Serilog;
using SystemHotkeys.Hotkeys;

namespace SystemHotkeys;

public sealed class PluginIntegration
    : IPluginIntegration,
        IEventProvider,
        IDynamicEventOptionsProvider,
        IDisposable
{
    internal const string DevicesOptionsSourceId = "macrodeck.devices";

    internal const string HotkeyPressedEventId = "hotkey-pressed";
    internal const string ComboParameterName = "combo";
    internal const string SuppressParameterName = "suppress";
    internal const string DeviceParameterName = "deviceId";
    internal const string ProfileParameterName = "profileId";
    internal const string FolderParameterName = "folderId";

    private readonly ILogger _logger;
    private readonly GlobalKeyboardHook _hook;

    private IIntegrationContext? _context;
    private volatile bool _anyBindingIsScoped;

    public PluginIntegration(ILogger logger)
    {
        _logger = logger.ForContext<PluginIntegration>();
        _hook = new GlobalKeyboardHook(logger);
        _hook.HotkeyPressed += OnHotkeyPressed;
    }

    public IReadOnlyList<IActionDefinition> Actions { get; } = [];

    public Task InitializeAsync(IIntegrationContext context)
    {
        _context = context;

        context.Events.BindingsChanged -= ApplyBoundCombos;
        context.Events.BindingsChanged += ApplyBoundCombos;
        context.Deck.ClientChanged -= OnDeckClientChanged;
        context.Deck.ClientChanged += OnDeckClientChanged;
        ApplyBoundCombos();

        _hook.Start();
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        if (_context is { } context)
        {
            context.Events.BindingsChanged -= ApplyBoundCombos;
            context.Deck.ClientChanged -= OnDeckClientChanged;
        }

        _hook.Dispose();
        _context = null;
        return Task.CompletedTask;
    }

    public void Dispose() => _hook.Dispose();

    public IReadOnlyList<EventDefinition> EventDefinitions { get; } =
        [
            new EventDefinition
            {
                Id = HotkeyPressedEventId,
                Name = Strings.Events.HotkeyPressed.Name(),
                Description = Strings.Events.HotkeyPressed.Description(),
                ConfigurationParameters =
                [
                    ActionParameter.KeyboardCombo(
                        ComboParameterName,
                        label: Strings.Events.HotkeyPressed.Combo.Label(),
                        description: Strings.Events.HotkeyPressed.Combo.Description(),
                        required: true
                    ),
                    ActionParameter.Toggle(
                        SuppressParameterName,
                        label: Strings.Events.HotkeyPressed.Suppress.Label(),
                        description: Strings.Events.HotkeyPressed.Suppress.Description(),
                        literalOnly: true
                    ),
                    ActionParameter.DynamicChoice(
                        DeviceParameterName,
                        label: Strings.Events.HotkeyPressed.Device.Label(),
                        description: Strings.Events.HotkeyPressed.Device.Description(),
                        optionsSourceId: DevicesOptionsSourceId,
                        placeholder: Strings.Events.HotkeyPressed.Device.Placeholder()
                    ),
                    ActionParameter.DynamicChoice(
                        ProfileParameterName,
                        label: Strings.Events.HotkeyPressed.Profile.Label(),
                        description: Strings.Events.HotkeyPressed.Profile.Description(),
                        placeholder: Strings.Events.HotkeyPressed.Profile.Placeholder()
                    ),
                    ActionParameter.DynamicChoice(
                        FolderParameterName,
                        label: Strings.Events.HotkeyPressed.Folder.Label(),
                        description: Strings.Events.HotkeyPressed.Folder.Description(),
                        placeholder: Strings.Events.HotkeyPressed.Folder.Placeholder()
                    ),
                ],
                PayloadParameters =
                [
                    ActionParameter.KeyboardCombo(
                        ComboParameterName,
                        label: Strings.Events.HotkeyPressed.Combo.Label()
                    ),
                    ActionParameter.DynamicChoice(
                        DeviceParameterName,
                        label: Strings.Events.HotkeyPressed.Device.Label(),
                        optionsSourceId: DevicesOptionsSourceId
                    ),
                    ActionParameter.DynamicChoice(
                        ProfileParameterName,
                        label: Strings.Events.HotkeyPressed.Profile.Label()
                    ),
                    ActionParameter.DynamicChoice(
                        FolderParameterName,
                        label: Strings.Events.HotkeyPressed.Folder.Label()
                    ),
                ],
            },
        ];

    private void ApplyBoundCombos()
    {
        if (_context is not { } context)
        {
            return;
        }

        var bindings = context.Events.GetBindings();

        _anyBindingIsScoped = BoundHotkeyCombos.AnyBindingIsScoped(
            bindings,
            HotkeyPressedEventId,
            DeviceParameterName,
            ProfileParameterName,
            FolderParameterName
        );

        var bound = BoundHotkeyCombos.FromBindings(
            bindings,
            HotkeyPressedEventId,
            ComboParameterName,
            SuppressParameterName,
            DeviceParameterName,
            ProfileParameterName,
            FolderParameterName,
            context.Deck.GetClients(),
            _logger
        );
        _hook.SetSuppressed(bound);
    }

    // Moving folders never raises BindingsChanged, so watch it separately
    private void OnDeckClientChanged(object? sender, DeckClientChangedEventArgs e) =>
        ApplyBoundCombos();

    private void OnHotkeyPressed(HotkeyCombo combo)
    {
        if (_context is not { } context)
        {
            return;
        }

        // Only fan out per client when something is actually scoped; see HotkeyPressedPayloads.
        var connectedClients = _anyBindingIsScoped ? context.Deck.GetClients() : [];
        foreach (
            var payload in HotkeyPressedPayloads.ForCombo(
                combo,
                connectedClients,
                ComboParameterName,
                DeviceParameterName,
                ProfileParameterName,
                FolderParameterName
            )
        )
        {
            context.Events.Publish(HotkeyPressedEventId, payload);
        }
    }

    // Device resolves through the "macrodeck.devices" options source, not here.
    public Task<DynamicOptionsResult> GetEventOptionsAsync(
        EventOptionsContext context,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<ActionParameterOption> options = _context is { } integrationContext
            ? context.ParameterName switch
            {
                ProfileParameterName => integrationContext
                    .Deck.GetProfiles()
                    .Select(profile => new ActionParameterOption
                    {
                        Value = profile.Id,
                        Label = profile.Label,
                    })
                    .ToList(),
                FolderParameterName =>
                [
                    .. integrationContext
                        .Deck.GetFolders()
                        .Select(folder => new ActionParameterOption
                        {
                            Value = folder.Id,
                            Label = folder.Label,
                        }),
                ],
                _ => [],
            }
            : [];

        return Task.FromResult(new DynamicOptionsResult { Options = options });
    }
}
