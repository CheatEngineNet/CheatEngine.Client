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
implement it and the Client calls it: `ILuaModule`, `ILuaOperation<TResult>`, `ILuaResultMapper<TSource, TResult>`,
`IMemoryCodec<T>` and `ICheatEngineClientModule`, whose members are frozen for 1.x). Public enums follow one charter
for 1.0: `int` backing, explicit values, and `Unknown = 0` on an outcome enum (`...Kind`, `...Status`, `...State`,
`...Effect`, `...Scope`); a new value can appear in a minor release.

### Capability Boundary

The contracts describe runtime, process, memory, inspection, AOB scanning, tables, protected Lua,
explicitly disposable value-scan sessions and target allocations. A contract is not an availability promise: callers
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
| `Client.ProcessSelection` | Operational adapter | Loaded CheatEngine.SDK 2.x at or above the consumed 2.0.0 | CheatEngine.SDK's read-only `Process.Current` observation | Unknown until Client receipts for Q30.a, Q31 and Q32 exist | `Unknown`; `Unavailable` when the package or host gate is `Missing` |
| `Client.TypedMemory` | Operational adapter | Loaded CheatEngine.SDK 2.x at or above the consumed 2.0.0 | Not probed by the snapshot (`Unknown`) | Unknown until Client receipts for Q20, Q21 and Q33 exist | `Unknown`; `Unavailable` when the package gate is `Missing` |
| `Client.PatternScanning` | Operational adapter | Loaded CheatEngine.SDK 2.x at or above the consumed 2.0.0 | Not probed by the snapshot (`Unknown`) | Unknown until Client receipts for Q27, Q28 and Q29 exist | `Unknown`; `Unavailable` when the package gate is `Missing` |
| `Client.ValueScanning` | Operational adapter, experimental (CECLIENT5001) | Loaded CheatEngine.SDK 2.x at or above the consumed 2.0.0 | Not probed by the snapshot (`Unknown`) | Unknown until Client receipts for Q25 and Q26 exist | `Unknown`; `Unavailable` when the package gate is `Missing` |
| `Client.Inspection` | Operational adapter | Loaded CheatEngine.SDK 2.x at or above the consumed 2.0.0 | Not probed by the snapshot (`Unknown`) | Unknown until Client receipts for Q16.b and Q28 exist | `Unknown`; `Unavailable` when the package gate is `Missing` |
| `Client.Tables` | Operational adapter | Loaded CheatEngine.SDK 2.x at or above the consumed 2.0.0 | Not probed by the snapshot (`Unknown`) | Unknown until Client receipts for Q34 exist | `Unknown`; `Unavailable` when the package gate is `Missing` |
| `Client.ProtectedLua` | Operational adapter | Loaded CheatEngine.SDK 2.x at or above the consumed 2.0.0 | Not probed by the snapshot (`Unknown`) | Unknown until Client receipts for Q05, Q16 and Q19 exist | `Unknown`; `Unavailable` when the package gate is `Missing` |
| `Client.UnsafeLuaExecution` | Operational, policy opt-in | Loaded CheatEngine.SDK 2.x at or above the consumed 2.0.0 | Not probed by the snapshot (`Unknown`) | Stays `Unknown`: no scenario covers arbitrary Lua | `Unavailable` without `EnableUnsafeLuaExecution()`; otherwise `Unknown` |
| `Client.Allocations` | Operational adapter, experimental (CECLIENT5002) | Loaded CheatEngine.SDK 2.x at or above the consumed 2.0.0 | Not probed by the snapshot (`Unknown`) | Unknown until Client receipts for Q30.a exist | `Unknown`; `Unavailable` when the package gate is `Missing` |
| `Client.Assembly` | Contract-only (Unavailable) | Loaded CheatEngine.SDK 2.x at or above the consumed 2.0.0 | Not probed by the snapshot (`Unknown`) | Unknown until Client receipts for Q32 exist | `Unavailable` |
<!-- capability-table:end -->

Every row also carries the lifetime gate (`Missing` once the activation has ended). A contract-only capability refuses
each operation with `CapabilityUnavailable` and `CheatEngineHostEffect.NotStarted`; no Cheat Engine work is dispatched.

`Client.ValueScanning` is an operational adapter over CheatEngine.SDK's scan sessions, published as an experimental
API (see "Experimental APIs" below): its implementation gate is `Satisfied`, and its qualification gate stays
`Unknown` until Client receipts for Q25 and Q26 exist.

`Client.Allocations` is an operational adapter over CheatEngine.SDK's target allocator, also published as an
experimental API: its implementation gate is `Satisfied`, and its qualification gate stays `Unknown` until Client
receipts for Q30.a exist.

`IUnsafeLuaClient` is intentionally separate from `ILuaClient` and is not registered by default.
It is for explicitly trusted source only and still never exposes a raw Lua state.

