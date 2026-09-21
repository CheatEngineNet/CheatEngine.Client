# ADR 0002: One Client activation per plugin enable epoch

- Status: Accepted
- Date: 2026-09-20

## Context

The SDK creates a plugin through a parameterless constructor and attaches the Lua runtime only for the enabled
lifetime. A long-lived provider, service, callback, or CE resource would therefore be able to outlive the host state
that makes it valid.

## Decision and why

`CheatEngineClientPlugin` creates a new `CheatEnginePluginBuilder`, service provider, and DI scope from `OnEnable`.
The derived plugin adds its own configuration sources in `Configure`; the Client does not load configuration implicitly.
For file-based configuration, a plugin may explicitly add an optional `appsettings.json` with `reloadOnChange: false`.

The base validates options after building the provider, resolves the activation-scoped aggregate `ICheatEngineClient`,
enables registered modules in registration order, then invokes `OnClientEnabled`. A failed enable rolls back every
callback that was entered.

On disable, the base clears the active Client reference first, invokes `OnClientDisabling`, disables enabled modules in
reverse order, drains Client-owned CE resources while the host is still attached, and finally disposes the scope,
provider, and configuration. Cleanup is best-effort and aggregates failures only after every step has been attempted.

Before an activation is published, construction has a separate rollback path. It releases only the stages that were
actually acquired—scope, provider, then the host-created configuration—in reverse construction order. Each stage gets
one independent cleanup attempt. The construction exception remains the primary failure; cleanup failures follow it as
diagnostics. Module callbacks and Client-owned CE resource draining are never run for an incomplete activation.

## Invariants

- SDK calls are forbidden from plugin constructors, field initializers, and static initialization. SDK-dependent
  services exist only while the plugin is enabled.
- `Configure` is a one-activation composition hook. It must not build a provider or reconfigure Client options after
  provider construction; reloadable runtime configuration would violate the activation boundary.
- A Client scope, cancellation token, SDK handle, Lua reference, or CE-owned resource never crosses an enable/disable
  epoch. `ICheatEngineClient.Epoch` and `Stopping` identify the active lifetime.
- Public Client operations are synchronous. They do not retain Lua state across an `await`, and main-thread work enters
  the SDK dispatcher as a bounded operation.
- The DI container owns disposal of services it created. Hosting owns the `ConfigurationManager` instance it created
  and releases it once after the scope and provider; it never disposes resolved services individually.

## Consequences and project value

- Re-enable starts from a new composition rather than a partially disposed singleton graph.
- Reverse-order cleanup and rollback make module ownership explicit and make failure paths unit-testable without
  mocking SDK statics.
- Clearing admission before cleanup prevents consumers from acquiring an activation while its resources are being
  released.
