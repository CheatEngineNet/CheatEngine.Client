# CheatEngine.Client.Hosting

DI-first hosting for an SDK-loaded Cheat Engine plugin, with one validated Client provider per enable epoch.

## Context

`CheatEngine.Client.Hosting` supplies `CheatEngineClientPlugin` and `CheatEnginePluginBuilder`. A plugin derives from
the base class, keeps its own public parameterless construction required by `CheatEngine.SDK`, and configures its
managed dependencies in `Configure`. The base constructor is protected: the SDK generator instantiates the attributed
concrete plugin, whose implicit public constructor may call it.

The host is intentionally synchronous and in-process. It is not a Generic Host and does not create a process-wide
service provider, retain a raw Lua state, discover services by reflection, or keep a configuration file watcher alive.

## Why this project exists

Cheat Engine controls plugin construction and the point at which its Lua runtime is attached. Reusing a provider across
enable/disable cycles could retain handles, configuration state, target state, or resources from an expired activation.

This package turns that lifecycle into a deterministic composition boundary. Each enable cycle has a new configuration,
a new validated service provider, a new scope, and a new `ICheatEngineClient` epoch. Everything is disposed before the
SDK detaches the runtime during disable.

## How it helps CheatEngine.Client

On enable, `CheatEngineClientPlugin` calls `Configure`, builds a provider with `ValidateOnBuild` and `ValidateScopes`,
resolves options to force validation, resolves the Client, starts registered modules in registration order, and then
calls `OnClientEnabled`.

On disable—or if activation fails—the host invokes `OnClientDisabling` and module cleanup in reverse order, drains
Client-owned Cheat Engine resources while the SDK context remains valid, disposes the scope and provider, and finally
disposes activation configuration. Cleanup failures are aggregated after all cleanup opportunities have run.

## Composition lifetime and DI ownership

`CheatEngineClientPlugin` is the supported composition root: every `OnEnable` creates a new
`CheatEnginePluginBuilder`, builds a new provider, and creates one activation scope from that provider. The Core Client
graph intentionally uses provider-local singleton registrations, so **activation-local** means “owned by this new
provider,” not “registered with Microsoft DI's `Scoped` lifetime.” A disable/re-enable cycle therefore constructs a
new Client graph, options cache, deterministic codecs, and module state without mechanically changing their DI
lifetimes.

Creating a second `IServiceScope` from the same provider does not create another Client activation. That second scope
has its own scoped application services and modules, but shares the provider's singleton Client graph, options, and
codecs; scopes are siblings, not nested activation roots. Hosting opens exactly one such scope for an enable epoch.
An integrator that needs an external persistent root must first introduce and qualify an explicit activation-factory
design—repeated `CreateScope()` calls are not a supported substitute.

The DI container owns the objects that it creates. Hosting never disposes resolved modules or services individually:
after lifecycle callbacks and Client resource drain, it disposes the activation scope, then the provider, and finally
its own `ConfigurationManager`. Register an application `IDisposable` under one owning descriptor. If application code
needs another service view of that object, use a non-disposable facade/projection or make ownership explicit; do not
forward the same disposable instance through multiple DI descriptors and expect Hosting to de-duplicate its disposal.
Instances supplied directly by the application remain application-owned.

Construction failure has a narrower rollback: only resources acquired before publication are released, once each, in
reverse construction order (`scope → provider → configuration`). Every stage is attempted even if an earlier disposal
fails. The original configuration, validation, or service-resolution failure remains primary; cleanup failures are
attached as secondary diagnostics. No module callback or Client-owned resource drain runs until an activation has been
created and published. The DI container disposes services it creates; Hosting explicitly releases its host-created
`ConfigurationManager` only after the scope and provider have been released.

Add configuration sources explicitly and keep reload disabled. The following is the normal plugin shape:

```csharp
using CheatEngine.Client;
using CheatEngine.Client.Hosting;
using CheatEngine.SDK.Annotations.Plugin;
using Microsoft.Extensions.Configuration;

namespace MyPlugin;

[CheatEnginePlugin("My Plugin")]
public sealed class Plugin : CheatEngineClientPlugin
{
    protected override void Configure(CheatEnginePluginBuilder builder)
    {
        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

        builder.Client.AddModule<MyClientModule>();
    }

    protected override void OnClientEnabled(ICheatEngineClient client)
    {
        // Use the client only for this enable epoch.
    }
}
```

Plugin projects must reference `CheatEngine.SDK` directly as well as `CheatEngine.Client`. The SDK's plugin entry-point
generator and native bridge build assets cannot be supplied through a transitive NuGet dependency. Set
`CheatEngineClientPluginProject` to `true` to opt into the Hosting profile. It verifies the two direct references
(`CECLIENT001`/`CECLIENT002`), exactly one attributed `CheatEngineClientPlugin` (`CECLIENT003`/`CECLIENT004`),
`net10.0`, C# 14, and an x64 or AnyCPU target (`CECLIENT005`–`CECLIENT007`). Disabling the SDK generator additionally
requires an explicit `CheatEngineClientManualBootstrap=true` acknowledgement; the SDK then validates the exact manual
entry point (`CECLIENT008` and `CESDK0003`).

```xml
<PropertyGroup>
  <CheatEngineClientPluginProject>true</CheatEngineClientPluginProject>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="CheatEngine.Client" Version="0.1.0" />
  <PackageReference Include="CheatEngine.SDK" Version="1.0.0" />
  <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.12" />
</ItemGroup>
```

`CheatEngine.Client.Templates` contains a complete plugin layout that applies this configuration and includes a bounded
AOB, memory, Address List, and Lua-module example.

## Managed deployment folder

Set `CheatEnginePluginOutputPath` only when a build should prepare a local managed deployment folder:

```powershell
dotnet build .\MyPlugin.csproj --configuration Release `
  -p:CheatEnginePluginOutputPath=C:\CheatEngineDeploy\MyPlugin
```

`PrepareCheatEnginePluginDeployment` runs only for the marked plugin profile and only when that property is non-empty.
It validates the plugin DLL, manifests, Client/SDK managed closure, and the SDK Lua bridge, stages the complete output,
then replaces each destination file with Windows write-through replacement semantics. It does not inspect or change a
Cheat Engine installation, runtime configuration, or plugin list. Windows cannot atomically replace a non-empty
directory, so deploy while the plugin is disabled; destination files are individually never copied in a partially
written state.
