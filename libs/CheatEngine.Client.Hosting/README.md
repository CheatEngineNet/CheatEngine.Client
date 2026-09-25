# CheatEngine.Client.Hosting

DI-first hosting for an SDK-loaded Cheat Engine plugin, with one validated Client provider per enable epoch.

## Context

`CheatEngine.Client.Hosting` supplies `CheatEngineClientPlugin` and `CheatEnginePluginBuilder`. A plugin derives from
the base class, keeps its own public parameterless construction required by `CheatEngine.SDK`, and configures its
managed dependencies in `Configure`. The base constructor is protected: the SDK generator instantiates the attributed
concrete plugin, whose implicit public constructor may call it.

The host is intentionally synchronous and in-process. It is not a Generic Host and does not create a process-wide
service provider, retain a raw Lua state, discover services by reflection, or keep a configuration file watcher alive.

A plugin also inherits CheatEngine.SDK's `protected static` `CheatEnginePlugin.Context`, the raw SDK plugin context of
the current enable. It is a raw SDK escape hatch outside every guarantee of the Client (activation epochs, main-thread
dispatch, failure classification, resource ownership and release, redaction): code that uses it, or any other
CheatEngine.SDK API directly, follows the CheatEngine.SDK contract instead.

## Installation

A plugin references [`CheatEngine.Client`](https://www.nuget.org/packages/CheatEngine.Client), which brings this
package at exactly its own version, and `CheatEngine.SDK` directly:

| Plugin project requirement | Value |
|---|---|
| Target framework | `net10.0` |
| Language | C# 14 (`<LangVersion>14.0</LangVersion>`) |
| .NET SDK | 10.0.401 or later: the Lua generator packed in this package is compiled against Roslyn 5.9.0 (CS9057 below it) |
| Platform | Windows x64; `PlatformTarget` is `x64` or `AnyCPU` |
| Cheat Engine | 7.7.0.10621 x64 (`cheatengine-x86_64.exe`), loading the plugin through its managed .NET host |
| `CheatEngine.SDK` | A direct `PackageReference` in `[2.0.0, 3.0.0)` |

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <LangVersion>14.0</LangVersion>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <PlatformTarget>x64</PlatformTarget>
  <CheatEngineClientPluginProject>true</CheatEngineClientPluginProject>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="CheatEngine.Client" Version="X.Y.Z" />
  <PackageReference Include="CheatEngine.SDK" Version="2.0.0" />
  <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.12" />
</ItemGroup>
```

Replace `X.Y.Z` with the installed CheatEngine.Client version. Keep `CheatEngine.SDK` on 2.x: this Client release is
built and tested against CheatEngine.SDK 2.0.0 and declares `[2.0.0, 3.0.0)`. A 3.x SDK fails the build with
`CECLIENT017`, and a version below 2.0.0 fails the restore with `NU1605`. The seven Client packages ship in lockstep:
reference this package directly only at the same version as `CheatEngine.Client`.
`CheatEngine.Client.Templates` (`dotnet new ceplugin`) writes this project with a complete plugin layout: a bounded
AOB, memory, Address List and Lua-module example.

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
new Client graph, options cache, and module state without mechanically changing their DI lifetimes. The Client
registers no memory codec: a codec is an application service that the plugin registers in `Services` and passes with
each codec request.

A new provider per enable isolates this plugin's Client graph from its previous enable epochs; it does **not** isolate
state that lives outside the container. CheatEngine.SDK static state (`PluginHost` and the current plugin context) and
Cheat Engine's Lua globals are shared by every plugin that loads the same SDK assemblies into the Cheat Engine process,
and a DI container cannot separate them. Coexistence of two plugins that share or do not share the SDK assemblies is
the live scenario Q09 (two plugins in one Cheat Engine process), for which no Client receipt exists yet.

Creating a second `IServiceScope` from the same provider does not create another Client activation. That second scope
has its own scoped application services and modules, but shares the provider's singleton Client graph, options, and
application singletons; scopes are siblings, not nested activation roots. Hosting opens exactly one such scope for an
enable epoch. An integrator that needs an external persistent root must first introduce and qualify an explicit
activation-factory design—repeated `CreateScope()` calls are not a supported substitute.

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

Add configuration sources explicitly and keep reload disabled. `CheatEnginePluginBuilder` exposes `Configuration`,
`Services`, `Logging` (the `ILoggingBuilder` of the activation provider), `Client` (the Client registrations) and
`PluginDirectory`, the folder of the plugin assembly. Resolve the files deployed with the plugin against
`PluginDirectory`: `AppContext.BaseDirectory` describes the Cheat Engine process that hosts .NET, not the plugin's
deployment folder. Hosting creates the builder and builds the provider itself; neither is public. The following is the
normal plugin shape:

```csharp
using CheatEngine.Client;
using CheatEngine.Client.Hosting;
using CheatEngine.Client.Modules;
using CheatEngine.SDK.Annotations.Plugin;

using Microsoft.Extensions.Configuration;

namespace MyPlugin;

[CheatEnginePlugin("My Plugin")]
public sealed class Plugin : CheatEngineClientPlugin
{
    protected override void Configure(CheatEnginePluginBuilder builder)
    {
        builder.Configuration
            .SetBasePath(builder.PluginDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

        builder.Client.AddModule<MyClientModule>();
    }

    protected override void OnClientEnabled(ICheatEngineClient client)
    {
        // Use the client only for this enable epoch.
    }
}

public sealed class MyClientModule : ICheatEngineClientModule
{
    public void OnEnabled(ICheatEngineClient client)
    {
        // Runs after the provider, the scope and the client of this enable exist, in registration order.
    }

    public void OnDisabling(ICheatEngineClient client)
    {
        // Runs in reverse registration order, before Client-owned resources are released.
    }
}
```

## Plugin profile build checks

Plugin projects must reference `CheatEngine.SDK` directly as well as `CheatEngine.Client`. The SDK's plugin entry-point
generator and native bridge build assets cannot be supplied through a transitive NuGet dependency. Set
`CheatEngineClientPluginProject` to `true` to opt into the Hosting profile, whose build targets report:

- the two direct references (`CECLIENT001`/`CECLIENT002`);
- exactly one attributed `CheatEngineClientPlugin` (`CECLIENT003`/`CECLIENT004`, and `CECLIENT009` when the plugin
  metadata cannot be read);
- `net10.0`, C# 14, and an x64 or AnyCPU target (`CECLIENT005`–`CECLIENT007`);
- a disabled SDK generator without the explicit `CheatEngineClientManualBootstrap=true` acknowledgement
  (`CECLIENT008`); the SDK then validates the exact manual entry point (`CESDK0003`);
- a `CheatEngine.SDK` of a major this release does not support (`CECLIENT017`);
- an incomplete managed deployment folder (`CECLIENT010`–`CECLIENT016`, see below).

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

## Cleanup diagnostics and redaction

Disable runs every cleanup stage even after an earlier stage fails, in this order: `CleanupScope` (the main-thread
cleanup scope), `ModuleCallbacks` (application hook, then modules in reverse order), `ClientResources` (Client-owned
Cheat Engine resources, while the SDK context is still attached), then `Scope`, `Provider`, and `Configuration`. One
failure is rethrown unchanged; several are reported together as one `AggregateException` in attempt order. Core applies
the same rule to its own resource registries, so a faulty module or lease never prevents the next release.

Once per enable, before the modules start, event 20 (`ActivationIdentified`, Information) identifies the activation:
epoch, plugin type name, CheatEngine.Client version, the consumed CheatEngine.SDK version and NuGet content hash
embedded at build time, the informational version of the loaded `CheatEngine.SDK.Engine` with a label that says whether
it is the reviewed package, another release of the supported major that the package gate accepts, or a release outside
that range, the package evidence state, and the supported host profile id `ce-7.7.0.10621-x64-managed-hostfxr`. It is
built from assembly metadata only: no path, no file read, and no Lua call. The Core diagnostic events (1000–1800) are
described in the
[`CheatEngine.Client.Core` README](https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/libs/CheatEngine.Client.Core/README.md#diagnostics-events).

Each failed stage is logged as event 6 with the activation epoch, the stable stage name, and the exception **type**
name only; event 7 reports how many stages were attempted and how many failed; event 5 reports the failure count.
Hosting never logs exception messages, addresses, values, symbol expressions, file paths, or Lua text: those are user
data and belong to the application's explicit opt-in; `CheatEngineFailure.ToString()` follows the same rule. Logging is
best effort: a logging provider that throws cannot abort enable, disable, or any remaining cleanup stage.

After the `ClientResources` stage and before the scope is disposed, Hosting reads CheatEngine.SDK's sticky external
Lua state reset fact once. When CheatEngine.SDK detected, during the activation or during those releases, that Cheat
Engine replaced its Lua state outside the plugin's control, event 8 (`ExternalLuaStateResetDetected`, Warning, epoch
only) says so: Lua work is refused until the next enable, and the Lua-bound releases were refused rather than made into
the replacement state. The read is a lock-free flag read, not a runtime snapshot: once CheatEngine.SDK detected the
reset it refuses every Lua admission, the snapshot's included. A read that fails changes nothing. A reset that
CheatEngine.SDK first detects when it detaches Lua, after the Client cleanup, is reported only by the SDK's own
`LuaStateReplacedExternally:` host log line.

## Cheat Engine host log (opt-in)

`builder.Logging.AddCheatEngineHostLog()` adds a logging provider that writes to CheatEngine.SDK's host log
(`CheatEngine.SDK.Hosting.Diagnostics.HostLog`), whose default sink is the Windows debug output of the Cheat Engine
process, shown by an attached debugger or a debug-output viewer. Nothing is added by default.

- Levels map to the four host log levels: `Trace` and `Debug` to `Trace`, `Information` to `Information`, `Warning` to
  `Warning`, and `Error` and `Critical` to `Error`; `None` is never written. An entry is written only when the logging
  filters admit it **and** `HostLog.IsEnabled` accepts its host level. `HostLog.MinimumLevel` is `Information` by
  default, so `Debug` and `Trace` entries need `HostLog.MinimumLevel = HostLogLevel.Trace`.
- By default an entry is `category[event id]: template`, the **message template** of the entry (for example
  `Cheat Engine Client activation {Epoch} enabled.`), followed by the exception type name. Placeholder values and
  exception messages are never written, because they can hold addresses, values, symbol expressions, paths, or Lua text
  (Q46). The template is written as the caller passed it: a message built by string interpolation, such as
  `logger.LogInformation($"Read {address}")`, is its own template and carries its values. Plugin code keeps them out of
  the host log only by logging constant structured templates or `LoggerMessage` methods, as the Client's own events do.
  `AddCheatEngineHostLog(options => options.IncludeFormattedMessages = true)` writes the formatted message and the
  exception instead; use it only to troubleshoot on a machine you control.
- The provider is added once: a later call adds nothing and keeps the options of the first call.
- The host log, its sink, and its minimum level belong to CheatEngine.SDK and are shared by every plugin that loads the
  same SDK assemblies. A sink that routes host log entries back into `ILogger` is contained: the host log drops the
  re-entrant entry instead of recursing.

CheatEngine.SDK can also write its own bounded identification line, `CheatEngineSdkIdentification` (SDK version, native
bridge fingerprint, bound Lua module hash, Cheat Engine and runtime versions, never a user path), at the start of every
enable attempt. It is off by default. Set `HostLog.IdentifyOnEnable = true` from a `[ModuleInitializer]` method, which
runs before any plugin code, so that it also covers the first enable; or set the environment variable
`CHEATENGINE_SDK_IDENTIFY_ON_ENABLE=1` for the Cheat Engine process, without rebuilding the plugin. Either one is
enough. The Client's own identification is event 20 above. A plugin assembly is the application Cheat Engine loads, so
the library warning CA2255 on a module initializer does not apply to it:

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using CheatEngine.SDK.Hosting.Diagnostics;

namespace MyPlugin;

internal static class HostLogSetup
{
    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255", Justification = "A plugin assembly is the application Cheat Engine loads.")]
    internal static void IdentifyEveryEnable()
    {
        HostLog.IdentifyOnEnable = true;
    }
}
```
