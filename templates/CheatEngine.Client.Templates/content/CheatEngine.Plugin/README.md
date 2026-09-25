# CheatEngine.Plugin

## Context

This project was created by `dotnet new ceplugin`. It is a C# 14, .NET 10, x64, managed in-process plugin for Cheat
Engine, built on the functional `CheatEngine.Client` API surface and hosted by `CheatEngineClientPlugin`.

It is also the canonical executable example for the CheatEngine.Client repository. The repository intentionally keeps
this project inside the template instead of maintaining a separate `samples/` copy.

## Why this project exists

The project provides a minimal but production-shaped plugin boundary:

- `[CheatEnginePlugin]` is the SDK entry-point annotation recognized by the generated bootstrap.
- `CheatEngineClientPlugin` creates a fresh DI container and Client activation for every enable cycle. The Client graph
  and options are provider-local singletons; the module scope is the one scope inside that new provider, not a
  persistent root that can be reused for a later enable.
- `Configure` explicitly loads the optional `appsettings.json` from `builder.PluginDirectory`, the folder of the plugin
  assembly, with `reloadOnChange: false`, and registers the generated `PluginLuaModule` through
  `AddLuaModule<PluginLuaModule>()`, then the application module. `AppContext.BaseDirectory` is not used: it describes
  the Cheat Engine process that hosts .NET, not the plugin's deployment folder.
- The plugin reports `CheatEngine Client Plugin` to Cheat Engine and exports the Lua status global
  `cheatengine_client_plugin_status`. `dotnet new ceplugin` derives both from the project name: the reported name is
  the project name in printable ASCII, and the global is the project name in ASCII lower_snake_case followed by
  `_status`. A Lua global has one owner: the Client refuses to replace a global that another plugin registered.
  Different project names can derive the same global, because the derivation lowercases the name and turns each
  camel-case boundary and each run of other characters, non-ASCII letters included, into `_`: `MyPlugin` and
  `My.Plugin` both give `my_plugin_status`, and a name without an ASCII letter or digit gives `plugin_status`. Rename
  the global in `Modules/PluginLuaFunctions.cs` when another plugin exports it.
- `PluginClientModule` demonstrates options, logging, a materialization-bounded AOB request (the module filter is
  applied after a global scan), typed memory access, the Address List record count, and normal Client module lifecycle
  callbacks.

The project references `CheatEngine.Client` **and** `CheatEngine.SDK` directly. The SDK reference must remain direct:
its plugin generator and native Lua bridge assets are build inputs, not a transitive implementation detail.
`<CheatEngineClientPluginProject>true</CheatEngineClientPluginProject>` enables the complete Hosting profile: it guards
both direct references, requires exactly one attributed `CheatEngineClientPlugin`, and checks `net10.0`, C# 14, and an
x64 or AnyCPU target. Leave `CheatEngineSdkGenerateEntryPoint` enabled unless you deliberately write the exact SDK
manual bootstrap and explicitly set `CheatEngineClientManualBootstrap=true`.

## How it helps improve CheatEngine.Client

This plugin is compiled by the template smoke test, which also builds an instance with warnings as errors under the
CheatEngine.Client repository's code style. It therefore continuously verifies the installation path that matters to
consumers: package restore and its lock file, SDK-generated bootstrap, copied bridge assets, functional Client
namespaces, and the DI-first lifecycle. Its normal host preconditions use `Try...` APIs, so an absent process or
pattern does not turn the example into an artificial activation failure.

`PluginLuaModule` is an attribute-only declaration. The Client generator emits the activation-scoped implementation
that acquires Lua state and invokes the generated SDK registration calls; application code contains neither those calls
nor raw Lua state or SDK ownership handles. The project does not demonstrate value scans because their API stays
experimental (`CECLIENT5001`) pending the opt-in Cheat Engine 7.7 x64 live validation of their lifecycle.

## Build

From this project directory, restore and build the managed plugin:

```powershell
dotnet restore .\CheatEngine.Plugin.csproj
dotnet build .\CheatEngine.Plugin.csproj --configuration Release --no-restore
```

`dotnet new ceplugin` already restores the project, unless `--no-restore` is passed. The first restore writes
`packages.lock.json` (`RestorePackagesWithLockFile`): the exact package graph of the plugin and the content hash of each
package, CheatEngine.SDK's included. Commit it with the project; the generated `.gitignore` excludes only build output
and IDE state. On a build machine, restore with `dotnet restore .\CheatEngine.Plugin.csproj --locked-mode`, so that a
changed package graph fails the restore instead of changing the plugin.

The lock also records `Microsoft.NET.ILLink.Tasks`, which the .NET SDK adds for `IsAotCompatible` at the version it
bundles, so a locked restore also fails (`NU1004`) after a .NET SDK update, a monthly patch included, that bundles
another version. Pin the exact .NET SDK in a `global.json` (`"rollForward": "disable"`) on every machine that restores
the plugin, or regenerate the lock with `dotnet restore .\CheatEngine.Plugin.csproj --force-evaluate` after each SDK
update and commit it.

Deploy the complete `bin\Release\net10.0` managed output produced by that build, including the plugin assembly,
`.runtimeconfig.json`, `CheatEngine.SDK` assemblies, and the SDK Lua bridge assets. Do not publish this project as a
Native AOT plugin binary: `IsAotCompatible` validates library compatibility only and is not a Cheat Engine plugin
loader guarantee.

To prepare a separate deployment folder without modifying Cheat Engine itself, pass an explicit output path:

```powershell
dotnet build .\CheatEngine.Plugin.csproj --configuration Release --no-restore `
  -p:CheatEnginePluginOutputPath=C:\CheatEngineDeploy\CheatEngine.Plugin
```

The opt-in target validates and stages the managed closure before individually replacing destination files with
write-through Windows replacement semantics. It never changes a Cheat Engine installation, configuration, or plugin
list. Because Windows cannot transactionally swap a non-empty directory, run it only while the plugin is disabled.

## Supported host profile

This Client release consumes CheatEngine.SDK 2.0.0 and names one Cheat Engine host profile, the qualifiable profile
that CheatEngine.SDK 2.0.0 names. A profile is what a qualification result can name; it is not itself a qualification
result.

| Item | Value |
|---|---|
| Profile id | `ce-7.7.0.10621-x64-managed-hostfxr` |
| Host executable | `cheatengine-x86_64.exe` 7.7.0.10621, machine AMD64, SHA-256 `9727076da50924e4a097b49a02155e4b34759269c3017ff31375364b8826eb4d`; not the `Cheat Engine.exe` launcher and not the `cheatengine-x86_64-SSE4-AVX2.exe` variant |
| Load profile | `managed-hostfxr`: the plugin is a framework-dependent .NET component started by Cheat Engine's nethost/hostfxr route |
| Runtime configuration | The qualification host's `ce.runtimeconfig.json` (`net10.0`), SHA-256 `68f5d81c0a17cc5bdac40bb3d5d88a624f4d31b414f7195ad847d57b0126ac2b`, is a local modification, not an installer baseline |
| Consumed SDK package | `CheatEngine.SDK` 2.0.0, source commit `325c47b573f8bd39a247f1d0101f110fa36c1696`, NuGet content hash (SHA-512, base64) `NLEdZYJ9LKW3EFNB4X5snKCQf7ZS86GkCQ+El7o+S1XQcxHQGjS45Q1ap8lfjQuIwm004mQ3TPxo+ph1yvRrlQ==` |
| SDK native bridge | `build/native/cheatengine-sdk-lua-bridge.dll`, SHA-256 `b008c8d8c136187f241542e6223dc0831999d8300dc2c4c01e1cf49f6fba7698` |
| Client qualification | `NotExecuted` for this Client tuple until Client qualification receipts exist for it (there is no separate qualification documentation tree; receipts, when they exist, are test-owned data under the relevant `*.Repository.Tests` project) |

Never edit an installed Cheat Engine to match this profile: its runtime configuration applies to every managed plugin of
the installation, and CheatEngine.Client never treats such an edit as a setup step. A stock installation is not a
qualified profile, and a result on this profile authorizes no x86 or ARM64 plugin claim.

## Configure and adapt

`appsettings.json` is optional and is loaded only because `Plugin.Configure` explicitly adds it. Leave
`CheatEngineClient:AllowedTableRoots` empty unless table import/export paths have been deliberately authorized;
loading a table can execute Lua. Configuration and module registrations are rebuilt at the next plugin enable, not
reloaded while an activation is active.

Let DI dispose objects that it creates. A module receives its disposable dependencies but does not call `Dispose` on
them; Hosting closes the activation scope and provider after module callbacks. Register a disposable implementation
under one owning service descriptor, and use a non-disposable facade if the application needs a second service view.

Before deployment, replace the illustrative AOB pattern and offset in `Modules/PluginClientModule.cs`. The Lua global
in `Modules/PluginLuaFunctions.cs` is already derived from the project name; a global you add or rename must stay an
ASCII Lua identifier that no other plugin exports. Keep AOB copies bounded: `FirstOrNone` copies
one address, never stops Cheat Engine early, and follows Cheat Engine's unspecified result order. `InModule` keeps only
matches that lie entirely inside the module, on every target. On a qualified local target it limits Cheat Engine's scan
to the module: an exhaustive MemScan that blocks Cheat Engine's main thread while it runs, and whose "nothing found" is
a factual zero (`FirstOrNone` returns `null`). On a CEServer or file-as-process target Cheat Engine scans the whole
target and the Client applies the module while copying, so `InModule` does not reduce the scan's cost there; when
Cheat Engine returns no result list at all, the scan fails with `IndeterminateHostResult` (zero matches or a host
failure: that route cannot tell them apart), and the example treats it as a skipped probe.

## Diagnostics and redaction

The example logs only data that is safe by default: counts, the activation epoch, and a failure's `Kind`, `Operation`,
and `HostEffect`. It never logs addresses, values, symbol expressions, file paths, Lua source, or a failure's `Message`
or `Exception`, because those are user data. If your application needs them for troubleshooting, add a separate log
event behind an explicit, documented opt-in (for example a configuration flag that is off by default) instead of
changing the default events.

Nothing the plugin logs is written anywhere until `Plugin.Configure` adds a logging provider, and the template adds
none. `builder.Logging.AddCheatEngineHostLog()` adds the opt-in provider that writes to CheatEngine.SDK's host log,
whose default sink is the Windows debug output of the Cheat Engine process, shown by an attached debugger or a
debug-output viewer. It writes each entry of level Information or higher (the default levels) as its category, event
id and message template: placeholder values and exception messages are never written. A message built by string
interpolation is its own template and carries its values, so log through constant templates or `LoggerMessage`
methods, as the example does. `AddCheatEngineHostLog(options => options.IncludeFormattedMessages = true)` writes
formatted messages and exceptions: use it only to troubleshoot on a machine you control.
