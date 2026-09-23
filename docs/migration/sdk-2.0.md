> Recreated 2026-09 from the audit, not the historical docs/ tree.

# Migrating CheatEngine.Client to CheatEngine.SDK 2.0

This page lists everything the Client must change, retest and requalify when it moves from the CheatEngine.SDK 1.0.0
package to the CheatEngine.SDK 2.0 package. It is a plan, not a result: nothing here is a qualification result, and
nothing here is available to a Client built against 1.0.0.

## Scope and status

- **Consumed today:** `CheatEngine.SDK` 1.0.0, declared as `[1.0.0, 2.0.0)`, NuGet content hash (SHA-512, base64)
  `n7nHqZ8vzo7Vf20jF0fkh/jUtR3yo1TwRGpXE7ERxZeJ4C5S/Nsft4lqOg7zGwfsD5Nh9tTVgdw4PrybJRF0gA==`, pinned by
  [`eng/CheatEngineSdk.props`](../../eng/CheatEngineSdk.props) and locked in every lock file (the Core build embeds the
  resolved entry of `libs/CheatEngine.Client.Core/packages.lock.json`).
- **Target:** CheatEngine.SDK 2.0.0, which is **not published**. Its API comes from the SDK branch
  `feat/audit-remediation-cicd` of the CheatEngine.SDK repository. Each SDK name below carries one of two labels:
  *on the SDK branch* (present in the branch sources this page was written from) or *planned (S-XXX)* (named by the
  SDK work item that introduces it, not on the branch yet).
- **Provisional names:** every SDK 2.0 name here is provisional until the final SDK `CompatibilitySuppressions.xml` and
  `PublicAPI.Unshipped.txt` freeze it; the documentation pass that follows the SDK release (DOCS-FINAL) rewrites the
  names and regenerates the break list from those files (PR-SEQ-21).
- **Evidence levels** used below: C1 managed tests with doubles, C2 package or fixture level, C3 a Cheat Engine host
  run on profile `ce-7.7.0.10621-x64-managed-hostfxr`, C4 several plugins in one Cheat Engine process.

## Why the Client stays on 1.0.0

A feature exists for the Client only when it is in the package the Client consumes, not when it is in the SDK
repository (audit ADR-10). The maintainers decided that the Client stays on CheatEngine.SDK 1.0.0 until 2.0.0 is
published, pinned in `eng/CheatEngineSdk.props`, compiled against, and qualified.

The build enforces that decision:

- `CHEATENGINECLIENT9016` (`Directory.Build.targets`) refuses a pin that is a prerelease, of another major than
  `_CheatEngineClientSupportedSdkMajor` (1), or outside its range, before NuGet resolves anything.
- `CECLIENT017` (Hosting build targets) fails the build of a plugin that resolves `CheatEngine.SDK` 2.0 or later next
  to this Client; `CheatEngineClientAllowUnsupportedSdk=true` turns it into a warning, and such a plugin is unsupported.

The concrete reason a Client compiled against 1.0.0 must not run on 2.x: SDK 2.0 renumbers `MemoryAccessFailure`
(`DestinationTooSmall` 4 → 5, `WriteFailed` 5 → 8, `InvalidResult` 6 → 9) because `PartialRead`, `PointerWidthUnknown`
and `PointerValueExceedsTargetWidth` were inserted, so a 1.0.0-compiled binary would read the new values under the old
names. It also removes the two-argument `AddressResolutionOptions` constructor that the Client's public
`IInspectionClient` signatures expose. Both are listed in the generated break list below.

## Same-lot adoption checklist

The move to 2.0 is one integration lot, never a dependency bump (audit A21-20, A10-19, ADR-10):

1. Move the pin in `eng/CheatEngineSdk.props` (`CheatEngineSdkVersion`, `CheatEngineSdkUpperBound`) and raise
   `_CheatEngineClientSupportedSdkMajor` in the same pull request. Regenerate every `packages.lock.json` with the .NET
   CLI in that pull request: `dotnet restore <project> --force-evaluate` one project at a time, the three Coexistence
   fixtures first, never a solution-level `--force-evaluate`; then `dotnet restore CheatEngine.Client.slnx --locked-mode`
   must pass. The Core build embeds the new identity from its lock file and the restored package
   (`CHEATENGINECLIENT9050` fails the build when it cannot).
