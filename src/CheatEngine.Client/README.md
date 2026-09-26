# CheatEngine.Client

High-level, lifecycle-safe C# APIs for in-process Cheat Engine plugins, built on
[CheatEngine.SDK](https://www.nuget.org/packages/CheatEngine.SDK).

`CheatEngine.Client` is the package a plugin references. It brings the activation-scoped host
([`CheatEngine.Client.Hosting`](https://www.nuget.org/packages/CheatEngine.Client.Hosting)), the fluent memory and AOB
builders ([`CheatEngine.Client.Fluent`](https://www.nuget.org/packages/CheatEngine.Client.Fluent)) and, through them,
the public contracts and the SDK-facing implementation, at exactly its own version. The package itself contains no
Cheat Engine host logic.

The Client runs in process inside an enabled Cheat Engine plugin. It is not a standalone executable, not Cheat Engine's
`luaclient` library and not an RPC client of `ceserver`.

## Requirements

| Plugin project requirement | Value |
|---|---|
| Target framework | `net10.0` (`CECLIENT005`) |
| Language | C# 14, `<LangVersion>14.0</LangVersion>` (`CECLIENT006`) |
| .NET SDK | 10.0.401 or later: the Lua generator packed in Hosting is compiled against Roslyn 5.9.0 |
| Platform | Windows x64; `PlatformTarget` is `x64` or `AnyCPU` (`CECLIENT007`) |
| Cheat Engine | 7.7.0.10621 x64 (`cheatengine-x86_64.exe`), loading the plugin through its managed .NET host |
| `CheatEngine.SDK` | A direct `PackageReference` in `[2.0.0, 3.0.0)` (`CECLIENT001`, `CECLIENT017`, `NU1605`) |
| Other Client packages | None: `CheatEngine.Client` brings them, each at exactly its own version |

The codes in parentheses are the build or restore errors that enforce a row; the
[Hosting README](https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/libs/CheatEngine.Client.Hosting/README.md#build-diagnostics)
lists every `CECLIENT` build diagnostic. No build error enforces the .NET SDK row: an older compiler does not run the
Lua generator and reports only warning CS9057, and only `dotnet new ceplugin` refuses an older SDK.

## Installation

Start from the `ceplugin` template, which writes the project below and a complete example plugin:

```powershell
dotnet new install CheatEngine.Client.Templates
dotnet new ceplugin --name MyPlugin
cd MyPlugin
dotnet build --configuration Release
```

To add the Client to an existing plugin project, keep these properties and direct references:

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

Replace `X.Y.Z` with the CheatEngine.Client version you install; the template writes it for you.
`Microsoft.Extensions.Configuration.Json` is needed only to load an `appsettings.json` file, as below.

`CheatEngine.SDK` must be referenced **directly** by the plugin project: its build assets generate the Cheat Engine
entry point and copy the native Lua bridge, and they do not flow through a transitive NuGet dependency.
`CheatEngineClientPluginProject` turns on the Hosting build checks of the plugin profile, which fail the build when that
reference is missing (`CECLIENT001`). Keep `CheatEngine.SDK` on 2.x: this Client release is built and tested against
CheatEngine.SDK 2.0.0 and declares `[2.0.0, 3.0.0)`. A 3.x SDK fails the build with `CECLIENT017`, and a version below
2.0.0 fails the restore with `NU1605`. Do not upgrade to 3.x until a Client release says so.

Deploy the complete framework-dependent build output as one folder: the plugin assembly, its `.deps.json` and
`.runtimeconfig.json`, the Client and SDK assemblies and the SDK's `cheatengine-sdk-lua-bridge.dll`. A Native AOT
plugin DLL is not a supported Cheat Engine plugin.

## A minimal plugin

The SDK owns the plugin annotation; `CheatEngineClientPlugin` builds a new, validated service provider for every enable
and gives each module the activation-scoped `ICheatEngineClient`:

```csharp
using CheatEngine.Client;
using CheatEngine.Client.Hosting;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Modules;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Annotations.Plugin;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MyPlugin;

[CheatEnginePlugin("My Plugin")]
public sealed class Plugin : CheatEngineClientPlugin
{
    protected override void Configure(CheatEnginePluginBuilder builder)
    {
        builder.Configuration
            .SetBasePath(builder.PluginDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

        builder.Client.AddModule<ScoreModule>();
    }
}

public sealed class ScoreModule(ILogger<ScoreModule> logger) : ICheatEngineClientModule
{
    public void OnEnabled(ICheatEngineClient client)
    {
        // A Try form returns an expected failure, such as no selected process, instead of throwing it.
        if (!client.Patterns.Aob("48 8B ?? ?? ?? 89")
                .InModule("game.exe")
                .Executable()
                .FirstOrNone()
                .TryExecute(out Address? match, out CheatEngineFailure failure))
        {
            logger.LogDebug("Score probe skipped: {Failure}", failure);
            return;
        }

        if (match is { } address)
        {
            client.Memory.At(address + 0x14).Write(999);
        }
    }

    public void OnDisabling(ICheatEngineClient client)
    {
    }
}
```

`CheatEngineFailure.ToString()` names the kind, the operation and the host effect only: never an address, a value or a
path. Never keep the client, a lease or a target-bound value after `OnDisabling`: every enable creates a new client.
The [repository README](https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/README.md#the-plugin-lifecycle)
describes the lifecycle.

## What 1.0 offers

**Available** means a stable 1.x API with an operational implementation. **Experimental** APIs are operational too, but
they carry an `[Experimental("CECLIENT500x")]` diagnostic and can change in a minor release until their live scenarios
pass; suppressing the diagnostic is the opt-in. At run time, `ICheatEngineRuntime.TryGetClientCapability` reports each
capability with its evidence, and none reports the runtime state `Available` before Client qualification receipts
exist for the scenarios named below.

<!-- capability-table:start -->
| Capability id | Implementation | 1.0 status | What you get | Qualification |
|---|---|---|---|---|
| `Client.ProcessSelection` | Operational adapter | Available | `client.Processes`: the selected target, attach, the local process catalog | Unknown until Client receipts for Q30.a, Q31 and Q32 exist |
| `Client.TypedMemory` | Operational adapter | Available | `client.Memory`: primitives, codecs, bytes, strings, pointer chains, batches | Unknown until Client receipts for Q20, Q21 and Q33 exist |
| `Client.PatternScanning` | Operational adapter | Available | `client.Patterns`: AOB scans with a bounded copy | Unknown until Client receipts for Q27, Q28 and Q29 exist |
| `Client.Inspection` | Operational adapter | Available | `client.Inspection`: modules, regions, symbols and symbol leases | Unknown until Client receipts for Q16.b and Q28 exist |
| `Client.Tables` | Operational adapter | Available | `client.Tables`: Address List records and trusted table files | Unknown until Client receipts for Q34 exist |
| `Client.ProtectedLua` | Operational adapter | Available | `client.Lua`: typed Lua operations and generated Lua modules | Unknown until Client receipts for Q05, Q16 and Q19 exist |
| `Client.UnsafeLuaExecution` | Operational, policy opt-in | Available with `EnableUnsafeLuaExecution()` | `IUnsafeLuaClient`: trusted Lua source, never a Lua state | Stays `Unknown`: no scenario covers arbitrary Lua |
| `Client.ValueScanning` | Operational adapter, experimental (CECLIENT5001) | Experimental | `client.ValueScans`: first and next scans, bounded pages | Unknown until Client receipts for Q25 and Q26 exist |
| `Client.Allocations` | Operational adapter, experimental (CECLIENT5002) | Experimental | `client.Allocations`: target allocations owned by leases | Unknown until Client receipts for Q30.a exist |
| `Client.Assembly` | Operational adapter, experimental (CECLIENT5003) | Experimental | `client.Assembly`: single-instruction assembly and disassembly | Unknown until Client receipts for Q32 exist |
| `Client.AutoAssemblerPatches` | Operational, policy opt-in, experimental (CECLIENT5004) | Experimental, with `EnableAutoAssemblerPatches()` | `IAutoAssemblerClient`: Auto Assembler patches owned by leases | Unknown until Client receipts for Q35 and Q44 exist |
<!-- capability-table:end -->

The activation lifecycle, main-thread dispatch (`client.Dispatcher`), runtime facts (`client.Runtime`), dependency
injection, options and modules are always available. The
[Abstractions README](https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/libs/CheatEngine.Client.Abstractions/README.md)
is the reference for every contract, its failures and its limits, and describes each experimental API under its
diagnostic id.

**Not offered in 1.0**, not even as a gated placeholder: timers and hotkeys; the debugger and breakpoints; the speed
hack; target-memory and file hashing; DBVM; remote execution and DLL injection; pausing, resuming or creating a process,
and attaching to the foreground process; assembly comments; and detaching from a process. No CheatEngine.SDK primitive
backs these yet; they may arrive in a 1.x minor release once the SDK provides an owner. IPC and remote clients, UI and
forms, structures, Mono and IL2CPP, and advanced ABI hooks are outside the scope of 1.0 and have no public API either.

## Supported host profile

This Client release consumes CheatEngine.SDK 2.0.0 and names one Cheat Engine host profile, the qualifiable profile
that CheatEngine.SDK 2.0.0 names: this tuple is what a plugin deploys against. A profile is what a qualification result
can name; it is not itself a qualification result.

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

## Versioning and compatibility

CheatEngine.Client follows [Semantic Versioning 2.0.0](https://semver.org/) from 1.0.0. Every Client package is
released with the same version; use one version for all of them.

The seven packages ship in lockstep. Each Client package depends on the Client packages it builds on at exactly its own
version (`[X.Y.Z]` in its nuspec, not the `X.Y.Z` minimum NuGet writes by default), because
`CheatEngine.Client.Extensions.DependencyInjection` and `CheatEngine.Client.Hosting` use internal types of
`CheatEngine.Client.Core` and of `CheatEngine.Client.Extensions.DependencyInjection`, which no public API baseline
protects. Reference `CheatEngine.Client` and let it bring the others; a Client package you reference directly takes the
same version. `CheatEngine.Client.Core` is not a standalone package: it has no public API and is published only as a
dependency of `CheatEngine.Client.Extensions.DependencyInjection`.

- **Patch releases (1.0.x)** fix behavior and documentation without changing the public API.
- **Minor releases (1.x)** add API without breaking code compiled against an earlier 1.x:
  - new types, members, overloads and namespaces;
  - new members on a **call-only** interface. The documentation of every public interface says whether it is
    *Call-only* (the Client implements it and applications call it, for example `ICheatEngineClient`, `IMemoryClient`
    or `ICheatEngineLease`) or *Implementable* (applications implement it and the Client calls it). Implement a
    call-only interface only in a test double, and expect to update the double in a minor release;
  - new enum values. Public enums are `int` enums whose explicit values never change meaning. An outcome enum (a name
    ending in `Kind`, `Status`, `State`, `Effect` or `Scope`) has `Unknown = 0`: handle a value you do not recognize
    like `Unknown`. An option enum has a valid default at 0 and never an outcome suffix.
- **Frozen for all of 1.x:** the *Implementable* interfaces (`ILuaModule`, `ILuaOperation<TResult>`,
  `ILuaResultMapper<TSource, TResult>`, `IMemoryCodec<T>` and `ICheatEngineClientModule`) never gain, lose or change a
  member.
- **Experimental APIs**, marked `[Experimental("CECLIENT500x")]`, can change or be removed in a minor release until
  their live qualification passes; using one is an explicit opt-in to that diagnostic.
- **Charter:** the public API charter of the `CheatEngine.Client.Abstractions` README fixes the Try and throwing forms,
  the names, the exception policy (no Client exception has a public constructor; `CheatEngineFailure.Throw` and
  `ToException` create them) and the CheatEngine.SDK value types a public signature may use; 1.x only adds to it.
- **Not contractual:** the text of `CheatEngineFailure.Message`, of `CheatEngineFailure.Operation` and of exception
  messages. Classify a failure by `CheatEngineFailure.Kind` and `HostEffect`, never by text.
- **CheatEngine.SDK:** Client 1.x depends on CheatEngine.SDK `[2.0.0, 3.0.0)`. Its descriptive value types (`Address`,
  `PointerSize`, `ModuleInfo` and the others the charter lists) are part of the Client's public signatures, so a new
  CheatEngine.SDK major version means a new Client major version, never a Client minor release.
- Removing or changing a stable public member, or changing the meaning of a value, happens only in a new major version.

The package contains no assembly of its own: its public types come from the Abstractions,
Extensions.DependencyInjection, Hosting and Fluent assemblies it brings. They live in the root namespace
`CheatEngine.Client` (`ICheatEngineClient`, `ICheatEngineLease`) and in functional namespaces such as
`CheatEngine.Client.Memory`, `.Scanning`, `.Tables`, `.Lua`, `.Processes`, `.Runtime`, `.Hosting` and
`.Extensions.DependencyInjection`.

## Documentation

| Package | Read it for |
|---|---|
| [`CheatEngine.Client.Hosting`](https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/libs/CheatEngine.Client.Hosting/README.md) | The plugin base class, the per-enable provider, logging, deployment and every `CECLIENT` build diagnostic |
| [`CheatEngine.Client.Extensions.DependencyInjection`](https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/libs/CheatEngine.Client.Extensions.DependencyInjection/README.md) | `builder.Client`, options, opt-ins, memory codecs and memory budgets |
| [`CheatEngine.Client.Fluent`](https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/libs/CheatEngine.Client.Fluent/README.md) | `Aob(...)`, `At(...)` and `Batch<T>()` builders |
| [`CheatEngine.Client.Abstractions`](https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/libs/CheatEngine.Client.Abstractions/README.md) | Every contract, failure and limit, the experimental APIs and the public API charter |
| [`CheatEngine.Client.Core`](https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/libs/CheatEngine.Client.Core/README.md) | The diagnostic events and the cost of Cheat Engine calls |
| [`CheatEngine.Client.Templates`](https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/templates/CheatEngine.Client.Templates/README.md) | `dotnet new ceplugin` |

Changes are listed in the [CHANGELOG](https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/CHANGELOG.md).
Report vulnerabilities privately, as
[SECURITY.md](https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/SECURITY.md) describes. Use the Client
only on local processes you are authorized to inspect or modify.
