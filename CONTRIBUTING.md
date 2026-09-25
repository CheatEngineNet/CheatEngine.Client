# Contributing to CheatEngine.Client

Thanks for contributing. CheatEngine.Client is a Windows x64, .NET 10 layer for in-process Cheat Engine 7.7 plugins,
built on the `CheatEngine.SDK` package. Keep changes focused, preserve existing patterns, and update the documentation
and tests that describe the behavior you change.

## Prerequisites

- Windows x64
- .NET SDK 10.0.401 exactly, as pinned in [`global.json`](global.json). `rollForward` is `disable`, so any other
  SDK, a newer one included, stops the build with the install command:
  `winget install Microsoft.DotNet.SDK.10 --version 10.0.401`
- PowerShell 7.4 or later, to run the CI checks locally (actionlint, zizmor, the lock-file check)
- Git

## Build and test

Run these commands from the repository root before every commit. Restores are locked: they fail when a project's
dependencies no longer match its committed `packages.lock.json`.

```powershell
dotnet restore CheatEngine.Client.slnx --locked-mode
dotnet build CheatEngine.Client.slnx -c Debug --no-restore
dotnet test --solution CheatEngine.Client.slnx -c Debug --no-build --fail-skips on --filter-not-trait "Category=PackageConsumption" --filter-not-trait "Category=LiveQualification"
dotnet build CheatEngine.Client.slnx -c Release --no-restore
```