2. Change together, in that lot: the production port, its test doubles, the capability gates of `RuntimeClient`, the
   examples and the `ceplugin` template, the package READMEs and the capability tables, and the qualification entries.
3. Never remove a "factory absent" or "CheatEngine.SDK 1.0.0 does not provide …" text without wiring the adapter that
   makes it false, and never wire an adapter without qualifying the package it runs on.
4. Rerun `SdkConsumerContractTests` (Q48): regenerate `ConsumedSdkSurface.cs`, extend `SdkApiUsage.cs`, and re-check the
   `ApprovedSdkClientTypes` allowlist against the 2.0 package.
5. Rerun this page's generator with the final suppression file and the `client-canary` report, then resolve every row.

## Induced Client breaks (generated)

The block below is written by [`eng/migration/Update-SdkMigrationBreakList.ps1`](../../eng/migration/Update-SdkMigrationBreakList.ps1)
from the SDK `src/CheatEngine.SDK/CompatibilitySuppressions.xml` (the ApiCompat baseline against 1.0.0), the Client's
consumed-surface inventory (`tests/CheatEngine.Client.Tests/SdkContract/ConsumedSdkSurface.cs`) and the allowlist of SDK
types allowed in public Client signatures (`source-generators/CheatEngine.Client.SourceGenerators.Lua/ApprovedSdkClientTypes.cs`).
*Public API* means the declaring type is allowlisted, so the break is a Client public break (audit A11-19); *Consumed*
names the inventory line that references it. Suppressions with neither exposure are counted, not listed. Do not edit the
block by hand.

<!-- generated:sdk-breaks:start -->
Provenance: SDK commit `b47688c5c7758bcacf8d13915360dbaeb350dab3`, CompatibilitySuppressions.xml SHA-256 `d6ef242bde213ec7b488a5e3037b6a217549c4c51786f67e35df9d823497fc07`, client-canary report: none, generated 2026-09-23.

| ApiCompat id | SDK member (DocId) | Client exposure | Action |
|---|---|---|---|
| CP0011 | `F:CheatEngine.SDK.Engine.Memory.MemoryAccessFailure.DestinationTooSmall` | Consumed (`CheatEngine.Client.Core T CheatEngine.SDK.Engine.Memory.MemoryAccessFailure`) | Recompile against 2.0 (a 1.0.0 binary reads the renumbered values under the old names); the Client only formats the value, then maps PartialRead, PointerWidthUnknown and PointerValueExceedsTargetWidth. |
| CP0011 | `F:CheatEngine.SDK.Engine.Memory.MemoryAccessFailure.InvalidResult` | Consumed (`CheatEngine.Client.Core T CheatEngine.SDK.Engine.Memory.MemoryAccessFailure`) | Recompile against 2.0 (a 1.0.0 binary reads the renumbered values under the old names); the Client only formats the value, then maps PartialRead, PointerWidthUnknown and PointerValueExceedsTargetWidth. |
| CP0011 | `F:CheatEngine.SDK.Engine.Memory.MemoryAccessFailure.WriteFailed` | Consumed (`CheatEngine.Client.Core T CheatEngine.SDK.Engine.Memory.MemoryAccessFailure`) | Recompile against 2.0 (a 1.0.0 binary reads the renumbered values under the old names); the Client only formats the value, then maps PartialRead, PointerWidthUnknown and PointerValueExceedsTargetWidth. |
| CP0002 | `M:CheatEngine.SDK.Engine.Inspection.AddressResolutionOptions.#ctor(System.Boolean,System.Boolean)` | Public API (`CheatEngine.SDK.Engine.Inspection.AddressResolutionOptions`) | Client public break: replace the UseHostSymbolTable path by EngineInspection.ResolveHostAddress, add the `*REMOVED*` PublicAPI lines, and review every `new AddressResolutionOptions(true)`, which binds Shallow on 2.0. |
| CP0002 | `M:CheatEngine.SDK.Engine.Inspection.AddressResolutionOptions.Deconstruct(System.Boolean@,System.Boolean@)` | Public API (`CheatEngine.SDK.Engine.Inspection.AddressResolutionOptions`) | Client public break: replace the UseHostSymbolTable path by EngineInspection.ResolveHostAddress, add the `*REMOVED*` PublicAPI lines, and review every `new AddressResolutionOptions(true)`, which binds Shallow on 2.0. |
| CP0002 | `M:CheatEngine.SDK.Engine.Inspection.AddressResolutionOptions.get_UseHostSymbolTable` | Public API (`CheatEngine.SDK.Engine.Inspection.AddressResolutionOptions`) | Client public break: replace the UseHostSymbolTable path by EngineInspection.ResolveHostAddress, add the `*REMOVED*` PublicAPI lines, and review every `new AddressResolutionOptions(true)`, which binds Shallow on 2.0. |
| CP0002 | `M:CheatEngine.SDK.Engine.Inspection.AddressResolutionOptions.set_UseHostSymbolTable(System.Boolean)` | Public API (`CheatEngine.SDK.Engine.Inspection.AddressResolutionOptions`) | Client public break: replace the UseHostSymbolTable path by EngineInspection.ResolveHostAddress, add the `*REMOVED*` PublicAPI lines, and review every `new AddressResolutionOptions(true)`, which binds Shallow on 2.0. |

