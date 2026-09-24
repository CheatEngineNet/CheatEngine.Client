# CheatEngine.Client.SourceGenerators.Lua

This Roslyn component turns explicit `CheatEngine.Client.Lua` declarations into activation-safe,
handle-free Client adapters. It is an analyzer asset consumed by plugin projects; it is not a runtime
dependency and never discovers application code through reflection.

`[CheatEngineLuaModule]` generates an `ILuaModule` around the ownership-aware registration that the CheatEngine.SDK
2.0.0 generator emits for the bindings type (`TryRegisterLuaFunctions`). The module registers with the `RejectExisting`
collision policy, so it refuses, before the first write, to replace a global that is already defined; it keeps the SDK
registration lease; and at release the lease writes a global only while it still holds the value the module installed,
so a third-party replacement survives (F12, Q16). The module never calls the legacy SDK
`RegisterLuaFunctions`/`UnregisterLuaFunctions` pair, which writes unconditionally.

`[CheatEngineLuaOperation]` turns a scalar `[LuaGlobal]` declaration into a readonly value operation
and strongly typed factory. Mapper calls use static abstract interface dispatch so the generated
runtime path stays trim- and AOT-friendly. Next to the factory, the containing class receives an `Execute` and a
`TryExecute` extension method on `ILuaClient` for that operation: they infer the operation and result types that the
`ILuaClient.Execute<TOperation, TResult>` pair takes, and pass the operation by reference, so
`client.Execute(Globals.CreateReadVersionLuaOperation(address))` never boxes it.

## What is generated for modules

For each valid module, one partial part of the module class with three members and one field:

- `Descriptor`: the module name and the export names, copied from the `[LuaFunction]` declarations of the bindings type,
  in declaration order. The Client reserves them for the activation before `Register` runs.
- `Register()`: hands `static state => Bindings.TryRegisterLuaFunctions(state, RejectExisting)` to the registrar and keeps
  the lease it returns in the field `_luaRegistration`.
- `Unregister()`: asks the registrar to release that lease and returns the `LuaModuleReleaseOutcome`.

Once per assembly that declares a valid module, two internal files in the namespace `CheatEngine.Client.Lua.Generated`:

- `CheatEngineLuaModuleRegistrar` takes every decision. It admits and ends the Lua operation, chooses which lease to
  release (an earlier registration of the module, the residual lease of a failed publication), keeps or consumes the
  module's lease, maps what CheatEngine.SDK reports to the Client vocabulary, and throws the classified refusal of a
  failed registration. It makes no SDK call.
- `CheatEngineLuaRegistrationAdapter` is the only generated code that calls CheatEngine.SDK, one SDK call per member
  and no decision: the Lua admission (`LuaRuntime.TryAcquireOperationWithOutcome`) and the end of the admitted
  operation, the module's registration delegate, and `LuaRegistrationLease.ReleaseWithOutcome` with or without a state.
  It copies every result into Client-owned values. It is a file of its own so that the EndToEnd tests can replace it
  with a double that forwards each call to a managed double of the SDK registration set, because
  `LuaRegistrationLease` has no public constructor.

`Register` and `Unregister` run on Cheat Engine's main thread: the Client dispatches them. `Register`, and an
`Unregister` that CheatEngine.SDK admits, do all their Lua work inside one admitted Lua operation, which they end before
returning. An `Unregister` that CheatEngine.SDK refuses with `Detached` or `ExternalStateReset` makes no Lua operation
(see Release below).

### Registration

| CheatEngine.SDK reports | `Register` throws a `CheatEngineOperationException` with |
|---|---|
| Admission `Detached` or `TransitionInProgress` | `ActivationExpired`, `NotStarted`; nothing ran |
| Admission `ExternalStateReset` | `RuntimeChanged`, `NotStarted` |
| Admission `ThreadNotAdmitted`, `NoStateForThread`, `Unknown` or an unknown status | `InvalidState`, `NotStarted` |
| `Succeeded` with a lease | nothing: the module keeps the lease |
| `Collision` | `OperationRejected`, `NotApplied`: `Lua global '<name>' is already defined and cannot be replaced by Client module '<module>'.` |
| `PreflightFailed` | `LuaError`, `NotApplied`: a protected lookup failed before anything was published |
| `PublicationFailed` | `LuaError`; `NotApplied` when the SDK rollback, or the release of the residual lease the rollback left, removed everything, `CleanupUnconfirmed` otherwise. The message carries the rollback counts. |
| `Unspecified`, a success without a lease, or an unknown kind | `InvalidHostResult`, `Unknown` |

