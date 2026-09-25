# CheatEngine.Client.Extensions.DependencyInjection

Explicit, AOT-aware dependency-injection composition for the high-level Cheat Engine client.

## Context

`CheatEngine.Client.Extensions.DependencyInjection` is the composition package for Client contracts and their internal
implementations. It registers the public domain services—`ICheatEngineClient`, memory, process, scanning, inspection,
table, Lua, runtime, and dispatch services—without making consumers reference implementation namespaces.

It is the composition layer of `CheatEngine.Client.Hosting`: `CheatEnginePluginBuilder` calls `AddCheatEngineClient`
for each activation provider, which Hosting builds, validates, and disposes around one Cheat Engine enable epoch, and
hands the returned builder to the plugin as `builder.Client`. Composing the Client in a provider that Hosting does not
own is not supported in 1.0: the Client services capture the SDK plugin context of one enable and must not outlive or
precede it.

It builds on Microsoft.Extensions dependency injection, options, configuration and logging (the packages are listed
under "Dependencies" below). The package enables the .NET configuration-binding generator and uses generated options
validation for `CheatEngineClientOptions`; it does not require the Generic Host.

## Installation

A plugin references [`CheatEngine.Client`](https://www.nuget.org/packages/CheatEngine.Client), which brings this
package at exactly its own version through Hosting, and uses it as `builder.Client` in
`CheatEngineClientPlugin.Configure`. The
[CheatEngine.Client README](https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/src/CheatEngine.Client/README.md)
gives the plugin project, the requirements (`net10.0`, C# 14, a .NET SDK 10.0.401 or later, Cheat Engine 7.7.0.10621
x64, a direct `CheatEngine.SDK` reference in `[2.0.0, 3.0.0)`) and a minimal plugin. Do not reference this package on
its own: composing the Client outside Hosting is not supported in 1.0, and every Client package takes the same version
(the seven packages ship in lockstep).

## Why this project exists

Cheat Engine plugins are created by the SDK through parameterless construction and have a bounded enable/disable
lifetime. A Client provider must therefore be composed explicitly for that lifetime: no global container, service
discovery, assembly scanning, or nested `ServiceProvider` is needed.

This package keeps the high-level API DI-first while preserving the Client's trimming and AOT constraints. It is the
only supported place to wire Core implementation types into the functional public contracts.

## How it helps CheatEngine.Client

`AddCheatEngineClient` adds direct service registrations, logging, options services and their validators, and the
Client facade. It returns a `CheatEngineClientBuilder`; the builder only adds registrations and never builds a
provider.

Configuration is always opt-in. `BindConfiguration(IConfiguration)` reads the `CheatEngineClient` section, while
`BindConfiguration(IConfigurationSection)` lets a plugin choose a different explicit section. The package never searches
for, loads, or watches `appsettings.json` on its own.

`CheatEngineClientOptions` controls table-file policy and memory budgets. Both properties are read-only and never
`null`; configuration binding and `Configure` delegates fill them:

- `AllowedTableRoots` (`IList<string>`) is empty by default, which denies table-file load/save access.
- `MemoryResourceLimits` holds the memory budgets described below.

The options validators are internal: `AddCheatEngineClient` registers them, and Hosting resolves the options before any
Client work, so an invalid value fails the enable.

AOB materialization and value-scan pages require their callers to provide an explicit bound. Unsafe Lua is deliberately
not an appsettings option: it can only be enabled with the explicit builder opt-in below.

The builder also provides explicit extension points:

- `AddModule<TModule>()` preserves module registration order and creates modules in the activation scope; Hosting
  enables modules in that order and disables them in reverse order. `AddLuaModule<TModule>()` registers a Lua module,
  such as one generated from `[CheatEngineLuaModule]`, whose exports the activation registers and releases.
- `EnableUnsafeLuaExecution()` registers the unsafe Lua facade only for the current activation policy. It never exposes
  an SDK `LuaState`. The opt-in satisfies only the policy evidence gate; it does not prove package support, host
  globals, or live qualification.
- `EnableAutoAssemblerPatches()` (experimental, `CECLIENT5004`) registers `IAutoAssemblerClient` the same way and
  satisfies the policy gate of `Client.AutoAssemblerPatches`. Configuration cannot enable it, calling it twice keeps one
  registration, and an `IAutoAssemblerClient` registered by another path makes it throw. Resolve the client from the
  activation provider, for example in a module constructor.

The Client never registers or resolves a memory codec implicitly. The built-in primitives (8- to 64-bit integers,
`float`, `double`, and `Address`) need none. A codec for any other type is an ordinary application service: register it
in `Configure` with `Services.AddSingleton<IMemoryCodec<T>, TCodec>()`, receive it by constructor injection, and pass it
with each codec request (`MemoryReadRequest<T>`, `MemoryWriteRequest<T>`):

```csharp
using System.Buffers.Binary;

using CheatEngine.Client;
using CheatEngine.Client.Hosting;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Modules;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Annotations.Plugin;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.DependencyInjection;

namespace MyPlugin;

[CheatEnginePlugin("My Plugin")]
public sealed class Plugin : CheatEngineClientPlugin
{
    protected override void Configure(CheatEnginePluginBuilder builder)
    {
        builder.Services.AddSingleton<IMemoryCodec<Position>, PositionCodec>();
        builder.Client.AddModule<PositionModule>();
    }
}

public readonly record struct Position(float X, float Y);

public sealed class PositionCodec : IMemoryCodec<Position>
{
    public bool TryRead(IMemoryReadContext context, Address address, out Position value, out CheatEngineFailure failure)
    {
        Span<byte> bytes = stackalloc byte[8];
        if (!context.TryReadBytes(address, bytes, out failure))
        {
            value = default;
            return false;
        }

        value = new Position(
            BinaryPrimitives.ReadSingleLittleEndian(bytes),
            BinaryPrimitives.ReadSingleLittleEndian(bytes[4..]));
        return true;
    }

    public bool TryWrite(
        IMemoryWriteContext context, Address address, in Position value, out CheatEngineFailure failure)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value.X);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[4..], value.Y);
        return context.TryWriteBytes(address, bytes, out failure);
    }
}

public sealed class PositionModule(IMemoryCodec<Position> codec) : ICheatEngineClientModule
{
    public void OnEnabled(ICheatEngineClient client)
    {
        // An illustrative pointer chain. A throwing form fails the enable when it does not resolve: use the Try forms
        // when the target may not be ready.
        Address root = client.Inspection.ResolveAddress(
            new SymbolExpression("game.exe+1A2B30"), AddressResolutionMode.Default);
        Address player = client.Memory.At(root).Follow([0x10, 0x28]).Resolve();

        Position position = client.Memory.Read(new MemoryReadRequest<Position>(player, codec));
        client.Memory.Write(new MemoryWriteRequest<Position>(player, position with { Y = 0 }, codec));
    }

    public void OnDisabling(ICheatEngineClient client)
    {
    }
}
```

A codec reads and writes through its context, never through Cheat Engine directly: the context charges every access
against the memory budgets below, and a codec reports an expected failure by returning `false` with the context's
failure, or with the `default` failure to let the Client classify it.

The hosting package creates one validating provider for each enable epoch and resolves
`IOptions<CheatEngineClientOptions>` immediately, so generated and semantic validation run before Client work starts.

## Provider and scope contract

`AddCheatEngineClient` configures one provider; it does not define a persistent application root. The supported plugin
path builds a **fresh provider per enable epoch**, then opens one activation scope. The Core Client graph and options
are intentionally provider-local singleton registrations: that is safe because the provider itself is discarded at
disable. Modules are scoped so they can consume scoped application services, but their scoped lifetime
does not make a second scope in the same provider a fresh Client activation.

A fresh provider per enable does not isolate CheatEngine.SDK static state (`PluginHost` and the current plugin
context) or Cheat Engine's Lua globals: every plugin that loads the same SDK assemblies into the Cheat Engine process
shares them, whatever its container. Coexistence of two plugins that share or do not share the SDK assemblies is
the live scenario Q09 (two plugins in one Cheat Engine process), for which no Client receipt exists yet.

Two scopes made from one external provider are ordinary sibling DI scopes. Their scoped services and modules differ,
while provider singletons, options, and Client services remain shared until the provider is disposed. Do not reuse such
a provider across Cheat Engine enable epochs; Client does not offer a persistent-root hosting mode or an activation
factory for it. Any future external-provider model must specify and test its activation-bound registrations separately.

The container disposes services it creates at their scope/provider boundary. Do not dispose services resolved from DI
in a module or plugin callback, and do not register one disposable object through multiple forwarding aliases. Give the
disposable one owning descriptor; expose an additional non-disposable facade when an application needs an alias. The
host itself owns only its `ConfigurationManager`, which it releases after the scope and provider.

## Diagnostics

`AddCheatEngineClient` calls `AddLogging()` and gives the activation's Core lifetime a diagnostics sink over the
provider's `ILoggerFactory`. Core emits bounded events (event ids 1000–1800; the
[`CheatEngine.Client.Core` README](https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/libs/CheatEngine.Client.Core/README.md#diagnostics-events)
lists them) under one category per domain: `CheatEngine.Client.Runtime`, `.Processes`, `.Memory`, `.Tables`,
`.Inspection`, `.Scanning`, `.Lua`, `.Lifetime`, and `.Assembly`. Select them with the standard `Logging:LogLevel`
filters, for example
`"CheatEngine.Client.Memory": "Debug"`; no Client option controls collection. A capability refusal is logged once per
capability and operation per activation. The events carry epochs, counts, widths, durations, and closed names only,
never addresses, values, symbol names, paths, or Lua text, and a logging provider that throws is contained: it cannot
change a Client result or cleanup.

## Memory resource limits

`CheatEngineClientOptions.MemoryResourceLimits` (configuration keys such as
`CheatEngineClient:MemoryResourceLimits:MaximumReadBytes`) is validated with the options and copied when the memory
client is created, so a later change does not affect the running activation. It uses the Client limit vocabulary.
`MaximumReadBytes`, `MaximumWriteBytes`, and `MaximumStringBytes` set the **maximum block size** of one operation.
`MaximumBatchOperationCount`, capped by `MemoryBatchLimits.MaximumOperationCount` (1024), sets the **request count per
batch**, and `MaximumBatchPayloadBytes` bounds that count multiplied by the element size. Together these budgets set the
**maximum scratch allocation**: the largest managed buffer allocated for one operation. The target process gets no
allocation. The **partial-effect state** of a batch write is not configurable: `MemoryBatchWriteEffectState` reports
it, because a failed batch keeps its completed prefix and is never rolled back.

## Dependencies

The package depends on `CheatEngine.Client.Abstractions` and `CheatEngine.Client.Core` at exactly its own version (the
Client packages ship in lockstep), and on `Microsoft.Extensions.Configuration.Abstractions`,
`Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`,
`Microsoft.Extensions.Options.ConfigurationExtensions` and `Microsoft.Extensions.Options.DataAnnotations`. Each
Microsoft.Extensions dependency is a minimum version: the latest 10.0.x patch reviewed for this release, which the
Client moves only through reviewed dependency updates and never lowers. NuGet resolves the lowest version that satisfies
every minimum of the graph; a plugin that needs a later 10.0.x patch references it directly.

This assembly grants `InternalsVisibleTo` to `CheatEngine.Client.Hosting`, which composes it, and to the repository's
test projects. The Client assemblies are not strong-named, so such a grant names an assembly, not a signing key. Its
internal members are not a contract and change in any release: use the public API only.