Suppressions listed: 7. Suppressions without Client exposure (counted, not listed): 2 of 9.

### Client canary

Status: placeholder — content arrives with DOCS-FINAL (V4c)
<!-- generated:sdk-breaks:end -->

## Adoption entries

Each entry states what the Client does on 1.0.0, the SDK 2.0 API it adopts, the Client change, the tests to rewrite, the
scenarios to requalify, and the audit rows it closes.

### F05 value scanning

- **Today on 1.0.0:** `Client.ValueScanning` is contract-only; its package gate is `Missing` because CheatEngine.SDK
  1.0.0 does not provide the public MemScan and FoundList ownership factory required by Client. `UnavailableValueScanner`
  keeps that exact text.
- **SDK 2.0 API:** `MemoryScanSessions` (on the SDK branch) with `MemoryScanCreationStatus`,
  `MemoryScanMaterializationStatus`, `MemoryScanInvalidationReason` and the appended `MemoryScanFailureKind` values.
- **Client change:** an adapter that delegates ownership to the SDK session; creation and materialization statuses map to
  classified failures; the session tracks the target selection and reports invalidation. Client DTOs never retain a
  `MemScan`, a `FoundList` or the result-file path as a reusable resource after disable (audit A13-19).
- **Tests to rewrite:** `UnavailableValueScannerTests`, `ValueScanSessionStateMachineTests`, the Q44 contract-only tests
  for value scanning, `ContractOnlyDomainsHaveNoOperationalImplementationWhileTheSdkMajorIsOne`.
- **Re-qualification:** Q25, Q26, Q30, Q44.
- **Audit:** A10-04, A13-18, A13-19, A09-19, F05.

### F06 AOB outcomes

- **Today on 1.0.0:** a missing result list is `IndeterminateHostResult` ("zero matches or a host failure"), because
  `AobScanner.TryScan` cannot tell them apart.
- **SDK 2.0 API:** `AobScanner.TryScanOutcome` with `AobScanOutcome` and `AobScanOutcomeKind` (on the SDK branch).
- **Client change:** map `Matches` → success, `NoMatches` → successful empty result, `NoResult` → stays a factual
  "no result list" outcome (spike C3 D1: Cheat Engine 7.7 returns no list for zero matches), `GlobalUnavailable` →
  `CapabilityUnavailable`, `ProtectedLuaFailure` → `LuaError`, `InvalidResult` and `ResultListCountUnavailable` →
  `InvalidHostResult`, `Unknown` → `Unknown`; retire `IndeterminateHostResult` for AOB; never classify by error text.
- **Tests to rewrite:** `PatternScannerBehaviorTests.MissingAobResultListIsAmbiguousAndNotNotFound`, the AOB rows of
  `SdkMappingContractTests`.
- **Re-qualification:** Q27.
- **Audit:** A10-14, A13-10, F06.

### F07 bounded AOB

- **Today on 1.0.0:** one global `AOBScan`; module and range are managed post-filters
  (`PatternScanScope.GlobalHostScanWithManagedFilter`).
- **SDK 2.0 API:** the SDK's host-range-bounded AOB route (planned, S-SCAN).
- **Client change:** add `PatternScanScope.HostRangeBounded` and switch module and range requests to the bounded route;
  keep the start address as a post-filter where the SDK scans from an aligned start (spike C3 D4). `RequireSingle` never
  uses the bounded route's `OnlyOneResult` nor a first-match opt-in: uniqueness needs the full count.
