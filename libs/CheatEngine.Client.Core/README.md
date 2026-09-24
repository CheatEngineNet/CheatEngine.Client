# CheatEngine.Client.Core

## Context

`CheatEngine.Client.Core` is the SDK-facing implementation layer of `CheatEngine.Client`. It maps
the public contracts from `CheatEngine.Client.Abstractions` onto `CheatEngine.SDK` 1.x while a
Cheat Engine plugin activation is current.

This package contains the main-thread dispatcher adapter, runtime probes, target-selection
tracking, typed memory and AOB adapters, copied inspection/table operations, protected Lua
operations, and lifecycle-owned cleanup infrastructure. It is an in-process layer for local,
authorized targets; it is not an IPC client or a standalone Cheat Engine host.

`ICheatEngineClient` is the governing contract of that in-process model: it runs inside an enabled Cheat Engine plugin
on Cheat Engine's main thread. It is not a `ceserver` network client, not Cheat Engine's `luaclient` (CELUA) library,
and not a universal RPC client.

## Why This Project Exists

The Client must use the SDK directly for its real runtime work, but its SDK-specific ownership and
Lua details must not become application-level implementation concerns. Core provides that boundary:
it translates stable contracts to SDK calls, normalizes expected failures, and owns the rules that
make an enable/disable/re-enable cycle safe.

Core references `CheatEngine.Client.Abstractions` and the full SDK package. It never references
`CheatEngine.Client.Fluent`. The Dependency Injection and Hosting packages compose Core into the
activation-scoped `ICheatEngineClient`; applications should obtain that facade through hosting or
DI instead of constructing Core services.

```text
Abstractions  ←  Core  ←  DependencyInjection  ←  Hosting
                    ↑
             CheatEngine.SDK

Abstractions  ←  Fluent
```

## How It Improves CheatEngine.Client

- Sends every Cheat Engine operation through the synchronous SDK main-thread dispatcher.
- Captures an SDK activation epoch and rejects stale work instead of letting a resource survive a
  disable/re-enable boundary.
- Closes ordinary work admission during disable, then drains Client-owned resources on the CE main
  thread in reverse creation order while the SDK context is still valid.
- Separates the plugin epoch from the selected-target epoch so target changes invalidate only
  target-bound resources.
- Copies SDK-owned scan/list data into managed values before releasing the owner; the public API
  never leaks SDK Lua state, CE objects, or `Owned<T>` wrappers.
- Maps predictable host failures to `CheatEngineFailure` and preserves ordinary .NET exceptions
  for programming errors.

## Ownership and Delivery Boundary

Core is a delivery package, not a second public facade. Its concrete service implementations and
infrastructure are internal; the supported public contracts remain in functional namespaces from
the Abstractions package such as `CheatEngine.Client.Memory`, `.Scanning`, `.Tables`, `.Lua`, and
`.Runtime`. Do not add consumer namespaces such as `CheatEngine.Client.Core`.

The package enables neither an SDK plugin entry point nor dynamic loading on its own. The final
plugin project must directly reference both `CheatEngine.Client` and `CheatEngine.SDK` so that the
SDK generator, build assets, native Lua bridge, and host bootstrap execute at the actual plugin
boundary.

## Capability Status and Limits

Core implements Client mappings, not host-qualified, for runtime facts and capabilities, process
selection, typed memory, bounded pointer chains and strings, AOB scans, copied inspection, Address
List operations, trusted table paths, and protected typed Lua work. They are tested against ports
and fakes; no Client qualification receipt exists yet, so their qualification gate reports
`Unknown` and no capability reports `Available`. All CE calls remain synchronous; a cancellation
token is observed before dispatch or between Client-managed steps, not as an interruption of an
already-running Lua primitive.

The other domains are contract-only: Core composes an unavailable adapter that refuses every
operation with `CapabilityUnavailable` and `CheatEngineHostEffect.NotStarted`, without dispatching
Cheat Engine work, and a capability test keeps them that way while the consumed SDK major is 1.

- **Value scans**: CheatEngine.SDK 1.0.0 does not provide the public MemScan and FoundList
  ownership factory required by Client. Although the public session contract and state machine are
  present, Core reports a capability failure rather than making an unverified ownership assumption.
- **Allocations and assembly**: CheatEngine.SDK 1.0.0 provides no target-bound owned allocation or
  Auto Assembler primitive, and allocation owners must not be wired before the package migration.

Core composes nothing for the domains that no CheatEngine.SDK primitive backs (timers, hotkeys, the debugger,
the speed hack, hashing, DBVM and remote execution): the Client has no contract for them, as the
`CheatEngine.Client.Abstractions` README states under "Not offered in 1.0".

