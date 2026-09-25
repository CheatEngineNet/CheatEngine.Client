# CheatEngine.Client.Tests

## Context

This is the consumer-facing smoke suite for the `CheatEngine.Client` meta-package project. It validates the public
graph that a plugin author receives through the recommended top-level package.

## Why this project exists

The meta-package must expose functional Client namespaces and fluent entry points without forcing consumers to know
the internal package layout. It must also keep SDK ownership wrappers and raw Lua handles behind the Client boundary.

## How it helps improve CheatEngine.Client

The smoke tests compile against the assembled consumer graph and inspect selected public contracts for prohibited
handle types. They catch accidental dependency omissions, namespace regressions, and public leakage of `LuaState`,
`LuaRef`, `CEObject`, `Owned<T>`, `MemScan`, or `FoundList` before package smoke tests run.

The suite is activation-independent. `PackageConsumptionSmokeTests` consumes the exact package directory supplied
through `CHEATENGINE_CLIENT_PACKAGE_SOURCE`, validates the template package, and builds isolated consumers. It does
not replace Core lifecycle tests or the opt-in live validation required for host-dependent capabilities such as value
scans.

`PackageConsumptionSmokeTests` carries the traits `Category=PackageConsumption` and `Qualification=Q40`. The CI
Release leg points `CHEATENGINE_CLIENT_PACKAGE_SOURCE` at the packages it uploads, and without it a CI run fails
instead of packing its own. Locally, without the variable, the tests pack the repository into a temporary feed.
`PackagedClientFeedFixture` restores and builds everything once, from folders outside any repository, with an isolated
NuGet global packages folder and package source mapping (`CheatEngine.Client*` from the local feed only, everything
else from nuget.org). Every fact writes its evidence (package, bridge and hash values) to the test output, which the
TRX report keeps. The facts prove that:

- `SevenPackagesAndFiveSymbolPackagesAreProduced`, `EveryClientPackageSharesOneVersion` and
  `InterClientDependenciesRequireTheExactCoPackedVersion`: the seven packages share one MinVer version, the five
  packages with build output carry a symbol package with PDBs, and each package depends on exactly the frozen set of
  Client packages at exactly the co-packed version (`[X.Y.Z]`, not the `X.Y.Z` minimum NuGet writes by default);
- `SdkFacingPackagesDeclareThePinnedSdkRange`: Abstractions, Core and Hosting declare the pinned `CheatEngine.SDK`
  range with frozen asset exclusions, and no other package depends on the SDK directly;
- `HostingPackageShipsOnlyTheGeneratorAssemblyAsAnalyzer` and `PackedAssembliesCarryTheMajorMinorAssemblyVersion`;
- `PackedReadmesContainNoRelativeLinks` and `EveryPackageNamesTheRepositoryCommitAndLicense`: nuget.org pages link
  absolutely, and each package names the repository commit, MIT, the project and the changelog;
- `SymbolPackagesCarrySourceLinkToTheRepositoryCommit` and `EveryPackageEmbedsAnSpdxSbomDescribingItsOwnIdentity`;
- `PackedTemplateReferencesTheCoPackedClientAndThePinnedSdk`,
  `PackedTemplateProjectDiffersFromTheRepositoryTemplateOnlyByStampedVersions` and
  `TemplatePackageInstallsListsAndUninstallsAsync`;
- `IsolatedConsumerResolvesClientPackagesOnlyFromTheLocalFeed`: the Client packages come from the tested directory and
  `CheatEngine.SDK` from nuget.org with the content hash of the reviewed SDK identity hardcoded in
  `PackagedClientFeedFixture` (shared-contracts.md §2.4);
- `IsolatedConsumerDeploysTheCompleteClosureWithThePackagedBridge` and
  `InstantiatedTemplateBuildsTheCompleteDeploymentClosure`: plugin, `.deps.json`, `.runtimeconfig.json`, Client and
  SDK assemblies and the native bridge (hash equal to the packed one) sit side by side, in the output and in the
  deployment folder (audit A04-09);
- `IsolatedConsumerDepsJsonRecordsPackagesWithoutWorkspacePaths`: `.deps.json` records the packages, with the SDK
  library carrying the NuGet content hash of the lock (measured, audit A21-02), and no workspace path;