- **Tests to rewrite:** `PatternScannerBehaviorTests`, `PatternScannerCoverageTests`, the materialization benchmark.
- **Re-qualification:** Q28, Q29.
- **Audit:** A10-13, F07.

### F13 owner release status

- **Today on 1.0.0:** the only "release not confirmed" signal is an exception from the SDK owner, reported as
  `InvalidState` with `CheatEngineHostEffect.CleanupUnconfirmed`.
- **SDK 2.0 API:** a structured, exception-free release outcome of `Owned<T>` (planned, S-RES).
- **Client change:** report `CleanupUnconfirmed` from the release outcome without catching an exception.
- **Tests to rewrite:** the AOB release tests of `PatternScannerBehaviorTests`, `OwnershipHandoffTests`.
- **Re-qualification:** Q27 (release path).
- **Audit:** F13.

### F08 runtime facts

- **Today on 1.0.0:** `ClientLuaGlobals` binds `getOpenedProcessID`, `targetIs64Bit`, `targetIsX86`, `targetIsArm`,
  `getPointerSize`, `getCEVersion`, `getSystemArchitecture` and `getABI`; `TargetArchitectureObserver` derives the ISA
  PID-first from the family facts; the configured pointer size uses the literal capability id
  `"Runtime.ConfiguredPointerSize"`.
- **SDK 2.0 API:** `RuntimeHostOperations` and `RuntimeProcessOperations.ObserveCurrent` (on the SDK branch);
  `RuntimeProcessOperations.ObserveTargetArchitecture` with `TargetArchitectureObservation` (`Bitness`, `IsX86Family`,
  `IsArmFamily`, `ConfiguredPointerSizeBytes`, `ConfiguredPointerSize`, `ConfiguredPointerSizeDiffersFromBitness`),
  `RuntimeProcessOperations.TryGetConfiguredPointerSize`, `RuntimeObservations.TryObserveRuntimeInfo` and
  `RuntimeCapabilityId.ConfiguredPointerSize` (planned, S-RT); `PointerSize.FromArchitecture` becomes obsolete as
  `CESDK7001` (planned, S-RT).
- **Client change:** replace the eight bindings and the observer by the SDK observation; replace the capability-id
  literal by the SDK static; keep the Client's private natural-width helpers (the Client already avoids
  `FromArchitecture` in product code).
- **Tests to rewrite:** `TargetArchitectureObserverTests`, `RuntimeClientTests`, `LuaRuntimeProbeTests`,
  `ProcessClientTests`, the ratchet lists of `ArchitectureRatchetTests`.
- **Re-qualification:** Q31, Q32.
- **Audit:** F08, A23-F08-2.

### Pointer primitives

- **Today on 1.0.0:** Client pointer paths use the process width and are refused on a configured/process width
  mismatch (`PointerWidthPolicy`); a 32-bit pointer chain refuses intermediate addresses above 4 GiB.
- **SDK 2.0 API:** `TargetMemory.TryReadPointer(Address, PointerSize, out Address, out MemoryAccessFailure)` and
  `TargetMemory.TryWritePointer(Address, Address, PointerSize, out MemoryAccessFailure)`, with
  `MemoryAccessFailure.PointerWidthUnknown` and `PointerValueExceedsTargetWidth` (on the SDK branch).
- **Client change:** pass the observed process width to the width-checked primitives and map the two new failures; keep
  the mismatch refusal until the SDK documents what the configured size affects (the remainder of CLI-MEM-1).
- **Tests to rewrite:** `MemoryPointerWidthTests`, `DefaultMemoryCodecsTests`, `MemoryClientTests`.
- **Re-qualification:** Q21, Q31.
- **Audit:** A10-18, A12-24 (SDK half).

### Target identity

- **Today on 1.0.0:** `openProcess` then a PID-bracketed observation; identifier reuse, CEServer and file-as-process
  backends are not detected.
- **SDK 2.0 API:** `RuntimeProcessOperations.SelectAndObserve`, `TargetSelection` and `TargetSelectionObservation` (on
  the SDK branch); `TargetBackend` with CEServer and file-as-process refusal of local incarnation evidence (planned, S-RT).