A registration that the module still owns from an earlier call is released first, inside the same admitted operation;
a lease of an earlier attachment or Lua state is only forgotten. When a publication fails and the SDK compensation
leaves a residual lease, the registrar releases that lease once more in the same operation. Any other exception is an
SDK fault (F15): it propagates, and `ILuaClient` classifies it through its SDK boundary.

### Release

`Unregister` returns `AlreadyReleased` without any Lua call when the module owns nothing. Otherwise it consumes the
lease, releases it, and maps the SDK release kind. Because the lease is consumed before the release runs, a later
attempt could only report `AlreadyReleased`: no kind below is retryable, and a result outside the documented shape is
`CleanupUnconfirmed`, which requires manual recovery and which the Client lease leaves to the activation cleanup report.

| `LuaRegistrationReleaseKind` | `LeaseReleaseKind` | Why |
|---|---|---|
| `Released` | `Released` | Every still-owned global was removed; replaced ones were left alone |
| `PartiallyReleased` | `PartiallyReleased` | A protected read or write failed; the failed globals are named and never retried. A partial release that names no global is `CleanupUnconfirmed`. |
| `Stale` | `RefusedRuntimeChanged` | The lease belongs to an earlier attachment or Lua state, so nothing was written. The SDK counts every entry as remaining: after a re-enable on the same Cheat Engine Lua state, the earlier functions stay in `_G` (they raise an error when called). The release therefore requires manual recovery; it is not `ExternallyRemoved`. |
| `AlreadyReleased` | `AlreadyReleased` | The lease was already consumed |
| `NotAttempted` | `CleanupUnconfirmed` | The SDK's value before any release, never the result of one (CheatEngine.SDK 2.0.0 does not return it from a release); the lease is consumed all the same |
| an unknown kind | `CleanupUnconfirmed` | Fails closed: a release began on a consumed lease and its result is not understood |

When CheatEngine.SDK refuses the admission with `Detached` or `ExternalStateReset`, the Lua universe that holds the
registration is gone for this attachment: the lease is consumed without any Lua call and reported stale
(`RefusedRuntimeChanged`). Any other refusal keeps the registration and returns `CleanupUnavailable`, which the Client
lease retries.

## SDK-imposed registration API

The generated adapter uses exactly the members listed in `GeneratedLuaSurfaceRatchetTests`, each with its reason: the
admission (`LuaRuntime.TryAcquireOperationWithOutcome`, `LuaRuntimeOperation.State`, `LuaRuntimeOperation.Dispose`),
`LuaRegistrationLease.ReleaseWithOutcome` (with and without a state), and the getters of `LuaRegistrationResult`,
`LuaRegistrationFailure`, `LuaRegistrationReleaseOutcome` and `LuaRegistrationReleaseFailure`. The state of the
admitted operation is only passed to the SDK: generated code never reads or writes the Lua stack. The list is exact and
may only shrink; the module part and the registrar call no SDK member (they name SDK types and enum values only).

## With the CheatEngine.SDK generator

The Client generator and the CheatEngine.SDK LuaBindings generator run on the same compilation, and each consumes what
the other emits: the module calls the SDK-generated `TryRegisterLuaFunctions`, and an operation calls the SDK-generated
body of its `[LuaGlobal]` method. `RealSdkGeneratorCompositionTests` loads the SDK generator of the pinned package and
requires both outputs to compile together. Two CheatEngine.SDK 2.0.0 behaviours follow (CRIT-07):

- **Kept functions fail closed.** `TryRegisterLuaFunctions` publishes through `LuaRegistrationSet`, which wraps each
  thunk in a closure that captures the attachment and Lua state identity. A script that kept one of the module's
  functions after disable, reset or re-enable gets an ordinary Lua error instead of a call into an ended activation.
- **Integers and addresses are never rounded.** The SDK integer and address marshallers refuse a Lua float at or above
  2^53. For an operation, the throwing form of the binding raises a `LuaException` whose status is `LUA_OK`, and the Try
  form returns `false`; the generated operation reports both as a `LuaError` failure, with the SDK exception attached
  for the throwing form, and never lets the exception escape `TryExecute`. These forms cannot tell such a refusal from
  an unresolved global; the SDK Outcome form (`LuaOperationStatus`) could, but `[CheatEngineLuaOperation]` does not
  support it in 1.0.

`LuaOptional<T>` is deferred past 1.0: an operation takes scalar inputs only (CECLUA1103), and the Client has no
contract for an omitted result yet.

## Diagnostics