### Experimental APIs

An experimental API is marked `[Experimental("CECLIENT500x")]`: the compiler reports that diagnostic wherever the API is
used, and suppressing it (`<NoWarn>$(NoWarn);CECLIENT5001</NoWarn>` in the project, or a local
`#pragma warning disable CECLIENT5001`) is the explicit opt-in. An experimental API can change or be removed in a minor
release. Its id is lifted, and the API becomes stable, only when every live scenario of its capability passes on the
exact host profile of the release; the documentation link of each diagnostic points to its anchor below.

<a id="CECLIENT5001"></a>

#### CECLIENT5001: value scans

- **Scope:** `ICheatEngineClient.Scans`, `IValueScanner`, `IValueScanSession` and their types: `ValueScanFirstRequest`,
  `ValueScanNextRequest`, `ValueScanValue`, `ValueScanValueType`, `ValueScanComparison`, `ValueScanReadRequest`,
  `ValueScanPage`, `ValueScanMatch`, `ValueScanSessionState` and `ValueScanInvalidationKind`. The shared
  `ScanProtectionFilter` and `ScanAlignment` options are stable.
- **Behavior:** a session owns one Cheat Engine `MemScan` and its `FoundList`, created through CheatEngine.SDK's
  scan-session factory for a target whose identity it could establish. A first or next scan starts Cheat Engine's scan
  and waits for it in the same call, on Cheat Engine's main thread; a read copies one page of at most 1024 results, each
  an address and Cheat Engine's value text. Read a typed value again with `IMemoryClient.ReadPrimitive<T>(match.Address)`.
  The session is a lease (`ICheatEngineLease`): its release destroys the found list, then the scanner, on the main thread,
  and it is released when Cheat Engine selects another process and before the plugin is disabled.
- **Known limits:** on Cheat Engine 7.7 the stop address is exclusive and the start address is not byte-exact. Cheat
  Engine's wait runs queued main-thread work, and a call to the same session from that work is refused with
  `InvalidState`. A scan cancelled after it started and before the Client waited for it stays `Scanning` until its
  release asks Cheat Engine to stop it. `ValueScanValue.FromSingle` and `FromDouble` write the value in fixed-point
  notation with the number of decimals the application passes (0 to 15) and a `.` separator, never in exponent notation:
  Cheat Engine's rounded exact comparison takes its precision from those digits, and its Lua documentation states that
  `3` matches 3.0 to 3.4999 while `3.0` matches 3.00 to 3.0499. An alignment divisor is written as decimal text, and an
  ordered comparison follows Cheat Engine's own signedness rules; none of this has a Client receipt yet.
- **Exit criteria:** the Client receipts of Q25 (session lifecycle, results, release) and Q26 (target change and stale
  owners) on the exact host profile; the capability's qualification gate stays `Unknown` until then.

<a id="CECLIENT5002"></a>

#### CECLIENT5002: target allocations

- **Scope:** `ICheatEngineClient.Allocations`, `IAllocationClient`, `ITargetMemoryLease`, `AllocationRequest` and
  `AllocationProtection`.
- **Behavior:** an allocation runs Cheat Engine's `allocateMemory` through CheatEngine.SDK's allocator, on Cheat
  Engine's main thread, for a target whose identity it could establish, with the requested size, an explicit protection
  (`PAGE_READWRITE` or `PAGE_EXECUTE_READWRITE`) and an optional preferred address. The allocation is a lease
  (`ICheatEngineLease`): its release frees it with `deAlloc` on the main thread, only in the process incarnation and
  the Lua runtime that made it. After Cheat Engine selected another process, or when the process identifier names
  another process, the release is refused (`RefusedTargetChanged`) and nothing is freed in the new target: the Client
  never selects the old process again. A refused or unconfirmed release sets `RequiresManualRecovery`, keeps
  `Address` and `Size` readable, is never retried, and is reported when the plugin is disabled. A release that cannot
  begin because CheatEngine.SDK detached is `CleanupUnavailable`: it frees nothing, is reported at deactivation too,
  and does not set `RequiresManualRecovery`. The lease is released when Cheat Engine selects another process and before
  the plugin is disabled. When Cheat Engine allocated but no lease could be published, the one compensating release is
  reported: `CleanupUnconfirmed`, with the address in the failure message, when it was not confirmed.
- **Executable memory:** `AllocationProtection.ExecuteReadWrite` needs no opt-in beyond this diagnostic. The allocation
  itself runs nothing; what the application writes into it, and executes, is its own responsibility.
- **Known limits:** Cheat Engine may round the size up to its page size and may allocate away from the preferred
  address; the release passes the requested size back to `deAlloc`. CheatEngine.SDK cannot make its check of the
  selected target atomic with the `deAlloc` that follows, so a selection change in that interval is not covered. None of
  this has a Client receipt yet.
