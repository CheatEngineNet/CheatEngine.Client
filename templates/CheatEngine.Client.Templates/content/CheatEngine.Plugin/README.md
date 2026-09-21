# CheatEngine.Plugin

## Context

This project was created by `dotnet new ceplugin`. It is a C# 14, .NET 10, x64, managed in-process plugin for Cheat
Engine, built on the functional `CheatEngine.Client` API surface and hosted by `CheatEngineClientPlugin`.

It is also the canonical executable example for the CheatEngine.Client repository. The repository intentionally keeps
this project inside the template instead of maintaining a separate `samples/` copy.

## Why this project exists

The project provides a minimal but production-shaped plugin boundary:

- `[CheatEnginePlugin]` is the SDK entry-point annotation recognized by the generated bootstrap.
- `CheatEngineClientPlugin` creates a fresh DI container and Client activation for every enable cycle.
- `Configure` explicitly loads the optional `appsettings.json` beside the plugin with `reloadOnChange: false` and
  registers the generated `PluginLuaModule` through `AddLuaModule<PluginLuaModule>()`, then the application module.
- `PluginClientModule` demonstrates options, logging, a bounded AOB request, typed memory access, an Address List
  snapshot, and normal Client module lifecycle callbacks.

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

## Configure and adapt

`appsettings.json` is optional and is loaded only because `Plugin.Configure` explicitly adds it. Leave
`CheatEngineClient:AllowedTableRoots` empty unless table import/export paths have been deliberately authorized;
loading a table can execute Lua. Configuration and module registrations are rebuilt at the next plugin enable, not
reloaded while an activation is active.

Before deployment, replace the illustrative AOB pattern and offset in `Modules/PluginClientModule.cs`, and choose an
application-specific Lua global name in `Modules/PluginLuaFunctions.cs`. Keep AOB operations bounded and avoid logging
memory contents or Lua scripts by default.
