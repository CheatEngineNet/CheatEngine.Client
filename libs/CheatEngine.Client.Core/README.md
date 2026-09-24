# CheatEngine.Client.Core

## Context

`CheatEngine.Client.Core` is the SDK-facing implementation layer of `CheatEngine.Client`. It maps
the public contracts from `CheatEngine.Client.Abstractions` onto `CheatEngine.SDK` 2.x while a
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

Core is not a standalone package. It has no public API: `CheatEngine.Client.Extensions.DependencyInjection` and
`CheatEngine.Client.Hosting` use its internal types through `InternalsVisibleTo`, and it is published only as a
dependency of `CheatEngine.Client.Extensions.DependencyInjection`. The seven Client packages ship in lockstep, with one
version, and each depends on the Client packages it builds on at exactly that version (`[X.Y.Z]` in its nuspec): a Core
of another version than the packages that call its internals is never a supported combination. Do not reference Core
on its own; reference `CheatEngine.Client`, or the Client package you need, at the same version as every other Client
package. Core also grants its internals to the repository's tests and benchmarks. The Client assemblies are not
strong-named, so an `InternalsVisibleTo` grant names an assembly, not a signing key; internal members are not a
contract and change in any release.

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
Cheat Engine work, and a capability test keeps them that way on the supported SDK major
(`_CheatEngineClientSupportedSdkMajor` in `eng/CheatEngineSdk.props`). Their implementation gate
is `Missing`; their package gate is the same consumed-SDK evidence as every other capability.

- **Value scans**: the public session contract and state machine are present, but Core composes no
  operational value-scan adapter: it reports a capability failure rather than making an unverified
  `MemScan`/`FoundList` ownership assumption.
- **Allocations and assembly**: Core composes no operational allocation or Auto Assembler adapter,
  and no allocation owner is wired.

Core composes nothing for the domains that no CheatEngine.SDK primitive backs (timers, hotkeys, the debugger,
the speed hack, hashing, DBVM and remote execution): the Client has no contract for them, as the
`CheatEngine.Client.Abstractions` README states under "Not offered in 1.0".

Every capability is described once, in the internal `ClientCapabilityCatalog`: its implementation
gate, where its policy and host gates come from, and the live scenarios its qualification gate
requires. `RuntimeClient` composes the snapshot from that catalog, and a repository test keeps the
capability tables of the READMEs in step with it. The per-capability evidence (implementation,
package, host, qualification, policy and lifetime gates) is in the capability table of the
`CheatEngine.Client.Abstractions` README. The package gate
compares the CheatEngine.SDK identity embedded in this assembly at build time (version, source
commit and NuGet content hash, as `AssemblyMetadata`, taken from the locked and restored package,
and the supported major of `eng/CheatEngineSdk.props`) with the informational version of the
`CheatEngine.SDK.Engine` assembly actually loaded; it reads assembly attributes only. It is
`Satisfied` for a release of the supported major at or above the consumed version, by SemVer
precedence, which is the range the packages declare; its reason says whether the loaded assembly is
exactly the reviewed package, a distinction the qualification gate needs (a receipt covers only the
tuple it was produced with). A build that cannot embed that identity fails with
`CHEATENGINECLIENT9050`. UI/forms, structures, Mono/IL2CPP, and ABI hooks are outside this layer.

## Runtime facts, target selection and pointer width

Every runtime and target fact is a read-only CheatEngine.SDK 2.0.0 operation, called through
`SdkRuntimeObservationPort`: `RuntimeObservations.TryObserveRuntimeInfo` for the snapshot,
`RuntimeHostOperations` for the host facts, `RuntimeProcessOperations` (`ObserveCurrent`,
`ObserveTargetArchitecture`, `TryGetConfiguredPointerSize`) for the target and `TargetSelection`
(`ObserveCurrent`, `ValidateCurrent`) for its identity. A snapshot never loads a
driver, runs remote code, changes the target, loads a table or allocates target memory; the
architecture ratchet keeps the port to that exact read-only list (Q45). The SDK reads the selected
process identifier before and after the target facts and reads none of them when no target, or a
file opened as a process, is selected: with no target Cheat Engine reports the facts of an x64
target. The ISA is the SDK's own derivation from the x86 and ARM family facts with the 64-bit fact,
never a Client inference.