- **Exit criteria:** the Client receipt of Q30.a (allocation, release, and the refusal after a target change) on the
  exact host profile; the capability's qualification gate stays `Unknown` until then.

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

CheatEngine.SDK 2.0.0 also resolves addresses in Cheat Engine's own process (`EngineInspection.ResolveHostAddress`)
and registers symbol lists (`SymbolLists`). Neither is a 1.0 goal of the Client: `IInspectionClient` resolves in the
target process only (`TryResolveAddress` with an `AddressResolutionMode`) and registers one symbol per lease.

### AOB scan semantics and limits

`IPatternScanner` picks one of three routes for each request. `PatternScanMetrics.Scope` names the one that ran and
`PatternScanOutcome.RouteReason` says why:

| Route (`PatternScanScope`)        | When (`PatternScanRouteReason`)                                                     | Answer                                                                          | Cheat Engine work and cost                                                                                                                         |
|-----------------------------------|-------------------------------------------------------------------------------------|---------------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------|
| `GlobalHostScan`                  | No module and no range (`UnscopedRequest`)                                          | Exact matches; zero matches are `IndeterminateHostResult`                       | One global `AOBScan` over the whole target                                                                                                         |
| `HostBoundedRange`                | A module and/or range, on a qualified local target (`ScopedRequestOnQualifiedTarget`) | Exact matches; zero matches are a factual empty result when the error text was read | An exhaustive MemScan limited to the module intersected with the range; blocks Cheat Engine's main thread and cannot be interrupted once started in 1.0 |
| `GlobalHostScanWithManagedFilter` | A module and/or range whose bounded route cannot run (`TargetIdentityNotQualified`) | Exact matches inside the module and range; a list without such a match is an empty success; a `nil` list is `IndeterminateHostResult` | One global `AOBScan` over the whole target, after the bounded scan when that scan had run; Core applies the module and range while copying |

The bounded route runs when `TargetSelection.ObserveCurrent` qualifies Cheat Engine's selected target as a local process
incarnation. A CEServer or file-as-process target, a MemScan session CheatEngine.SDK could not create, or a target the
SDK could not qualify during the scan falls back to the global route with managed filters.

One scope rule applies on every route, so the same request returns the same addresses whichever route ran:

- with a module, a match is kept only when all of its pattern bytes lie inside `[BaseAddress, BaseAddress + ImageSize)`;
  a match that straddles the module end is never reported;
- with a range, a match is kept when its start lies in `[Start, End]`: the range end is the last allowed match start,
  and the bounded route scans up to `End + pattern length`, saturated at the top of the address space.

Core resolves the module before any scan, and a request whose module and range leave no room for one whole match (a
range that ends before the module can hold one, or a module smaller than the pattern) is refused (`OperationRejected`,
`NotStarted`) before any scan. `AobScanRequest.MaximumResults` bounds only how many addresses Core copies; it never stops
Cheat Engine early, and every route copies at most 65,535 addresses (`IsTruncated` reports a result cut by either
limit). The copied order is Cheat Engine's result-list order, which Cheat Engine does not specify: the first copied
address is not guaranteed to be the lowest address or the first logical region.

The memory protection and alignment of a scan are Client values: `ScanProtectionFilter` holds one
`ScanProtectionRequirement` (`Unspecified`, `Required`, `Excluded`, `Any`) per Cheat Engine flag (executable,
copy-on-write, writable), and `ScanAlignment` is `None`, `AlignedTo(divisor)` or `LastDigits(digits)`. Both validate
when they are created, and Core translates them into Cheat Engine's protection text (for example `+X-C-W`, or the
empty "find everything" text for the default filter) and fast-scan method on every route; no CheatEngine.SDK option type
appears in the public surface.

Four scan limits are distinct and must not be confused:

| Limit                   | Meaning                                                                                                           |
|-------------------------|-------------------------------------------------------------------------------------------------------------------|
| Cheat Engine work limit | The bounds on `HostBoundedRange`; none on the global routes                                                       |
| Available results       | `PatternScanMetrics.HostResultCount`, the number of rows Cheat Engine returned                                    |
| Materialization limit   | `AobScanRequest.MaximumResults`, which bounds `PatternScanMetrics.MaterializedCount`                              |
| Call deadline           | None: cancellation is observed only between Cheat Engine calls and Client-managed steps                           |

`IPatternScanner.ScanDetailed` returns a `PatternScanOutcome` with the same classification as `TryScan` (`IsSuccess`,
`Result`, `Failure`) plus:

- `Metrics` (`PatternScanMetrics`): the scope, the host result count, the examined, filtered-out and copied counts, the
  bounded route's below-start and at-or-after-stop skips, the unread rows, whether the in-request count is exact
  (`InBoundsCountIsExact`), and the Cheat Engine scan time (`HostScanElapsed`) separately from the Client copy time
  (`MaterializationElapsed`). Counts and durations never contain addresses and are safe to log.