- `InstantiatedTemplateReferencesTheSdkDirectly` (audit A04-10) and
  `PackagedClientPluginWithoutDirectSdkReferenceReportsCECLIENT001Async`;
- `PluginReferencingTheNextSdkMajorReportsCECLIENT017Async`: a plugin that references directly, next to the packed
  Client, the pinned `CheatEngine.SDK` re-versioned to the major of the pin's upper bound fails its build with
  `CECLIENT017`, both as a prerelease (inside the declared range, no NuGet warning) and as a stable release (NU1608);
  `CheatEngineClientAllowUnsupportedSdk=true` turns the error into a warning;
- `PluginReferencingAnSdkBelowTheDeclaredRangeFailsRestoreAsync`: a plugin that references a re-versioned
  `CheatEngine.SDK` below the declared range fails its restore with NU1605 (package downgrade).

These are package-level results (fixture level C2); a Cheat Engine host run of Q40 is a separate qualification.
`PackageSourceResolutionTests` has no category, so both CI legs check the package source rules.

`SymbolPackagesCarrySourceLinkToTheRepositoryCommit` expects Source Link URLs of
`https://raw.githubusercontent.com/CheatEngineNet/CheatEngine.Client/<commit>/`, which the .NET SDK derives from the
`origin` remote of the checkout that packs. CI checks out this repository, so it holds there; a local run in a clone
whose `origin` is a fork or a local path produces other URLs or none, and fails that fact.

`BuildGuardTests` run the repository's MSBuild guard targets against real projects with overridden global properties,
without restoring or building: `CommittedPinPassesTheSdkGuardAsync`, `NextMajorPinFailsWithCHEATENGINECLIENT9016Async`
and `PrereleaseSdkPinFailsWithCHEATENGINECLIENT9016Async` prove that the consumed `CheatEngine.SDK` pin cannot move to
the next major or to a prerelease package, and that such a pin never produces a package (the pack guard refuses it
with `CHEATENGINECLIENT9016` too). `RoslynPinDriftFailsWithCHEATENGINECLIENT9020Async` proves that the Roslyn pin of the
packed Lua generator cannot drift from its declared floor, and `LockstepGuardAcceptsMinVerAndRefusesEveryOtherVersionSourceAsync`
that a package version comes from MinVer only (`CHEATENGINECLIENT9019`). `SbomGuardRefusesAPackWithoutTheSbomAsync` proves
that a package cannot be packed without its SPDX SBOM (`CHEATENGINECLIENT9021`), and
`ShippingProjectWithoutTrimReferenceVerificationFailsWithCHEATENGINECLIENT9008Async` that a shipping project cannot be
packed with the trim-compatibility verification of its references (`VerifyReferenceTrimCompatibility`, IL2125) turned
off.

The SDK versions that these guard cases, the `CHEATENGINECLIENT9050` pin-drift case of
`ConsumedSdkIdentityEmbeddingTests` and the two re-versioned SDK facts above probe are derived from
`eng/CheatEngineSdk.props` (`Infrastructure/SdkPin.cs`), so they keep testing the same boundaries when the pin moves.

Two metadata suites read the built Client assemblies with `System.Reflection.Metadata`. `Architecture/` is the
ADR-01 ratchet. It freezes the direct Lua-stack and SDK-owner usages, the `[LuaGlobal]` inventory, and the absence of
native imports, and it rejects Client logging that could carry user data (Q46). A new debt entry fails the suite. When a
debt entry disappears, it must be deleted from its frozen list in the same change, so the lists only shrink. `SdkContract/` holds the Q48 consumer contracts against the consumed CheatEngine.SDK (the pin). It checks the shared
SDK type allowlist, the committed consumed-surface inventory, and the compile-only `SdkApiUsage` map. Both suites are
activation-independent and run in the Debug and Release test legs.

## Live qualification