Every generator diagnostic is an error. Ids are allocated per range and never renumbered or reused: 1001-1006 module
shape, 1101-1106 operation shape, 1201-1209 module ownership (Q16; 1205-1209 unused). Each id is tracked in
`AnalyzerReleases.Unshipped.md` (moved to `AnalyzerReleases.Shipped.md` at release); both files are `AdditionalFiles` of
the generator project, so the release-tracking analyzers (RS2000-RS2008) fail the build when a descriptor and its row
disagree.

| Id | Title | Reported when | What to do |
|---|---|---|---|
| CECLUA1001 | Lua module must be a non-static partial class | The module is not a top-level, concrete, non-static, non-generic, non-file-local partial class. | Declare `internal sealed partial class MyModule : ILuaModule;` at namespace level. |
| CECLUA1002 | Lua module requires a static SDK bindings type | The bindings type is not a non-generic, non-file-local static class. | Point `[CheatEngineLuaModule(typeof(...))]` at the `static partial` class that holds the `[LuaFunction]` methods. |
| CECLUA1003 | Lua module bindings export nothing | The bindings type declares no `[LuaFunction]` export. | Add at least one `[LuaFunction("name")]` static method, or remove the module. |
| CECLUA1004 | Lua module exports must have unique names | An export name is missing, blank, or declared twice. | Give every `[LuaFunction]` of the bindings type its own non-blank Lua name. |
| CECLUA1005 | Lua module name cannot be blank | The explicit module name is empty or whitespace. | Pass a non-blank name, or omit it to use the module type name. |
| CECLUA1006 | Lua module needs a public constructor | Only non-public explicit constructors exist, so dependency injection cannot create the module. | Make one constructor public, or remove the explicit constructors. |
| CECLUA1101 | Lua operation requires a supported SDK global declaration | The operation is not a static partial `[LuaGlobal]` method of a top-level static partial class. | Declare `[CheatEngineLuaOperation][LuaGlobal("name")] public static partial T Name(...);` in a top-level `static partial` class. |
| CECLUA1102 | Lua operation method cannot be overloaded | Several `[CheatEngineLuaOperation]` methods share a name. | Rename the overloads: each operation needs its own method name. |
| CECLUA1103 | Lua operation has an unsupported result shape | Inputs are not scalar, or the result is not one return value or one trailing `out` value. | Use scalar inputs and one result; `LuaOptional<T>` inputs are deferred past 1.0. |
| CECLUA1104 | Lua operation result requires a mapper | A non-scalar SDK result has no `ILuaResultMapper`. | Pass `typeof(MyMapper)` to `[CheatEngineLuaOperation]`, where `MyMapper` implements `ILuaResultMapper<TSource, TResult>`. |
| CECLUA1105 | Lua operation mapper does not match the SDK result | The mapper does not implement `ILuaResultMapper` for that SDK result. | Implement `ILuaResultMapper<TSource, TResult>` with `TSource` equal to the declared SDK result. |
| CECLUA1106 | Lua operation mapper must project a safe Client result | The mapped graph exposes an SDK lifetime, interop, callback, or opaque framework type. | Map to a copied value: scalars, approved SDK value types, closed immutable collections or closed DTOs of them. |
| CECLUA1201 | Lua export is owned by more than one Lua module | Two modules of one compilation export the same Lua global; reported on the later module (file path, then position). | Remove the export from one of the bindings types: a Lua global has one owning module per plugin assembly. |
| CECLUA1202 | Lua module declares a member reserved by the generated registration | The module declares `Register`, `Unregister`, `Descriptor`, `s_descriptor`, `_luaRegistration`, or an explicit implementation of `ILuaModule`. No source is generated. | Rename or remove the member; the generator implements `ILuaModule`. |
| CECLUA1203 | Lua module inherits a Lua module implementation | A base type already implements `ILuaModule` or is itself a `[CheatEngineLuaModule]`. No source is generated. | Derive the module from `object`; compose shared behavior instead of inheriting a module. |
| CECLUA1204 | Lua module annotation is not the contract type | `[CheatEngineLuaModule]` does not come from `CheatEngine.Client.Abstractions`, or a `[LuaFunction]` on the bindings type does not come from `CheatEngine.SDK.Annotations`. Look-alike exports are never counted; no source is generated. | Remove the look-alike attribute type and use the contract attributes. |

## Limits

- `__index`/`__newindex` metamethods on `_G` run during the SDK preflight, publication and release; their effects are
  not undone (a limit of the SDK registration set). A metamethod that publishes under the module's names during
  registration is unsupported.
