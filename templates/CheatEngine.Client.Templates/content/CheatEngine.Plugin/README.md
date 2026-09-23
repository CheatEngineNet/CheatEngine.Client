# CheatEngine.Plugin

## Context

This project was created by `dotnet new ceplugin`. It is a C# 14, .NET 10, x64, managed in-process plugin for Cheat
Engine, built on the functional `CheatEngine.Client` API surface and hosted by `CheatEngineClientPlugin`.

It is also the canonical executable example for the CheatEngine.Client repository. The repository intentionally keeps
this project inside the template instead of maintaining a separate `samples/` copy.

## Why this project exists

The project provides a minimal but production-shaped plugin boundary:

- `[CheatEnginePlugin]` is the SDK entry-point annotation recognized by the generated bootstrap.
- `CheatEngineClientPlugin` creates a fresh DI container and Client activation for every enable cycle. The Client graph,
  options, and codecs are provider-local singletons; the module scope is the one scope inside that new provider, not a
  persistent root that can be reused for a later enable.
- `Configure` explicitly loads the optional `appsettings.json` beside the plugin with `reloadOnChange: false` and
  registers the generated `PluginLuaModule` through `AddLuaModule<PluginLuaModule>()`, then the application module.
- `PluginClientModule` demonstrates options, logging, a materialization-bounded AOB request (the module filter is
  applied after a global scan), typed memory access, an Address List snapshot, and normal Client module lifecycle
  callbacks.

The project references `CheatEngine.Client` **and** `CheatEngine.SDK` directly. The SDK reference must remain direct:
its plugin generator and native Lua bridge assets are build inputs, not a transitive implementation detail.
`<CheatEngineClientPluginProject>true</CheatEngineClientPluginProject>` enables the complete Hosting profile: it guards
both direct references, requires exactly one attributed `CheatEngineClientPlugin`, and checks `net10.0`, C# 14, and an
x64 or AnyCPU target. Leave `CheatEngineSdkGenerateEntryPoint` enabled unless you deliberately write the exact SDK
manual bootstrap and explicitly set `CheatEngineClientManualBootstrap=true`.

## How it helps improve CheatEngine.Client

This plugin is compiled by the template smoke test. It therefore continuously verifies the installation path that
matters to consumers: package restore, SDK-generated bootstrap, copied bridge assets, functional Client namespaces,
and the DI-first lifecycle. Its normal host preconditions use `Try...` APIs, so an absent process or pattern does not
turn the example into an artificial activation failure.

`PluginLuaModule` is an attribute-only declaration. The Client generator emits the activation-scoped implementation
that acquires Lua state and invokes the generated SDK registration calls; application code contains neither those calls
nor raw Lua state or SDK ownership handles. The project does not demonstrate value scans because their complete
Create/Scan/Destroy lifecycle is still capability-gated pending the opt-in Cheat Engine 7.7 x64 live validation.

## Build

From this project directory, restore and build the managed plugin:

```powershell
dotnet restore .\CheatEngine.Plugin.csproj
dotnet build .\CheatEngine.Plugin.csproj --configuration Release --no-restore
```

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

This Client release consumes CheatEngine.SDK 1.0.0 and names one Cheat Engine host profile, the profile that
CheatEngine.SDK 1.0.0 targets. A profile is what a qualification result can name; it is not itself a qualification
result.

| Item | Value |
|---|---|
| Profile id | `ce-7.7.0.10621-x64-managed-hostfxr` |
| Host executable | `cheatengine-x86_64.exe` 7.7.0.10621, machine AMD64, SHA-256 `9727076da50924e4a097b49a02155e4b34759269c3017ff31375364b8826eb4d`; not the `Cheat Engine.exe` launcher and not the `cheatengine-x86_64-SSE4-AVX2.exe` variant |
| Load profile | `managed-hostfxr`: the plugin is a framework-dependent .NET component started by Cheat Engine's nethost/hostfxr route |
| Runtime configuration | The qualification host's `ce.runtimeconfig.json` (`net10.0`), SHA-256 `68f5d81c0a17cc5bdac40bb3d5d88a624f4d31b414f7195ad847d57b0126ac2b`, is a local modification, not an installer baseline |
| Consumed SDK package | `CheatEngine.SDK` 1.0.0, NuGet content hash (SHA-512, base64) `n7nHqZ8vzo7Vf20jF0fkh/jUtR3yo1TwRGpXE7ERxZeJ4C5S/Nsft4lqOg7zGwfsD5Nh9tTVgdw4PrybJRF0gA==` |
| SDK native bridge | `build/native/cheatengine-sdk-lua-bridge.dll`, SHA-256 `da08c2ba03019da3a8c432ef061d5d6133fd2169ba3a6a8e9ac903353856d994` |
| Client qualification | `NotExecuted` for this Client tuple until Client qualification receipts exist (the CheatEngine.Client repository's `docs/qualification/` pages will record them) |

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

Before deployment, replace the illustrative AOB pattern and offset in `Modules/PluginClientModule.cs`, and choose an
application-specific Lua global name in `Modules/PluginLuaFunctions.cs`. Keep AOB copies bounded: `InModule` filters the
addresses after a global Cheat Engine scan, so it does not reduce the scan's cost, and `FirstOrNone` follows Cheat
Engine's unspecified result order. With CheatEngine.SDK 1.0.0 a scan that finds nothing is reported as
`IndeterminateHostResult` (zero matches or a host failure), so the example treats it as a skipped probe.

## Diagnostics and redaction

The example logs only data that is safe by default: counts, the activation epoch, and a failure's `Kind`, `Operation`,
and `HostEffect`. It never logs addresses, values, symbol expressions, file paths, Lua source, or a failure's `Message`
or `Exception`, because those are user data. If your application needs them for troubleshooting, add a separate log
event behind an explicit, documented opt-in (for example a configuration flag that is off by default) instead of
changing the default events.
