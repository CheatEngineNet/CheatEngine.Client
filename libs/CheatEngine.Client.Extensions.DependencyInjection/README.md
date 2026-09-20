# CheatEngine.Client.Extensions.DependencyInjection

Explicit, AOT-aware dependency-injection composition for the high-level Cheat Engine client.

## Context

`CheatEngine.Client.Extensions.DependencyInjection` is the composition package for Client contracts and their internal implementations. It registers the public domain services—`ICheatEngineClient`, memory, process, scanning, inspection, table, Lua, runtime, and dispatch services—without making consumers reference implementation namespaces.

It depends on `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Options`, and `Microsoft.Extensions.Configuration`. The package enables the .NET configuration-binding generator and uses generated options validation for `CheatEngineClientOptions`; it does not require the Generic Host.

## Why this project exists

Cheat Engine plugins are created by the SDK through parameterless construction and have a bounded enable/disable lifetime. A Client provider must therefore be composed explicitly for that lifetime: no global container, service discovery, assembly scanning, or nested `ServiceProvider` is needed.

This package keeps the high-level API DI-first while preserving the Client's trimming and AOT constraints. It is the only supported place to wire Core implementation types into the functional public contracts.

## How it helps CheatEngine.Client

`AddCheatEngineClient` adds direct service registrations, the built-in deterministic memory codecs, options services, and the Client facade. It returns a `CheatEngineClientBuilder`; the builder only adds registrations and never builds a provider.

Configuration is always opt-in. `BindConfiguration(IConfiguration)` reads the `CheatEngineClient` section, while `BindConfiguration(IConfigurationSection)` lets a plugin choose a different explicit section. The package never searches for, loads, or watches `appsettings.json` on its own.

`CheatEngineClientOptions` controls table-file policy:

- `AllowedTableRoots` is empty by default, which denies table-file load/save access.

AOB materialization and value-scan pages require their callers to provide an explicit bound. Unsafe Lua is deliberately
not an appsettings option: it can only be enabled with the explicit builder opt-in below.

The builder also provides explicit extension points:

- `AddModule<TModule>()` preserves module registration order and creates modules in the activation scope; Hosting enables
  modules in that order and disables them in reverse order.
- `AddMemoryCodec<T, TCodec>()` adds a singleton deterministic codec without reflective structure marshalling.
- `EnableUnsafeLuaExecution()` registers the unsafe Lua facade only for the current activation policy. It never exposes an SDK `LuaState`.

```csharp
using CheatEngine.Client.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
IConfiguration configuration = new ConfigurationBuilder().Build();

services.AddCheatEngineClient(configuration)
    .AddMemoryCodec<MyValue, MyValueCodec>();
```

For an SDK-loaded plugin, use `CheatEngine.Client.Hosting` instead of manually building this collection. The hosting package creates one validating provider for each enable epoch and resolves `IOptions<CheatEngineClientOptions>` immediately, so generated and semantic validation run before Client work starts.