The SDK reports every outcome as a status, never through Lua error text, and
`RuntimeObservationMapping` maps each status value explicitly. When the aggregate snapshot cannot be
produced (a file opened as a process, a target change, or a global that raised or returned a
malformed value), the Client reads the host facts on their own, and each fact alone when that fails,
and observes the target through `TargetArchitectureObserver`. A target fact that raises or is
malformed narrows the observation to the PID, the bitness and the configured pointer size, which it
keeps only when two selected-PID reads agree; the ISA, the backend and the ABI then stay `Unknown`.
`IProcessClient` returns a status that establishes no target with its own kind: `TargetChanged`,
`TargetIdentityUnavailable` for a file opened as a process, `CapabilityUnavailable` for an absent
global, `LuaError`, `InvalidHostResult`.

The one call that changes Cheat Engine's selection is `RuntimeProcessOperations.SelectAndObserve`,
behind `SdkProcessSelectionPort`, and `ProcessClient.TryAttach` is its only caller (architecture
ratchet). A normal return of `openProcess` is not success by itself: the SDK reads the selected PID
again, and a refused attach keeps the SDK status as its kind (`OperationRejected` for an
unconfirmed selection, `TargetNotAttached`, `TargetIdentityUnavailable`, `TargetChanged`,
`CapabilityUnavailable`, `LuaError`, `InvalidHostResult`) with an `Unknown` host effect. The
selection is observed again after a refusal, so the epoch follows what Cheat Engine now selects.

Cheat Engine's selected target is ambient. For a local process the selection identity is the PID
and its incarnation, the creation time CheatEngine.SDK observed together with the local backend; a
known incarnation is checked with `TargetSelection.ValidateCurrent`. `ProcessClient` advances the
target-selection epoch, and releases target-bound leases, when the PID changes, when the same PID
denotes another incarnation, when a known backend, ISA or process width changes to another known
value, or when Cheat Engine reports no target or a file opened as a process; a fact that is
transiently unknown, including an incarnation that cannot be read, keeps the epoch and the last
known value. Like the SDK, the Client cannot see a selection that changed and changed back between
two observations (A-B-A). A CEServer target or a target whose backend is not established has no
incarnation and no local metadata: local process name and path come from the operating system and
describe local processes only.

Codecs use the target bitness (`targetIs64Bit`, the width `readPointer` follows), never the plugin's
own width. Every pointer read and write goes through the width-qualified `TargetMemory.TryReadPointer`
and `TryWritePointer` overloads with that observed bitness, so CheatEngine.SDK refuses a value that
does not fit a 32-bit target instead of truncating it. When the SDK reports that Cheat Engine's
configured pointer size differs from the bitness (`ConfiguredPointerSizeDiffersFromBitness`), or when
the bitness is unknown, the Client's own pointer-typed operations are refused before any memory
access (`NotStarted`: `OperationRejected` for a mismatch, `InvalidState` for a selected target
without a bitness); the configured size is exposed as a fact to custom codecs. A width that cannot
be observed keeps the kind of the status the SDK reported; only an observed "no process selected" is
`TargetNotAttached`. A pointer chain on a 32-bit target also refuses a base address, or an address it
computes by adding an offset, above 4 GiB, because the SDK qualifies only the pointer values it
reads.

Every `MemoryAccessFailure` the SDK reports reaches the caller through `MemoryAccessFailureMapping`,
value by value and never as text: see the table in the Abstractions README.

Cost: every `ReadPrimitive<Address>`/`WritePrimitive<Address>` call, every Address primitive batch,
every pointer chain and every built-in Address codec invocation observes the target facts once
inside its dispatched call, through one `ObserveTargetArchitecture` (about nine Lua global calls in
one Lua admission: the selected PID twice, `isConnectedToCEServer`, `targetIs64Bit`, `targetIsX86`,
`targetIsArm`, `targetIsAndroid`, `getABI`, `getPointerSize`). A batch pays this once for all its
items, so hot pointer-read loops should use batches or explicit 32/64-bit integer reads.

## Address List records and symbols

Every trusted table load that reaches Cheat Engine advances the table generation of the activation
inside the dispatched load, on Cheat Engine's main thread; a record identifier handed out before it
is refused with `InvalidState` (checked before dispatch and again inside the dispatched call) until
a new snapshot observes it. Each snapshot is judged by the generation read when it was copied, so a
snapshot copied before a concurrent load never hands out current identifiers. `TrySetActive` reads the record's state before and after one setter call and
reports applied, unchanged, refused by the host, pending or indeterminate; it never retries.
Symbol registration refuses a name that already resolves (`EngineInspection.ResolveAddress`), then
registers through CheatEngine.SDK's ownership coordinator (`SymbolRegistry.TryRegisterOwned`) and
registers the lease with the activation in the same main-thread callback. The lease release
delegates to the SDK lease, which unregisters the name only when it still resolves to the leased
address and no newer coordinator registration superseded it (a replaced name is left in place);
both checks are best effort, not atomic. Each release attempt is event 1701 with the operation
`Inspection.ReleaseSymbol`.

