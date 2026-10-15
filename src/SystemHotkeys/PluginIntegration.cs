using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using Serilog;
using SystemHotkeys.Hotkeys;

namespace SystemHotkeys;

public sealed class PluginIntegration
    : IPluginIntegration,
        IEventProvider,
        IDynamicEventOptionsProvider,
        IIntegrationIssueProvider,
        IDisposable
{
    internal const string DevicesOptionsSourceId = "macrodeck.devices";

    internal const string HotkeyPressedEventId = "hotkey-pressed";
    internal const string ComboParameterName = "combo";
    internal const string SuppressParameterName = "suppress";
    internal const string DeviceParameterName = "deviceId";
    internal const string ProfileParameterName = "profileId";
    internal const string FolderParameterName = "folderId";

    internal const string KeyboardAccessIssueId = "keyboard-access-required";
    internal const string AccessibilityIssueId = "accessibility-required";
    internal const string ShortcutsNotTakenIssueId = "shortcuts-not-taken";

    private readonly ILogger _logger;
    private readonly KeyboardHook _hook;

    private IIntegrationContext? _context;
    private volatile bool _anyBindingIsScoped;
    private volatile bool _suppressionRequested;

    public PluginIntegration(ILogger logger)
    {
        _logger = logger.ForContext<PluginIntegration>();
        _hook = KeyboardHook.Create(logger);
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

        var requested = BoundHotkeyCombos.RequestedAnywhere(
            bindings,
            HotkeyPressedEventId,
            ComboParameterName,
            SuppressParameterName
        );
        _hook.SetRequestedSuppression(requested);

        _suppressionRequested = requested.Count > 0;
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

    public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(
        CancellationToken cancellationToken = default
    )
    {
        IReadOnlyList<IntegrationIssue> issues = _hook.Access switch
        {
            KeyboardHookAccess.Denied =>
            [
                new IntegrationIssue
                {
                    Id = KeyboardAccessIssueId,
                    Title = Strings.Issues.KeyboardAccessRequired.Title(),
                    Description = OperatingSystem.IsLinux()
                        ? Strings.Issues.KeyboardAccessRequired.Description.Linux()
                        : Strings.Issues.KeyboardAccessRequired.Description.MacOs(),
                    ActionLabel = Strings.Issues.KeyboardAccessRequired.Action(),
                    Severity = IntegrationIssueSeverity.Error,
                },
            ],
            KeyboardHookAccess.ObserveOnly when _suppressionRequested && OperatingSystem.IsLinux() =>
            [
                new IntegrationIssue
                {
                    Id = ShortcutsNotTakenIssueId,
                    Title = Strings.Issues.ShortcutsNotTaken.Title(),
                    Description = Strings.Issues.ShortcutsNotTaken.Description(),
                    ActionLabel = Strings.Issues.ShortcutsNotTaken.Action(),
                    Severity = IntegrationIssueSeverity.Warning,
                },
            ],
            KeyboardHookAccess.ObserveOnly when _suppressionRequested =>
            [
                new IntegrationIssue
                {
                    Id = AccessibilityIssueId,
                    Title = Strings.Issues.AccessibilityRequired.Title(),
                    Description = Strings.Issues.AccessibilityRequired.Description(),
                    Severity = IntegrationIssueSeverity.Warning,
                },
            ],
            _ => [],
        };

        return Task.FromResult(issues);
    }

    public async Task<IssueResolution> ResolveIssueAsync(
        string issueId,
        CancellationToken cancellationToken = default
    ) =>
        issueId switch
        {
            KeyboardAccessIssueId when _hook.Access != KeyboardHookAccess.Denied => IssueResolution.Ok(),
            KeyboardAccessIssueId => IssueResolution.Failed(
                Strings.Issues.KeyboardAccessRequired.StillDenied()
            ),
            ShortcutsNotTakenIssueId when await _hook.OpenShortcutSettingsAsync() => IssueResolution.Ok(),
            ShortcutsNotTakenIssueId => IssueResolution.Failed(
                Strings.Issues.ShortcutsNotTaken.CannotOpen()
            ),
            _ => IssueResolution.Failed(Strings.Issues.Unknown()),
        };

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