- **Client change:** attach through `SelectAndObserve`; bind target-bound leases to the SDK selection evidence instead of
  the Client selection epoch alone; expose the backend; refuse local metadata for non-local backends.
- **Tests to rewrite:** `ProcessClientTests`, `ProcessSnapshotTests`, `LocalProcessHostTests`.
- **Re-qualification:** Q30.
- **Audit:** A12-01, A12-06.

### Allocations

- **Today on 1.0.0:** `Client.Allocations` is contract-only; CheatEngine.SDK 1.0.0 provides no target-bound owned
  allocation primitive.
- **SDK 2.0 API:** `TargetMemoryAllocator.TryAllocate(TargetAllocationRequest, out AllocatedRegion?)` with an acquire
  outcome that reports `NotInvoked` and unconfirmed compensation (planned, S-RES); `AllocatedRegion` (on the SDK branch).
- **Client change:** an adapter over the SDK owner, never a Client-made owner; an allocation is never freed on a new
  target; a published region is released only through its SDK owner.
- **Tests to rewrite:** `UnavailableAdvancedClientsTests` (allocation rows),
  `ContractOnlyDomainsHaveNoOperationalImplementationWhileTheSdkMajorIsOne`, the allocation lock of `CapabilityRatchetTests`.
- **Re-qualification:** Q30.
- **Audit:** A10-07, A12-19.

### Assembly and Auto Assembler

- **Today on 1.0.0:** `Client.Assembly` is contract-only; CheatEngine.SDK 1.0.0 provides no target-bound owned Auto
  Assembler primitive.
- **SDK 2.0 API:** an Auto Assembler apply outcome with an effect state and a disable token (planned, S-RES).
- **Client change:** a patch operation with a readable result and a lifetime. The Client never rebuilds a `[DISABLE]`
  script from saved bytes when Cheat Engine provides the disable information (audit A15-08). Before the first Client
  release, `AssemblyInstructionRequest` (address and instruction only) needs a destination buffer, an assemble
  preference and a skip-range-check option, or an explicit statement that it does not offer them (audit A15-05).
- **Tests to rewrite:** `UnavailableAdvancedClientsTests` (assembly rows), the assembly lock of `CapabilityRatchetTests`.
- **Re-qualification:** Q35.
- **Audit:** A10-07, A15-05, A15-08.

### Memory-record activation and tables

- **Today on 1.0.0:** `SdkTableRecordMutationPort` reads `Active` and `AsyncProcessing` through `CEObject` properties
  before and after one setter call; a trusted load advances the Client table generation; the parent read uses raw Lua.
- **SDK 2.0 API:** an activation outcome for a record identifier (`Applied`, `Unchanged`, `RefusedByHost`, `Pending`,
  `Indeterminate`, `NotAttempted`) and `MemoryRecord.TryGetActive` / `TryGetAsyncProcessing` (planned, S-RES);
  `CheatTableFiles.TryLoad(string, bool)` and `CheatTableFiles.TrySave(string)` (on the SDK branch) with a
  load-in-progress refusal and a tri-state parent read (planned, S-RES).
- **Client change:** the SDK outcome replaces `TableRecordActivation`; `SdkTableFilePort` calls `CheatTableFiles`; the
  raw parent read and the `CEObject` property calls leave the Lua-usage ratchet. The table-generation rule stays: the SDK
  does not promise that a record identifier survives a load.
- **Tests to rewrite:** `TableClientMutationTests`, `TableClientGenerationTests`, `SdkTableRecordMutationPortTests`.
- **Re-qualification:** Q34, Q35.
- **Audit:** A14-01, A14-05, A14-12.

### Symbols

- **Today on 1.0.0:** `InspectionClient` resolves the name before `registerSymbol` and the lease resolves it again
  before `unregisterSymbol` (`SymbolLeaseReleaseKind`).
- **SDK 2.0 API:** `SymbolRegistry.TryRegisterOwned` → `SymbolRegistrationLease` and `SymbolRegistry.TryGetName` (on the
  SDK branch); release kinds `Released`, `AlreadyReleased`, `Replaced`, `ExternallyRemoved`, `CleanupUnavailable` and
  the `SymbolList` owner (planned, S-RES).
- **Client change:** the SDK lease replaces the Client preflight and release logic; the public
  `SymbolLeaseReleaseKind` maps one to one.