`LiveQualification/` is the sandboxed Cheat Engine runner: C# test code, no script and no extra package. A live fact
builds plugins from the packed Client packages, loads them into a private copy of Cheat Engine 7.7.0.10621 x64 and
drives them against a disposable gtutorial target. Live facts carry `Category=LiveQualification` and a `Session=S0`..`S6`
trait and share the serial collection `Live qualification`. Both CI legs and every local gate exclude them by trait. They
are never skipped: without the opt-in they fail at once with the instructions below, and they also fail when `CI=true`.

One session runs in this order:

1. `HostProcessGuard` refuses to start while a `cheatengine-*`, `Cheat Engine` or `gtutorial*` process runs, or while
   another debug output listener (DebugView) owns `DBWIN_BUFFER`.
2. `CheatEngineInstallation` verifies the source installation without writing to it: the SHA-256 of
   `cheatengine-x86_64.exe` (`9727076D…`, the hash the harness gate pins), its file version 7.7.0.10621, its AMD64
   machine (read with `PEReader`) and the hashes of `gtutorial-x86_64.exe` (`2DABEFFD…`) and `gtutorial-i386.exe`
   (`9131B1CA…`). It fingerprints the host executable and the `autorun` folder before the run and again after it.
3. `SandboxLayout` creates `<run root>/<yyyyMMddTHHmmssZ>-<4 hex>/`. `CheatEngineRegistryGuard` then protects the user
   state before anything can change it:
   - it takes a recursive snapshot of `HKCU\Software\Cheat Engine` (every value name, type and raw data, every subkey)
     into `<run>/hkcu-backup.json`, reads it back, and copies `%APPDATA%\Cheat Engine` to `<run>/appdata-backup/` with
     its listing in `<run>/appdata-backup.json`; a value type it cannot restore exactly stops the session;
   - it writes the crash marker `<run root>/registry-restore-pending.json`, naming those backups, and only then
     neutralizes the operator's plugin list for the session;
   - after the session it deletes the key tree, recreates it from the snapshot and proves it equal, does the same for
     the folder, and removes the marker only after both are verified;
   - a marker found when a session begins means a previous run crashed: the guard restores and verifies that run's
     backup first, then fails the new run with an explanation. If that restore fails, the marker stays and the message
     names the backups to restore by hand.

   The guard only accepts `HKCU\Software\Cheat Engine` with `%APPDATA%\Cheat Engine`, or a test scratch key
   `HKCU\Software\CheatEngine.Client.Tests\<guid>` with a temporary folder. The installation is then copied to
   `<run>/ce` and verified again.
4. `PluginBundleBuilder` compiles the harness sources in an isolated consumer outside any repository, against the
   packed `CheatEngine.Client` of `CHEATENGINE_CLIENT_PACKAGE_SOURCE` and `CheatEngine.SDK` from nuget.org
   (`PackagedClientFeedFixture`), and deploys the complete closure to `<run>/plugins/<name>`.
5. `TargetLauncher` starts the sandbox's gtutorial after checking its hash, and records its process id, start time and
   modules; no `speedhack`, `allochook`, `luaclient`, `vehdebug` or `dbk` module may appear in it (Q45).
6. `AuthorizationManifestWriter` writes the `ce77-live-probe-v1` manifest: the pinned host, the target's process id and
   hash, `disposable`, and an expiry at most 25 minutes ahead. The harness gate (`QualificationAuthorization`, compiled
   into this project) accepts it, and it accepts nothing longer than 30 minutes. For fault scenarios the writer also
   writes `liveprobe.fault.json` next to the plugin.
7. `LuaDriverScript` generates `<run>/ce/autorun/zz_cheatengine_client_qualification.lua`: a `createTimer` state machine
   on the main thread, one step per tick, each under `pcall`. It waits for the main form, opens the target, loads the
   plugin, waits for the harness functions, calls them, inspects the settings form read-only, clears the address list,
   writes `DONE` and calls `closeCE()`. Each step appends `R<TAB>step<TAB>ok|error|notexecuted<TAB>%q` to the transcript.
   Until the spike proves that `getSettingsForm()` can perform them, the plugin toggles through Settings > Plugins are
   operator steps (plan A12): a window that leaves Cheat Engine usable shows the prompt, and the driver waits up to 90
   seconds for the plugin's function to disappear after a disable or come back after an enable, or, for the enable that
   must fail, for the operator's Done. The observed effect is recorded `ok`; Skip, closing the window or no action in
   time is recorded `notexecuted`.
