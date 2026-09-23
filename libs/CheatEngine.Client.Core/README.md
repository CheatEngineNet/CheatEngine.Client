# CheatEngine.Client.Core

## Context

`CheatEngine.Client.Core` is the SDK-facing implementation layer of `CheatEngine.Client`. It maps
the public contracts from `CheatEngine.Client.Abstractions` onto `CheatEngine.SDK` 1.x while a
Cheat Engine plugin activation is current.

This package contains the main-thread dispatcher adapter, runtime probes, target-selection
tracking, typed memory and AOB adapters, copied inspection/table operations, protected Lua
operations, and lifecycle-owned cleanup infrastructure. It is an in-process layer for local,
authorized targets; it is not an IPC client or a standalone Cheat Engine host.

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

Core implements the currently qualified Client mappings for runtime facts and capabilities,
process selection, typed memory, bounded pointer chains and strings, AOB scans, copied inspection,
Address List operations, trusted table paths, and protected typed Lua work. All CE calls remain
synchronous; a cancellation token is observed before dispatch or between Client-managed steps, not
as an interruption of an already-running Lua primitive.

Value scan creation is deliberately unavailable. Although the public session contract and state
machine are present, CheatEngine.SDK 1.0.0 does not expose a public owner factory for the
`MemScan` and `FoundList` instances that Client would need. Core reports a capability failure
rather than making an unverified ownership assumption. It must not be documented or released as
a live value-scanning implementation until the Cheat Engine 7.7 x64 creation, destruction,
disable, and reactivation gate passes.

UI/forms, debugger and breakpoints, Auto Assembler, injection, public remote allocations,
structures, hotkeys/timers, speedhack, DBVM, Mono/IL2CPP, and ABI hooks are outside this layer's
current delivered capability.

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
`libs/CheatEngine.Client.Abstractions/README.md` ("Failure, exception and cancellation contract"); the Runtime and
Processes domains still catch only some SDK exception types.