- `HostOutcome` (`PatternScanHostOutcome`): what Cheat Engine reported for the scan that ran, before the Client decided
  the result, for example `NoResult` for a global `nil` or `HostReportedError` for a bounded error text.
- `RouteReason` (`PatternScanRouteReason`): why the scan ran on its route.
- `TargetIdentityVerified`: whether the copied addresses are attributed to one qualified local target incarnation for
  the whole scan; always on a successful bounded scan, only when the selection was the same qualified incarnation
  before and after the call on an unscoped global scan, and never on a failure or on the
  `GlobalHostScanWithManagedFilter` route, whose `TargetIdentityNotQualified` reason it never contradicts.

The global routes call `AobScanner.TryScanOutcome` of CheatEngine.SDK 2.0.0, which reports each host outcome
separately:

| Host outcome                        | Client result                                                        |
|-------------------------------------|----------------------------------------------------------------------|
| A result list with matches          | Success with the copied addresses                                    |
| An empty result list                | Success without addresses (a factual no-match)                       |
| `nil` (no result list)              | `IndeterminateHostResult`, `Completed`                               |
| `AOBScan` absent or not callable    | `CapabilityUnavailable`, `NotStarted`                                |
| A protected Lua error               | `LuaError`, `Unknown`; the message names the Lua status              |
| A value that is not a result list   | `InvalidHostResult`, `Completed`                                     |
| A list whose count cannot be read   | `InvalidHostResult`, `Completed`                                     |
| An outcome the Client does not know | `IndeterminateHostResult`, `Unknown`                                 |

On a global route a scan that finds nothing returns `IndeterminateHostResult` with the message "CE AOBScan returned
nil: on CE 7.7 zero matches and host failures share this shape": Cheat Engine 7.7 returns `nil` for zero matches, and a
host failure can return the same shape. It is never reported as `NotFound` or as a host rejection. The SDK also observes Cheat Engine's
selected target just before and just after the call: when the target changed in between, or its identity was lost or
gained, the addresses may belong to another process, so they are discarded and the scan fails with `TargetChanged` or
`TargetIdentityUnavailable` (`Completed`). The result list is released once through the SDK's `ReleaseWithOutcome`; any
outcome other than a confirmed release is `CleanupUnconfirmed`, and copied addresses are then discarded.

The bounded route calls `AobScanner.TryScanWithinBounds`, the stable overload without a call deadline:

| Host outcome                                          | Client result                                                                         |
|-------------------------------------------------------|---------------------------------------------------------------------------------------|
| In-bounds matches                                     | Success with the copied addresses                                                     |
| No in-bounds match, error text read                   | Success without addresses: a factual zero                                             |
| No in-bounds match, error text unreadable             | `IndeterminateHostResult`, `Completed`                                                |
| Cheat Engine reported an error text                   | `OperationRejected`, `Completed`; the message carries the bounded, unparsed text      |
| Empty bounds                                          | `OperationRejected`, `NotStarted`                                                     |
| Session not created, target not qualified in the scan | Fallback to the global route with managed filters                                     |
| Target changed, Lua runtime changed                   | `TargetChanged`, `RuntimeChanged`                                                     |
| Protected Lua failure, malformed result               | `LuaError`, `InvalidHostResult`                                                       |
| Cancellation observed by the SDK                      | `Cancelled`: `NotStarted` before the scan completed, `Completed` after it             |
| Deadline expired, unknown outcome                     | `IndeterminateHostResult`, `Unknown`                                                  |

The SDK releases the MemScan session once, child before parent, on every exit; any release that is not confirmed is
`CleanupUnconfirmed` and discards the copy, and a session whose creation rollback was not confirmed is never hidden
behind a fallback.

### Target selection, runtime facts and pointer width

Cheat Engine's selected target is ambient: `IProcessClient.Attach` changes Cheat Engine's global selection, and a
snapshot or a session that holds a process identifier does not stop the user, another plugin or a script from selecting
another process. `ProcessSnapshot.SelectionEpoch`, CheatEngine.SDK's PID-bracketed observation and, for a local process,
its incarnation (the PID and the creation time the SDK observed) reduce that risk for Client-owned leases; they are not
transactions. The selection epoch advances when the same PID denotes another process, but neither the SDK nor the Client
can see a selection that changed and changed back between two observations (A-B-A). `Attach` is CheatEngine.SDK's
`SelectAndObserve`: a normal return of Cheat Engine's selection call is not success until the selected process
identifier is read again. `ProcessSnapshot.Backend` says how Cheat Engine reaches the target. `ProcessSnapshot.StartTimeUtc`
(the creation time of the incarnation), `Name` and `ExecutablePath` describe a local process only: a CEServer target, a
file opened as a process or a target whose backend is not established never has them, and they do not prove liveness.
`IProcessClient.TryGetLocalProcesses` reads the local operating-system catalog offline: it never reaches Cheat Engine,
needs no current activation, and a local identifier is never evidence of a Cheat Engine target.

