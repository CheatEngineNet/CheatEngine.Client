# CheatEngine.Client.SourceGenerators.Lua

This Roslyn component turns explicit `CheatEngine.Client.Lua` declarations into activation-safe,
handle-free Client adapters. It is an analyzer asset consumed by plugin projects; it is not a runtime
dependency and never discovers application code through reflection.

`[CheatEngineLuaModule]` generates an `IDescribedLuaModule` and `IOwnershipAwareLuaModule` adapter around
the SDK-generated `RegisterLuaFunctions`. It refuses, before the first write, to replace a global that is
already defined, pins the value it published under each export, and at release clears a global only
while it still holds that value, so a third-party replacement survives (F12, Q16). It never calls the
legacy `UnregisterLuaFunctions`, which writes `nil` unconditionally.

`[CheatEngineLuaOperation]` turns a scalar `[LuaGlobal]` declaration into a readonly value operation
and strongly typed factory. Mapper calls use static abstract interface dispatch so the generated
runtime path stays trim- and AOT-friendly.

## Ownership-aware release (Q16)

Audit finding F12 and qualification scenario Q16: a disabled plugin must never overwrite a global that a third party (a
script, a table, another plugin) put under one of its names after registration. On CheatEngine.SDK 1.0.0 the generated
adapter implements that itself; every step runs inside one admitted `LuaRuntimeOperation` on Cheat Engine's main thread.

`Register()`:

1. Refuses without any Lua write when the previous registration of this instance is still current in the Lua state;
   pins left over from an earlier state or attachment are released first (that only marks them released).
2. Preflight, unchanged: reads every export and throws
   `InvalidOperationException("Lua global '<name>' is already defined and cannot be replaced by Client module '<module>'.")`
   before the first write when one is not `nil`.
3. Calls the SDK-generated `RegisterLuaFunctions` once, then pins the value published under each export
   (`TryGetGlobal` + `CreateRef`). A published value that reads back `nil` is a registration failure.
4. Any failure after the first write rolls back: pinned exports are released ownership-aware (below); unpinned exports
   are cleared only when they are not `nil`, because the preflight proved them `nil` inside the same operation. The thrown
   exception has the type of the original failure and a message that starts with its message; when rollback steps also
   fail, `InnerException` is an `AggregateException` whose first element is the original failure. CheatEngine.SDK 1.0.0
   has no `LuaException` constructor that takes both a status and an inner exception, so that combined `LuaException`
   reports `Status == LuaStatus.Ok`; the original status stays on the original failure (the first inner exception).
   Without a rollback failure the original exception is thrown itself, status included.

`Unregister()`:

1. Returns without any SDK call when the instance owns no registration, so the lease retry after a failed release is a
   no-op and `LastReleaseOutcome` keeps its value.
2. Consumes the ownership before the first Lua call; a release is attempted once and never retried.
3. A registration that belongs to an earlier Lua state or attachment is reported `Stale` (every export `NotAttempted`)
   without any Lua operation.
4. Otherwise every export is attempted, in export order: the current global is compared with the pinned value by
   primitive identity (`RawEquals`, no `__eq`). Still the module's: set to `nil` (`Removed`). A different value, including
   a wrapper of the module's function: left untouched (`Replaced`). Already `nil`: nothing written (`Absent`). A failed
   read, comparison or clear: `Failed`. Every pin is released; a pin release failure is reported without changing the
   export status.
5. The `LuaModuleReleaseOutcome` is published through `IOwnershipAwareLuaModule.LastReleaseOutcome` before a failure is
   thrown: one failure as is, several as an `AggregateException` (mapped by Core to `CheatEngineFailureKind.Unknown`).

The vocabulary mirrors the CheatEngine.SDK 2.0 registration leases, so the migration is a mapping, not a redesign.

## ADR-01 registered exception

ADR-01 makes the SDK the only native authority: Client code does not touch the Lua stack. The ownership check above is
the single, frozen exception of generated Client code, because SDK 1.0.0 has no ownership-aware registration. Every Lua
state access is confined to one private nested struct of each generated module (the SDK port); the algorithm itself runs
over a private port interface, which lets the EndToEnd tests execute it against a managed double. Generated module code
may use exactly these SDK members, frozen by `GeneratedLuaSurfaceRatchetTests`:

- in the public `Register`/`Unregister` only: `LuaRuntime.AcquireOperation()`, `LuaRuntimeOperation.State`,
  `LuaRuntimeOperation.Dispose()`;
