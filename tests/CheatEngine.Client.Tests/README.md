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
  `InterClientDependenciesRequireTheCoPackedVersion`: the seven packages share one MinVer version, the five packages
  with build output carry a symbol package with PDBs, and inter-Client dependencies require the co-packed version;
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
that a package cannot be packed without its SPDX SBOM (`CHEATENGINECLIENT9021`).

The SDK versions that these guard cases, the `CHEATENGINECLIENT9050` pin-drift case of
`ConsumedSdkIdentityEmbeddingTests` and the two re-versioned SDK facts above probe are derived from
`eng/CheatEngineSdk.props` (`Infrastructure/SdkPin.cs`), so they keep testing the same boundaries when the pin moves.

Two metadata suites read the built Client assemblies with `System.Reflection.Metadata`. `Architecture/` is the
ADR-01 ratchet. It freezes the direct Lua-stack and SDK-owner usages, the `[LuaGlobal]` inventory, and the absence of
native imports, and it rejects Client logging that could carry user data (Q46). A new debt entry fails the suite. When a
debt entry disappears, it must be deleted from its frozen list in the same change, so the lists only shrink. `SdkContract/` holds the Q48 consumer contracts against the consumed CheatEngine.SDK (the pin). It checks the shared
SDK type allowlist, the committed consumed-surface inventory, and the compile-only `SdkApiUsage` map. Both suites are
activation-independent and run in the Debug and Release test legs.

## Run

From the repository root:

```powershell
dotnet test --project .\tests\CheatEngine.Client.Tests\CheatEngine.Client.Tests.csproj --configuration Release
```

Without `CHEATENGINE_CLIENT_PACKAGE_SOURCE`, that run packs the repository itself. To test the exact packed files, as
the CI Release leg does:

```powershell
dotnet build CheatEngine.Client.slnx --configuration Release
dotnet pack CheatEngine.Client.slnx --configuration Release --no-build --output ./artifacts/nuget
$env:CHEATENGINE_CLIENT_PACKAGE_SOURCE = (Resolve-Path ./artifacts/nuget).Path
dotnet test --project ./tests/CheatEngine.Client.Tests/CheatEngine.Client.Tests.csproj --configuration Release --no-build --fail-skips on
```

Without the package consumption tests (the CI Debug leg):

```powershell
dotnet test --project ./tests/CheatEngine.Client.Tests/CheatEngine.Client.Tests.csproj --configuration Debug --no-build --fail-skips on --filter-not-trait "Category=PackageConsumption"
```