8. `DebugOutputCapture` owns the DBWIN objects (4096-byte section, `DBWIN_BUFFER_READY` and `DBWIN_DATA_READY`) and keeps
   only the Cheat Engine process's messages (`DebugOutputBuffer`).
9. `HostProcessGuard` starts `<run>/ce/cheatengine-x86_64.exe` directly, never the launcher, with an environment
   stripped of `DOTNET_*`, `MSBUILD*`, `TESTINGPLATFORM*`, `VSTEST*` and every inherited `CHEATENGINE_*`,
   `CE_SDK_LIVE_PROBE_*` and `CECLIENT_QUALIFICATION_*` value. It adds only the session's `CE_SDK_LIVE_PROBE_*` and
   `CECLIENT_QUALIFICATION_*` inputs and `CHEATENGINE_SDK_IDENTIFY_ON_ENABLE=1`. A session that exceeds 10 minutes is
   closed, then killed, and marked `TimedOut`. A `finally` always stops Cheat Engine and the target and restores the user
   state.
10. `TranscriptParser` decodes the transcript (Lua `%q` escapes). `ReceiptLedger` writes `receipts.jsonl`
    (`cheatengine-client-qualification-receipt/v1`): the run directory becomes `<run>`, and a receipt that still holds
    a local path, the user name or the machine name is refused. `QualificationSummaryWriter` writes `summary.json`
    (`cheatengine-client-qualification-summary/v1`): the package, SDK, host and target tuple with the
    `qualifiedSourceDigest`, one verdict per scenario and capability derived from the receipts (NotExecuted unless every
    check passed or one failed), and `registryRestored`.

`QualifiedSourceDigest` binds evidence to the shipping sources. It is the SHA-256 of a `sha256sum`-style manifest (one
`<sha256>  <path>` line per input, ordinal order) over `libs/**`, `src/**`, `source-generators/**` and `templates/**`
(lock files included), `Directory.Build.*`, `Directory.Packages.props`, `eng/*.props` and `global.json`, each with CRLF
normalized to LF. It excludes `*.md`, `PublicAPI.*.txt`, `AnalyzerReleases.*.md` and `HostQualificationEvidence.cs`, and
skips what `.gitignore` excludes (`bin`, `obj`, `artifacts` and tool folders). The same source file is compiled into
`CheatEngine.Client.Repository.Tests`, whose evidence tests recompute it; a change to any input after a recorded run
requires a new run.

`LiveSandboxSpikeTests` (`Session=S0`) is the spike: it loads the harness on gtutorial-x86_64, calls `status`, `runtime`
and `capabilities(1)`, inspects the settings form, asks the operator to disable and enable the harness, and closes Cheat
Engine, then requires the user state restored, the source installation unchanged and no process left. The facts the spike establishes are still pending and will be
recorded here: the registry values of the plugin list, whether the settings toggle is feasible, the dialogs Cheat Engine
shows, what disable does at `closeCE`, what `loadPlugin` enables, whether elevation is needed, whether hostfxr needs
`DOTNET_ROOT` once `DOTNET_*` is removed, and the exact behaviour of the driver's `openFileAsProcess` call. Until the
plugin-list values are known, the guard neutralizes none of them, so the operator's own Cheat Engine plugins would load in
the sandbox too; the guard still restores whatever the session changes. Spike receipts are never committed.