- in the SDK port only: `LuaState.Top`, `SetTop`, `TryGetGlobal`, `TrySetGlobal`, `IsNil`, `PushNil`, `CreateRef`,
  `TryPushRef`, `RawEquals`; `LuaRef.IsCurrent`, `LuaRef.Release(LuaState)` (never `LuaRef.Dispose()`, which acquires
  an ambient state); `LuaStatus.IsOk`, `LuaError.FromStack`, `LuaException(LuaError)`;
- in the generic registration rollback, which touches no Lua state: `LuaException(string, Exception)` and an
  `is LuaException` type test, so a combined failure keeps the type of the original one.

Each port method records `Top` and restores it in a `finally` block, reading the error value with `LuaError.FromStack`
before that restoration. The port is the only generated code that runs against the real Lua state, and no test in this
repository can execute it; `SdkPortDecisionTests` pins what it decides (see Tests). Removal condition: adopting the CheatEngine.SDK 2.0 registration leases
(`TryRegisterLuaFunctions` with `RejectExisting` and `LuaRegistrationLease.ReleaseWithOutcome`) deletes the port, the
preflight and the reserved member prefix; the entry is tracked in docs/migration/sdk-2.0.md.

## Diagnostics

Every generator diagnostic is an error. Ids are allocated per range and never renumbered or reused: 1001-1006 module
shape, 1101-1106 operation shape, 1201-1209 module ownership (Q16; 1205-1209 unused). Each id is tracked in
`AnalyzerReleases.Unshipped.md` (moved to `AnalyzerReleases.Shipped.md` at release); both files are `AdditionalFiles` of
the generator project, so the release-tracking analyzers (RS2000-RS2008) fail the build when a descriptor and its row
disagree.

| Id | Title | Reported when |
|---|---|---|
| CECLUA1001 | Lua module must be a non-static partial class | The module is not a top-level, concrete, non-static, non-generic, non-file-local partial class. |
| CECLUA1002 | Lua module requires a static SDK bindings type | The bindings type is not a non-generic, non-file-local static class. |
| CECLUA1003 | Lua module bindings export nothing | The bindings type declares no `[LuaFunction]` export. |
| CECLUA1004 | Lua module exports must have unique names | An export name is missing, blank, or declared twice. |
| CECLUA1005 | Lua module name cannot be blank | The explicit module name is empty or whitespace. |
| CECLUA1006 | Lua module needs a public constructor | Only non-public explicit constructors exist, so dependency injection cannot create the module. |
| CECLUA1101 | Lua operation requires a supported SDK global declaration | The operation is not a static partial `[LuaGlobal]` method of a top-level static partial class. |
| CECLUA1102 | Lua operation method cannot be overloaded | Several `[CheatEngineLuaOperation]` methods share a name. |
| CECLUA1103 | Lua operation has an unsupported result shape | Inputs are not scalar, or the result is not one return value or one trailing `out` value. |
| CECLUA1104 | Lua operation result requires a mapper | A non-scalar SDK result has no `ILuaResultMapper`. |
| CECLUA1105 | Lua operation mapper does not match the SDK result | The mapper does not implement `ILuaResultMapper` for that SDK result. |
| CECLUA1106 | Lua operation mapper must project a safe Client result | The mapped graph exposes an SDK lifetime, interop, callback, or opaque framework type. |
| CECLUA1201 | Lua export is owned by more than one Lua module | Two modules of one compilation export the same Lua global; reported on the later module (file path, then position). |
| CECLUA1202 | Lua module declares a member reserved by the generated registration | The module declares `Register`, `Unregister`, `Descriptor`, `LastReleaseOutcome`, `s_descriptor`, a member starting with `__CheatEngineLua`, or an explicit implementation of the module contract. No source is generated. |
| CECLUA1203 | Lua module inherits a Lua module implementation | A base type already implements `ILuaModule` or is itself a `[CheatEngineLuaModule]`. No source is generated. |
| CECLUA1204 | Lua module annotation is not the contract type | `[CheatEngineLuaModule]` does not come from `CheatEngine.Client.Abstractions`, or a `[LuaFunction]` on the bindings type does not come from `CheatEngine.SDK.Annotations`. Look-alike exports are never counted; no source is generated. |

## Limits

- `__index`/`__newindex` metamethods on `_G` run during the preflight, the observation and the clear, exactly as they do
  for the SDK 2.0 leases; their effects are not undone. A metamethod that publishes under the module's names during
  registration is unsupported.
- A third party that kept a reference to the module's function can still call it after the global was removed; that is
  the SDK's disabled-plugin callback contract (Q15), not something this generator qualifies.