Every fact is a read-only CheatEngine.SDK 2.0.0 observation that reads the selected process identifier before and after
the target facts, because Cheat Engine reports the same family, width and pointer size as an x64 target when no target
is opened. A fact the SDK could not establish stays unknown; none is inferred from another. `CheatEngineRuntimeSnapshot`
groups them in `Version`, `Platform`, `Capabilities` and `Lua` and keeps separate facts:

- **Versions** (`CheatEngineRuntimeVersionInfo`): the complete four-part Cheat Engine file version
  (`getCheatEngineFileVersion`), compared with the qualified baseline component by component as integers; the loaded
  CheatEngine.SDK package version (`SdkPackageVersion`) and whether it is exactly the reviewed package
  (`IsReviewedSdkPackage`).
- **Host** (`CheatEngineRuntimePlatformInfo`): the operating system, whether Cheat Engine itself is 64-bit and the host
  architecture, each from its own global.
- **Target backend** (`TargetBackend`): a local process, CEServer, a file opened as a process, or unknown.
- **Target architecture (ISA)**: CheatEngine.SDK's derivation from Cheat Engine's x86 and ARM family facts together with
  its 64-bit fact, never from the 64-bit fact alone; contradictory or missing facts give
  `CheatEngineArchitecture.Unknown`.
- **Bitness** (`CheatEngineRuntimePlatformInfo.TargetBitness`, `ProcessSnapshot.Bitness`): the target bitness
  (`targetIs64Bit`, the process width `readPointer` follows) as observed; it can be known while the ISA is unknown.
- **Configured pointer size** (`ConfiguredPointerSizeBytes` and `ConfiguredPointerSize` on both types): the value Cheat
  Engine reports through `getPointerSize()`. It is per-attachment state, independent of the bitness, reset when a process
  is opened, and can hold any integer. `ConfiguredPointerSizeDiffersFromBitness` reports a mismatch as a fact.
- **External Lua state reset** (`CheatEngineRuntimeLuaInfo.ExternalStateResetDetected`): CheatEngine.SDK detected that
  Cheat Engine replaced its Lua state outside the plugin's control; Lua work is then refused with `RuntimeChanged`.

Cheat Engine's pointer read follows the process width, not the configured size. The Client therefore passes the
observed process width to CheatEngine.SDK's width-qualified pointer reads and writes on every pointer-typed operation
(`Address` primitives, primitive batches, pointer chains). Before any memory access it refuses the operation with
`CheatEngineHostEffect.NotStarted` when the width is unknown (`InvalidState` for a selected target, otherwise the kind of
the status CheatEngine.SDK reported, such as `TargetNotAttached`) and when the configured size is known and differs
(`OperationRejected`); the built-in `Address` codec follows the same policy. A configured size that could not be
observed is no evidence of a mismatch. On a 32-bit target nothing is truncated: writing an `Address` above 4 GiB is
refused with `OperationRejected` and `NotStarted`, a pointer value above 4 GiB returned by Cheat Engine is refused with
`OperationRejected` and `Completed`, and a pointer chain refuses a base or computed address above 4 GiB and names the hop
in its message.
Custom codecs receive the facts on `IMemoryReadContext` and `IMemoryWriteContext` (`Bitness`, `ConfiguredPointerSize`,
`ConfiguredPointerSizeBytes`, `ConfiguredPointerSizeDiffersFromBitness`); an unknown bitness is `PointerSize.Unknown`, and
the reason is reported if the codec then returns `false`. What the configured size affects besides the reported value is
not established.

### Address List records and symbols

A `MemoryRecordId` is valid in the Address List state in which this activation observed it. A trusted table load that
reached Cheat Engine (merge or replace, even a failed one) makes every identifier handed out before it stale, and every
identifier-taking operation refuses a stale identifier with `InvalidState` and `CheatEngineHostEffect.NotStarted` until a
new snapshot observes it again. The check runs before dispatch and again on Cheat Engine's main thread, where the load
advances the table generation, so concurrent callers cannot hand out or use an identifier of the earlier table state.
Loads made outside this activation are not detected.