The test command is the one of the CI Debug leg: it runs every test except the package consumption tests
(`Category=PackageConsumption`) and the live qualification tests (`Category=LiveQualification`); the Release build
compiles the configuration that is packed. A skipped test fails the run: an environment-dependent test is fixed or
deleted, never skipped. The live qualification tests start a sandboxed Cheat Engine, so every command here, like CI,
excludes them by trait; run without that filter, they fail with the instructions of their opt-in (see
[Live qualification](#live-qualification)).

The package consumption tests consume the exact packages you pack, as the CI Release leg does. Run them as well when a
change reaches what is packed: a project file, `eng/`, `Directory.Build.*`, `Directory.Packages.props`, `templates/`,
a packed README, or a public API that the template or a README snippet uses. The Native AOT probe completes the set:

```powershell
Remove-Item artifacts/nuget -Recurse -Force -ErrorAction SilentlyContinue
dotnet pack CheatEngine.Client.slnx -c Release --no-build -o artifacts/nuget
$env:CHEATENGINE_CLIENT_PACKAGE_SOURCE = (Resolve-Path artifacts/nuget).Path
dotnet test --solution CheatEngine.Client.slnx -c Release --no-build --fail-skips on --filter-not-trait "Category=LiveQualification"
Remove-Item Env:CHEATENGINE_CLIENT_PACKAGE_SOURCE
dotnet publish tests/CheatEngine.Client.AotProbe/CheatEngine.Client.AotProbe.csproj -c Release --no-restore -o artifacts/aot-probe
./artifacts/aot-probe/CheatEngine.Client.AotProbe.exe
```

Without `CHEATENGINE_CLIENT_PACKAGE_SOURCE`, the package consumption tests pack the repository themselves, which is
slower.

They include the template smoke tests: they install the packed `CheatEngine.Client.Templates` package in an isolated
template home, run `dotnet new ceplugin --dry-run`, instantiate the template into a temporary directory outside the
repository, restore it against the packed Client packages and nuget.org only, and build it in Release configuration.
They check that the generated project references the co-packed `CheatEngine.Client` version and the pinned
`CheatEngine.SDK` directly, and that the build output holds the complete deployment closure next to the plugin. To run
only the project that holds them, after the pack above:

```powershell
$env:CHEATENGINE_CLIENT_PACKAGE_SOURCE = (Resolve-Path artifacts/nuget).Path
dotnet test --project tests/CheatEngine.Client.Tests/CheatEngine.Client.Tests.csproj -c Release --no-build --fail-skips on --filter-not-trait "Category=LiveQualification"
Remove-Item Env:CHEATENGINE_CLIENT_PACKAGE_SOURCE
```

### Lock files

Never edit a `packages.lock.json` by hand, and never let an IDE restore rewrite them. After a dependency change,
regenerate the affected projects one at a time, on Windows, with the SDK of `global.json`, no IDE open on the working
tree: `dotnet restore <project> --force-evaluate`. Restore the three live-plugin coexistence fixtures first if they are
affected: they stay outside Central Package Management and keep version 1 lock files, and a solution-level
`--force-evaluate` restore rewrites them into the Central Package Management shape. Never run
`dotnet restore CheatEngine.Client.slnx --force-evaluate` for that reason.

Commit the regenerated files on their own, as `Regenerate lock files after <reason>`. On a rebase conflict in a lock
file, take either side and regenerate it again; never merge a lock file by hand. The `Lock files` CI job runs
`dotnet restore CheatEngine.Client.slnx --locked-mode`: it is the real guard, because `--locked-mode` fails outright
when a committed `packages.lock.json` no longer matches its project graph, and does not notice a hand-edited resolved
version.

### The consumed CheatEngine.SDK

The Client consumes exactly one `CheatEngine.SDK` package, pinned in the single reviewed source
[`eng/CheatEngineSdk.props`](eng/CheatEngineSdk.props): never write an SDK version literal anywhere else. There is no
script for the bump; the props file's own header documents the procedure, all of it in one pull request: update
`CheatEngineSdkVersion`, update the reviewed identity literals it names
(`tests/CheatEngine.Client.Tests/Packaging/PackagedClientFeedFixture.cs`,
`tests/CheatEngine.Client.Repository.Tests/LockFiles/LockFileTests.cs`) and the identity the three install guides state
to the new package's hashes, regenerate every `packages.lock.json` (coexistence fixtures first, one project at a time),
and update the SDK version named in prose (`SdkPinTests.ProseMentionsOfTheConsumedSdkEqualThePin` lists the files).
`CHEATENGINECLIENT9016`, `9017` and `CECLIENT017` guard the pin at build and consumption time. Moving to another SDK
major is a migration of the Client, not a dependency bump: the props file's "Major migration" checklist lists what else
it changes.

### NuGet audit and build guards

NuGet audits every package, direct and transitive, from the `low` severity up:

- high (`NU1903`) and critical (`NU1904`) advisories fail every restore and build;
- low and moderate advisories (`NU1901`, `NU1902`), an unavailable audit source (`NU1900`) and `NU1905` stay warnings in
  ordinary builds;
- `-p:AuditPipeline=true` turns every audit code into an error, for a stricter local or ad hoc CI run.

To accept one advisory, add `<NuGetAuditSuppress Include="<advisory URL>"/>` with a comment that gives the
justification and an expiry date. Never suppress an advisory on the release path. Never lower the policy itself:
`CHEATENGINECLIENT9030` refuses an analysis level other than the pinned `10.0-recommended`, `CHEATENGINECLIENT9031`
refuses a weakened audit (off, a mode other than `all`, a level other than `low`, or `NU1903`/`NU1904` in `NoWarn` or
`WarningsNotAsErrors`), and `CHEATENGINECLIENT9032` refuses a project without a lock file or outside Central Package
Management (the coexistence fixtures excepted).

## Continuous integration

Pull requests run `Pull request CI`, pushes to `main` run `Main CI`, and version tags run `Release`; all three call the
reusable `ci.yml` through a job named `CI`. There is no merge queue. One check is required on a pull request, and no
other:

- `CI / Gate` requires every other `ci.yml` job to succeed. The only exception is `Sonar`: it must succeed when the
  analysis is expected (`SONAR_EXPECTED`: a pull request from this repository, a push to `main`, a manual run) and must be
  skipped otherwise (pull requests from forks or Dependabot, which receive no secrets, and the release run). A Sonar run
  that was not expected fails the Gate too: it means that the job condition and the Gate's copy of it drifted apart. The
  Gate's job summary lists every job with its result, the required result and the reason.

Drafts do not run CI until they are marked ready for review, so `CI / Gate` stays pending. CodeRabbit's automatic
review still checks the pull request title and the changelog rule of [Pull request conventions](#pull-request-conventions)
on every push, including on drafts, but it is advisory: it never blocks a merge (see [`.coderabbit.yaml`](.coderabbit.yaml)).

| Check (`CI / ...`)             | Runner         | What it does                                                                                                                    |
|--------------------------------|----------------|-----------------------------------------------------------------------------------------------------------------------------------|
| `Build and test (Debug)`       | `windows-2025` | Locked restore, build, one `dotnet test --solution` run with coverage (package consumption and live qualification tests excluded by trait) |
| `Build and test (Release)`     | `windows-2025` | Locked restore, build, pack (exact package set, embedded SBOM), benchmark discovery, one test run against the packed packages (live qualification tests excluded by trait) |
| `Native AOT publication probe` | `windows-2025` | Publishes and runs `tests/CheatEngine.Client.AotProbe`: trim and Native AOT compatibility of the Client graph, not a Cheat Engine load |
| `Sonar / Analyze`              | `windows-2025` | SonarQube Cloud CI-based analysis with the Debug coverage; waits for the quality gate except on pushes to `main`                  |
| `Lint`                         | `ubuntu-24.04` | actionlint and the offline zizmor audits over the workflows                                                                       |
| `Format`                       | `ubuntu-24.04` | `dotnet format whitespace . --folder --verify-no-changes --exclude artifacts`                                                     |
| `Dependency review`            | `ubuntu-24.04` | On pull requests, reviews dependency changes against `.github/dependency-review-config.yml`; a notice on other events             |
| `Lock files`                   | `windows-2025` | `dotnet restore CheatEngine.Client.slnx --locked-mode`                                                                            |
| `Gate`                         | `ubuntu-24.04` | The required check described above                                                                                                |

A test module that shows no activity for 15 minutes is dumped and fails; on failure, `build-test` uploads the hang and
crash dumps and the binary logs. No CI job uses a NuGet cache, because the release run reaches every job, and every job
that runs `dotnet` goes through `.github/actions/setup-dotnet`, which restores with `--locked-mode`. SonarQube Cloud runs
as CI-based analysis only; Automatic Analysis stays off for the project, otherwise the scanner fails at `begin`.

`WorkflowContractTests` (in `tests/CheatEngine.Client.Repository.Tests/Workflows`) freezes these rules. It fails on a
`ci.yml` job missing from the Gate's `needs`, a Sonar condition that differs from the Gate's `SONAR_EXPECTED`, an action
not pinned to a full commit SHA with a version comment, a checkout that keeps credentials, a runner label other than
`windows-2025` or `ubuntu-24.04`, a job without a timeout, a package cache on a workflow the release reaches, a
`pull_request_target` or `merge_group` trigger, a path filter on the required workflows, or an artifact name outside the
reserved list.

These workflows are advisory, never required: CodeQL, OpenSSF Scorecard, the online zizmor audits and the NuGet
dependency snapshot submission (`Dependency submission`, which runs the GitHub Component Detection action against the
locked restore).

### Run the CI checks locally

The build already enforces formatting, compiler and analyzer rules. The remaining checks run from the repository root;
actionlint 1.7.12 and zizmor 1.30.1 are the versions CI pins:

```powershell
dotnet format whitespace . --folder --verify-no-changes --exclude artifacts
actionlint
zizmor --offline .github
dotnet restore CheatEngine.Client.slnx --locked-mode
```

### Runner labels

Jobs run on `windows-2025` and `ubuntu-24.04`, never on a moving `-latest` label, and Dependabot does not update
`runs-on`. To move to a new runner image, change the label in every workflow and in `PinnedRunners` of
`WorkflowContractTests`, in one pull request.

### Flaky tests

No required run retries a test, and `--fail-skips on` rules out skipping one. A flaky test is fixed or deleted in the
pull request that finds it; there is no scheduled job that re-runs threading-sensitive tests outside a normal CI run.

## Style and analyzers

- Follow [`.editorconfig`](.editorconfig): tab-indented C#, two-space project and configuration files. Builds treat
  formatting, compiler and analyzer diagnostics as errors; run `dotnet format` on the projects you touch. A commit that
  only reformats or mechanically renames code is listed in [`.git-blame-ignore-revs`](.git-blame-ignore-revs), and the
  pull request that contains it is merged with a merge commit ([Merge policy](#merge-policy)).
- Use file-scoped namespaces, explicit types instead of `var`, braces, explicit accessibility and `_camelCase` private
  fields. Constants and `static readonly` fields are PascalCase at every accessibility, and async methods end with
  `Async`. Test names are PascalCase sentences; async tests keep the `Async` suffix.
- Public APIs require XML documentation. A public API change in Abstractions, Fluent, Hosting or the DI extensions is
  declared in that project's `PublicAPI.Unshipped.txt` (RS0016/RS0017 are errors); Core has no public API and the
  `CheatEngine.Client` facade ships no assembly. `PublicAPI.Shipped.txt` changes only in a release pull request, which
  moves `Unshipped` to `Shipped` once, as its last API commit ([RELEASING](RELEASING.md#prepare-a-release)). 1.0.0 is
  the first release: its pull request (#59) cleared every `PublicAPI.Shipped.txt` when it started, so no `*REMOVED*`
  entry exists for an API that never shipped. From 1.0.0 on, the 1.x rules of the
  [README](README.md#versioning-and-compatibility) apply: a stable public API is never removed or changed before 2.0.
- An experimental API carries `[Experimental("CECLIENT500x")]`, whose `UrlFormat` points to its section of the
  Abstractions README, and its PublicAPI lines keep the `[CECLIENT500x]` prefix (`ClientExperimentalDiagnosticsTests`).
  It leaves experimental only when committed host evidence covers every scenario of its capability
  ([RELEASING](RELEASING.md#qualification-gate)).
- Core implementation types stay `internal sealed`, and every Cheat Engine interaction goes through the Client's
  dispatcher and ports; the SDK remains the only native authority.
- `InternalsVisibleTo` grants serve the package graph and this repository only: every project grants `<Project>.Tests`
  ([`Directory.Build.props`](Directory.Build.props)), Core grants Extensions.DependencyInjection, Hosting and
  `CheatEngine.Client.Benchmarks`, and Extensions.DependencyInjection grants Hosting and
  `CheatEngine.Client.Hosting.Tests`. The Client assemblies are not strong-named, so a grant names an assembly, not a
  signing key, and any assembly with that name sees the internals. Internal members are never a contract: they change
  in any release, and the Core and Extensions.DependencyInjection READMEs say so. Add a grant only for a Client package
  that composes another one, or for a test or benchmark project of this repository.
- The architecture ratchet in `tests/CheatEngine.Client.Tests/Architecture` freezes the Client's remaining ADR-01 debt:
  its direct Lua and owner usages. The Client binds no Lua global itself, and that list stays empty. Shrinking a list
  is always allowed. Growing it requires a registered exception, in the ratchet itself, with its reason and the name of
  the CheatEngine.SDK primitive that is missing (`AwaitingSdkPrimitive`, like the `MemoryRecord` child-count getter);
  an exception that names no missing SDK primitive is not accepted, and a new need goes to the CheatEngine.SDK
  repository first. The only permanent entries are the unsafe Lua opt-in's (`UnsafeLuaClient`). The typed SDK Lua API
  the Client uses is an exact, reasoned inventory of its own, and no Client code references or suppresses an
  `[Experimental]` SDK member (`CESDK5xxx`).

## Package READMEs and implementation notes

The README next to each packed project is its nuget.org page, written for plugin authors: installation, requirements,
what the package offers and its contracts, with absolute `https://` links only (`PackedReadmesContainNoRelativeLinks`).
What only a contributor needs lives here instead.

Every C# block of a packed README and of the root README is labelled `csharp` and is a whole file (usings, namespace,
types): `ReadmeSnippetCompilationTests` compiles the blocks of each README as one plugin project against the packed
Client, with warnings as errors, so run the package consumption tests when you change one. A block that cannot compile
on purpose is labelled `csharp nocompile`, and the line right above its opening fence says why:
`<!-- nocompile: the reason -->`. A C# block labelled `cs` or `c#` fails the test instead of escaping it.

### Changing a package

- **Abstractions:** every change is a public API change. Keep request and value types immutable, preserve the
  functional namespaces, add XML documentation, declare the change in `PublicAPI.Unshipped.txt` and add focused
  contract tests in `tests/CheatEngine.Client.Abstractions.Tests`.
- **Fluent:** add a fluent surface only when it preserves an existing explicit contract and has a bounded terminal
  operation. Never store a Cheat Engine resource in a builder, add a Core dependency or introduce an assembly-derived
  namespace; add behavior tests in `tests/CheatEngine.Client.Fluent.Tests` for every public member or terminal
  condition.
- **Core:** treat lifetime, dispatch and disposal changes as host-safety changes. Keep SDK handles internal, route new
  Cheat Engine work through the dispatcher, add a capability observation for an optional binding, and test target and
  activation invalidation and reverse-order cleanup in `tests/CheatEngine.Client.Core.Tests`. Core's public baseline is
  intentionally empty: a public type there needs an explicit product-surface decision. The ordinary test suite uses
  SDK-facing ports and fakes; it does not replace the opt-in live qualification.

### Core internals

The public behavior below is specified in the Abstractions README; this is how Core implements it.

#### Capabilities and gates

Every capability composes one operational adapter, so every implementation gate is satisfied, and
`CapabilityRatchetTests` keeps it that way on the supported SDK major (`_CheatEngineClientSupportedSdkMajor` in
`eng/CheatEngineSdk.props`). The value scans (`CECLIENT5001`) are sessions over CheatEngine.SDK's `MemoryScanSessions`
owners and the target allocations (`CECLIENT5002`) leases over its `TargetMemoryAllocator` regions, released on Cheat
Engine's main thread before the SDK detaches and never through another target.

Every capability is described once, in the internal `ClientCapabilityCatalog`: its implementation gate, where its
policy and host gates come from, and the live scenarios its qualification gate requires. `RuntimeClient` composes the
snapshot from that catalog, and `CapabilityDocumentationTests` keeps the capability tables of the READMEs in step with
it. The qualification gate comes from the internal `HostQualificationGate`: it is `Satisfied` only when the host
evidence this build embeds (`HostQualificationEvidence`, empty until a live run is recorded, and excluded from the
qualified source digest) names exactly the loaded CheatEngine.SDK package, which must be the reviewed one, Cheat Engine
7.7.0.10621 and the supported host profile, when the observed host is Cheat Engine 7.7.0.10621 64-bit on Windows with a
local target of an architecture the run covered, when the evidence names this Client version, and when every scenario
of the capability passed without a waiver; otherwise it is `Unknown` and names the first condition that does not hold.

The package gate compares the CheatEngine.SDK identity embedded in the Core assembly at build time (version, source
commit and NuGet content hash, as `AssemblyMetadata`, taken from the locked and restored package, and the supported
major of `eng/CheatEngineSdk.props`) with the informational version of the `CheatEngine.SDK.Engine` assembly actually
loaded; it reads assembly attributes only. Its reason says whether the loaded assembly is exactly the reviewed package,
a distinction the qualification gate needs (a receipt covers only the tuple it was produced with). A build that cannot
embed that identity fails with `CHEATENGINECLIENT9050`.

**Instructions** (`CECLIENT5003`): the internal `AssemblyClient` runs each call in one dispatched callback behind one
`LuaAdmission`, observes the instruction profile once (`InstructionProfiles.TryObserveCurrent`), refuses an address
wider than that profile before any instruction function of Cheat Engine is called, and passes the same profile to
`SdkInstructionPort` (`InstructionAssembler`, `InstructionDisassembler`, `InstructionNavigator` and the counted
`TargetMemory.TryReadBytes`). Assembly uses a 16-byte buffer bounded by `MemoryResourceLimits.MaximumReadBytes`, with
one retry at the exact length the SDK reports, and refuses an empty result; a disassembly reads its bytes from target
memory for the reported length, between two SDK target checks, and never parses the disassembler's byte column.
`InstructionMapping` maps every `InstructionOperationStatus` totally, and a step that follows an earlier instruction
call of the same Client call is never `NotStarted`.

**Auto Assembler patches** (`CECLIENT5004`): the internal `AutoAssemblerClient` is registered only by
`EnableAutoAssemblerPatches()`, which also sets `CoreClientPolicy.EnableAutoAssemblerPatches`, and it refuses every call
without that policy (`CapabilityUnavailable`, `NotStarted`, no dispatch). Its only Cheat Engine calls go through
`SdkAutoAssemblerPort` (`AutoAssemblerPatcher.TryApplyWithOutcome` and `TryCheck` with bounded options, behind
`LuaAdmission`); `AutoAssemblerMapping` maps every SDK outcome category totally. The applied patch is handed to
`AutoAssemblerPatchLease`, a target-bound `HostResourceLease` registered inside the same dispatched callback under the
selection of the process incarnation the SDK bound the patch to (`ITargetSelectionBinder`, as for allocations and value
scans), which releases through the SDK owner's `ReleaseWithTargetOutcome` and never rebuilds a `[DISABLE]` section; a
refused registration is reported by `LeaseRegistration`. That owner consumes its disable information on every release
status, so `AutoAssemblerMapping.ToReleaseOutcome` reports `NotInvoked` as `RefusedRuntimeChanged` (manual recovery)
instead of the shared retryable `CleanupUnavailable`.

#### Runtime facts, target selection and pointer width

Every runtime and target fact is a read-only CheatEngine.SDK call through `SdkRuntimeObservationPort`:
`RuntimeObservations.TryObserveRuntimeInfo` for the snapshot, `RuntimeHostOperations` for the host facts,
`RuntimeProcessOperations` (`ObserveCurrent`, `ObserveTargetArchitecture`, `TryGetConfiguredPointerSize`) for the target
and `TargetSelection` (`ObserveCurrent`, `ValidateCurrent`) for its identity; the architecture ratchet keeps the port
to that exact read-only list (Q45). The SDK reads the selected process identifier before and after the target facts and
reads none of them when no target, or a file opened as a process, is selected. The SDK reports every outcome as a
status, never through Lua error text, and `RuntimeObservationMapping` maps each status value explicitly. When the
aggregate snapshot cannot be produced, the Client reads the host facts on their own, and each fact alone when that
fails, and observes the target through `TargetArchitectureObserver`. A target fact that raises or is malformed narrows
the observation to the PID, the bitness and the configured pointer size, which it keeps only when two selected-PID
reads agree; the ISA, the backend and the ABI then stay `Unknown`.

The one call that changes Cheat Engine's selection is `RuntimeProcessOperations.SelectAndObserve`, behind
`SdkProcessSelectionPort`, and `ProcessClient.TryAttach` is its only caller (architecture ratchet). A refused attach
keeps the SDK status as its kind with an `Unknown` host effect, and the selection is observed again after a refusal, so
the epoch follows what Cheat Engine now selects. For a local process the selection identity is the PID and its
incarnation, the creation time CheatEngine.SDK observed together with the local backend; a known incarnation is checked
with `TargetSelection.ValidateCurrent`. `ProcessClient` advances the target-selection epoch, and ends the target-bound
leases of the earlier selection, when the PID changes, when the same PID denotes another incarnation, when a known
backend, ISA or process width changes to another known value, or when Cheat Engine reports no target or a file opened
as a process; a fact that is transiently unknown keeps the epoch and the last known value.

Every pointer read and write goes through the width-qualified `TargetMemory.TryReadPointer` and `TryWritePointer`
overloads with the observed bitness, and every `MemoryAccessFailure` reaches the caller through
`MemoryAccessFailureMapping`, value by value and never as text.

#### Address List records and symbols

Every trusted table load that reaches Cheat Engine advances the table generation of the activation inside the
dispatched load; each snapshot is judged by the generation read when it was copied. Table files load and save through
CheatEngine.SDK's `CheatTableFiles`, only after the `AllowedTableRoots` policy admitted the path, and `TableMapping`
classifies its `LuaOperationStatus` value by value; the Client binds no Cheat Engine global itself. Delete, parent
assignment and activation are CheatEngine.SDK `AddressListMutations` commands (`Delete`, `SetParent` with the Client's
explicit traversal limit of 4096 records, `SetActive`), also classified by `TableMapping`; the record is copied again
after a completed command and a failed copy is never merged with the command's result. Snapshots read the record
through CheatEngine.SDK's typed `MemoryRecord` getters (`TryGetActive`, `TryGetAsync`, `TryGetAsyncProcessing`,
`TryGetScript`, `TryGetOffsetCount`); the child count alone is still Cheat Engine's `Count` property, because the SDK
has no child-count getter and `TryGetChild` cannot tell a missing child from a failed read (`FrozenLuaUsage`,
`AwaitingSdkPrimitive`). `TryGetScript` cannot tell a record without a script from a failed read either; a getter that
tells them apart is awaited from the SDK. A failed `Create` is rolled back once through `AddressListMutations.Delete`.

Symbol registration refuses a name that already resolves (`EngineInspection.ResolveAddress`), then registers through
CheatEngine.SDK's ownership coordinator (`SymbolRegistry.TryRegisterOwned`) and registers the lease with the
activation in the same main-thread callback.

#### AOB scans

`PatternScanner` runs a request without a module or range as one global Cheat Engine `AOBScan`. A module and/or range
request resolves the module, builds the SDK's `AobScanBounds` (the module intersected with the range, whose inclusive
end becomes `End + pattern length`, checked and saturated) and, when the port's `TargetSelection.ObserveCurrent`
observation qualifies the target, runs the stable `AobScanner.TryScanWithinBounds` overload with a destination of
`min(MaximumResults + 1, ScanResourceLimits.MaximumPatternMatches)` addresses and the caller's token. An unqualified
target, or a session the SDK could not create or attach to one target, falls back to the global scan with the module
and range applied while copying; the fallback never reports a verified target identity. Both routes copy through one
scope predicate (`PatternScanner.IsInsideRequest`), copy at most `ScanResourceLimits.MaximumPatternMatches - 1`
addresses, and send the empty protection text for the default filter.
`tests/CheatEngine.Client.Benchmarks/AobRouteComparisonBenchmarks.cs` compares the Client cost of the two routes over a
fake port, and `PatternScannerMaterializationBenchmarks.cs` measures the copy cost alone; the Cheat Engine scan cost is
a live-host measurement.

The SDK owner of the result list is handed to the Client wrapper through `OwnershipHandoff`, so a failure between
acquisition and publication releases the Cheat Engine list exactly once; when that release is not confirmed, the typed
`OwnershipHandoffException` carries its kind and the scan fails with `CleanupUnconfirmed`. The scanner then releases
the list exactly once on every path, inside the dispatched callback, through the SDK owner's never-throwing
`ReleaseWithOutcome`, mapped with `SdkReleaseOutcomes`. The AOB port calls `AobScanner.TryScanOutcome` with its target
context, and `AobScanMapping` classifies every outcome from SDK outcome values only, never from Cheat Engine or Lua
error text.

#### Leases and release outcomes

Every Client lease derives from the internal `HostResourceLease`, which implements `ICheatEngineLease` once: the
release runs on Cheat Engine's main thread through the activation dispatcher, attempts are serialized and idempotent,
`Dispose` never throws, and each attempt is logged with its operation name, kind and effect only (event 1701). A lease
registers with the activation registry and, when it is bound to the selected target, with the target-selection lifetime
too. A complete outcome unregisters it; a retryable or incomplete one keeps it registered, and the activation drain
retries or reports it (Q43).

`SdkReleaseOutcomes` maps the CheatEngine.SDK release statuses totally (`TargetReleaseStatus`,
`SymbolRegistrationReleaseKind`; an unknown value is `Unknown` with an unknown effect) and combines the parts of one
lease by keeping the outcome that leaves the most to do. The Lua module lease (`Lua.Release`) maps the kind its module
reports with `LuaModuleReleaseMapping`; `LuaRegistrationReleaseKind` is mapped by the generated Lua registrar, which
owns the SDK registration lease (see the generator README). Every lease release is named `<Service>.Release`, and every
failure operation `<Service>.<Member>` after the public call that produced it (`OperationNameTests`).

#### SDK boundary and the Try contract

Every Client-internal CheatEngine.SDK call (ports, `TargetMemory`, `EngineInspection`, `AobScanner`, Address List
access and mutations, `CheatTableFiles`, protected Lua execution) runs behind `SdkBoundary`: an SDK exception becomes a
classified `CheatEngineFailure` by exception type and the SDK's own failure category, never by message text. Client
lifecycle exceptions are never translated, and an SDK fault observed after the activation ended is reported as
`CheatEngineActivationExpiredException`. Lua work that Core runs itself asks for its admission through `LuaAdmission`
(`LuaRuntime.TryAcquireOperationWithOutcome`), and a refusal is classified from the SDK's admission status. A
CheatEngine.SDK call that acquires its own admission (`AddressListMutations`, `CheatTableFiles`, `SymbolRegistry`,
`TargetMemory`, scans) raises a plain `InvalidOperationException` when it is refused, which `SdkBoundary` reports as
`OperationRejected` with `Unknown`, unless the activation ended or the SDK detected an external Lua state reset
(`RuntimeChanged`). Consumer-supplied code (dispatcher callbacks, codecs, typed Lua operations) is never wrapped.

## Evidence and qualification levels

State the qualification level of every validation you report: **C0** static contract, **C1** managed tests or test
doubles, **C2** native fixture, **C3** the exact Cheat Engine host with a loaded plugin, **C4** several components (two
plugins, a target switch). A C1 or C2 success is never presented as host qualification, and a Native AOT publication is
never a Cheat Engine load. CI runs static and managed tests only; the Cheat Engine 7.7 x64 live suite is opt-in and never
runs in CI.

### Live qualification

The live qualification tests (`tests/CheatEngine.Client.Tests/LiveQualification`, `Category=LiveQualification`) build
plugins from the packed Client packages, load them into a sandboxed copy of Cheat Engine 7.7.0.10621 x64 and drive
disposable gtutorial targets. They run only on a maintainer workstation that opts in, with the operator present, never
in CI (they fail when `CI=true`). The procedure, from the prerequisites and the opt-in to the protection of the user's
Cheat Engine state and the commands, is in
[`tests/CheatEngine.Client.Tests/README.md`](tests/CheatEngine.Client.Tests/README.md#live-qualification);
[RELEASING](RELEASING.md#qualification-gate) says which run a release needs. A result counts only as the committed,
redacted evidence of a run under `tests/CheatEngine.Client.Tests/LiveQualification/Evidence/`, and until that evidence
exists no document claims a host qualification (`QualificationEvidenceTests`).

## Branches and pull requests

1. Update your local `main`, then create a focused branch from it.
2. Make the smallest change that solves the problem, with its tests.
3. Run the relevant build, test and pack commands.
4. Open a pull request against `main`; do not push directly to the protected branch.
5. Record consumer-visible changes under `## [Unreleased]` in [`CHANGELOG.md`](CHANGELOG.md), in one of its four
   categories: **Added** (an extension), **Changed** (a semantic correction), **Security** (a hardened refusal) or
   **Deployment** (what is built, packed, pinned or published).

Fill in the pull request template: the problem, the resulting behavior, the validation commands and results with their
qualification level, the API and compatibility impact, and any remaining host-level limitation. Pull requests are
merged once the required checks pass, as the merge policy below says.

### Merge policy

- **Squash merge** by default: the pull request title becomes the commit subject on `main`, except for a pull request
  with a single commit, whose squash keeps that commit's subject ([Commits](#commits) gives it the same conventions).
- **Merge commit**, never a squash or a rebase, for a pull request that contains a commit listed in
  [`.git-blame-ignore-revs`](.git-blame-ignore-revs). A squash or a rebase would give that commit a new SHA on `main`,
  the listed SHA would no longer exist there, and `git blame` would stop ignoring the reformat. The 1.0.0 release pull
  request (#59) is merged this way. GitHub gives the merge commit its own subject (`Merge pull request #N from ...`)
  and repeats the pull request title in its message, so the title conventions below still apply.
- A branch whose commits `.git-blame-ignore-revs` lists is never rebased: later changes are new commits on top of it.

### Pull request conventions

No required check enforces these; CodeRabbit's automatic review checks them on every push and flags a miss, but it is
advisory and never blocks a merge. Follow them anyway, since the title becomes the subject of the squash commit on
`main`, or the message of the merge commit:

- **Title:** an imperative sentence (`Add`, `Fix`, `Keep`...) that starts with an uppercase letter, has at most 72
  characters, no Conventional Commit prefix such as `feat:` and no trailing period.
- **Changelog:** a change under `libs/`, `src/`, `source-generators/` or `templates/` (lock files excepted) needs an entry
  under `## [Unreleased]` in `CHANGELOG.md`. When nothing consumer-visible changes, put the line
  `<!-- changelog: not-needed -->` on its own line in the description instead.
- **Dependabot:** its pull requests are exempt. Rename their `chore(deps): ...` squash subject to an imperative sentence
  when merging.

## Dependency updates

Dependabot opens weekly pull requests for NuGet packages, GitHub Actions (the workflows and `.github/actions`) and the
.NET SDK of `global.json`. A new release waits 7 days (30 for a NuGet major); security updates are not delayed. Minor
and patch updates are grouped, and so are NuGet security updates, except a `CheatEngine.SDK` update, version or
security: it moves the reviewed pin, so it arrives in a pull request of its own and passes only once the bump procedure
of [`eng/CheatEngineSdk.props`](eng/CheatEngineSdk.props) is complete (identity literals, every lock file, prose). The
pin is also the lower bound of the `CheatEngine.SDK` range that Abstractions, Core and Hosting publish, so a release
that moves it raises that bound for every consumer and records it under **Deployment** in `CHANGELOG.md`. Dependabot
ignores `CheatEngine.SDK` majors (a Client migration), the Roslyn packages (they move with the Lua generator's compiler
floor, `CHEATENGINECLIENT9020`), the SDK-implicit ILLink and ILCompiler packages (they move with `global.json`) and .NET
SDK majors ([`.github/dependabot.yml`](.github/dependabot.yml)).

The `Microsoft.Extensions.*` versions of [`Directory.Packages.props`](Directory.Packages.props) are published floors,
not only build inputs: Extensions.DependencyInjection and Hosting declare the ones they reference as minimum versions
in their nuspecs, every consumer inherits them, and the packed template takes
`Microsoft.Extensions.Configuration.Json` from the same file (`CHEATENGINECLIENT9018`). They all follow the latest
reviewed 10.0.x patch, the .NET major the packages target, and move together only through Dependabot: its grouped
minor-and-patch pull request or a security update, never a hand edit in an unrelated change. A floor is never lowered,
and a released one stays in that release's nuspecs; a consumer that needs a later patch references it directly.
Dependabot also offers `Microsoft.Extensions` majors: take one only together with a move to that .NET major, never as a
dependency bump.

Dependabot does not regenerate every lock file. When `Lock files` fails on a Dependabot pull request, check the branch
out, run `dotnet restore <project> --force-evaluate` for each affected project, commit `Regenerate lock files after
<update>` and push; Dependabot then stops rebasing that pull request. A .NET SDK update also needs the new version
moved by hand into `sdk.errorMessage` in the same commit.

## Security

Report a vulnerability privately, as [`SECURITY.md`](SECURITY.md) describes, never in a public issue, discussion or pull
request.

## Commits

Use focused commits with an imperative subject of at most 72 characters, without a Conventional Commit prefix and
without a trailing period, such as `Harden the package consumption smoke tests`. Explain why in the body. Do not add
`Co-authored-by` trailers.

## Releases

Versions come from MinVer and the nearest `v*` tag; the seven packages always share one version. Releases are built,
attested and published by `release.yml` through NuGet trusted publishing, only from a tag on `main`. See
[`RELEASING.md`](RELEASING.md).