## Diagnostics events

Core has no logging dependency. It reports bounded events to an internal sink carried by the
activation lifetime; `CheatEngine.Client.Extensions.DependencyInjection` writes them through
`Microsoft.Extensions.Logging` with source-generated events and one category per domain, so the
standard `Logging:LogLevel` filters select them:

| Event id | Level | Event | Category |
|---|---|---|---|
| 1000 | Debug | `RuntimeSnapshotCaptured`: activation epoch, target architecture, process and configured pointer bytes, mismatch flag | `CheatEngine.Client.Runtime` |
| 1001 | Debug | `CapabilityRefused`: capability id, operation, gate, gate state; once per capability and operation per activation | `CheatEngine.Client.Runtime` |
| 1100 | Debug | `TargetSelectionAdvanced`: activation and selection epochs, operation, reason (`PidChanged`, `ProcessReused`, `BackendChanged`, `ArchitectureChanged`, `WidthChanged`, `TargetDetached`) | `CheatEngine.Client.Processes` |
| 1200 | Information | `PointerWidthMismatchRefused`: operation, process and configured pointer bytes | `CheatEngine.Client.Memory` |
| 1201 | Debug | `MemoryBatchCompleted`: operation, requested and completed counts, effect state | `CheatEngine.Client.Memory` |
| 1300 | Debug | `TableGenerationAdvanced`: activation epoch, table generation | `CheatEngine.Client.Tables` |
| 1301 | Debug | `StaleRecordIdentifierRefused`: operation, table generation | `CheatEngine.Client.Tables` |
| 1302 | Debug | `RecordActivationNotApplied`: operation, requested state, status (`RefusedByHost`, `Pending`, `Indeterminate`) | `CheatEngine.Client.Tables` |
| 1400 | Debug | `SymbolRegistrationRejected`: operation, reason (`AlreadyResolves`, `LookupFailed`) | `CheatEngine.Client.Inspection` |
| 1500 | Debug | `PatternScanCompleted`: scope, host result and materialized counts, truncation, scan and copy milliseconds | `CheatEngine.Client.Scanning` |
| 1600 | Debug | `LuaOperationCompleted`: operation, failure kind or `None`, milliseconds, script length (unsafe Lua only) | `CheatEngine.Client.Lua` |
| 1700 | Warning | `CoreResourceCleanupFailed`: resource and exception type names | `CheatEngine.Client.Lifetime` |
| 1701 | Debug | `LeaseReleased`: release operation, `LeaseReleaseKind`, host effect | `CheatEngine.Client.Lifetime` |

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

`PatternScanner` runs a request without a module or range as one global Cheat Engine `AOBScan`. A module and/or range
request resolves the module, builds the SDK's `AobScanBounds` (the module intersected with the range, whose inclusive end
becomes `End + pattern length`, checked and saturated) and, when the port's `TargetSelection.ObserveCurrent` observation
qualifies the target, runs the stable `AobScanner.TryScanWithinBounds` overload with a destination of
`min(MaximumResults + 1, ScanResourceLimits.MaximumPatternMatches)` addresses and the caller's token. An unqualified
target, or a session the SDK could not create or attach to one target, falls back to the global scan with the module and
range applied while copying; the fallback never reports a verified target identity. Both routes copy through one scope
predicate (`PatternScanner.IsInsideRequest`): a module keeps a match only when all of its pattern bytes lie inside it, and
a range keeps a match whose start lies in `[Start, End]`. On the bounded route the predicate turns Cheat Engine's
observed stop-bound behavior into a Client guarantee, and bounds that cannot hold one whole match are refused before any
scan. Both routes copy at most `ScanResourceLimits.MaximumPatternMatches - 1` addresses, and both send the empty
protection text for the default filter. `MaximumResults` bounds only the copy, and a cancellation token cannot interrupt a started
scan: a cancellation observed after the scan returns `Cancelled` with `CheatEngineHostEffect.Completed`. `ScanDetailed` measures the Cheat
Engine scan call and the Client copy separately (`PatternScanMetrics`) and reports the host outcome, the route reason and
whether the target identity was verified; `tests/CheatEngine.Client.Benchmarks/AobRouteComparisonBenchmarks.cs`
compares the Client cost of the two routes over a fake port, and
`tests/CheatEngine.Client.Benchmarks/PatternScannerMaterializationBenchmarks.cs` measures the copy cost alone over a fake
port; the Cheat Engine scan cost is a live-host measurement.