- **Tests to rewrite:** `InspectionClientBehaviorTests`, `SymbolRegistrationLeaseTests`,
  `SymbolRegistrationLeaseCleanupCoverageTests`.
- **Re-qualification:** Q16.
- **Audit:** A14-25, A14-39.

### Lua module generator

- **Today on 1.0.0:** the Client generator registers module functions with the legacy SDK registration pair, with the
  1.0.0 collision fix of the Client generator.
- **SDK 2.0 API:** `LuaRegistrationSet` and `LuaRegistrationLease` (on the SDK branch); the legacy pair becomes
  `[Obsolete]` as `CESDK1006` (planned, S-REG).
- **Client change:** the generator moves to registration leases in the same lot as the bump, otherwise the obsolete
  legacy pair breaks every consumer that treats warnings as errors inside generated code; the 1.0.0 collision fix is
  removed. The final generated shape is owned by the Client generator work (CLI-REG-3).
- **Tests to rewrite:** the generator snapshot and collision tests, the coexistence fixture.
- **Re-qualification:** Q15, Q16.
- **Audit:** A19-16, F12.

### Timers and hotkeys

- **Today on 1.0.0:** `Client.Timers` and `Client.Hotkeys` are contract-only.
- **SDK 2.0 API:** timer and hotkey owners (planned, S-EVT).
- **Client change:** activation-scoped timers and hotkeys built on the SDK owners. Review `ITimerClient` (recurring
  timers only) for a one-shot lifetime and `HotkeyEvent` (name, gesture, time) for down, up and repeat before the first
  Client release. The Client never changes Cheat Engine's global hotkey settings, and a `CancellationToken` never cancels
  a callback that has started.
- **Tests to rewrite:** `UnavailableAdvancedClientsTests` (timer and hotkey rows).
- **Re-qualification:** Q36, Q37.
- **Audit:** A16-09, A16-13.

### Unsafe Lua

- **Today on 1.0.0:** `UnsafeLuaClient` executes the chunk with `LuaState.TryExecute` and reads the error with
  `LuaError.FromStack` (frozen raw Lua usage).
- **SDK 2.0 API:** the SDK's protected chunk-execution outcome (name frozen by DOCS-FINAL).
- **Client change:** execute through the SDK outcome; the raw usage leaves the ratchet.
- **Tests to rewrite:** `UnsafeLuaClientTests`.
- **Re-qualification:** Q48 (consumer contract).
- **Audit:** A11-06.

### Exceptions and enums

- **Today on 1.0.0:** `CoreFailureFactory` maps the 1.0.0 `EngineException` subclasses and status values;
  `SdkMappingContractTests` prove the mapping is total.
- **SDK 2.0 API:** appended `EngineFailureKind` values (`TargetIdentityUnavailable`, `TargetIdentityMismatch`) and
  `MemoryScanFailureKind` values, new `EngineException` subclasses, and SDK-owned status enums whose zero value is
  `Unknown` or `Unspecified` (on the SDK branch).
- **Client change:** map every new value and subclass; treat the zero value as unknown, never as success.
- **Tests to rewrite:** `SdkMappingContractTests` (the oracle), `CoreFailureFactoryTests`.
- **Re-qualification:** Q48.
- **Audit:** A11-19.

### AddressResolutionOptions

- **Today on 1.0.0:** `AddressResolutionOptions(bool UseHostSymbolTable = false, bool Shallow = false)` appears in the
  public `IInspectionClient` signatures.
- **SDK 2.0 API:** `AddressResolutionOptions(bool Shallow = false)`; host-table resolution moves to
  `EngineInspection.ResolveHostAddress` returning a `HostAddress` (on the SDK branch).
- **Client change:** a Client **public** break: remove or replace the `UseHostSymbolTable` path in the public
  signatures, add `*REMOVED*` lines to `PublicAPI.Unshipped.txt`, and review every `new AddressResolutionOptions(true)`:
  it still compiles on 2.0 but binds `Shallow`.
- **Tests to rewrite:** `InspectionClientBehaviorTests`, `PublicClientSignatureBoundaryTests`.
- **Re-qualification:** Q48.
- **Audit:** A11-19.

### Diagnostics and package identity

- **Today on 1.0.0:** the Core assembly embeds the consumed identity (version, source commit, content hash) and compares
  it with the loaded `CheatEngine.SDK.Engine` informational version; Hosting logs it once per enable (event 20).
