<div align="center">

# CheatEngine.Client

**High-level, lifecycle-safe C# APIs for modern Cheat Engine plugins.**

[![CI](https://img.shields.io/github/actions/workflow/status/CheatEngineNet/CheatEngine.Client/main-ci.yml?branch=main&style=flat-square&logo=githubactions&logoColor=white&labelColor=24292f)](https://github.com/CheatEngineNet/CheatEngine.Client/actions/workflows/main-ci.yml)
[![NuGet](https://img.shields.io/nuget/vpre/CheatEngine.Client?style=flat-square&logo=nuget&logoColor=white&labelColor=24292f&color=004880)](https://www.nuget.org/packages/CheatEngine.Client)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet&logoColor=white&labelColor=24292f)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Windows x64](https://img.shields.io/badge/platform-Windows%20x64-0078D4?style=flat-square&labelColor=24292f)](#requirements)

[Quick start](#quick-start) · [Lifecycle](#the-plugin-lifecycle) · [Packages](#packages-and-direct-sdk-reference) · [Capabilities](#v010-capability-status) · [Contributing](#build-and-validation)

</div>

## Context

`CheatEngine.Client` is an in-process, dependency-injection-first layer for plugins loaded by Cheat Engine. It builds on
[`CheatEngine.SDK`](https://www.nuget.org/packages/CheatEngine.SDK) and turns its low-level host bindings into
bounded, typed, fluent C# operations for the lifetime of one plugin activation.

The aggregate `ICheatEngineClient` gives an enabled plugin access to runtime facts and capabilities, main-thread
dispatch, process selection, typed memory, AOB scans, inspection, address tables, and protected Lua operations. It
never exposes a `LuaState`, CE object handle, raw native pointer, or SDK ownership wrapper to plugin code.

## Why this project exists

`CheatEngine.SDK` deliberately owns the difficult boundary: the generated Cheat Engine entry point, Lua protection,
native bridge, host object model, and compile-time plugin/Lua diagnostics. Those are SDK concerns and
`CheatEngine.Client` does not reimplement them.

The Client exists for the application layer above that boundary. It makes recurring plugin concerns explicit and
testable:

| SDK boundary                                          | Client policy above it                                                                                          |
|-------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------|
| Plugin bootstrap and protected Lua calls              | One activation-scoped `ICheatEngineClient`; no raw Lua lifetime escapes                                         |
| Host-owned temporary objects and main-thread affinity | Synchronous dispatcher boundary, copied results, and deterministic cleanup                                      |
| Primitive Lua/host operations                         | Typed memory codecs, bounded strings and pointer chains, immutable AOB builders                                 |
| Plugin construction                                   | One validated DI provider per enable epoch; explicit modules and configuration                                  |
| Host failures and capability differences              | `Try...` methods with `CheatEngineFailure`, convenience methods that throw, and runtime capability observations |

This separation lets a plugin stay ordinary, DI-friendly C# while retaining the SDK as the sole authority for ABI and
Lua safety. It also keeps the high-level surface honest: a contract is not presented as a working Cheat Engine feature
until its ownership, thread-affinity, and lifecycle path are established.

## How it helps improve Cheat Engine plugin projects

The Client centralizes lifecycle, ownership, dispatch, options, and capability policy once, rather than requiring each
plugin to reproduce them around low-level SDK calls. This lowers the cost of adding a feature, gives tests a stable
contract boundary, and keeps the generated plugin template focused on application code. It also makes the supported
surface reviewable: high-level APIs remain fluent for consumers while the Core remains the only SDK mapper.

## Requirements

| Requirement                 | Baseline                                                                           |
|-----------------------------|------------------------------------------------------------------------------------|
| .NET SDK                    | 10.0.401 or later                                                                  |
| Target framework / language | `net10.0` / C# 14                                                                  |
| Cheat Engine host           | 7.7, Windows x64                                                                   |
| Plugin form                 | Framework-dependent managed plugin output folder                                   |
| SDK package                 | `CheatEngine.SDK` 1.x; the Client publishes a compatible range of `[1.0.0, 2.0.0)` |

Cheat Engine remains the compatibility authority. The Client is not an IPC client, a remote-process service, or a
standalone executable; v0.1 runs only inside an enabled Cheat Engine plugin.

## Quick start

The maintained starting point is the `ceplugin` template. It is both a usable project and the repository's executable
example of the required plugin shape.

```powershell
dotnet new install CheatEngine.Client.Templates
dotnet new ceplugin --name MyPlugin
cd MyPlugin
dotnet build --configuration Release
```

The generated project intentionally retains these direct dependencies:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <LangVersion>14.0</LangVersion>
  <PlatformTarget>x64</PlatformTarget>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  <CheatEngineClientPluginProject>true</CheatEngineClientPluginProject>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="CheatEngine.Client" Version="0.1.0" />
  <PackageReference Include="CheatEngine.SDK" Version="1.0.0" />
  <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.12" />
</ItemGroup>
```

`CheatEngine.SDK` must be referenced **directly by the plugin project**. Its build assets generate the Cheat Engine
entry point and provide the native Lua bridge; NuGet transitivity is not sufficient at that host boundary. Setting
`CheatEngineClientPluginProject` opts the project into the Hosting package's `CECLIENT001` guard, which fails the
build if the direct SDK reference is removed.

Start with the template rather than copying this fragment into an existing plugin: it also demonstrates module
registration, generated Lua exports, validated options, bounded AOB and typed-memory access, and an Address List
snapshot. See the [template guide](templates/CheatEngine.Client.Templates/README.md) and the generated
[plugin README](templates/CheatEngine.Client.Templates/content/CheatEngine.Plugin/README.md).

### Minimal plugin shape

The SDK still owns the plugin annotation. The Client base owns the activation-scoped composition:

```csharp
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

        builder.Client.AddModule<PluginClientModule>();
    }
}
```

An activation module receives the scoped client in `OnEnabled` and `OnDisabling`. Fluent calls remain bounded and
handle-free:

```csharp
Address address = client.Patterns
    .Aob("48 8B ?? ?? ?? 89")
    .InModule("game.exe")
    .ReadableExecutable()
    .RequireSingle()
    .Execute();

client.Memory.At(address + 0x14).Write(999);
```

Use the `Try...` terminal operations when absence of a process, scan result, or runtime capability is an expected
condition. Do not make a worker wait for the Cheat Engine thread if that worker can call back into the Client.

## The plugin lifecycle

The parameterless plugin instance is created by the SDK, but every enable creates new managed state:

```text
OnEnable
  -> Configure a new builder (explicit configuration, services, modules, codecs)
  -> Build and validate a new provider and scope
  -> Resolve options and ICheatEngineClient
  -> Enable modules in registration order
  -> OnClientEnabled

OnDisable
  -> Stop admitting the active client
  -> OnClientDisabling
  -> Disable modules in reverse order
  -> Drain Client-owned CE resources while the SDK context is valid
  -> Dispose scope, provider, and configuration
```

`ICheatEngineClient.Epoch` and `ICheatEngineClient.Stopping` identify that activation. Never retain the client, a
resource lease, a Lua reference, a cancellation token, or a target-bound value across disable/re-enable. Constructors,
field initializers, and static initialization must not call Cheat Engine; the SDK binding is valid only after enable.

All Client operations are synchronous. A cancellation token can prevent dispatch or stop Client-managed work between
steps, but it does not claim to interrupt a Lua primitive that has already started. Read
[ADR 0002](docs/adr/0002-plugin-activation-lifecycle.md) before adding a service that touches Cheat Engine.

## Packages and direct SDK reference

The recommended package is `CheatEngine.Client`. The delivery graph stays deliberately one-way:

```text
CheatEngine.Client
├─ CheatEngine.Client.Fluent ────────────────> public contracts
└─ CheatEngine.Client.Hosting
   ├─ CheatEngine.Client.Extensions.DependencyInjection
   │  ├─ CheatEngine.Client.Core ────────────> CheatEngine.SDK
   │  └─ public contracts
   └─ CheatEngine.SDK

public contracts ────────────────────────────> stable SDK value/runtime types only
```

| Package                                                                                                                 | Purpose                                                           | Consume directly when                                  |
|-------------------------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------|--------------------------------------------------------|
| [`CheatEngine.Client`](src/CheatEngine.Client/README.md)                                                                | Umbrella package for the high-level fluent and hosting experience | Building a normal plugin                               |
| [`CheatEngine.Client.Hosting`](libs/CheatEngine.Client.Hosting/README.md)                                               | `CheatEngineClientPlugin` and one-provider-per-activation host    | Integrating the host into an existing composition root |
| [`CheatEngine.Client.Extensions.DependencyInjection`](libs/CheatEngine.Client.Extensions.DependencyInjection/README.md) | Explicit DI registrations, modules, memory codecs, and options    | Composing the Client without the plugin base           |
| [`CheatEngine.Client.Fluent`](libs/CheatEngine.Client.Fluent/README.md)                                                 | Immutable fluent memory and AOB builders                          | Depending only on fluent request construction          |
| [`CheatEngine.Client.Abstractions`](libs/CheatEngine.Client.Abstractions/README.md)                                     | Contracts, requests, failures, and value vocabulary               | Referencing contracts without an implementation        |
| [`CheatEngine.Client.Core`](libs/CheatEngine.Client.Core/README.md)                                                     | SDK-facing implementation                                         | Normally composed through DI, not called directly      |
| [`CheatEngine.Client.Templates`](templates/CheatEngine.Client.Templates/README.md)                                      | `dotnet new ceplugin`                                             | Starting a new plugin                                  |

Package and assembly names describe delivery, not user code. Consumer-facing APIs use functional namespaces such as
`CheatEngine.Client.Memory`, `.Scanning`, `.Tables`, `.Lua`, `.Processes`, `.Runtime`, and `.Hosting`.

## v0.1.0 capability status

The Client reports runtime capability rather than assuming a particular Cheat Engine global or ownership contract. The
following table is a delivery statement, not a substitute for a live host check.

| Area                                                | v0.1.0 status                   | Boundary                                                                                                                                                                         |
|-----------------------------------------------------|---------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Lifecycle, dispatch, DI, modules, options           | Available                       | Per-enable provider and scope; modules stop in reverse order                                                                                                                     |
| Runtime facts and selected process                  | Available                       | Snapshot and attachment state are re-read through the active host                                                                                                                |
| Typed memory and finite pointer chains              | Available                       | Built-in primitives plus explicitly registered deterministic codecs; strings and byte ranges are bounded                                                                         |
| Modules, regions, symbols, and custom-symbol leases | Available                       | Results are copied; leases are activation-scoped                                                                                                                                 |
| AOB scanning                                        | Available                       | Patterns are normalized; terminals are `FirstOrNone`, `RequireSingle`, or bounded `Take`                                                                                         |
| Address List and memory records                     | Available                       | Snapshots and hierarchy materialization are bounded; table file access requires an allowed root                                                                                  |
| Typed protected Lua and explicit Lua modules        | Available                       | No Lua state crosses the public Client contract                                                                                                                                  |
| Value scanning                                      | **Capability-gated**            | The public state machine exists, but Client session creation stays unavailable until the internal `MemScan`/`FoundList` ownership path passes its Cheat Engine 7.7 x64 live gate |
| Arbitrary Lua source                                | Policy-gated and off by default | Requires explicit unsafe opt-in; raw Lua state remains hidden                                                                                                                    |

IPC, remote clients, UI/forms, debugger and breakpoints, Auto Assembler, injection, remote allocations, structures,
hotkeys/timers, speedhack, DBVM, Mono/IL2CPP, and advanced ABI hooks are outside v0.1. They have no placeholder
public API. The full current-state rationale is in [ADR 0004](docs/adr/0004-capability-matrix.md).

## AOT, trimming, and deployment

Shipping Client projects target `net10.0`, enable nullable analysis, warnings as errors, trim/AOT compatibility
analysis, reference-AOT verification, deterministic builds, XML documentation, Source Link, symbol packages, and
package/API validation. `CheatEngine.Client.AotProbe` publishes the complete Client graph as Native AOT for `win-x64`
to validate those library constraints.

That is **not** a claim that Cheat Engine can load a Native AOT plugin DLL. The supported deployment remains the
framework-dependent managed plugin output folder. Deploy it as one unit: your plugin assembly, its `.deps.json` and
`.runtimeconfig.json`, Client and SDK assemblies, and the SDK's `cheatengine-sdk-lua-bridge.dll` must remain together.

The template sets `IsAotCompatible` and `VerifyReferenceAotCompatibility` to protect the application code path, while
leaving the plugin itself in the SDK-supported managed form. See [ADR 0003](docs/adr/0003-package-and-aot-policy.md)
for the package and AOT policy.

## Build and validation

The repository pins the .NET SDK in [global.json](global.json), uses Central Package Management, and commits NuGet
lock files. Run the normal Windows validation sequence from the repository root:

```powershell
dotnet restore CheatEngine.Client.slnx --locked-mode
dotnet build CheatEngine.Client.slnx --configuration Release --no-restore
dotnet test --solution CheatEngine.Client.slnx --configuration Release --no-build --no-restore
dotnet pack CheatEngine.Client.slnx --configuration Release --no-build --no-restore
./eng/Invoke-PackageSmoke.ps1 -PackageSource ./artifacts/packages
./eng/Invoke-TemplateSmoke.ps1 -PackageSource ./artifacts/packages
dotnet publish tests/CheatEngine.Client.AotProbe/CheatEngine.Client.AotProbe.csproj --configuration Release --runtime win-x64 --no-restore --output ./artifacts/aot-probe
./artifacts/aot-probe/CheatEngine.Client.AotProbe.exe
```

The [Windows CI workflow](.github/workflows/ci.yml) runs the locked restore, Release build, Microsoft Testing Platform
tests, package API validation, isolated package smoke test, template smoke test, and Native AOT graph probe. The
Cheat Engine 7.7 x64 live suite is opt-in and intentionally excluded from ordinary CI; no CI result should be read as
proof that an untested live-host feature is available.

## Security and scope

This project is for local processes you are authorized to inspect or modify. It does not add network control, remote
transport, or a mechanism to bypass Cheat Engine or host protections.

Table loading can execute Lua in the host. Keep `AllowedTableRoots` empty unless the plugin has an explicit,
trusted import/export location; an empty set disables table file access. Arbitrary Lua source is separately opt-in and
should remain disabled unless the plugin has a deliberate trust boundary. Avoid logging target-memory contents or Lua
source by default.

## Architecture records

The decisions that constrain the public surface and delivery model are maintained as short ADRs:

- [Layered in-process architecture](docs/adr/0001-layered-in-process-architecture.md)
- [One Client activation per plugin enable epoch](docs/adr/0002-plugin-activation-lifecycle.md)
- [Package and AOT policy](docs/adr/0003-package-and-aot-policy.md)
- [Capability delivery matrix](docs/adr/0004-capability-matrix.md)

For the SDK's bootstrap, generated Lua bindings, native bridge, and host ABI details, start with the
[CheatEngine.SDK README](https://github.com/CheatEngineNet/CheatEngine.SDK#readme).
