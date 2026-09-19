# System Hotkeys

A [Macro Deck 3](https://macro-deck.app/) plugin (Windows only) that installs a global keyboard hook and
publishes a `hotkey-pressed` event whenever a modifier+key combination is pressed anywhere on the
machine - press Alt+F1 while the browser has focus, and Macro Deck still sees it. This is what Macro
Deck 2's per-button hotkey assignment turns into in Macro Deck 3: bind any Automation or widget flow to
`hotkey-pressed` as an event trigger, and it runs whenever the combo you picked fires. The hook is
passive by default (every key still reaches whatever app is focused) with one opt-in exception, set
right on the trigger itself - see
["Keeping a hotkey from reaching other apps"](#keeping-a-hotkey-from-reaching-other-apps) below. A
hotkey is also global by default, firing everywhere - see
["Scoping a hotkey to a device, profile or folder"](#scoping-a-hotkey-to-a-device-profile-or-folder) to
narrow it down.

## Using this plugin

1. Install and run the plugin against your Macro Deck instance (see "Run and debug against Macro Deck"
   below for development, or pack and install it for real use). Nothing to set up first - the plugin has
   no configuration of its own; every setting lives on the trigger itself.
2. In Macro Deck, open the **Automations** page (or a widget's own flow editor) and add a trigger of type
   **Event**, bound to this plugin's `Hotkey pressed` event.
3. Set the trigger's **Hotkey** field. It is Macro Deck's keyboard-combo editor: click **Record** and
   press the combo. That works for letters, digits, `F1`-`F24`, the navigation and editing block, the
   punctuation keys, every numpad key (digits, `+`, `-`, `*`, `/` and `.`, which Macro Deck records as
   distinct from the main-row keys) and the media keys. Picking a key from the list in **Select** mode
   stores the same values.
4. Build whatever the Automation or flow should do when that hotkey fires.

A key with no modifier is allowed. A bare non-typing key (a function or media key) is a fine global
hotkey; a bare letter or digit fires the trigger on **every** press of it anywhere on the machine, so
reach for one only when that is genuinely what you want. A key your keyboard's own software rewrites
(an `Fn` layer that turns `Fn+F4` into numpad `+`, for instance) reaches the plugin only as whatever it
was rewritten to - `Fn` itself is never visible to Windows. The hook only observes: it never blocks or
alters a keystroke for any other application, unless you turn that on for a specific trigger - next.

## Keeping a hotkey from reaching other apps

The hook is passive by default: every key you press still reaches whatever application is focused,
exactly as if the plugin were not running. That is a problem for a combo whose trailing key also means
something to that application - `Ctrl+Shift+F3` toggling a game's debug overlay every time it is used to
mute, for instance.

This is a second field right on the same trigger, next to **Hotkey**:

1. On the same **Event** trigger you set the **Hotkey** field on, enable **Also stop this key from
   reaching other apps**.
2. Save the Automation or flow. That is the whole setup - there is no separate settings screen, and
   nothing to enter twice. The plugin reads what triggers are actually bound to `hotkey-pressed` (and
   which of them opted in) straight from Macro Deck itself, and stays current automatically whenever a
   trigger's combo or this toggle is edited, added or removed.

The plugin logs at Information level, every time the bound triggers change, `Suppressing N bound hotkey
combo(s).`. Check the log viewer first if suppression seems to have no effect - it says immediately how
many combos the plugin currently thinks it should intercept.

A suppressed combo still publishes `hotkey-pressed` as usual - the toggle only decides what the rest of
the system sees. Only the trailing key is intercepted (its key-down, Windows' own repeats of it while
held, and its key-up); a modifier held as part of the combo, such as Ctrl or Shift, is never touched and
keeps doing whatever it normally does elsewhere - which is why `Ctrl+Shift+F3` above only needs `F3`
swallowed to stop leaking into the game. Swallowing a combo's own modifiers too (so a bind that uses a
bare Ctrl or Shift, e.g., does not also trigger a game's crouch or sprint) is a further step this plugin
does not take: doing it without adding input lag to every ordinary use of that modifier needs synthetic
key-up correction after the fact, which is real added complexity worth reaching for only once an actual
bind needs it.

This works for the overwhelming majority of applications, games included - but a `WH_KEYBOARD_LL` hook is
not guaranteed to reach every input path (a title using exclusive DirectInput or kernel-level anti-cheat
capture may still see the raw keystroke). There is no way to confirm that without testing the specific
application.

## Scoping a hotkey to a device, profile or folder

By default a hotkey is global: it fires (and, if enabled, suppresses) everywhere, all the time. Three
more fields on the same trigger - **Device**, **Profile** and **Folder** - each independently narrow
that. This is the closest this plugin gets to Macro Deck 2's per-profile hotkey assignment, and it is
what solves the classic duplicated-button problem: the same "Next" button, on the same combo, bound
once in every folder so whichever copy is actually open is the one that reacts.

1. On the trigger, set any combination of **Device** (one paired device, by the name you gave it in
   Macro Deck), **Profile**, and **Folder**. Leave a field on its **Any** default to not filter on it.
2. Save. The trigger now only fires, and only suppresses, while some connected client matches every
   field you set, all at once - setting both Device and Folder means that folder *on that specific
   device*, not either one on its own, on any device. Leave every field on **Any** for the existing
   global behavior.

For example: bind the same combo in Folder A, B and C, each trigger's **Folder** field set to its own
folder. Whichever folder is actually open when the combo fires is the only one whose trigger runs. Want
two devices to react independently rather than always together? Give each its own combo, with **Device**
set to that one device - a shared combo scoped only by folder reacts to whichever device happens to have
that folder open, which is the right call for reacting on every open copy in step, not for keeping two
devices independent.

Device only lists paired devices - a client with none behind it (a plain browser tab or the desktop
app's own window) can never match a Device filter, only Profile and/or Folder.

A press only publishes once per connected client when it has to - when at least one trigger bound to
`hotkey-pressed` actually sets Device, Profile or Folder. A plain global hotkey, with nothing scoped
anywhere, always publishes exactly once, no matter how many clients are connected.

## Installing

Grab the packed `.macroDeckPlugin` artifact from the
[latest release](https://github.com/PyFlat/System-Hotkeys/releases) (or the store listing, once
published) and install it from Macro Deck's plugin manager.

## Requirements

- .NET SDK 10.0
- A running Macro Deck desktop app for [interactive debugging](#run-and-debug-against-macro-deck)
- Windows - the keyboard hook is a `user32.dll` P/Invoke surface, so this plugin ships win-x64 only.
  Contributions adding macOS or Linux support are very welcome.

## Quick start

```bash
dotnet build
```

```bash
dotnet test
```

Build and tests need no Macro Deck installation. For an interactive session, use the checked-in
**Macro Deck - Real Host** launch profile after the one-time setup below.

## Project layout

```
src/SystemHotkeys/
  Program.cs             the host builder - a few lines and a RunAsync
  manifest.json           identity, icon and the win-x64-only entrypoint
  macrodeck-build.json    how `macrodeck-plugin build` publishes it
  PluginIntegration.cs    the integration: the hotkey-pressed event, its combo and suppress-toggle
                          parameters, and reading them back through IEventPublisher.GetBindings
  Hotkeys/                the keyboard hook itself
    NativeMethods.cs        the WH_KEYBOARD_LL P/Invoke surface
    GlobalKeyboardHook.cs   installs the hook, pumps its message loop, detects a completed combo,
                            swallows a suppressed combo's trailing key
    HotkeyCombo.cs          held keys into the { modifiers, key } shape the combo editor stores
    VirtualKeys.cs          virtual-key code to editor key-token mapping
    BoundHotkeyCombos.cs    turns GetBindings() into the set of combos currently opted into suppression
  Localization/Strings.resx   the default-culture strings, one file per language
  Assets/icon.svg         the icon the manifest declares
  Properties/launchSettings.json   the shared real-host debug profile
tests/SystemHotkeys.Tests/
  PluginIntegrationTests.cs   the event's shape, through the harness
  Hotkeys/                     unit tests for the combo/key-name/binding logic above -
                                the actual OS hook is not something a test harness can drive
```

[AGENTS.md](AGENTS.md) is the full, detailed rule set this plugin is written against (lifecycle,
async/concurrency, localization, logging, comment style, verification steps) - read it before making a
non-trivial change, human or AI.

## Building against a local SDK build

The plugin pins the Macro Deck SDK's packages to an exact published version in
`Directory.Packages.props` rather than floating (see the comment there for why). While a change is
still unreleased, pack the SDK from a Macro Deck 3 checkout into this repository's `local-feed/` and
build against that version instead:

```bash
dotnet pack MacroDeck.slnx -c Release -p:Version=3.0.0-local.1 -o <path-to-this-repo>/local-feed
```

```bash
dotnet build -p:MacroDeckSdkVersion=3.0.0-local.1
```

`NuGet.config` already lists `local-feed/` as a package source, and `MacroDeckSdkVersion` sets the
version for every Macro Deck package at once. Nothing in the repository pins the local version by
default, so a plain `dotnet build` goes back to the pinned published one.

Pick a version that cannot collide with a real release - `3.0.0-local.N` rather than reusing a
published preview version, which would put a hand-built package into the global NuGet cache under the
name of a published one.

## Localization

Every user-facing string is a key in `Localization/Strings.resx`, reached through the generated
`Strings` class - never a literal. Add a `<data>` entry, build, and use the generated `Strings.*`
member; see [AGENTS.md](AGENTS.md#localization) for the full rules (placeholders, plurals, adding a
language). The
[localization guide](https://docs.macro-deck.app/sdk/localization/) is the upstream reference.

`.resx` was chosen because [JetBrains Rider's Localization
Manager](https://www.jetbrains.com/help/rider/Localizing_Applications.html) reads it - every key as a
row, every culture as a column, missing translations highlighted - but nothing requires Rider; these
are plain `.resx` files, and any translator can work from a CSV export.

## Run and debug against Macro Deck

The project contains exactly one interactive launch profile: **Macro Deck - Real Host**. It launches
the plugin project directly, so Rider and Visual Studio attach the debugger to plugin code without a
wrapper or child-process attach. The profile connects in self-registering mode to the installed Macro
Deck desktop app at `http://127.0.0.1:8193`.

For the first run:

1. Start Macro Deck.
2. Open **Developer Tools → Plugin tokens**, create a token and copy it. It is shown only once.
3. Store the token in the source project's **.NET User Secrets** using one of the methods below. The
   project is already initialized; do not run `dotnet user-secrets init`.
4. Select **Macro Deck - Real Host** and start it with **Debug**.
5. Once enrollment succeeds, remove the token from User Secrets.

### Set the token in Rider or Visual Studio

In Rider, right-click `SystemHotkeys` in the Solution Explorer and select
**Tools → .NET User Secrets**. In Visual Studio, right-click the same source project and select
**Manage User Secrets**. Do not select the `.Tests` project.

The IDE opens a `secrets.json` file stored in your user profile, outside this repository. Replace its
contents with:

```json
{
  "MacroDeck:Plugin:EnrollmentToken": "<paste the one-time token here>"
}
```

Save the file, then start **Macro Deck - Real Host**. After enrollment, reopen `secrets.json` and
remove the `MacroDeck:Plugin:EnrollmentToken` entry.

### Set the token from a terminal

From the repository root on macOS or Linux, use the following form. It reads the token without echoing
it and does not put the value in shell history or process arguments:

```bash
project="src/SystemHotkeys/SystemHotkeys.csproj"
printf "Enrollment token: "
read -rs md_enrollment_token
printf '\n'
printf '{"MacroDeck:Plugin:EnrollmentToken":"%s"}\n' "$md_enrollment_token" |
  dotnet user-secrets set --project "$project"
unset md_enrollment_token
```

After the first successful profile launch, remove the one-time token:

```bash
dotnet user-secrets remove "MacroDeck:Plugin:EnrollmentToken" --project "$project"
```

With PowerShell 7, use the equivalent masked-input form:

```powershell
$project = "src/SystemHotkeys/SystemHotkeys.csproj"
$token = Read-Host "Enrollment token" -MaskInput
@{ "MacroDeck:Plugin:EnrollmentToken" = $token } |
  ConvertTo-Json -Compress |
  dotnet user-secrets set --project $project
Remove-Variable token
```

Then remove it after enrollment:

```powershell
dotnet user-secrets remove "MacroDeck:Plugin:EnrollmentToken" --project $project
```

The profile persists the exchanged plugin credential under
`src/SystemHotkeys/.macrodeck-dev-state/`, which is ignored by Git and excluded from the packed
artifact. Later profile launches reuse that credential.

User Secrets are local-only but not encrypted. Never put the enrollment token in `launchSettings.json`,
a shared IDE configuration, a literal command argument or a commit. If you intentionally clear the
local state, create a fresh token and repeat the User Secrets step. Self-registration only works
against a host on the same machine. See the official
[Rider User Secrets guide](https://www.jetbrains.com/help/rider/Manage_NET_user_secrets.html) and
[.NET Secret Manager guide](https://learn.microsoft.com/aspnet/core/security/app-secrets?view=aspnetcore-10.0)
for more background.

## The developer CLI

`macrodeck-plugin` validates, inspects, packs and conformance-tests the plugin. Interactive starts use
the launch profile above.

```bash
dotnet tool install --global MacroDeck.Plugin.Cli --prerelease
```

`--prerelease` is required while the 3.0 SDK is in preview: only preview versions are published, and
`dotnet tool install` picks stable ones by default. Drop it once 3.0 ships.

The tool needs the **ASP.NET Core shared framework**, not just the .NET runtime - its stub host is a
real Kestrel server.

| Command | What it does |
| --- | --- |
| `build` | Publishes the win-x64 runtime identifier using `macrodeck-build.json`, then packs the result. |
| `validate` | Checks a manifest, version directory or artifact against the real manifest reader, the JSON Schema, the permission vocabulary and declared file digests. |
| `inspect` | Reports what installing an artifact would find - entrypoints, permissions, dependencies, conflicts, compatibility, signature shape, size. |
| `pack` | Builds a `.macroDeckPlugin` artifact, validating the manifest first and recomputing `files[]` digests. |
| `run` | Launches the plugin against a real host or a disposable stub one, streaming its output. |
| `test` | Runs the conformance suite and writes a text, JSON or Markdown report. |
| `sign`, `verify`, `keygen` | Creator signing for a packed artifact. |

### Running without a host

```bash
macrodeck-plugin run --project src/SystemHotkeys --stub-host
```

`--stub-host` starts a disposable in-process host, so this needs no Macro Deck installation: the plugin
registers, negotiates the protocol and initializes, and its log output is streamed until you interrupt
it. `--artifact <file>` does the same for a packed artifact, which is what proves an entrypoint path in
the manifest matches what `build` actually wrote. Drop `--stub-host` to attach to the running desktop
app instead; for debugging with breakpoints, use the launch profile above rather than this.

### Packing a release

`build` is the whole path: it reads `macrodeck-build.json`, publishes win-x64 into its `runtimes/win-x64/`
slot and packs the artifact.

```bash
macrodeck-plugin build --source src/SystemHotkeys --output ./artifacts
```

```bash
macrodeck-plugin inspect --artifact ./artifacts/<id>-<version>.macroDeckPlugin
```

Packing validates before it writes, so a bad manifest never becomes an artifact. It discards whatever
`files[]` the source manifest declared and recomputes every digest from disk, and fills in `languages`
from `Localization/`. It cannot sign anything: sign *after* packing, against the packed manifest, or the
digest will not match.

A plain `dotnet build -c Release` does not produce a packable layout - the manifest points at
`runtimes/win-x64/`, which only `build` assembles. Use `validate` against a built artifact or a version
directory rather than against `bin/Release/net10.0`.

### Conformance

```bash
macrodeck-plugin test --project src/SystemHotkeys --report markdown --output conformance.md
```

The suite drives a real session against the plugin: capability contracts, invocation and cancellation
semantics, reconnect and resume behaviour, the reserved `/_macrodeck/*` endpoints, and logging limits.
Checks are Required or Recommended, each with a stable id (`MDC0401`, …) you can select with `--check`
or `--category`. A check can report `SKIP` with a reason when the plugin gives it nothing to observe.

Exit codes make it usable as a CI gate - `0` conformant, `1` the plugin is wrong, `2` usage error, `3`
input unreadable, `4` cancelled. `1` and `3` are deliberately distinct: a missing file is an
environment problem, not a verdict about the plugin.

## Testing

```bash
dotnet test
```

The test project references `MacroDeck.Plugin.Testing`, which provides a loopback test host, fakes and
assertions for testing a plugin without a running Macro Deck. `PluginTestHarness.Create` builds the
plugin from the same `Action<PluginHostBuilder>` `Program.cs` uses - no socket, no host, no built
executable:

```csharp
await using var harness = PluginTestHarness.Create(builder => builder.RegisterIntegration<PluginIntegration>());
await harness.InitializeIntegrationsAsync();
```

Drive capabilities through the typed clients it exposes (`harness.Events`, …) rather than calling a
handler directly, so parameter binding is under test too. The `Hotkeys/` unit tests cover the
combo/key-name/binding logic in isolation, since the actual OS hook is not something a test harness can
drive.

The conformance suite above covers the protocol contract; these tests are for the plugin's own
behaviour.

## License

MIT - see [LICENSE](LICENSE). Macro Deck itself is licensed under Apache 2.0.

## Further reading

- [Plugin development docs](https://github.com/Macro-Deck-App/Macro-Deck-3/tree/main/docs/plugin-development)
- [Sample plugins](https://github.com/Macro-Deck-App/Macro-Deck-Sample-Plugins) - a worked example per capability
- [`plugin-hosting.md`](https://github.com/Macro-Deck-App/Macro-Deck-3/blob/main/docs/plugin-development/plugin-hosting.md) - the builder API, registration modes, the artifact format and every `MACRO_DECK_PLUGIN_*` variable
- [`sdk-reference.md`](https://github.com/Macro-Deck-App/Macro-Deck-3/blob/main/docs/plugin-development/sdk-reference.md) - every interface and record the plugin builds against
- [`cli.md`](https://github.com/Macro-Deck-App/Macro-Deck-3/blob/main/docs/plugin-development/cli.md) - every CLI command and option
- [`testing-plugins.md`](https://github.com/Macro-Deck-App/Macro-Deck-3/blob/main/docs/plugin-development/testing-plugins.md) - the test harness, the fakes and the manual clock
- [`conformance.md`](https://github.com/Macro-Deck-App/Macro-Deck-3/blob/main/docs/plugin-development/conformance.md) - the conformance suite and its check ids
- [`analyzers.md`](https://github.com/Macro-Deck-App/Macro-Deck-3/blob/main/docs/plugin-development/analyzers.md) - the compile-time diagnostics