`ITableClient.TrySetActive` reports what Cheat Engine did: already in the requested state (success, the setter is not
called), applied (success), a pending asynchronous activation (success; the snapshot's `State.IsAsyncProcessing` is
`true` and a later snapshot observes the final state), refused by an activation callback, script or record type
(`OperationRejected`, `Started`, with the post-change snapshot) or indeterminate (`IndeterminateHostResult`, `Started`). The setter is called at most once and never retried. Delete, parent assignment
and activation are CheatEngine.SDK `AddressListMutations` commands. They are refused without changing the record, with
`NotStarted`, while a table file loads on Cheat Engine's main thread (`InvalidState`, a script of that table calling
the Client) or after Cheat Engine's Lua runtime changed (`RuntimeChanged`). Creation, update and selection, which
CheatEngine.SDK has no command for, are refused by the Client while one of its trusted table loads runs (`InvalidState`,
`NotStarted`). A parent assignment walks the chain above the requested parent up to 4096 records: a record as its own
parent or under one of its descendants is `OperationRejected`, a longer chain `ResultLimitExceeded`. A delete or parent
assignment that raised after it started
is `LuaError` with `Started` and is not retried. Selecting a record is a host-visible effect on Cheat Engine's user
interface.

`IInspectionClient.TryRegisterSymbol` first resolves the name: a name that already resolves (a registered symbol, a
module or an expression that parses as an address) is refused with `OperationRejected` and `NotStarted`, and a failed
check registers nothing. The name is then registered through CheatEngine.SDK's symbol ownership coordinator; a
registration the SDK could not hand over to its lease was compensated once by the SDK and is reported with
`CleanupUnconfirmed`. `ISymbolRegistrationLease` is an `ICheatEngineLease`: `Release` unregisters the name only when it
still resolves to the leased address and no newer registration of the name through the SDK coordinator superseded the
lease, and reports `Released`, `Replaced` or `ExternallyRemoved` (the name no longer resolves to the address, left in
place), `Superseded`, `RefusedRuntimeChanged` (the Lua runtime of the registration is gone; the name may remain),
`CleanupUnconfirmed` (the unregistration began and failed) or the retryable `CleanupUnavailable` (the lease stays
active and the activation cleanup tries again). `Dispose` never throws. The check and the unregistration are not
atomic.

### Failure, exception and cancellation contract

`Try*` does not mean "never throws". Every family follows three rules, then the per-family details below:

- **Returned as `CheatEngineFailure`:** request refusals, policy refusals, budget refusals, pre-admission cancellation,
  Cheat Engine results that are false, absent, indeterminate, or malformed, and every CheatEngine.SDK exception raised by
  Client-internal SDK work (mapped by exception type and the SDK's own failure category, never by message text). No
  SDK exception type crosses a `Try*`. A Lua admission that the Client asks for itself (for example unsafe Lua)
  and that CheatEngine.SDK refuses is `ActivationExpired`, `RuntimeChanged` or `InvalidState` with `NotStarted`,
  never `OperationRejected`. A CheatEngine.SDK call that acquires its own admission (Address List mutations, table
  files, memory, inspection, scans) raises a plain `InvalidOperationException` when it is refused: that is
  `OperationRejected` with `Unknown` while the activation is current, `RuntimeChanged` after CheatEngine.SDK
  detected an external Lua state reset, and a thrown `CheatEngineActivationExpiredException` once the activation
  ended.
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
| Patterns / AOB (`IPatternScanner`, including `ScanDetailed`, Fluent `Aob`) | The start of the global `AOBScan` or of the bounded scan; the SDK also observes the token between the bounded route's Cheat Engine calls; later cancellation discards the copy | `NotStarted` (validation, module lookup, cancellation before the scan, `AOBScan` unavailable), `Completed` (cancellation or invalid data after the scan, `IndeterminateHostResult` for a `nil` result, a target changed during the scan), `CleanupUnconfirmed` (result-list release not confirmed), `Unknown` (SDK fault during the scan call, protected Lua error, unrecognized outcome) | None published: a failed scan never returns a prefix |
| Memory primitives, codecs, bytes, strings, pointer chains (`IMemoryClient`) | Dispatch admission; one call is one Cheat Engine operation | `NotStarted` (budget, unsupported type, unknown or mismatched pointer width, unavailable memory global, a pointer value above a 32-bit target on a write, a pointer chain base address above it), `Completed` (a pointer value or computed chain address above a 32-bit target after the reads returned), `Unknown` (SDK fault, host refusal, a failed codec) | `ReadBytesDetailed` reports the confirmed prefix of a partial byte read; a codec may perform several reads or writes, and a failed write codec can leave earlier writes in place |
| Memory batches (`IMemoryClient.ReadPrimitiveBatchDetailed`, `WritePrimitiveBatchDetailed`) | Dispatch admission: a `Cancelled` dispatch reports `MemoryBatchWriteEffectState.NotStarted` | `NotStarted` (admission, pre-dispatch cancellation, unsupported type), `Started` (a completed prefix persists), `Unknown` (SDK fault or other dispatch failure) | `EffectState` is authoritative: `Partial` with `CompletedCount`/`FailedIndex`, never rolled back; `IsSuccess` is `true` only when every operation completed |
| Inspection and symbol leases (`IInspectionClient`) | Dispatch admission | `NotStarted` (name already reserved by this activation, name already resolves, failed collision check, Lua stack unavailable), `Started` (`registerSymbol` failed or returned an invalid result), `Completed` (the activation began stopping, or had drained its resources, before it owned the lease, and the registration was released), `CleanupUnconfirmed` (a registration CheatEngine.SDK could not hand over, or whose release was not confirmed), `Unknown` (SDK fault, unavailable `registerSymbol`) | A faulted or refused registration is not claimed and not retried by name; a replaced or superseded name is left in place; lease releases are reported as `LeaseReleaseOutcome`, never thrown |
| Tables (`ITableClient`) | Dispatch admission; `Find` filters a copied snapshot | `NotStarted` (policy, stale record identifier, a mutation CheatEngine.SDK refused before changing the record: record or parent not found, self-parent, cycle, traversal limit, table load in progress, runtime changed; a creation, update or selection during a trusted table load; an unavailable table file function or Lua stack), `Started` (activation refused by the host or indeterminate; a delete or parent assignment that raised after it started; a table load or save that raised or returned an unexpected result), `Completed` (`Find` cancelled after the snapshot, failed `Create` whose rollback was confirmed, a completed mutation whose record could not be copied), `CleanupUnconfirmed` (record rollback not confirmed), `Unknown` (SDK fault, including a Lua admission CheatEngine.SDK refused inside an Address List command or a table file call, which is `OperationRejected` while the activation is current) | A failed `Create` deletes the partial record once and never retries; a refused activation can leave partial script effects; `loadTable` can execute table Lua |
| Lua typed operations and modules (`ILuaClient`) | Dispatch admission | `NotStarted` (cancellation, name already reserved by this activation, a Lua admission refused by CheatEngine.SDK), `NotApplied` (a global already defined, or a failed lookup or publication that the SDK rolled back completely), `CleanupUnconfirmed` (a publication whose rollback left a global), `Unknown` (SDK fault); otherwise the operation's own failure | A module release that fails is reported as `PartiallyReleased` with its failed globals and never retried; operation exceptions are rethrown unchanged |
| Unsafe Lua (`IUnsafeLuaClient`) | Dispatch admission | `NotStarted` (policy, or a Lua admission refused by CheatEngine.SDK), `Unknown` (SDK fault; the script may have run partially) | The script may have run partially before a Lua error |
| Runtime and Processes (`ICheatEngineRuntime`, `IProcessClient`) | Dispatch admission; `AttachExactName` also observes it before the local process catalog, and `GetLocalProcesses`, which never dispatches, between catalog steps (`NotStarted`) | `Completed` (CheatEngine.SDK reported a status that establishes no target: `TargetChanged`, `TargetIdentityUnavailable` for a file opened as a process, `CapabilityUnavailable`, `LuaError`, `InvalidHostResult`), `Unknown` (SDK fault, no selected target, an attach that CheatEngine.SDK refused or could not confirm) | A fact CheatEngine.SDK could not read stays `Unknown` in the snapshot instead of failing the call; `Attach` changes Cheat Engine's global selection |
| Capability-gated domains (assembly) | Not applicable: no Cheat Engine work is dispatched | `NotStarted` (`CapabilityUnavailable` or `Cancelled`) | None |
| Value scans (`IValueScanner`, `IValueScanSession`) | The start of Cheat Engine's first or next scan; a cancellation between the start and the wait leaves the session `Scanning`, and a later one discards the result | `NotStarted` (validation, session state, re-entrant call, changed target or runtime, cancellation before the start), `Started` (a scan, wait or reset call that failed or was cancelled before the wait), `Completed` (cancellation after the wait or the copy, a malformed count or page), `NotApplied` (a refused creation that CheatEngine.SDK rolled back), `CleanupUnconfirmed` (creation rollback not confirmed), `Unknown` (SDK fault) | A read publishes a whole page or nothing; a failed scan leaves the session `Invalidated` until a reset |
| Allocations (`IAllocationClient`, `ITargetMemoryLease`) | The `allocateMemory` call; a later cancellation frees the new allocation and publishes no lease | `NotStarted` (validation, cancellation before the call, target identity unavailable or changed, unavailable global), `NotApplied` (`allocateMemory` returned nil), `Completed` (cancellation after the call, or an allocation without owner whose compensating release was confirmed), `CleanupUnconfirmed` (that release was refused or not confirmed; the message carries the address), `Unknown` (SDK fault, Lua error, malformed result) | An allocation is published as a lease or released at once; a refused or unconfirmed release is never retried and requires manual recovery |

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

Every target-memory failure CheatEngine.SDK reports (`MemoryAccessFailure`) maps value by value; the message names the
category, never an address or a value.

| CheatEngine.SDK `MemoryAccessFailure` | `CheatEngineFailureKind` | `CheatEngineHostEffect` |
|---|---|---|
| `GlobalUnavailable` | `CapabilityUnavailable` | `NotStarted` |
| `LuaError` | `LuaError` | `Unknown` |
| `ReadFailed` | `MemoryReadFailed` | `Unknown` |
| `PartialRead` | `MemoryReadFailed`; `ReadBytesDetailed` keeps the confirmed prefix | `Unknown` |
| `DestinationTooSmall` | `ResultLimitExceeded` | `Unknown` |
| `PointerWidthUnknown` | `InvalidState` | `NotStarted` |
| `PointerValueExceedsTargetWidth` | `OperationRejected`; a pointer chain names the hop | `NotStarted` for a write, `Completed` for a read |
| `WriteFailed` | `MemoryWriteFailed` | `Unknown` |
| `InvalidResult` | `InvalidHostResult` | `Unknown` |
| A failure without a recognized cause | `IndeterminateHostResult` | `Unknown` |

`IMemoryClient.ReadBytesDetailed` reads through CheatEngine.SDK's counted byte read and returns a
`MemoryBytesReadOutcome`: `Bytes` is the contiguous prefix CheatEngine.SDK verified (`ConfirmedLength` of
`RequestedLength`), `IsComplete` and `IsSuccess` say whether every byte arrived, and `Failure` says why not. A partial copy
is therefore never confused with a host failure that copied nothing. `TryReadBytes` and `ReadBytes` report the same
failure and publish all or nothing; a codec context read that does not fill its buffer returns `false` and leaves the
buffer cleared.

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

### Typed memory routes

`IMemoryClient` has two typed routes, and it never resolves a codec implicitly:

- **Primitives** (`TryReadPrimitive<T>`, `TryWritePrimitive<T>`, the primitive batches and their throwing forms) take
  `where T : unmanaged` and support exactly `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`,
  `double` and `Address` (a target pointer, read and written at the observed bitness). Any other `T` is refused with
  `OperationRejected` and `HostEffect.NotStarted` before dispatch, without a Cheat Engine call.
- **Codecs** (`TryRead<T>`, `TryWrite<T>` and their throwing forms): the `MemoryReadRequest<T>` or `MemoryWriteRequest<T>`
  carries the `IMemoryCodec<T>` the application built or resolved.

String requests carry an explicit `MemoryStringEncoding` (`MemoryStringReadRequest.Create`,
`MemoryStringWriteRequest.CreateBounded`). The primitive batch outcomes expose `Failure` and `IsSuccess`, and
`MemoryBatchWriteEffectState` is `Unknown` (0), `NotStarted`, `Partial` or `Complete`.

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

A module generated from `[CheatEngineLuaModule]` registers through its bindings' SDK-generated
`TryRegisterLuaFunctions` with the `RejectExisting` collision policy and keeps the CheatEngine.SDK registration lease.
`ILuaModule.Unregister()` releases that lease: CheatEngine.SDK writes an exported Lua global only while it still holds
the value the module installed, compared by primitive identity, and never overwrites a value a third party put there
(audit finding F12, qualification scenario Q16). The returned `LuaModuleReleaseOutcome` copies what the SDK observed:
`Kind` in the Client lease vocabulary (`Released`, `PartiallyReleased`, `RefusedRuntimeChanged` for a registration of an
earlier Lua attachment or state or an admission refused with `Detached` or `ExternalStateReset`, which consumes it,
`AlreadyReleased` when nothing was owned, `CleanupUnavailable` when CheatEngine.SDK refused the Lua admission for another
reason and the module kept its registration, `CleanupUnconfirmed` when CheatEngine.SDK consumed the registration but
reported a release outside its documented shape), `RemovedCount`, `ReplacementCount` (a replaced or
already-`nil` global, left untouched), `RestoredCount` (always `0` for a generated module), `RemainingCount` and the
`FailedExports` of a partial release, which is never retried. A manual `ILuaModule` reports its release with the
`LuaModuleReleaseOutcome` factories. The outcome holds copied names and counts only. `ILuaModuleLease` is an
`ICheatEngineLease`: its `Release()` calls `Unregister()` on Cheat Engine's main thread, keeps the reported outcome in
`ModuleReleaseOutcome`, and returns the same kind with its host effect (`Completed` for `Released`, `Started` for
`PartiallyReleased` and `CleanupUnconfirmed`, `NotStarted` for a release that wrote nothing); an exception thrown by a
module is `CleanupUnconfirmed`. Only `CleanupUnavailable` and `Unknown` keep the lease active for a retry. This behavior is covered by managed tests against a double of the SDK registration set (C1); it is
not a host qualification.
