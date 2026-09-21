# ADR 0004: Capability delivery matrix

- Status: Accepted
- Date: 2026-09-20

## Context

An interface alone does not establish that a Cheat Engine operation is safe to create, use, dispose, or repeat across
plugin activation. Source and package evidence must also be kept separate. The reviewed SDK source baseline
`aa3fcc3cdf629468e69d0c68817183d44a719894` contains
`CheatEngine.SDK.Engine.Scanning.Values.MemoryScanSessions.TryCreate`, whereas the released `CheatEngine.SDK` 1.0.0
package consumed by Client does not expose that factory. The Client therefore cannot adopt `MemScan` and `FoundList`
instances from the released package without inventing an unverified handle-lifetime contract.

| Evidence object | `MemoryScanSessions.TryCreate` | Client consequence |
|---|---|---|
| SDK source `aa3fcc3cdf629468e69d0c68817183d44a719894` | Present | A later SDK artifact may make an adoption path possible; source presence alone does not enable Client. |
| Released NuGet `CheatEngine.SDK` 1.0.0 | Absent | `IValueScanner` remains capability-gated. |

The required Cheat Engine 7.7 x64 ownership and lifecycle qualification remains separate from both observations.

## Decision and why

The Client reports implementation status separately from public vocabulary. A capability is not represented as usable
only because an abstraction can describe it.

### Status vocabulary

- **Implemented**: the aggregate `ICheatEngineClient` composes an operational Client implementation for an enable
  epoch. The listed host boundary still applies before claiming live-host qualification.
- **Capability-gated**: a public surface exists, but the implementation reports an unsupported capability instead of
  assuming an unproven SDK ownership, affinity, or cancellation contract.
- **Deferred**: V1 intentionally provides no operational route through the aggregate Client.

| Area | Public surface | Current source status | Boundary before live qualification |
|---|---|---|---|
| Plugin lifecycle and DI | `CheatEngineClientPlugin`, `CheatEnginePluginBuilder`, modules, options | Implemented | Exercise enable, rollback, disable, and repeated epoch activation in a CE host. |
| Runtime and capability facts | `ICheatEngineRuntime` | Implemented | Add evidence-backed observations per CE version and architecture as the SDK surface expands. |
| Process, module, and inspection | `IProcessClient`, `IInspectionClient` | Implemented | Map only SDK APIs with verified normal-return and thread contracts. |
| Typed memory | `IMemoryClient`, codecs, and requests | Implemented | Keep reads and writes bounded and classify host failures without leaking Lua state. |
| AOB scan | `IPatternScanner`, `AobScanRequest` | Implemented | Copy returned addresses and release temporary SDK owners in the same CE operation. |
| Value scan | `IValueScanner`, `IValueScanSession`, page records | Capability-gated | Require CE 7.7 x64 evidence for creation, first/next scan, read, ordered destruction, disable, and re-enable. |
| Address tables | `ITableClient`, copied record contracts | Implemented | Validate current-table lifetime, updates, and configured table-root enforcement. |
| Protected and unsafe Lua | `ILuaClient`, `IUnsafeLuaClient` | Protected operations implemented; arbitrary source is policy-gated | Keep arbitrary source opt-in and unavailable by default. |
| IPC or remote Client | None in V1 | Deferred | Define transport, authentication, handle epochs, and backpressure in a separate product decision. |

## Consequences and project value

- `IValueScanner.CreateSession` remains unavailable until the internal `createMemScan` and `createFoundList` owner has
  passed creation, first and next scans, reading, ordered destruction, disable, and re-enable in Cheat Engine 7.7 x64.
  The Client will not bypass the SDK restriction with reflection or a hand-rolled public owner.
- Templates and README examples can expose only guarded operations. They must not imply value scanning, arbitrary Lua,
  or host-side behavior that ordinary CI has not established.
- The matrix gives reviewers one place to distinguish a deliberate product gate from a missing implementation, keeping
  package release claims honest as SDK evidence changes.

## Current template boundary

The template is the maintained source example. It compiles against the hosted plugin shape with explicit JSON
configuration and no reload watcher, demonstrates explicit module DI and validated options, and registers an
application-owned `ILuaModule` through `ILuaClient.RegisterModule`. Its activation-scoped lease owns unregistration of
the generated SDK Lua export. SDK binding calls remain inside that application module; no Lua state or SDK ownership
handle crosses the Client contract.

The generated project takes an Address List snapshot and performs a guarded AOB and typed-memory probe. It performs no
operation when the required runtime precondition is absent, does not log target-memory contents, and retains the epoch,
ownership, and main-thread constraints defined by these ADRs.
