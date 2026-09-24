# CheatEngine.Client.Abstractions

## Context

`CheatEngine.Client.Abstractions` is the stable, handle-free vocabulary of the in-process
`CheatEngine.Client` plugin API. It targets C# 14 and .NET 10 on top of
[CheatEngine.SDK](https://www.nuget.org/packages/CheatEngine.SDK), and is valid only for the
current Cheat Engine plugin activation.

It defines the public contracts used by the product facade, implementations, fluent helpers,
hosting, extensions, and application code. It is intentionally synchronous: an attached Cheat
Engine Lua runtime and its main-thread work must not be retained across an `await` boundary.

## Why This Project Exists

The SDK correctly exposes the Cheat Engine runtime, Lua bridge, ownership wrappers, and ABI-level
concepts. Application code should not need to carry those handles through its own architecture.
This project establishes a smaller product boundary with explicit failure, ownership, lifetime,
and materialization rules.

It is the bottom of the Client project graph: it has no Client project reference. Its only package
dependency is the `CheatEngine.SDK` compile/runtime surface required for stable value and query
types such as `Address`, target identifiers, inspection metadata, scan requests, and address-list
identifiers. SDK build assets, generators, native assets, and analyzers deliberately do not flow
through this package.

```text
CheatEngine.Client.Abstractions
          ↑              ↑
       Fluent          Core
                         ↑
              DependencyInjection / Hosting
```

## How It Improves CheatEngine.Client

- Makes domain behavior testable against interfaces instead of Cheat Engine statics.
- Keeps expected failures explicit through `Try...(..., out CheatEngineFailure)`; only the throwing
  convenience methods throw, through `CheatEngineFailure.Throw(CancellationToken)`, and a cancellation
  surfaces as an `OperationCanceledException`.
- Keeps CE-owned objects out of the public surface: no `LuaState`, `LuaRef`, `CEObject`,
  `Owned<T>`, or raw native handle escapes this package.
- Requires bounded copies for scans, table snapshots, strings, byte reads, finite pointer chains,
  homogeneous primitive batches, and inspection collections, avoiding unbounded materialization and
  leaked SDK ownership.
- Makes lifecycle constraints visible: `ICheatEngineClient.Epoch`, `Stopping`, leases, and scan
  sessions are activation-scoped; stale resources report `CheatEngineActivationExpiredException`.

## Public Surface

Package and assembly names are not consumer namespaces. Public code belongs to functional
namespaces only:

| Namespace                    | Responsibility                                                              |
|------------------------------|-----------------------------------------------------------------------------|
| `CheatEngine.Client`         | `ICheatEngineClient`, the activation-scoped facade, and `ICheatEngineLease` |
| `.Dispatching` / `.Runtime`  | main-thread dispatch and runtime/capability observations                    |
| `.Processes` / `.Inspection` | target selection, copied process/module/region/symbol data                  |
| `.Memory`                    | bounded primitive, byte, string, codec, and pointer-chain operations        |
| `.Scanning`                  | AOB contracts and the value-scan session contract                           |
| `.Tables`                    | copied Address List records and explicitly trusted table I/O requests       |
| `.Lua` / `.Modules`          | typed protected Lua operations, explicit modules, and leases                |
| `.Allocations` / `.Assembly` | selection-bound allocation and reversible patch leases                      |
| `.Results`                   | classified expected failures, exceptions, and lease release outcomes        |

No public consumer should use `CheatEngine.Client.Abstractions` as a namespace.

The documentation of every public interface says whether it is **Call-only** (the Client implements it and applications
call it; a 1.x minor release can add members, so implement it only in a test double) or **Implementable** (applications
implement it and the Client calls it: `ILuaModule`, `IDescribedLuaModule`, `IOwnershipAwareLuaModule`,
`ILuaOperation<TResult>`, `ILuaResultMapper<TSource, TResult>`, `IMemoryCodec<T>` and `ICheatEngineClientModule`,
whose members are frozen for 1.x). Public enums follow one charter for 1.0: `int` backing, explicit values, and
`Unknown = 0` on an outcome enum (`...Kind`, `...Status`, `...State`, `...Effect`, `...Scope`); a new value can appear
in a minor release.

### Capability Boundary

The contracts describe runtime, process, memory, inspection, AOB scanning, tables, protected Lua,
and explicitly disposable value-scan sessions. A contract is not an availability promise: callers
must inspect `ICheatEngineRuntime` capability observations or handle `CapabilityUnavailable`.

Each `ClientCapabilityAvailability` also exposes immutable `Evidence`: implementation, consumed package artifact,
host observation, live qualification, policy, and activation lifetime are distinct gates. `Available` requires all six;
missing, faulted, and malformed host observations remain distinguishable instead of being collapsed into a generic
unavailable result.

`Evidence.EffectiveReasonCode` is the stable, typed identity of the gate supplying `Evidence.EffectiveReason`; use it
with that gate's public state instead of parsing the human-readable reason text or duplicating the Client's
deterministic priority. The reason text remains available for display and diagnostics.

The table below is what this Client build reports through `ICheatEngineRuntime.TryGetClientCapability`. No Client
capability is host-qualified yet: the qualification gate stays `Unknown` until a Client qualification receipt exists,
so no capability reports `Available`. The package gate of every capability is evidence, not a version name: it
compares the informational version of the loaded `CheatEngine.SDK.Engine` with the CheatEngine.SDK 2.0.0 package this
build consumed, and follows the range the packages declare. It is `Satisfied` for a release of the same major at or
above that version, by SemVer precedence (a prerelease of the consumed version is below it), and its reason says
whether the loaded assembly is exactly the reviewed package or another 2.x release; it is `Missing` for another major or
an older version, and `Unknown` when the loaded assembly declares no semantic informational version or the build embeds
no identity. A contract-only capability is refused by its implementation gate, whatever its package gate reports.
Probes are read-only: taking a snapshot never loads a driver, runs remote code, changes the target or allocates target
memory. Each capability's qualification gate requires receipts for the live scenarios named in its row.

<!-- capability-table:start -->
| Capability id | Implementation | Package | Host | Qualification | Status reported at runtime |
|---|---|---|---|---|---|
| `Client.ProcessSelection` | Operational adapter | Loaded CheatEngine.SDK 2.x at or above the consumed 2.0.0 | Read-only opened-process probe | Unknown until Client receipts for Q30.a, Q31 and Q32 exist | `Unknown`; `Unavailable` when the package or host gate is `Missing` |
| `Client.TypedMemory` | Operational adapter | Loaded CheatEngine.SDK 2.x at or above the consumed 2.0.0 | Not probed by the snapshot (`Unknown`) | Unknown until Client receipts for Q20, Q21 and Q33 exist | `Unknown`; `Unavailable` when the package gate is `Missing` |
| `Client.PatternScanning` | Operational adapter | Loaded CheatEngine.SDK 2.x at or above the consumed 2.0.0 | Not probed by the snapshot (`Unknown`) | Unknown until Client receipts for Q27, Q28 and Q29 exist | `Unknown`; `Unavailable` when the package gate is `Missing` |
| `Client.ValueScanning` | Contract-only (Unavailable) | Loaded CheatEngine.SDK 2.x at or above the consumed 2.0.0 | Not probed by the snapshot (`Unknown`) | Unknown until Client receipts for Q25 and Q26 exist | `Unavailable` |
| `Client.Inspection` | Operational adapter | Loaded CheatEngine.SDK 2.x at or above the consumed 2.0.0 | Not probed by the snapshot (`Unknown`) | Unknown until Client receipts for Q16.b and Q28 exist | `Unknown`; `Unavailable` when the package gate is `Missing` |
| `Client.Tables` | Operational adapter | Loaded CheatEngine.SDK 2.x at or above the consumed 2.0.0 | Not probed by the snapshot (`Unknown`) | Unknown until Client receipts for Q34 exist | `Unknown`; `Unavailable` when the package gate is `Missing` |
| `Client.ProtectedLua` | Operational adapter | Loaded CheatEngine.SDK 2.x at or above the consumed 2.0.0 | Not probed by the snapshot (`Unknown`) | Unknown until Client receipts for Q05, Q16 and Q19 exist | `Unknown`; `Unavailable` when the package gate is `Missing` |
| `Client.UnsafeLuaExecution` | Operational, policy opt-in | Loaded CheatEngine.SDK 2.x at or above the consumed 2.0.0 | Not probed by the snapshot (`Unknown`) | Stays `Unknown`: no scenario covers arbitrary Lua | `Unavailable` without `EnableUnsafeLuaExecution()`; otherwise `Unknown` |
| `Client.Allocations` | Contract-only (Unavailable) | Loaded CheatEngine.SDK 2.x at or above the consumed 2.0.0 | Not probed by the snapshot (`Unknown`) | Unknown until Client receipts for Q30.a exist | `Unavailable` |
| `Client.Assembly` | Contract-only (Unavailable) | Loaded CheatEngine.SDK 2.x at or above the consumed 2.0.0 | Not probed by the snapshot (`Unknown`) | Unknown until Client receipts for Q32 exist | `Unavailable` |
<!-- capability-table:end -->

Every row also carries the lifetime gate (`Missing` once the activation has ended). A contract-only capability refuses
each operation with `CapabilityUnavailable` and `CheatEngineHostEffect.NotStarted`; no Cheat Engine work is dispatched.

The value-scan contract and state model are published, but Core does **not** create a live `MemScan`/`FoundList`
session: this build composes no operational value-scan adapter, so the implementation gate of `Client.ValueScanning` is
`Missing` and every session request is refused. An adapter changes that gate only once it passes the transfer,
lifecycle and target-change tests and the Cheat Engine 7.7 live gate. Do not treat `IValueScanner` as available until
its capability reports it.

`IUnsafeLuaClient` is intentionally separate from `ILuaClient` and is not registered by default.
It is for explicitly trusted source only and still never exposes a raw Lua state.

### Not offered in 1.0

These Cheat Engine features have no public Client contract, not even a gated placeholder:

- timers and hotkeys;
- the debugger and breakpoints;
- the speed hack;
- target-memory and file hashing;
- DBVM;
- remote execution and DLL injection;
- pausing, resuming or creating a process, and attaching to the foreground process;
- assembly comments;
- detaching from a process.

No CheatEngine.SDK primitive backs these yet; they may arrive in a 1.x minor release once the SDK provides an owner.

### AOB scan semantics and limits

`IPatternScanner` runs one global Cheat Engine `AOBScan` per request. `AobScanRequest.Module` and
`AobScanRequest.Range` are managed post-filters: Core resolves the module first, then Cheat Engine scans the whole
target, and Core copies only the addresses inside the module or range. They do not reduce Cheat Engine's scan time or
memory. `AobScanRequest.MaximumResults` bounds only how many filtered addresses Core copies; it never stops Cheat Engine
early. The copied order is Cheat Engine's result-list order, which Cheat Engine does not specify: the first copied
address is not guaranteed to be the lowest address or the first logical region.

Four scan limits are distinct and must not be confused:

| Limit                   | On the current route                                                                   |
|-------------------------|----------------------------------------------------------------------------------------|
| Cheat Engine work limit | None: Cheat Engine always runs a global scan (`PatternScanScope.GlobalHostScanWithManagedFilter`) |
| Available results       | `PatternScanMetrics.HostMatchCount`, the size of Cheat Engine's result list             |
| Materialization limit   | `AobScanRequest.MaximumResults`, which bounds `PatternScanMetrics.MaterializedCount`    |
| Call deadline           | None: cancellation is observed only between Client-managed steps                        |

`IPatternScanOutcomeClient.ScanDetailed` is a companion contract (the `IMemoryBatchClient` pattern) that returns the
same classification as `TryScan` plus `PatternScanMetrics`: host, examined, filtered-out, and copied counts, the scan
scope, and the Cheat Engine scan time (`HostScanElapsed`) separately from the Client copy time
(`MaterializationElapsed`). Counts and durations never contain addresses and are safe to log.

A scan that finds nothing returns `CheatEngineFailureKind.IndeterminateHostResult`: Cheat Engine 7.7 returns no result
list for zero matches, and Core's AOB route calls the boolean `AobScanner.TryScan` of CheatEngine.SDK 2.0.0, which
reports that exactly as it reports an unresolved global, a protected Lua failure, or a non-object result. It is never
reported as `NotFound` or as a host rejection; an empty result list that Cheat Engine does return remains a normal,
successful no-match. On this route the indeterminate category is the accurate one.

### Target selection, runtime facts and pointer width

Cheat Engine's selected target is ambient: `IProcessClient.Attach` changes Cheat Engine's global selection, and a
snapshot or a session that holds a process identifier does not stop the user, another plugin or a script from selecting
another process. `ProcessSnapshot.SelectionEpoch` and the PID-bracketed observation reduce that risk for Client-owned
leases; they are not transactions. `ProcessSnapshot.Name` and `ExecutablePath` are optional local metadata from the
operating system: they describe a local process only, never a CEServer target or a file opened as a process, and they do
not prove liveness.

Every observation reads the opened process identifier first, because Cheat Engine reports the same family, width and
pointer size as an x64 target when no target is opened. The runtime snapshot keeps separate facts:

- **Target architecture (ISA)**: derived from Cheat Engine's x86 and ARM family facts together with its 64-bit fact,
  never from the 64-bit fact alone; contradictory or missing facts give `CheatEngineArchitecture.Unknown`.
- **Process width** (`TargetPointerSize`, `ProcessSnapshot.TargetPointerSize`): the width of the target process as
  observed; it can be known while the ISA is unknown.
- **Configured pointer size** (`CheatEngineRuntimePlatformInfo.ConfiguredPointerSize`, runtime capability
  `Runtime.ConfiguredPointerSize`): the value Cheat Engine reports through `getPointerSize()`. It is per-attachment state,
  independent of the process width, reset when a process is opened, and can hold any integer.
  `ConfiguredPointerSizeDiffersFromTargetPointerSize` reports a mismatch as a fact.

Cheat Engine's pointer read follows the process width, not the configured size. The Client therefore keeps the process
width for every pointer-typed operation (`Address` primitives, primitive batches, pointer chains, the built-in `Address`
codec) and, when the configured size is known and differs, refuses the operation before any memory access with
`OperationRejected` and `CheatEngineHostEffect.NotStarted`. A configured size that could not be observed is no evidence
of a mismatch. A pointer chain on a 32-bit target refuses an intermediate address above 4 GiB instead of truncating it.
Custom codecs receive the facts through `IMemoryPointerWidthContext` (`ProcessPointerSize`, `ConfiguredPointerSize`,
`ConfiguredPointerSizeDiffersFromProcessWidth`); what the configured size affects besides the reported value is not
established.

### Address List records and symbols

A `MemoryRecordId` is valid in the Address List state in which this activation observed it. A trusted table load that
reached Cheat Engine (merge or replace, even a failed one) makes every identifier handed out before it stale, and every
identifier-taking operation refuses a stale identifier with `InvalidState` and `CheatEngineHostEffect.NotStarted` until a
new snapshot observes it again. The check runs before dispatch and again on Cheat Engine's main thread, where the load
advances the table generation, so concurrent callers cannot hand out or use an identifier of the earlier table state.
Loads made outside this activation are not detected.

`ITableClient.TrySetActive` reports what Cheat Engine did: already in the requested state (success, the setter is not
called), applied (success), refused by an activation callback, script or record type (`OperationRejected`, `Started`,
with the post-change snapshot), pending asynchronous activation (`IndeterminateHostResult`, `Started`) or indeterminate
(`InvalidHostResult`, `Unknown`). The setter is called at most once and never retried. Selecting a record is a
host-visible effect on Cheat Engine's user interface.

`IInspectionClient.TryRegisterSymbol` first resolves the name: a name that already resolves (a registered symbol, a
module or an expression that parses as an address) is refused with `OperationRejected` and `NotStarted`, and a failed
check registers nothing. Releasing the lease unregisters the name only when it still resolves to the leased address;
`IDetailedSymbolRegistrationLease.ReleaseDetailed` reports `Released`, `AlreadyReleased`, `Replaced` (a third party
replaced it, left in place), `ExternallyRemoved` or `CleanupUnavailable` (nothing confirmed; the lease stays active and
`Dispose` throws with `CleanupUnconfirmed`). The check and the unregistration are not atomic.

### Failure, exception and cancellation contract

`Try*` does not mean "never throws". Every family follows three rules, then the per-family details below:

- **Returned as `CheatEngineFailure`:** request refusals, policy refusals, budget refusals, pre-admission cancellation,
  Cheat Engine results that are false, absent, indeterminate, or malformed, and every CheatEngine.SDK exception raised by
  Client-internal SDK work (mapped by exception type and the SDK's own failure category, never by message text). No
  SDK exception type crosses a `Try*`. A Lua admission that CheatEngine.SDK refuses is `ActivationExpired`,
  `RuntimeChanged` or `InvalidState` with `NotStarted`, never `OperationRejected`.
- **Thrown:** `CheatEngineActivationExpiredException` when the activation has ended, `CheatEngineClientLifecycleException`
  when it is stopping, and `ArgumentException`/`ArgumentNullException`/`ArgumentOutOfRangeException` for invalid
  arguments (programming errors). An expired activation is never reported as `Cancelled` or `CapabilityUnavailable`.
- **Consumer code:** exceptions thrown by application-supplied code (dispatcher callbacks, `IMemoryCodec<T>` codecs,
  `ILuaOperation<T>` operations) are rethrown as the same instance, never converted into a failure.

Every throwing convenience form (the method without `Try`, and the Fluent `Execute` terminals) returns the value of
its `Try` form or throws that form's failure through `CheatEngineFailure.Throw(cancellationToken)`, passing the token it
received. The exception type depends only on `CheatEngineFailure.Kind`, and every exception keeps the complete failure,
including its `HostEffect`:

| `CheatEngineFailure.Kind` | Exception thrown | Base type |
|---|---|---|
| `Cancelled` | `CheatEngineOperationCanceledException`, whose `CancellationToken` is the token the operation observed | `OperationCanceledException` |
| `ActivationExpired` | `CheatEngineActivationExpiredException` | `CheatEngineClientException` |
| `InvalidState` | `CheatEngineClientLifecycleException` | `CheatEngineClientException` |
| Any other kind, including a value this version does not define | `CheatEngineOperationException` | `CheatEngineClientException` |
| None: the `default` failure, which no operation returns | `InvalidOperationException` (a programming error) | `Exception` |

A cancelled throwing call is therefore handled with `catch (OperationCanceledException)`, like any other .NET
cancellation; read `CheatEngineOperationCanceledException.Failure.HostEffect` to learn whether Cheat Engine work had
started. The `Try` form of the same call returns the same failure instead of throwing it.

A `Try` method leaves its `failure` output `default` only when it returns `true`. The `default` failure is safe to
read: `IsDefault` is `true`, `Operation` and `Message` are empty strings (never `null`), `Kind` and `HostEffect` are
`Unknown`, and `Exception` is `null`. `CheatEngineFailure` has a single constructor,
`(kind, operation, message, exception = null, hostEffect = Unknown)`, which rejects an empty operation or message.

`CheatEngineFailure.HostEffect` states how far the Cheat Engine primitive got: `NotStarted`, `Started` (effects may
persist), `Completed` (the primitive returned; the failure happened while Core copied or validated), `NotApplied` (the
primitive returned its documented negative result, so nothing was applied), `CleanupUnconfirmed` (a resource or change
may remain), or the conservative `Unknown`. A `CancellationToken` never interrupts a Cheat Engine
call that has started and never removes a callback, primitive, or effect that has begun: it is observed only before
dispatch and between Client-managed steps.

| Family | Cancellation stops preventing the host effect at | `HostEffect` values produced | Partial effects |
|---|---|---|---|
| Dispatcher (`ICheatEngineDispatcher`) | Dispatch admission: a `Cancelled` result proves the callback did not run | `NotStarted` (cancelled), `Unknown` (infrastructure failure) | Whatever the callback did; callback exceptions are rethrown unchanged |
| Patterns / AOB (`IPatternScanner`, `IPatternScanOutcomeClient`, Fluent `Aob`) | The start of the global `AOBScan`; later cancellation discards the copy | `NotStarted` (validation, module lookup, cancellation before the scan), `Completed` (cancellation or invalid data after the scan, `IndeterminateHostResult` when no result list is returned), `CleanupUnconfirmed` (result-list release not confirmed), `Unknown` (SDK fault during the scan call) | None published: a failed scan never returns a prefix |
| Memory primitives, codecs, bytes, strings, pointer chains (`IMemoryClient`) | Dispatch admission; one call is one Cheat Engine operation | `NotStarted` (budget, unsupported type, configured/process pointer-width mismatch, no process width), `Started` (a pointer chain stopped at an intermediate address above a 32-bit process width), `Unknown` (SDK fault, host refusal) | A codec may perform several reads or writes; a failed write codec can leave earlier writes in place |
| Memory batches (`IMemoryBatchClient`) | Dispatch admission: a `Cancelled` dispatch reports `MemoryBatchWriteEffectState.NotStarted` | `NotStarted` (admission, pre-dispatch cancellation, unsupported type), `Started` (a completed prefix persists), `Unknown` (SDK fault or other dispatch failure) | `EffectState` is authoritative: `Partial` with `CompletedCount`/`FailedIndex`, never rolled back |
| Inspection and symbol leases (`IInspectionClient`) | Dispatch admission | `NotStarted` (name already reserved by this activation, name already resolves, failed collision check), `CleanupUnconfirmed` (lease release not confirmed), `Unknown` (SDK fault) | A faulted `registerSymbol` is not claimed and not retried; a replaced name is left in place |
| Tables (`ITableClient`) | Dispatch admission; `Find` filters a copied snapshot | `NotStarted` (policy, invalid relationship, stale record identifier, activation of a record that was not found), `Started` (activation refused by the host or pending), `Completed` (`Find` cancelled after the snapshot, failed `Create` whose rollback was confirmed), `CleanupUnconfirmed` (record rollback not confirmed), `Unknown` (SDK fault, `loadTable` fault, indeterminate activation) | A failed `Create` destroys the partial record once and never retries; a refused activation can leave partial script effects; `loadTable` can execute table Lua |
| Lua typed operations and modules (`ILuaClient`) | Dispatch admission | `NotStarted` (cancellation), otherwise the operation's own failure | Owned by the operation; operation exceptions are rethrown unchanged |
| Unsafe Lua (`IUnsafeLuaClient`) | Dispatch admission | `NotStarted` (policy, or a Lua admission refused by CheatEngine.SDK), `Unknown` (SDK fault; the script may have run partially) | The script may have run partially before a Lua error |
| Runtime and Processes (`ICheatEngineRuntime`, `IProcessClient`) | Dispatch admission | `Completed` (the selected target changed during the observation: `IndeterminateHostResult`), `Unknown` (SDK fault, no selected target) | A fact probe that fails leaves that fact `Unknown` in the snapshot or capability evidence instead of failing the call; `Attach` changes Cheat Engine's global selection |
| Capability-gated domains (allocations, assembly) | Not applicable: no Cheat Engine work is dispatched | `NotStarted` (`CapabilityUnavailable` or `Cancelled`) | None |
| Value scans (`IValueScanner`) | Not applicable: no Cheat Engine work is dispatched | Not yet reported (`Unknown`) | None; the refusal is the same `CapabilityUnavailable` or `Cancelled`, and reporting `NotStarted` here is scheduled with the other value-scan changes |

### Failure kinds and host effects

`CheatEngineFailureKind` says why an operation failed and `CheatEngineHostEffect` says how far the Cheat Engine primitive
got. Both are `int` enums whose values never change meaning; new values can be added, so handle an unrecognized value
like `Unknown`.

| `CheatEngineFailureKind` | Value | Meaning |
|---|---|---|
| `Unknown` | 0 | The failure could not be classified more precisely |
| `Cancelled` | 1 | The caller's token was observed; `HostEffect` tells whether Cheat Engine work had started |
| `CapabilityUnavailable` | 2 | A required Cheat Engine capability or Lua global is unavailable, or the activation did not enable it |
| `OperationRejected` | 3 | Cheat Engine or the Client rejected the request |
| `NotFound` | 4 | Absence of the requested resource was established |
| `AmbiguousMatch` | 5 | One result was expected and several were observed |
| `ResultLimitExceeded` | 6 | The host result exceeded the caller's materialization limit |
| `LuaError` | 7 | A protected Lua call failed |
| `BindingError` | 8 | A CheatEngine.SDK binding could not uphold its documented contract |
| `InvalidHostResult` | 9 | Cheat Engine returned a value outside the documented result shape |
| `Unsupported` | 10 | The feature is intentionally not supported by this Client version |
| `TargetNotAttached` | 11 | No target process is attached |
| `MemoryReadFailed` | 12 | A target-memory read failed |
| `MemoryWriteFailed` | 13 | A target-memory write failed |
| `ActivationExpired` | 14 | The Client activation that owns the call or resource has ended |
| `InvalidState` | 15 | The operation is not valid in the current lifecycle or session state |
| `IndeterminateHostResult` | 16 | Several documented causes (for example no match and a host failure) are indistinguishable; never treat it as absence |
| `TargetChanged` | 17 | The target the call or resource was bound to is no longer Cheat Engine's selected target: another process, or another incarnation of the same process identifier |
| `TargetIdentityUnavailable` | 18 | The identity of Cheat Engine's current target could not be established, so the call was refused instead of running against an unverified target |
| `RuntimeChanged` | 19 | Cheat Engine's Lua runtime was replaced outside the plugin's control, or the resource belongs to an earlier Lua attachment; disable and re-enable the plugin to recover |

When CheatEngine.SDK reports the effect state of an effectful operation, Core maps it value by value; the other host
effects are observed by the Client itself.

| `CheatEngineHostEffect` | Value | Meaning | CheatEngine.SDK `EngineEffectState` mapped to it |
|---|---|---|---|
| `Unknown` | 0 | Any effect is possible | `Unknown`, and any value this Client version does not know |
| `NotStarted` | 1 | The primitive was not invoked | `NotStarted` |
| `Started` | 2 | The primitive was invoked; neither its completion nor a rollback was established | None: observed by the Client |
| `Completed` | 3 | The primitive ran to completion; the failure happened afterwards inside the Client | `Applied` |
| `CleanupUnconfirmed` | 4 | A resource or change may remain because its release or rollback was not confirmed | None: observed by the Client |
| `NotApplied` | 5 | The primitive returned its documented negative result: nothing was applied and nothing needs cleanup | `NotApplied` |

### Leases and release outcomes

Every Client lease implements `ICheatEngineLease` (`IDisposable`): `Release()` releases the resource on Cheat Engine's
main thread and returns a `LeaseReleaseOutcome`; `Dispose()` performs the same release, **never throws**, and discards
the outcome; `LastReleaseOutcome` keeps the outcome of the attempt that ended the lease, and `IsReleased` says that no
later attempt will be made. A repeated release returns `AlreadyReleased` without a Cheat Engine call. The outcome's
`Kind` says what happened and its `HostEffect` how far the release call got; exactly one of three flags is `true`:

| `LeaseReleaseKind` | Value | Flag | Meaning |
|---|---|---|---|
| `Unknown` | 0 | `IsRetryable` | No outcome could be established; the lease stays active |
| `Released` | 1 | `IsComplete` | Released and confirmed |
| `AlreadyReleased` | 2 | `IsComplete` | An earlier attempt ended the lease; nothing was done |
| `PartiallyReleased` | 3 | `RequiresManualRecovery` | Part released, part failed; the failed part may remain |
| `Replaced` | 4 | `IsComplete` | A third party replaced the resource; it was left in place |
| `Superseded` | 5 | `IsComplete` | A newer Client registration replaced the lease |
| `ExternallyRemoved` | 6 | `IsComplete` | The resource was already gone |
| `RefusedNoTarget` | 7 | `RequiresManualRecovery` | Refused before any call: no target is selected |
| `RefusedTargetChanged` | 8 | `RequiresManualRecovery` | Refused before any call: another process or process incarnation is selected |
| `RefusedTargetIdentityUnavailable` | 9 | `RequiresManualRecovery` | Refused before any call: the target identity could not be established |
| `RefusedRuntimeChanged` | 10 | `RequiresManualRecovery` | Refused before any call: the Lua runtime that created the resource is gone |
| `CleanupUnconfirmed` | 11 | `RequiresManualRecovery` | A release call began without a confirmed result; it is never retried |
| `CleanupUnavailable` | 12 | `IsRetryable` | No release call could begin; the lease stays active |

Only `Unknown` and `CleanupUnavailable` are retryable, as in CheatEngine.SDK: a release call that began is never
retried. A retryable lease is retried by a later `Release()` and, at the latest, by the activation cleanup before the
plugin is disabled. A lease that is still incomplete then (a retry that failed again, a refusal, an unconfirmed or
partial cleanup) is reported in the aggregated deactivation failure as a `CheatEngineOperationException` whose failure
has the host effect `CleanupUnconfirmed`; it is never thrown to the code that released or disposed the lease. A
target-bound lease is also released when Cheat Engine selects another process. `LeaseReleaseOutcome.ToString()`
returns only the kind and the effect.

### Diagnostics and redaction

`CheatEngineFailure.Kind`, `Operation`, and `HostEffect`, together with counts and durations such as
`PatternScanMetrics`, are safe to log. `CheatEngineFailure.Message` and `CheatEngineFailure.Exception`, addresses,
values, symbol expressions, module names, file paths, and Lua source or error text are **user data**: log them only on an
explicit opt-in chosen by the application. `CheatEngineFailure.ToString()` returns only
`"{Kind} in {Operation} (host effect: {HostEffect})"`, so a structured logger that formats the failure object emits no
user data by default. Client libraries never log user data themselves: Hosting events carry epochs, stage names,
counts, and exception type names only, and a test rejects any Client `LoggerMessage` event whose parameters could carry
an address, expression, path, script, message, exception, or failure object. The Core diagnostic events (runtime
snapshots, capability refusals, target-selection changes, pointer-width refusals, batch counts, table generations,
activation and symbol outcomes, scan metrics, Lua durations, cleanup failures) follow the same rule; the
`CheatEngine.Client.Core` README lists them.

### Memory limits and batch effects

`MemoryResourceLimits` is copied once per activation. A byte, string, or batch request over a budget fails before dispatch
with `OperationRejected` and `HostEffect.NotStarted`. A codec access over a budget fails that codec call before the
access reaches Cheat Engine. The Client uses four terms for these limits:

- **Maximum block size**: `MaximumReadBytes`, `MaximumWriteBytes`, and `MaximumStringBytes` bound the contiguous block that
  one byte, codec, or string operation may copy. A custom codec's context reads and writes are charged cumulatively
  against the same budgets during one codec call.
- **Request count per batch**: `MaximumBatchOperationCount` can tighten, but never raise, the hard
  `MemoryBatchLimits.MaximumOperations` (1024). `MaximumBatchPayloadBytes` also bounds the count multiplied by the
  element size.
- **Maximum scratch allocation**: the largest managed buffer the Client allocates for one operation is the byte array of a
  byte read (at most `MaximumReadBytes`) or the value array of a batch read (at most `MaximumBatchPayloadBytes`). These
  operations allocate nothing in the target process.
- **Partial-effect state**: a batch write runs in order and is never rolled back.
  `MemoryPrimitiveBatchWriteOutcome.EffectState` reports `NotStarted`, `Partial` (with `CompletedCount` and
  `FailedIndex`), `Complete`, or `Unknown`.

`MemoryStringReadRequest.MaximumLength` is passed unchanged as Cheat Engine's `readString` `maxlength` argument. Cheat
Engine 7.7 does not document whether it counts bytes or characters, so treat it as a host-side bound. This is still to be
qualified on a live host (C3). For admission, the Client charges it as bytes for UTF-8 and as twice that for UTF-16.

## Contribution and Validation

Changes here are public API changes. Keep request/value types immutable, preserve functional
namespaces, add XML documentation, update `PublicAPI.Unshipped.txt`, and add focused contract
tests in `tests/CheatEngine.Client.Abstractions.Tests` before moving an entry to
`PublicAPI.Shipped.txt`.

From the repository root, validate the complete package graph:

```powershell
dotnet restore CheatEngine.Client.slnx --locked-mode
dotnet build CheatEngine.Client.slnx --configuration Release --no-restore
dotnet test --solution CheatEngine.Client.slnx --configuration Release --no-build --no-restore
```

## Lua module release outcomes

A module generated from `[CheatEngineLuaModule]` implements `IOwnershipAwareLuaModule`: at release it clears an exported
Lua global only while the global still holds the value the module published, compared by primitive identity, and never
overwrites a value a third party put there (audit finding F12, qualification scenario Q16). Every export is attempted;
`LastReleaseOutcome` then reports a `LuaModuleReleaseOutcome` with one `LuaExportReleaseStatus` per export (`Removed`,
`Replaced`, `Absent`, `Failed`, or `NotAttempted` for a registration that belongs to an earlier Lua state) and a computed
`LuaModuleReleaseKind` (`Released`, `PartiallyReleased`, `Stale`). The outcome is published before `Unregister` throws
for a failed export, and a second `Unregister` is a no-op. The outcome holds copied names and statuses only. The
vocabulary follows the CheatEngine.SDK 2.0.0 registration leases (`LuaRegistrationReleaseKind` values 1 to 3 have the
same meaning and number), which generated modules do not use: the ownership check is the generator's own. This
behavior is covered by managed tests against a Lua-globals double (C1); it is not a host qualification.