- A third party that kept a reference to one of the module's functions can still call it after the global was removed
  while the plugin stays enabled. After disable or a Lua state reset, the SDK's epoch-capturing closure makes such a
  call raise an ordinary Lua error instead of entering managed code.
- CECLUA1202 checks the members the module declares. A non-module base class that declares `Register`, `Unregister` or
  `Descriptor` makes the generated member hide it (compiler warning CS0108, an error under `TreatWarningsAsErrors`)
  instead of reporting a CECLUA diagnostic.
- The registrar and the adapter are internal types with fixed names. Two assemblies that both declare Lua modules and
  see each other's internals (`InternalsVisibleTo`) get the compiler warning CS0436 for them.
- Evidence level: C0 (the exact SDK surface of the adapter and its confinement, compile) and C1 (EndToEnd execution of
  the module and the registrar against a managed double of the SDK registration set, which compares values by object
  identity). There is no Lua 5.3 fixture in this repository, so there is no C2 evidence, and nothing here is
  host-qualified; the C4 observation is the Q16 scenario of the Coexistence protocol
  (tests/CheatEngine.Client.LivePlugin.Coexistence). The epoch-capturing closure of a kept function runs inside the
  SDK registration set, so its only evidence here is that the module publishes through `LuaRegistrationSet` (the
  composition test); the host behaviour is Q16.

## Tests

`tests/CheatEngine.Client.SourceGenerators.Lua.Tests` backs each statement above. The EndToEnd tests replace only the
generated SDK adapter; the module and the registrar run unchanged.

| Promise | Tests |
|---|---|
| Release writes only still-owned globals and reports the SDK facts | `LuaModuleOwnershipEndToEndTests.UnregisterRemovesEveryExportTheModuleStillOwns`, `UnregisterLeavesAThirdPartyReplacementUntouched`, `UnregisterTreatsAWrappedFunctionAsAReplacement`, `UnregisterRemovesAValueAThirdPartyRestoredToTheModulesOwn`, `UnregisterCountsAnExportThatIsAlreadyAbsentAsAReplacementAndWritesNothing`, `ThirdPartyValuesOfAnyLuaTypeAreCountedAsReplacements` |
| Stale registrations write nothing and require manual recovery; a partial release is never retried; a refused admission keeps the registration | `UnregisterAfterALuaStateReplacementWritesNothingAndReportsRefusedRuntimeChanged`, `UnregisterAfterAReattachWritesNothingAndLeavesTheEarlierGlobalsInPlace`, `UnregisterWithoutTheLuaUniverseConsumesTheRegistrationAsStaleWithoutLua`, `UnregisterWithoutAdmissionKeepsTheRegistrationForALaterAttempt`, `UnregisterReportsIndependentFailuresAsAPartialReleaseThatIsNeverRetried` |
| Registration refuses before any write and classifies every failure | `RegisterRefusesAnOccupiedExportBeforeAnyWrite`, `RegisterReportsAPreflightReadFailureBeforeAnyWrite`, `RegisterRollsBackWhatItPublishedWhenPublicationFails`, `RegisterReportsARollbackCompletedByTheResidualReleaseAsNotApplied`, `RegisterReportsARollbackThatLeftAGlobalAsAnUnconfirmedCleanup`, `RegisterWithoutAdmissionIsRefusedBeforeAnyLuaCall` |
| Each call runs in one admitted operation that it ends; the module registers with `RejectExisting` | `RegisterAndUnregisterEachRunInOneAdmittedOperationThatTheyEnd`, `AFailedPublicationEndsItsOperationAfterReleasingTheResidualLease`, `RegisterAgainReleasesTheEarlierRegistrationFirst` |
| Every SDK outcome enum is mapped totally and fails closed | `GeneratedRegistrarMappingTests` |
| The SDK surface is exact and confined to the adapter | `GeneratedLuaSurfaceRatchetTests` |
| No legacy registration; contract, projection and emitted text compared separately; no `unsafe` code required; refused without an attached SDK runtime | `ModuleContractTests`, `ModuleSnapshots` |
| The Client and SDK generators compile together; the module publishes through `LuaRegistrationSet`; integer results use the refusing marshallers | `RealSdkGeneratorCompositionTests` |
| A refused integer result is a `LuaError` failure and never an exception out of `TryExecute` | `OperationRefusalEndToEndTests` |
| Diagnostics are tracked, located and deterministic; models stay cached | `CheatEngineLuaDiagnosticCatalogTests`, `ModuleShapeDiagnosticTests`, `IncrementalityTests`, `IdentifierStabilityTests` |

The EndToEnd and outcome tests carry `[Trait("Qualification", "Q16")]`.