- Only Lua failures are handled: a `LuaException` or a failing `LuaStatus`. A programming or lifecycle exception from an
  SDK call (F15) propagates as is. After a successful publication, `Register` then rolls nothing back: the published
  globals and the pins already created stay in the Lua state (`LuaRef` has no finalizer; the slots live until the state
  is destroyed). During `Unregister`, the remaining exports are not attempted and no outcome is published, but the
  registration is already consumed, so the lease retry does nothing.
- CECLUA1202 checks the members the module declares. A non-module base class that declares `Register`, `Unregister`,
  `Descriptor` or `LastReleaseOutcome` makes the generated member hide it (compiler warning CS0108, an error under
  `TreatWarningsAsErrors`) instead of reporting a CECLUA diagnostic.
- Evidence level: C0 (ratchet, symbol-resolved decisions and stack discipline of the SDK port, compile) and C1 (EndToEnd
  execution of the algorithm around the port against a managed Lua-globals double). The double compares values by
  object identity, so how `lua_rawequal` compares strings, numbers or light C functions is not exercised. This repository
  has no Lua 5.3 fixture, so there is no C2 evidence, and nothing here is host-qualified; the C4 observation is the Q16
  scenario of the Coexistence protocol (tests/CheatEngine.Client.LivePlugin.Coexistence).

## Tests

`tests/CheatEngine.Client.SourceGenerators.Lua.Tests` backs each statement above. The EndToEnd tests replace the SDK
port with a managed double: they prove the algorithm around the port, never what the port decides. The port, the only
generated code that runs in a real plugin, is proven by the C0 checks of `SdkPort/`; a change to their expected paths is
a change of the Q16 contract, not a formatting change.

| Promise | Tests |
|---|---|
| The SDK port reads the global, reports `nil` before any pin push, and decides owned or replaced by `RawEquals` of the global and the pinned value; `CreateRef` is the only pin; a clear writes `nil` under the export name | `SdkPortDecisionTests` (C0: symbol-resolved paths of every port method, executed `ExportName`, and `MutatedPortsAreRejected`, which proves the check fails for identity and decision mutations) |
| The SDK port restores the stack on every path | `SdkPortStackDisciplineTests` (C0, syntax) |
| The algorithm never clears a replaced global and clears only owned ones | `LuaModuleOwnershipEndToEndTests.UnregisterLeavesAThirdPartyReplacementUntouched`, `UnregisterRemovesEveryExportTheModuleStillOwns`, `UnregisterRemovesAValueAThirdPartyRestoredToTheModulesOwn`; `UnregisterTreatsAWrappedFunctionAsAReplacement` and `ThirdPartyValuesOfAnyLuaTypeAreReportedAsReplaced` show that any non-owned observation is kept (identity stand-ins, not Lua type semantics) |
| Every export is attempted; the outcome is published before the throw; ownership is consumed before Lua, so a retry is a no-op | `UnregisterAttemptsEveryExportAndAggregatesIndependentFailures`, `UnregisterConsumesOwnershipBeforeTouchingLuaSoARetryIsANoOp`, `UnregisterConsumesOwnershipEvenWhenAnSdkExceptionEscapesTheRelease`, `PublicUnregisterWithoutAnOwnedRegistrationReturnsWithoutAcquiringTheLuaRuntime` |
| Registration refuses before any write and rolls back only what it published; the original failure is kept | `RegisterRefusesAnOccupiedExportBeforeAnyWrite`, `RegisterRollsBackOnlyTheExportsItPublishedWhenPublicationFails`, `RegisterRollsBackWhenOwnershipCannotBeCaptured`, `RollbackFailureIsReportedWithoutReplacingThePrimaryRegistrationFailure`, `ARegistrationFailureWithoutRollbackFailureKeepsItsLuaStatus`, `RollbackFailureKeepsThePrimaryLuaStatusOnTheFirstInnerException` |
| Stale registrations write nothing | `UnregisterAfterALuaStateReplacementWritesNothingAndReportsStale`, `RegisterAfterAStaleRegistrationReleasesTheStaleTokensFirst` |
| The SDK surface is frozen and confined to the port | `GeneratedLuaSurfaceRatchetTests` |
| No legacy unregistration; contract, projection and emitted text compared separately; no `unsafe` code required | `ModuleContractTests`, `ModuleSnapshots` |
| Diagnostics are tracked, located and deterministic; models stay cached | `CheatEngineLuaDiagnosticCatalogTests`, `ModuleShapeDiagnosticTests`, `IncrementalityTests`, `IdentifierStabilityTests` |

The SdkPort, EndToEnd and outcome tests carry `[Trait("Qualification", "Q16")]`.
