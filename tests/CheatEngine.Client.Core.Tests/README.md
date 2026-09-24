# CheatEngine.Client.Core.Tests

## Context

This is the unit-test suite for Core's internal adapters and activation-bound orchestration. It uses internal ports and
fakes where a real Cheat Engine host would otherwise be required.

## Why this project exists

Core owns the behavior that is easy to get wrong at the SDK boundary: dispatch admission, epoch expiration, target
selection changes, LIFO resource cleanup, failure mapping, protected Lua calls and module leases, memory codecs, AOB
result ownership, process/runtime adapters, table mutations, and symbol-registration cleanup.

It also verifies the conservative value-scan gate and its managed state machine. It does not fabricate SDK-owned scan
handles or present an unvalidated Cheat Engine 7.7 x64 value-scan lifecycle as supported.

## How it helps improve CheatEngine.Client

The suite separates deterministic policy tests from live-host validation. That lets Core prove cleanup ordering,
rollback behavior, bounded materialization, and stale-resource rejection without mocking SDK statics or requiring a
user process. Regressions in lifecycle code fail quickly before they can leak into a plugin activation.

Three suites guard the SDK boundary contracts. `Infrastructure/TryContractTests` checks that no SDK exception crosses a
`Try*` method, that an expired activation is never reclassified, and that consumer exceptions are rethrown as the same
instance. `Infrastructure/OwnershipHandoffTests` checks that an SDK owner is released exactly once when publication
fails. `SdkContract/SdkMappingContractTests` checks that every status value and exception type of the consumed SDK
that the Client translates maps to a known Client failure kind.

The runtime and target suites pin the observed-fact model: `Domains/TargetArchitectureObserverTests` and
`Domains/RuntimeClientTests` derive the ISA from the family facts with the PID read first and never from the 64-bit
fact alone (Q31, Q32), `Domains/ProcessClientTests` keeps the selection identity and the observed width,
`Domains/MemoryPointerWidthTests` refuses pointer-typed paths on a configured/process width mismatch and keeps the codec
width at the process width, and `RuntimeClientTests` also locks the package, qualification and read-only probe gates
(Q44, Q45). `Infrastructure/ConsumedSdkIdentityTests` pins the package gate's version rule: a loaded CheatEngine.SDK of
the supported major at or above the pin, by SemVer precedence, is `Satisfied`, exact or not; another major or an older
version is `Missing`; a missing or malformed version is `Unknown`. `Domains/TableClientGenerationTests` and
`Domains/TableClientMutationTests` refuse record identifiers captured before a trusted table load and report factual
activation outcomes (Q34, Q35);
`Domains/InspectionClientBehaviorTests` and `Domains/SymbolRegistrationLeaseTests` check the symbol collision preflight,
the registration through a fake CheatEngine.SDK ownership coordinator (handoff, supersession, activation reservation)
and the mapping of every SDK release kind onto a lease whose `Dispose` never throws (Q16.b, Q43);
`Domains/InspectionMappingTests` proves the symbol registry status mappings total. `Infrastructure/CoreDiagnosticsTests` runs one operation of every emitting domain and
proves that diagnostic events are never emitted inside a dispatched callback, carry only closed names, counts and
epochs (Q46), and that a throwing sink changes no result.

Tests that serve as qualification evidence carry a `Qualification` trait (Q16, Q16.b, Q21, Q27, Q28, Q29, Q31, Q32, Q33, Q34,
Q35, Q43, Q44, Q45, Q46, Q48), so a Q filter selects them. They are C1 evidence (managed tests with doubles), never a
Cheat Engine host result.

## Run

From the repository root:

```powershell
dotnet test --project .\tests\CheatEngine.Client.Core.Tests\CheatEngine.Client.Core.Tests.csproj --configuration Release
```

Run the opt-in Cheat Engine live suite separately when it is available and a controlled local fixture has been
authorized; this project is not a substitute for that host-level gate.