The sessions of the qualification are the live facts of `LiveSessionTests` (`Session=S1` to `S6`, one `Qualification`
trait per scenario), described as data in `SessionPlans` (setup and driver steps), `ScenarioCatalog` (scenarios, levels,
sessions, release gate and the capability map) and `ScenarioEvaluators` (one C# evaluator per check):

- **S1**, the x64 core on gtutorial-x86_64, with the Auto Assembler opt-in, a table root below the session directory and
  the lifecycle sink: identity (Q05), bundle identity (Q40), the capability probe (Q45), round trips (Q20, Q21), the
  partial batch (Q33), `setPointerSize(4)` then 8 (Q31), target facts and instructions (Q32), AOB scans (Q27–Q29), value
  scans (Q25, Q26), an allocation (Q30.a), Auto Assembler patches (Q35), tables (Q34), a symbol lease (Q16.b), worker
  admission (Q19), the 2^53 marshalling rule (Q21) and logs (Q46).
- **S2**, lifecycle and faults, without the Auto Assembler opt-in and with the `ModuleOnDisabling` fault: the policy
  refusal (Q44), the operator toggles of Q05, Q06 and the kept-function check of Q16, and the last disable read from
  the lifecycle sink (Q43), the one at `closeCE` or else the operator's last. The `Configure` fault of Q06 is written
  for one enable only; the switch then selects `ModuleOnDisabling` again, so every later enable keeps the fault Q43
  reads.
- **S3**, target identity on two gtutorial-x86_64 instances: an allocation, a scan session and a patch on A, then
  `openProcess(B)` (each lease has ended with a `RefusedTargetChanged` release that requires manual recovery, and a
  release attempt on B returns that refusal again without any Cheat Engine call), back on A (the refused leases stay
  ended; a new allocation releases), and a copy opened as a file (no allocation or scan session,
  `TargetIdentityUnavailable`; the AOB fallback route). Each process Cheat Engine selects is the one the Client reports
  next, with a later selection epoch (Q26, Q28, Q30.a, Q32, Q35). Q30.b, the reuse of a process id, cannot be produced
  on demand and stays NotExecuted (waivable, plan A12). A test proves that every driver step is read by a check or is
  reviewed setup.
- **S4**, the x86 target gtutorial-i386: bitness 4, an address above 4 GiB refused, the x86 module scan and instruction
  profile (Q21, Q28, Q32).
- **S5a** and **S5b**, coexistence in two load orders (A, then the SDK 1.x neighbour, then B; and the neighbour, A, B):
  Q09, Q10 and Q16. The neighbour is a plain CheatEngine.SDK 1.x plugin generated in a temporary consumer
  (`NeighbourPluginSource`) that reports its identity as booleans only.
- **S6**, the template instantiated from the packed Templates package as `QualTemplatePlugin`, bundled and loaded (Q40).

The release gate is Q09, Q10, Q40, Q43, Q44, Q45 and Q46 on the host, plus Q48 in CI (`SdkConsumerContractTests`). The
sessions share one run directory, one `receipts.jsonl` and one `summary.json`, rewritten after each session. Run S2 and
S5 with the operator at the keyboard: a check that needs a plugin toggle through Settings > Plugins is NotExecuted
unless the driver saw that toggle done, and the Q43 checks are NotExecuted when no enable was disabled, neither by the
operator nor at `closeCE` (a spike fact). The Q05 identification check reads the `CheatEngineSdkIdentification` line
that CheatEngine.SDK 2.0.0 writes through its debug output sink. A live fact fails when a check fails or the workstation
is not left as it was; a NotExecuted check is recorded, never turned into a pass. Run one session, or all of them, with
the opt-in above and `--filter-trait Session=S1` (up to `S6`), or `--filter-trait Category=LiveQualification`.

Run it from the repository root, in PowerShell, with Cheat Engine, every gtutorial and DebugView closed:

```powershell
dotnet build CheatEngine.Client.slnx -c Release --no-restore
dotnet pack CheatEngine.Client.slnx -c Release --no-build -o artifacts/nuget
$env:CHEATENGINE_CLIENT_PACKAGE_SOURCE = (Resolve-Path artifacts/nuget).Path
$env:CHEATENGINE_CLIENT_LIVE_QUALIFICATION = 'I_AUTHORIZE_CE77_LIVE_PROBES_ON_A_DISPOSABLE_TARGET'
dotnet test --project tests/CheatEngine.Client.Tests/CheatEngine.Client.Tests.csproj -c Release --no-build --filter-trait Session=S0
Remove-Item Env:CHEATENGINE_CLIENT_LIVE_QUALIFICATION
```

Optional inputs: `CHEATENGINE_CLIENT_LIVE_QUALIFICATION_CE_DIRECTORY` (default `C:/Program Files/Cheat Engine`, only
ever read) and `CHEATENGINE_CLIENT_LIVE_QUALIFICATION_RUN_ROOT` (default
`%LOCALAPPDATA%/CheatEngine.Client.LiveQualification/runs`, refused inside the repository or the installation). Each
run keeps its directory, sandbox included, for inspection; delete old runs by hand.

The runner's decisions are unit-tested in both CI legs, without starting anything: `LiveQualificationOptInTests` (the
opt-in, CI refusal, required packages, run root placement, the command above, and that every live fact is serial,
traited and never skipped), `CheatEngineInstallationTests` (fake files: hashes, machine, version, sandbox copy,
fingerprint, run directories), `AuthorizationManifestTests` (the harness gate and fault switch accept what the runner
writes), `LuaDriverScriptTests` (the reviewed driver text), `TranscriptParserTests`, `ReceiptLedgerTests`,
`QualificationSummaryWriterTests`, `DebugOutputBufferTests` (process id filter and ANSI decoding),
`HostProcessGuardTests` (blocking process names, the sandbox environment, injected modules),
`LiveSandboxSessionTests` (the S0 receipts derived from a transcript and the workstation checks), `ScenarioCatalogTests`
(every scenario has checks in the sessions it names, the release gate and the capability map are covered, each live
fact carries its scenario traits), `SessionPlanTests` (the reviewed step order of every session, harness calls only,
operator toggles never attempted, every step a check reads exists), `EvaluatorTests` (every kind of check on canned
evidence, passing and failing), `NeighbourPluginSourceTests` (the generated SDK 1.x neighbour, compared by hand once
with the SDK's published 1.x quick start and never read from the SDK repository), and the serial
`RegistrySnapshotTests` and `RegistryRecoveryTests`. The last two write the registry, but only a test-owned scratch key
`HKCU\Software\CheatEngine.Client.Tests\<guid>` and a temporary folder standing for `%APPDATA%\Cheat Engine`; they delete
the scratch key afterwards, and its parent `HKCU\Software\CheatEngine.Client.Tests` once it is empty. They never open
`HKCU\Software\Cheat Engine`. `RegistrySnapshotTests` round-trips every value type through the backup and restore and
proves which keys the guard accepts; `RegistryRecoveryTests` proves the neutralized session state, the verified restore,
the restore on dispose, the crash marker recovery that fails the next run, the marker kept while a restore does not
verify, and the removal of a `%APPDATA%` folder the session created. `QualifiedSourceDigestTests` proves that a CRLF and an
LF checkout hash the same, that the input and exclusion lists are exactly the plan's, that content and path changes move
the digest while excluded files never do, and, running `git ls-files`, that every tracked input is enumerated and no file
git ignores is.

## Run

From the repository root:

```powershell
dotnet test --project .\tests\CheatEngine.Client.Tests\CheatEngine.Client.Tests.csproj --configuration Release --filter-not-trait "Category=LiveQualification"
```

Without `CHEATENGINE_CLIENT_PACKAGE_SOURCE`, that run packs the repository itself. To test the exact packed files, as
the CI Release leg does:

```powershell
dotnet build CheatEngine.Client.slnx --configuration Release
dotnet pack CheatEngine.Client.slnx --configuration Release --no-build --output ./artifacts/nuget
$env:CHEATENGINE_CLIENT_PACKAGE_SOURCE = (Resolve-Path ./artifacts/nuget).Path
dotnet test --project ./tests/CheatEngine.Client.Tests/CheatEngine.Client.Tests.csproj --configuration Release --no-build --fail-skips on --filter-not-trait "Category=LiveQualification"
```

Without the package consumption tests (the CI Debug leg):

```powershell
dotnet test --project ./tests/CheatEngine.Client.Tests/CheatEngine.Client.Tests.csproj --configuration Debug --no-build --fail-skips on --filter-not-trait "Category=PackageConsumption" --filter-not-trait "Category=LiveQualification"
```

Both CI legs, and every command above, exclude the live qualification tests (`Category=LiveQualification`) by trait:
they start a sandboxed Cheat Engine and run only on a maintainer workstation that opts in.
