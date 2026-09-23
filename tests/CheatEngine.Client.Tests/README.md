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
  `TemplatePackageInstallsListsAndUninstalls`;
- `IsolatedConsumerResolvesClientPackagesOnlyFromTheLocalFeed`: the Client packages come from the tested directory and
  `CheatEngine.SDK` from nuget.org with the content hash of `eng/sdk/consumed-sdk.json`;
- `IsolatedConsumerDeploysTheCompleteClosureWithThePackagedBridge` and
  `InstantiatedTemplateBuildsTheCompleteDeploymentClosure`: plugin, `.deps.json`, `.runtimeconfig.json`, Client and
  SDK assemblies and the native bridge (hash equal to the packed one) sit side by side, in the output and in the
  deployment folder (audit A04-09);
- `IsolatedConsumerDepsJsonRecordsPackagesWithoutWorkspacePaths`: `.deps.json` records the packages, with the SDK
  library carrying the NuGet content hash of the lock (measured, audit A21-02), and no workspace path;
- `InstantiatedTemplateReferencesTheSdkDirectly` (audit A04-10) and
  `PackagedClientPluginWithoutDirectSdkReferenceReportsCECLIENT001`;
- `PluginReferencingSdkTwoReportsCECLIENT017`: a plugin that references a re-versioned `CheatEngine.SDK` 2.x package
  directly next to the packed Client fails its build with `CECLIENT017` (and NuGet reports NU1608).

These are package-level results (fixture level C2); a Cheat Engine host run of Q40 is a separate qualification.
`PackageSourceResolutionTests` has no category, so both CI legs check the package source rules.

`SdkCanaryRecipeTests.CanaryRecipeBuildsAgainstACandidateSdkButNeverPacks` shares the same fixture and category. It
runs the canary recipe of `eng/sdk/README.md` (the SDK-side client-canary job, audit Q48) on a throw-away copy of
`CheatEngine.Client.Abstractions`, against the pinned SDK re-versioned as `2.0.0-alpha.0.42`: the restore rewrites the
copy's lock file instead of failing (NU1005), `CHEATENGINECLIENT9016` reports without failing the build, and the pack
is still refused.

`BuildGuardTests` run the repository's MSBuild guard targets against real projects with overridden global properties,
without restoring or building: `CommittedPinPassesTheSdkGuard`, `SdkMajorTwoPinFailsWithCHEATENGINECLIENT9016`,
`PrereleaseSdkPinFailsWithCHEATENGINECLIENT9016` and `CanarySwitchKeepsTheBuildRunningButStillBlocksPack` prove that the
consumed `CheatEngine.SDK` pin cannot move to a 2.x or prerelease package, and that the SDK-side canary build can report
breakage but never produce a package. `RoslynPinDriftFailsWithCHEATENGINECLIENT9020` proves that the Roslyn pin of the
packed Lua generator cannot drift from its declared floor, and `LockstepGuardAcceptsMinVerAndRefusesEveryOtherVersionSource`
that a package version comes from MinVer only (`CHEATENGINECLIENT9019`). `SbomGuardRefusesAPackWithoutTheSbom` proves
that a package cannot be packed without its SPDX SBOM (`CHEATENGINECLIENT9021`).

`ReleaseScriptTests` run the release scripts of `eng/release` in `pwsh` against `FakeGitHubCli.ps1`, an in-memory
stand-in for the gh CLI, so no GitHub call is made. They have no category, so both CI legs run them:
`DispatchStartedFromATagIsADryRunWithEmptyOutputs` and `PushOfSomethingOtherThanAReleaseTagIsRefused` prove that only
a tag push can release; `NewDraftCarriesExactlyTheFilesOfTheRun`, `DraftLeftByAnotherRunOfTheTagIsReplacedByTheFilesOfThisRun`,
`DraftThatAlreadyCarriesTheFilesOfTheRunIsKept` and `PublishedReleaseWithOtherAssetsFailsTheDraftJobBeforeAnyPush`
prove that a release asset is compared by SHA-256, never by name alone; `FinalizeRefusesToPublishADraftWhoseAssetsDifferFromTheRun`
and `FinalizePublishesTheCheckedDraftById` prove that a draft is published only when every asset is a file of the run.

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
