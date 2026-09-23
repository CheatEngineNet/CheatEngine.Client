# CheatEngine.Client.Extensions.DependencyInjection

Explicit, AOT-aware dependency-injection composition for the high-level Cheat Engine client.

## Context

`CheatEngine.Client.Extensions.DependencyInjection` is the composition package for Client contracts and their internal
implementations. It registers the public domain services—`ICheatEngineClient`, memory, process, scanning, inspection,
table, Lua, runtime, and dispatch services—without making consumers reference implementation namespaces.

It depends on `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Options`, and
`Microsoft.Extensions.Configuration`. The package enables the .NET configuration-binding generator and uses generated
options validation for `CheatEngineClientOptions`; it does not require the Generic Host.

## Why this project exists

Cheat Engine plugins are created by the SDK through parameterless construction and have a bounded enable/disable
lifetime. A Client provider must therefore be composed explicitly for that lifetime: no global container, service
discovery, assembly scanning, or nested `ServiceProvider` is needed.

This package keeps the high-level API DI-first while preserving the Client's trimming and AOT constraints. It is the
only supported place to wire Core implementation types into the functional public contracts.

## How it helps CheatEngine.Client

`AddCheatEngineClient` adds direct service registrations, the built-in deterministic memory codecs, options services,
and the Client facade. It returns a `CheatEngineClientBuilder`; the builder only adds registrations and never builds a
provider.

Configuration is always opt-in. `BindConfiguration(IConfiguration)` reads the `CheatEngineClient` section, while
`BindConfiguration(IConfigurationSection)` lets a plugin choose a different explicit section. The package never searches
for, loads, or watches `appsettings.json` on its own.

`CheatEngineClientOptions` controls table-file policy:

- `AllowedTableRoots` is empty by default, which denies table-file load/save access.

AOB materialization and value-scan pages require their callers to provide an explicit bound. Unsafe Lua is deliberately
not an appsettings option: it can only be enabled with the explicit builder opt-in below.

The builder also provides explicit extension points:

- `AddModule<TModule>()` preserves module registration order and creates modules in the activation scope; Hosting
  enables
  modules in that order and disables them in reverse order.
- `AddMemoryCodec<T, TCodec>()` adds a singleton deterministic codec without reflective structure marshalling.
- `EnableUnsafeLuaExecution()` registers the unsafe Lua facade only for the current activation policy. It never exposes
  an SDK `LuaState`. The opt-in satisfies only the policy evidence gate; it does not prove package support, host
  globals, or live qualification.

```csharp
using CheatEngine.Client.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
IConfiguration configuration = new ConfigurationBuilder().Build();

services.AddCheatEngineClient(configuration)
    .AddMemoryCodec<MyValue, MyValueCodec>();
```

For an SDK-loaded plugin, use `CheatEngine.Client.Hosting` instead of manually building this collection. The hosting
package creates one validating provider for each enable epoch and resolves `IOptions<CheatEngineClientOptions>`
immediately, so generated and semantic validation run before Client work starts.

## Provider and scope contract

`AddCheatEngineClient` configures one provider; it does not define a persistent application root. The supported plugin
path builds a **fresh provider per enable epoch**, then opens one activation scope. The Core Client graph, options, and
deterministic codecs are intentionally provider-local singleton registrations: that is safe because the provider itself
is discarded at disable. Modules are scoped so they can consume scoped application services, but their scoped lifetime
does not make a second scope in the same provider a fresh Client activation.

Two scopes made from one external provider are ordinary sibling DI scopes. Their scoped services and modules differ,
while provider singletons, options, and Client services remain shared until the provider is disposed. Do not reuse such
a provider across Cheat Engine enable epochs; Client does not offer a persistent-root hosting mode or an activation
factory for it. Any future external-provider model must specify and test its activation-bound registrations separately.

The container disposes services it creates at their scope/provider boundary. Do not dispose services resolved from DI
in a module or plugin callback, and do not register one disposable object through multiple forwarding aliases. Give the
disposable one owning descriptor; expose an additional non-disposable facade when an application needs an alias. The
host itself owns only its `ConfigurationManager`, which it releases after the scope and provider.

## Memory resource limits

`CheatEngineClientOptions.MemoryResourceLimits` (configuration keys such as
`CheatEngineClient:MemoryResourceLimits:MaximumReadBytes`) is validated with the options and copied when the memory
client is created, so a later change does not affect the running activation. It uses the Client limit vocabulary.
`MaximumReadBytes`, `MaximumWriteBytes`, and `MaximumStringBytes` set the **maximum block size** of one operation.
`MaximumBatchOperationCount`, capped by `MemoryBatchLimits.MaximumOperations` (1024), sets the **request count per
batch**, and `MaximumBatchPayloadBytes` bounds that count multiplied by the element size. Together these budgets set the
**maximum scratch allocation**: the largest managed buffer allocated for one operation. The target process gets no
allocation. The **partial-effect state** of a batch write is not configurable: `MemoryBatchWriteEffectState` reports
it, because a failed batch keeps its completed prefix and is never rolled back.
