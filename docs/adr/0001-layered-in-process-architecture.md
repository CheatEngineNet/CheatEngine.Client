# ADR 0001: Layered in-process Client architecture

- Status: Accepted
- Date: 2026-09-20

## Context

`CheatEngine.SDK` exposes a managed route into a live Cheat Engine plugin host. Its Lua state, CE objects, ownership
wrappers, and thread-affinity rules are host-bound implementation details. The Client needs a higher-level,
dependency-injection-friendly API without recreating the SDK ABI or turning those implementation details into public
lifetime obligations.

## Decision and why

`CheatEngine.Client` is an in-process, high-level API hosted by a Cheat Engine plugin. It is not an external-process
adapter for `CheatEngine.SDK`, and V1 has no IPC endpoint.

```text
Plugin assembly
  -> CheatEngine.Client.Hosting
     -> CheatEngine.Client.Extensions.DependencyInjection
        -> CheatEngine.Client.Core -> CheatEngine.SDK -> Cheat Engine Lua/runtime
           ^
CheatEngine.Client.Abstractions <- CheatEngine.Client.Fluent
```

`Abstractions` owns public contracts, copied value types, failures, and module boundaries. `Fluent` creates immutable
operation descriptions and has no SDK access. `Core` is the only Client domain layer that maps operations to the SDK.
The DI extension owns explicit registrations and options validation; `Hosting` connects that composition to the plugin
lifecycle. The root `CheatEngine.Client` package is the consumer-facing umbrella.

The labels in the diagram are package and assembly identities, not consumer namespace prefixes. Public contracts use
functional namespaces such as `CheatEngine.Client.Memory`, `.Scanning`, `.Tables`, `.Lua`, `.Runtime`, and `.Processes`
regardless of their delivery package.

## Consequences and project value

- Public APIs do not expose `LuaState`, `CEObject`, `Owned<T>`, native pointers, or other host-bound SDK lifetimes.
- Application Lua exports are registered explicitly through `ILuaClient.RegisterModule`; the returned lease owns
  activation-scoped unregistration. An `ILuaModule` may encapsulate generated SDK bindings internally but cannot let a
  Lua state or SDK handle cross the Client contract.
- Builders remain pure. A complete Cheat Engine operation, including temporary-owner cleanup, crosses the SDK boundary
  as one synchronous operation. This keeps fluent composition testable without a live host.
- A future remote bridge is a separate plugin-hosted product with its own protocol, authentication, backpressure, and
  epoch-lifetime decision. It cannot introduce retries, transport concerns, or serializable handles into
  `ICheatEngineClient` V1.