The SDK owner of the result list is handed to the Client wrapper through `OwnershipHandoff`, so a failure between
acquisition and publication releases the Cheat Engine list exactly once; when that release is not confirmed, the typed
`OwnershipHandoffException` carries its kind and the scan fails with `CleanupUnconfirmed`. The scanner then releases the list exactly once
on every path, inside the dispatched callback, through the SDK owner's never-throwing `ReleaseWithOutcome`, mapped with
`SdkReleaseOutcomes`. Any status other than `Released` is an unconfirmed release: a scan that had succeeded fails with
`InvalidState` and `CheatEngineHostEffect.CleanupUnconfirmed`, and copied addresses are discarded rather than reported as
a success; a scan that had already failed keeps its kind and gains the `CleanupUnconfirmed` effect.

The AOB port calls `AobScanner.TryScanOutcome` with its target context, and `AobScanMapping` classifies every outcome:
`NoResult` is `IndeterminateHostResult` ("CE AOBScan returned nil: on CE 7.7 zero matches and host failures share this
shape"), never `NotFound` or `OperationRejected`; a target that changed during the call discards the addresses
(`TargetChanged` or `TargetIdentityUnavailable`). Classification uses SDK outcome values only, never Cheat Engine or Lua
error text.

## Leases and release outcomes

Every Client lease derives from the internal `HostResourceLease`, which implements the public
`ICheatEngineLease` contract once: the release runs on Cheat Engine's main thread through the
activation dispatcher, attempts are serialized and idempotent, `Dispose` never throws, and each
attempt is logged with its operation name, kind and effect only (event 1701). A lease registers
with the activation registry and, when it is bound to the selected target, with the
target-selection lifetime too. A complete outcome unregisters it. A retryable outcome
(`Unknown`, `CleanupUnavailable`) keeps it registered, so the activation drain retries it once
more before the plugin is disabled; an outcome that requires manual recovery (a refusal, a
partial or unconfirmed cleanup) keeps it registered as well, and the drain turns every
incomplete outcome into one `CheatEngineOperationException` of the aggregated deactivation
failure (Q43), with the host effect `CleanupUnconfirmed`. A target change disposes a
target-bound lease without throwing to the code that selected the new target.

`SdkReleaseOutcomes` maps the CheatEngine.SDK 2.0.0 release statuses totally
(`TargetReleaseStatus`, `SymbolRegistrationReleaseKind`, `LuaRegistrationReleaseKind`; an
unknown value is `Unknown` with an unknown effect) and combines the parts of one lease by
keeping the outcome that leaves the most to do. The existing symbol and Lua module leases move
onto this base with their domains.

## SDK boundary and the Try contract

Every Client-internal CheatEngine.SDK call (ports, generated `ClientLuaGlobals` bindings, `TargetMemory`,
`EngineInspection`, `AobScanner`, Address List access, protected Lua execution) runs behind `SdkBoundary`: an SDK
exception becomes a classified `CheatEngineFailure` (by exception type and the SDK's own failure category, never by
message text, with the known `CheatEngineHostEffect`), so no SDK exception type crosses a `Try*` method of the
Patterns, Memory, Inspection, Tables, or Unsafe Lua domains. Client lifecycle exceptions are never translated, and an SDK
fault observed after the activation ended is reported as `CheatEngineActivationExpiredException`. A plain
`InvalidOperationException` observed after CheatEngine.SDK detected an external Lua state reset is `RuntimeChanged`.
Lua work that Core runs itself asks for its admission through `LuaAdmission`
(`LuaRuntime.TryAcquireOperationWithOutcome`): a refusal is classified from the SDK's admission status, with
`NotStarted` and never as a rejection: `ActivationExpired` (plugin detached or transitioning), `RuntimeChanged`
(external Lua state reset) or `InvalidState` (called off the main thread, or an unrecognized status). Consumer-supplied code (dispatcher callbacks, codecs, typed Lua operations) is
never wrapped: the dispatcher rethrows its exceptions unchanged. The per-family table lives in
`libs/CheatEngine.Client.Abstractions/README.md` ("Failure, exception and cancellation contract"). The Runtime and
Processes domains follow the same boundary: a fact probe that fails leaves the fact `Unknown` in the snapshot, and any
other SDK fault is returned as a classified failure.