- **SDK 2.0 API:** the 2.x informational version and the SDK's own identification diagnostic at enable (planned,
  S-HOST).
- **Client change:** none in the embedding: the Core build takes the 2.0.0 identity from the regenerated lock file and
  the restored package's nuspec (`CHEATENGINECLIENT9050` refuses a build that cannot embed it); the package gate and
  event 20 follow it unchanged; the Client event and the SDK event must not both log paths.
- **Tests to rewrite:** `ConsumedSdkIdentityEmbeddingTests`, the package-gate tests of `RuntimeClientTests`,
  `SdkPinTests`.
- **Re-qualification:** Q40, Q48.
- **Audit:** A10-19, A21-20.

## ADR-01 frozen exceptions to remove

Each Client binding of a Cheat Engine Lua global is a registered ADR-01 exception, frozen by
`LuaGlobalBindingsAreFrozenToTheRegisteredAdr01Exceptions` in `tests/CheatEngine.Client.Tests/Architecture`. Removing a
row here and its entry there is the removal test: the frozen list only shrinks, and a new binding without a row here
fails `EveryClientLuaGlobalsBindingHasARemovalEntryInTheMigrationGuide`.

| Lua global | SDK 2.0 replacement | Status |
|---|---|---|
| `getOpenedProcessID` | `RuntimeProcessOperations.ObserveCurrent` (identifier) and `ObserveTargetArchitecture` | on the SDK branch / planned (S-RT) |
| `openProcess` | `RuntimeProcessOperations.SelectAndObserve` | on the SDK branch |
| `getCEVersion` | `RuntimeHostOperations.TryGetCheatEngineVersion`, then `RuntimeObservations.TryObserveRuntimeInfo` | on the SDK branch / planned (S-RT) |
| `getSystemArchitecture` | `RuntimeHostOperations.TryGetSystemArchitecture` | on the SDK branch |
| `getABI` | `RuntimeHostOperations.TryGetTargetAbi` | on the SDK branch |
| `targetIs64Bit` | `TargetArchitectureObservation.Bitness` | planned (S-RT) |
| `targetIsX86` | `TargetArchitectureObservation.IsX86Family` | planned (S-RT) |
| `targetIsArm` | `TargetArchitectureObservation.IsArmFamily` | planned (S-RT) |
| `getPointerSize` | `RuntimeProcessOperations.TryGetConfiguredPointerSize` / `TargetArchitectureObservation.ConfiguredPointerSize` | planned (S-RT) |
| `loadTable` | `CheatTableFiles.TryLoad(string, bool)` | on the SDK branch |
| `saveTable` | `CheatTableFiles.TrySave(string)` | on the SDK branch |
| `getNameFromAddress` | `SymbolRegistry.TryGetName(Address, out string?)` | on the SDK branch |
| `registerSymbol` | `SymbolRegistry.TryRegisterOwned` → `SymbolRegistrationLease` | on the SDK branch |
| `unregisterSymbol` | release of the `SymbolRegistrationLease` | on the SDK branch; release kinds planned (S-RES) |

The raw Lua and owner usages frozen by `DirectLuaStateUsageIsLimitedToTheFrozenAllowlist` leave with their adoption
entry:

| Client usage | Leaves with |
|---|---|
| `SdkTableRecordMutationPort` parent read (`MemoryRecord.TryRead` on a `LuaState`, `LuaFrame`, `LuaState.IsNil`, `LuaRuntime.AcquireOperation`) | the tri-state parent read (planned, S-RES) |
| `SdkTableRecordMutationPort` and `TableClient` handle property calls (`CEObject.TryGetProperty`, `TrySetProperty`, `TryCallMethod` for `Active`, `AsyncProcessing` and selection) | the activation outcome and `MemoryRecord` getters (planned, S-RES) |
| `UnsafeLuaClient` chunk execution (`LuaState.TryExecute`, `LuaError.FromStack`, `LuaFrame`, `LuaRuntime.AcquireOperation`) | the protected chunk-execution outcome |
| `SdkAobScanPort` result-list owner (`Owned<StringList>`, `StringList.TryGetCount`, `TryGetItem`) | `AobScanner.TryScanOutcome` (on the SDK branch) |
| `ClientLuaGlobals` generated call support and marshallers | the removal of the bindings above |
| Collision fix of the Client Lua generator for SDK 1.0.0 (Q16) | the generator move to registration leases |