The per-capability evidence (implementation, package, host, qualification, policy and lifetime
gates) is in the capability table of the `CheatEngine.Client.Abstractions` README. The package gate
compares the CheatEngine.SDK identity embedded in this assembly at build time (version, source
commit and NuGet content hash, as `AssemblyMetadata`, taken from the locked and restored package)
with the informational version of the `CheatEngine.SDK.Engine` assembly actually loaded; it reads
assembly attributes only. A build that cannot embed that identity fails with
`CHEATENGINECLIENT9050`, except the SDK-side canary build, whose package gate reports `Unknown`. UI/forms,
structures, Mono/IL2CPP, and ABI hooks are outside this layer.

## Runtime facts, target selection and pointer width

The runtime probes are read-only (`getCEVersion`, `getSystemArchitecture`, `getABI`,
`getOpenedProcessID`, `targetIs64Bit`, `targetIsX86`, `targetIsArm`, `getPointerSize`): a snapshot
never loads a driver, runs remote code, changes the target or allocates target memory. Each is a
`ClientLuaGlobals` binding generated by the CheatEngine.SDK 1.0.0 Lua binding generator and a
registered ADR-01 exception, listed for removal in the SDK 2.0 migration guide. Every observation reads the opened process identifier first and again
at the end: with no target opened, Cheat Engine reports the facts of an x64 target, and facts
observed while the target changed, or that a failed closing read cannot confirm, are discarded
(`Unknown` in the runtime snapshot, `IndeterminateHostResult` from `IProcessClient`, with a
message that names the change or the failed read) instead of being mixed. The
ISA comes from the x86 and ARM family facts with the 64-bit fact, never from the 64-bit fact alone.
A generated binding reports an undefined global, a raised Lua error and an unexpected result type
as the same exception with CheatEngine.SDK 1.0.0, so a failed probe is recorded as faulted evidence
with that reason and the fact stays `Unknown`.

Cheat Engine's selected target is ambient. `ProcessClient` advances the target-selection epoch,
and releases target-bound leases, when the PID changes, when a known ISA or process width changes
to another known value, or when Cheat Engine reports no target; a fact that is transiently unknown
keeps the epoch and the last known value. Local process name and path come from the operating
system and describe local processes only.

Codecs use the process width. When Cheat Engine's configured pointer size is known and differs from
the process width, the Client's own pointer-typed operations are refused before any memory access
(`OperationRejected`, `NotStarted`); the configured size is exposed as a fact to custom codecs.
A width that cannot be observed (a faulted or non-local opened-process read, an unconfirmed or
changed target) is reported as `IndeterminateHostResult`; only an observed "no process opened" is
`TargetNotAttached`.

Cost: every `ReadPrimitive<Address>`/`WritePrimitive<Address>` call, every Address primitive batch,
every pointer chain and every built-in Address codec invocation observes the target facts once
inside its dispatched call, which adds about six Lua global calls (the opened PID twice,
`targetIs64Bit`, `targetIsX86`, `targetIsArm`, `getPointerSize`). A batch pays this once for all
its items, so hot pointer-read loops should use batches or explicit 32/64-bit integer reads.

## Address List records and symbols

Every trusted table load that reaches Cheat Engine advances the table generation of the activation
inside the dispatched load, on Cheat Engine's main thread; a record identifier handed out before it
is refused with `InvalidState` (checked before dispatch and again inside the dispatched call) until
a new snapshot observes it. Each snapshot is judged by the generation read when it was copied, so a
snapshot copied before a concurrent load never hands out current identifiers. `TrySetActive` reads the record's state before and after one setter call and
reports applied, unchanged, refused by the host, pending or indeterminate; it never retries.
Symbol registration refuses a name that already resolves, and a lease unregisters its name only
when the name still resolves to the leased address (a replaced name is left in place). Both checks
use the same CheatEngine.SDK 1.0.0 `EngineInspection.ResolveAddress` call and are best effort, not
atomic.

## Diagnostics events

Core has no logging dependency. It reports bounded events to an internal sink carried by the
activation lifetime; `CheatEngine.Client.Extensions.DependencyInjection` writes them through
`Microsoft.Extensions.Logging` with source-generated events and one category per domain, so the
standard `Logging:LogLevel` filters select them:

| Event id | Level | Event | Category |
|---|---|---|---|
| 1000 | Debug | `RuntimeSnapshotCaptured`: activation epoch, target architecture, process and configured pointer bytes, mismatch flag | `CheatEngine.Client.Runtime` |
| 1001 | Debug | `CapabilityRefused`: capability id, operation, gate, gate state; once per capability and operation per activation | `CheatEngine.Client.Runtime` |
| 1100 | Debug | `TargetSelectionAdvanced`: activation and selection epochs, operation, reason (`PidChanged`, `ArchitectureChanged`, `WidthChanged`, `TargetDetached`) | `CheatEngine.Client.Processes` |
| 1200 | Information | `PointerWidthMismatchRefused`: operation, process and configured pointer bytes | `CheatEngine.Client.Memory` |
| 1201 | Debug | `MemoryBatchCompleted`: operation, requested and completed counts, effect state | `CheatEngine.Client.Memory` |
| 1300 | Debug | `TableGenerationAdvanced`: activation epoch, table generation | `CheatEngine.Client.Tables` |
| 1301 | Debug | `StaleRecordIdentifierRefused`: operation, table generation | `CheatEngine.Client.Tables` |
| 1302 | Debug | `RecordActivationNotApplied`: operation, requested state, status (`RefusedByHost`, `Pending`, `Indeterminate`) | `CheatEngine.Client.Tables` |
| 1400 | Debug | `SymbolRegistrationRejected`: operation, reason (`AlreadyResolves`, `LookupFailed`) | `CheatEngine.Client.Inspection` |
| 1401 | Debug | `SymbolLeaseReleased`: release kind | `CheatEngine.Client.Inspection` |
| 1500 | Debug | `PatternScanCompleted`: scope, host match and materialized counts, truncation, scan and copy milliseconds | `CheatEngine.Client.Scanning` |
| 1600 | Debug | `LuaOperationCompleted`: operation, failure kind or `None`, milliseconds, script length (unsafe Lua only) | `CheatEngine.Client.Lua` |
| 1700 | Warning | `CoreResourceCleanupFailed`: resource and exception type names | `CheatEngine.Client.Lifetime` |

Events are emitted after the dispatched Cheat Engine work returned, never inside a dispatched
callback. They never carry an address, a value, a symbol or module name, a path, a process
identifier or name, a Lua script or its text, an exception message or a failure object. A logger
or provider that throws is contained and never changes an operation result or a cleanup.

## Contribution and Validation

Treat lifetime, dispatch, and disposal changes as host-safety changes. Keep SDK handles internal,
route new CE work through the dispatcher, add a capability observation for optional bindings, and
test target/activation invalidation and reverse-order cleanup in
`tests/CheatEngine.Client.Core.Tests`.

Core's direct public baseline is intentionally empty; changes that create a public type require an
explicit product-surface decision and corresponding `PublicAPI.Unshipped.txt` update. Validate the
repository from its root:

```powershell
dotnet restore CheatEngine.Client.slnx --locked-mode
dotnet build CheatEngine.Client.slnx --configuration Release --no-restore
dotnet test --solution CheatEngine.Client.slnx --configuration Release --no-build --no-restore
```

The ordinary test suite uses SDK-facing ports/fakes and does not replace the opt-in live Cheat
Engine qualification gate.

## AOB scan cost, classification, and release

`PatternScanner` resolves an optional module, runs one global Cheat Engine `AOBScan` through the SDK, and then copies
the result list. Module and range filters are applied while copying; `MaximumResults` bounds only the copy. Neither
reduces Cheat Engine's scan time or memory, and a cancellation token cannot interrupt a started scan: a cancellation
observed after the scan returns `Cancelled` with `CheatEngineHostEffect.Completed`. `ScanDetailed` measures the Cheat
Engine scan call and the Client copy separately (`PatternScanMetrics`), and
`tests/CheatEngine.Client.Benchmarks/PatternScannerMaterializationBenchmarks.cs` measures the copy cost alone over a fake
port; the Cheat Engine scan cost is a live-host measurement.

The SDK owner of the result list is handed to the Client wrapper through `OwnershipHandoff`, so a failure between
acquisition and publication releases the Cheat Engine list exactly once. The scanner then releases the list exactly once
on every path, inside the dispatched callback. With CheatEngine.SDK 1.0.0 the only "release not confirmed" signal is an
exception from the SDK owner; the scan then fails with `InvalidState` and `CheatEngineHostEffect.CleanupUnconfirmed`, and
copied addresses are discarded rather than reported as a success.

With CheatEngine.SDK 1.0.0 a missing result list is `IndeterminateHostResult` ("zero matches or a host failure"), never
`NotFound` or `OperationRejected`; classification uses SDK return values only, never Cheat Engine or Lua error text.

## SDK boundary and the Try contract

Every Client-internal CheatEngine.SDK call (ports, generated `ClientLuaGlobals` bindings, `TargetMemory`,
`EngineInspection`, `AobScanner`, Address List access, protected Lua execution) runs behind `SdkBoundary`: an SDK
exception becomes a classified `CheatEngineFailure` (by exception type, with the known `CheatEngineHostEffect`), so no
SDK exception type crosses a `Try*` method of the Patterns, Memory, Inspection, Tables, or Unsafe Lua domains. Client
lifecycle exceptions are never translated, and an SDK fault observed after the activation ended is reported as
`CheatEngineActivationExpiredException`. Consumer-supplied code (dispatcher callbacks, codecs, typed Lua operations) is
never wrapped: the dispatcher rethrows its exceptions unchanged. The per-family table lives in
`libs/CheatEngine.Client.Abstractions/README.md` ("Failure, exception and cancellation contract"). The Runtime and
Processes domains follow the same boundary: a fact probe that fails leaves the fact `Unknown` in the snapshot, and any
other SDK fault is returned as a classified failure.
