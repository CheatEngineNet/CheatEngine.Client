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
- Keeps expected failures explicit through `Try...(..., out CheatEngineFailure)` and throws
  `CheatEngineClientException`-derived exceptions only from the convenience methods.
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

| Namespace                    | Responsibility                                                        |
|------------------------------|-----------------------------------------------------------------------|
| `CheatEngine.Client`         | `ICheatEngineClient`, the activation-scoped facade                    |
| `.Dispatching` / `.Runtime`  | main-thread dispatch and runtime/capability observations              |
| `.Processes` / `.Inspection` | target selection, copied process/module/region/symbol data            |
| `.Memory`                    | bounded primitive, byte, string, codec, and pointer-chain operations  |
| `.Scanning`                  | AOB contracts and the value-scan session contract                     |
| `.Tables`                    | copied Address List records and explicitly trusted table I/O requests |
| `.Lua` / `.Modules`          | typed protected Lua operations, explicit modules, and leases          |
| `.Allocations` / `.Assembly` | selection-bound allocation and reversible patch leases                |
| `.RemoteExecution`           | bounded DLL injection and remote-call requests                        |
| `.Debugger` / `.Hotkeys`     | synchronous copied callbacks and bounded stream projections           |
| `.Timers` / `.Speed`         | activation-scoped timers and validated speed observations/mutations   |
| `.Hashing` / `.Dbvm`         | separate memory/file hashes and explicit DBVM observation/control     |
| `.Results`                   | classified expected failures and lifecycle exceptions                 |

No public consumer should use `CheatEngine.Client.Abstractions` as a namespace.

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
deterministic
priority. The reason text remains available for display and diagnostics.

In particular, the value-scan contract and state model are published, but the Core implementation
does **not** currently create a live `MemScan`/`FoundList` session. The next SDK line now contains a
production owner factory with parent rollback and child-before-parent teardown, but Client
enablement remains blocked by the Cheat Engine 7.7 x64 ownership and reactivation live gate. Do
not treat `IValueScanner` as available until that gate promotes its capability.

`IUnsafeLuaClient` is intentionally separate from `ILuaClient` and is not registered by default.
It is for explicitly trusted source only and still never exposes a raw Lua state.

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

With CheatEngine.SDK 1.0.0 a scan that finds nothing returns `CheatEngineFailureKind.IndeterminateHostResult`: Cheat
Engine 7.7 returns no result list for zero matches, and SDK 1.0.0 cannot distinguish that from an unresolved global, a
protected Lua failure, or a non-object result. It is never reported as `NotFound` or as a host rejection; an empty
result list that Cheat Engine does return remains a normal, successful no-match. The SDK 2.0 migration (see
`docs/migration/sdk-2.0.md` once it exists) replaces this with the detailed SDK outcome.

### Failure, exception and cancellation contract

`Try*` does not mean "never throws". Every family follows three rules, then the per-family details below:

- **Returned as `CheatEngineFailure`:** request refusals, policy refusals, budget refusals, pre-admission cancellation,
  Cheat Engine results that are false, absent, indeterminate, or malformed, and every CheatEngine.SDK exception raised by
  Client-internal SDK work (mapped by exception type, never by message text). No SDK exception type crosses a `Try*`.
- **Thrown:** `CheatEngineActivationExpiredException` when the activation has ended, `CheatEngineClientLifecycleException`
  when it is stopping, and `ArgumentException`/`ArgumentNullException`/`ArgumentOutOfRangeException` for invalid
  arguments (programming errors). An expired activation is never reported as `Cancelled` or `CapabilityUnavailable`.
- **Consumer code:** exceptions thrown by application-supplied code (dispatcher callbacks, `IMemoryCodec<T>` codecs,
  `ILuaOperation<T>` operations) are rethrown as the same instance, never converted into a failure.

`CheatEngineFailure.HostEffect` states how far the Cheat Engine primitive got: `NotStarted`, `Started` (effects may
persist), `Completed` (the primitive returned; the failure happened while Core copied or validated), `CleanupUnconfirmed`
(a resource or change may remain), or the conservative `Unknown`. A `CancellationToken` never interrupts a Cheat Engine
call that has started and never removes a callback, primitive, or effect that has begun: it is observed only before
dispatch and between Client-managed steps.