## Re-qualification list

With the 2.0 tuple (new package, native bridge and consumed identity), these scenarios are re-run; a result on the 1.0.0
tuple never transfers without an argued justification.

| Scenario | What changes with 2.0 | Levels to re-run |
|---|---|---|
| Q10 | Two package versions in separate folders: a 1.x and a 2.x plugin in one process | C4 |
| Q16 | Generated module registration moves to SDK registration leases; symbol leases move to the SDK | C2, C4 |
| Q21 | Width-checked pointer primitives and the codec matrix | C1, C2, C3 |
| Q25 | Value-scan creation through the SDK factory, including a failed list publication | C1, C3 |
| Q26 | First scan, next scan, results and destruction through the SDK session | C3 |
| Q27 | AOB outcome kinds instead of the 1.0.0 indeterminate result | C1, C3 |
| Q28 | Bounded AOB route with matches outside the module | C1, C3 |
| Q29 | Result limit and cancellation during the copy on the bounded route | C1, C3 |
| Q30 | Target identity evidence, PID reuse, allocations never freed on a new target | C3, C4 |
| Q31 | Configured pointer size read through the SDK observation | C3 |
| Q32 | Target ISA from the SDK observation, CEServer and file-as-process backends | C1, C3 |
| Q34 | Record identifiers across table loads with the SDK table files | C3 |
| Q35 | Record and script activation outcomes; Auto Assembler rollback | C3 |
| Q36 | One-shot timer lifetime on the SDK owner | C3 |
| Q37 | Hotkey callback during module shutdown on the SDK owner | C3 |
| Q40 | Clean installation from the 2.x packages | C3 |
| Q48 | The Client compiled and run against the 2.x package: consumer contracts and the break list | C1, C3 |

## Still contract-only after 2.0

No SDK 2.0 work item provides a qualified primitive for these capabilities, so they stay contract-only
(`Unavailable`) after the migration until a qualified increment adds one (audit ADR-11, A17-21):

| Capability | Reason |
|---|---|
| `Client.Debugger` | No SDK wave covers breakpoints and debug events for the Client. |
| `Client.Speed` | No SDK wave covers speed control. |
| `Client.Hashing` | No SDK wave covers target-memory and file hashing. |
| `Client.Dbvm` | No SDK wave covers DBVM observation or control. |
| `Client.RemoteExecution` | No SDK wave covers injection and remote calls. |

## Mixing SDK 1.x and 2.x plugins in one Cheat Engine process

Loading a plugin built on CheatEngine.SDK 1.x and another built on CheatEngine.SDK 2.x into the same Cheat Engine process
is **not qualified**. They share Cheat Engine's Lua state and globals, and each SDK version carries its own static host
state. Treat the combination as unsupported unless a committed Q10 C4 receipt of the SDK repository says otherwise; none
exists today.

## Traceability

| Audit row | Where this page answers it |
|---|---|
| A09-19 | Adoption entries (Checkpoint C integrations stay out of the 1.0.0 Client) |
| A10-04 | F05 value scanning |
| A10-07 | Allocations; Assembly and Auto Assembler |
| A10-13 | F07 bounded AOB |
| A10-14 | F06 AOB outcomes |
| A10-19 | Same-lot adoption checklist; Diagnostics and package identity |
| A11-06 | ADR-01 frozen exceptions to remove (raw usage removal list) |
| A11-19 | Induced Client breaks (generated); AddressResolutionOptions |
| A12-19 | Allocations |
| A13-10 | F06 AOB outcomes |
| A13-18 | F05 value scanning |
| A13-19 | F05 value scanning (DTO constraint) |
| A15-05 | Assembly and Auto Assembler (`AssemblyInstructionRequest` review) |
| A15-08 | Assembly and Auto Assembler (no `[DISABLE]` rebuild) |
| A16-09 | Timers and hotkeys (`ITimerClient` review) |
| A16-13 | Timers and hotkeys (`HotkeyEvent` review, no global settings change) |
| A17-21 | Still contract-only after 2.0 |
| A19-16 | Lua module generator |
| A21-20 | Same-lot adoption checklist |
| ADR-11b | Still contract-only after 2.0 |
| PR-SEQ-21 | Scope and status (DOCS-FINAL freezes names and the break list) |
