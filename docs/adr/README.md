# Architecture Decision Records

## Context

These records define the architectural constraints for `CheatEngine.Client`: its public boundaries, plugin lifetime,
package layout, and delivered capability scope. They complement API documentation; they are not a product roadmap or a
substitute for the SDK contract.

## Why this directory exists

The Client sits above a host-bound SDK with thread-affinity and ownership rules. Recording the decisions keeps a
convenient public API from silently acquiring unsafe handles, cross-epoch state, indirect SDK build assets, or
unsupported host assumptions.

## How the records improve the project

Each ADR is a review boundary. A change that alters a listed decision must update the relevant record or add a new one,
so source code, package behavior, templates, and CI gates remain aligned.

| Record | Decision | Primary effect |
|---|---|---|
| [0001](0001-layered-in-process-architecture.md) | Keep the Client in-process and isolate SDK domain mapping in Core. | Prevent host handles and transport concerns from leaking through functional Client APIs. |
| [0002](0002-plugin-activation-lifecycle.md) | Create one composition and Client scope for each plugin enable epoch. | Makes cleanup, module rollback, and epoch invalidation deterministic. |
| [0003](0003-package-and-aot-policy.md) | Require a direct SDK reference in plugin projects and distinguish AOT analysis from an AOT plugin binary. | Preserves SDK generators and native bridge assets at the plugin boundary. |
| [0004](0004-capability-matrix.md) | Report implemented, gated, and deferred capabilities separately. | Prevents contracts and templates from implying unverified Cheat Engine behavior. |

## Delivery alignment

The Windows CI workflow restores the locked graph, builds and tests Release, packs the public APIs, validates isolated
package and template consumption, then publishes and runs the Native AOT graph probe. Live Cheat Engine validation is
intentionally opt-in and remains a release boundary where an ADR identifies it; ordinary CI does not claim to replace
that host evidence.