| Family | Cancellation stops preventing the host effect at | `HostEffect` values produced | Partial effects |
|---|---|---|---|
| Dispatcher (`ICheatEngineDispatcher`) | Dispatch admission: a `Cancelled` result proves the callback did not run | `NotStarted` (cancelled), `Unknown` (infrastructure failure) | Whatever the callback did; callback exceptions are rethrown unchanged |
| Patterns / AOB (`IPatternScanner`, `IPatternScanOutcomeClient`, Fluent `Aob`) | The start of the global `AOBScan`; later cancellation discards the copy | `NotStarted` (validation, module lookup, cancellation before the scan), `Completed` (cancellation or invalid data after the scan, SDK 1.0.0 `IndeterminateHostResult`), `CleanupUnconfirmed` (result-list release not confirmed), `Unknown` (SDK fault during the scan call) | None published: a failed scan never returns a prefix |
| Memory primitives, codecs, bytes, strings, pointer chains (`IMemoryClient`) | Dispatch admission; one call is one Cheat Engine operation | `NotStarted` (budget, unsupported type), `Unknown` (SDK fault, host refusal) | A codec may perform several reads or writes; a failed write codec can leave earlier writes in place |
| Memory batches (`IMemoryBatchClient`) | Dispatch admission: a `Cancelled` dispatch reports `MemoryBatchWriteEffectState.NotStarted` | `NotStarted` (admission, pre-dispatch cancellation, unsupported type), `Started` (a completed prefix persists), `Unknown` (SDK fault or other dispatch failure) | `EffectState` is authoritative: `Partial` with `CompletedCount`/`FailedIndex`, never rolled back |
| Inspection and symbol leases (`IInspectionClient`) | Dispatch admission | `NotStarted` (name already reserved by this activation), `Unknown` (SDK fault) | A faulted `registerSymbol` is not claimed and not retried |
| Tables (`ITableClient`) | Dispatch admission; `Find` filters a copied snapshot | `NotStarted` (policy, invalid relationship), `Completed` (`Find` cancelled after the snapshot, failed `Create` whose rollback was confirmed), `CleanupUnconfirmed` (record rollback not confirmed), `Unknown` (SDK fault, `loadTable` fault) | A failed `Create` destroys the partial record once and never retries; `loadTable` can execute table Lua |
| Lua typed operations and modules (`ILuaClient`) | Dispatch admission | `NotStarted` (cancellation), otherwise the operation's own failure | Owned by the operation; operation exceptions are rethrown unchanged |
| Unsafe Lua (`IUnsafeLuaClient`) | Dispatch admission | `NotStarted` (policy), `Unknown` (SDK fault; the script may have run partially) | The script may have run partially before a Lua error |
| Runtime and Processes (`ICheatEngineRuntime`, `IProcessClient`) | Dispatch admission | Not yet reported (`Unknown`) | Current behavior: only some SDK `Engine*Exception` types are caught, so a `LuaException` from a generated binding can still escape a `Try*`; aligning these domains with the SDK boundary is scheduled with the SDK 2.0 migration work |
| Capability-gated domains (allocations, assembly, remote execution, debugger, hotkeys, timers, speed, hashing, DBVM) | Not applicable: no Cheat Engine work is dispatched | `NotStarted` (`CapabilityUnavailable` or `Cancelled`) | None |
| Value scans (`IValueScanner`) | Not applicable: no Cheat Engine work is dispatched | Not yet reported (`Unknown`) | None; the refusal is the same `CapabilityUnavailable` or `Cancelled`, and reporting `NotStarted` here is scheduled with the other value-scan changes |

### Diagnostics and redaction

`CheatEngineFailure.Kind`, `Operation`, and `HostEffect`, together with counts and durations such as
`PatternScanMetrics`, are safe to log. `CheatEngineFailure.Message` and `CheatEngineFailure.Exception`, addresses,
values, symbol expressions, module names, file paths, and Lua source or error text are **user data**: log them only on an
explicit opt-in chosen by the application. `CheatEngineFailure.ToString()` returns only
`"{Kind} in {Operation} (host effect: {HostEffect})"`, so a structured logger that formats the failure object emits no
user data by default. Client libraries never log user data themselves: Hosting events carry epochs, stage names,
counts, and exception type names only, and a test rejects any Client `LoggerMessage` event whose parameters could carry
an address, expression, path, script, message, exception, or failure object.

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
