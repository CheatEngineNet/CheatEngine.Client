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
`CheatEngineClientPluginProject` to `true` to opt into the Hosting build guard; it emits `CECLIENT001` at compile time
when the direct SDK `PackageReference` is absent.

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
