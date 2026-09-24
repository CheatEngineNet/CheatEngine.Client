# CheatEngine.Client.Repository.Tests

## Context

Repository policy tests for CheatEngine.Client: the solution inventory, the SDK pin, the lock files and the workflow
and governance contracts, checked directly against the committed files.

## Why this project exists

Repository rules must not live in scripts that CI can silently stop running. They are enforced by C# tests that only
read committed files: the project never builds, packs, restores or starts a process, so it runs in seconds.

## How it helps improve CheatEngine.Client

- `Solution/SolutionInventoryTests` proves that every `*.csproj` on disk is built by CI through
  `CheatEngine.Client.slnx`, unless an explicit, reasoned exclusion says otherwise (the template content project).
- `Release/RepositoryDocumentsTests` proves that the repository carries what a published package links to: an MIT
  `LICENSE` equal to the package license expression, a `CHANGELOG.md` whose `[Unreleased]` section separates the four
  release categories, and a `RELEASING.md` that documents the trusted publishing policy for `CheatEngine.Client*`.
- `Packaging/SdkPinTests` proves that the Client consumes one reviewed `CheatEngine.SDK` package (audit ADR-10, A21-01,
  A21-02):
  - `SdkVersionAppearsAsALiteralOnlyInTheSdkPropsFile`: project files derive every SDK version from
    `eng/CheatEngineSdk.props`;
  - `ProseMentionsOfTheConsumedSdkEqualThePin`: documentation and diagnostics that name the SDK version name the pin;
  - `EveryLockFileResolvesThePinnedSdkWithOneContentHash`: every lock resolves the pin with one content hash;
  - `SdkPinIsAStableVersionOfTheSupportedMajor` and `CoexistenceFixturesDeriveTheirSdkVersionFromThePin`.
- `Packaging/PackageVersioningTests` and `Packaging/PackageMetadataTests` pin the package versioning and metadata:
  MinVer configured once for every package (`MinVerIsConfiguredOnceForEveryPackage`), the Roslyn pin of the packed
  generator equal to its floor (`RoslynPinsEqualTheDeclaredComponentFloor`,
  `TemplateSdkConstraintDoesNotExceedTheRepositorySdk`), readable template defaults
  (`TemplateProjectDefaultsMatchTheCentralVersions`), one description per package, the license holder as copyright, the
  `artifacts/nuget` output folder, Source Link from the .NET SDK, and the same SPDX SBOM settings in both profiles.
- `Packaging/PublicApiFileTests` keeps the PublicAPI baselines truthful before the first release: every shipping library
  declares both files and a project that packs no assembly declares none, entries are ordinally sorted, no `*REMOVED*`
  entry and no `PublicAPI.Shipped.txt` entry exists until `CHANGELOG.md` records a dated release, and the files that
  still suppress RS0026/RS0027 may only shrink.
- `Release/ReleaseWorkflowTests` proves that `release.yml` keeps the contract job order, calls `ci.yml` with the
  package version and a 90-day retention but no Sonar, confines the `nuget` environment and secrets to `publish`, never
  caches packages, and pushes the seven packages in dependency order (audit A21-06).
  `DraftAndPublishRunOnlyForTagPushesOfThisRepository` proves that `attest`, `draft-release` and `publish` share one
  condition (a push, a tag, this repository, a verified version), that `verify` receives the event name, so a
  `workflow_dispatch` started from a tag stays a dry run, and that no later job can run after them once they are
  skipped. `TheWriteTokenReachesOnlyTheStepsThatCallGitHub` proves that the `contents: write` token never reaches a
  restore or test step. The release chain tests (audit PKG-06 to PKG-08) prove that `finalize-release` reads and
  publishes the draft with `gh release view` and `gh release edit`, then verifies the downloaded assets; that a re-run
  deletes a stale draft with `gh release delete`; that `publish` checks every package and symbol package against
  `SHA256SUMS` before it pushes the packages, then the symbols; that every `gh attestation verify` pins the signer
  workflow, the tag and hosted runners; that `stage` writes the SBOMs and `SHA256SUMS` on dry runs without an OIDC
  token; and that each workflow declares the seven package ids once, equal to the packable projects.
- `Toolchain/ToolchainPinTests` keeps the build reproducible from the commit alone: `global.json` pins the exact .NET
  SDK (`rollForward: disable`, with an `errorMessage` naming the install command), `AnalysisLevel` is a numbered
  release rather than `latest`, and the NuGet audit policy blocks high and critical advisories in every build. The
  build-time guards `CHEATENGINECLIENT9030`-`9032` (`Directory.Build.targets`) catch per-project or command-line
  overrides of the same settings.
- `LockFiles/LockFileTests` checks the structural invariants of every committed lock file directly: every project has
  one, the Coexistence fixtures keep version 1 lock files without `CentralTransitive` entries (the failure a
  solution-level `--force-evaluate` once caused, which is why regeneration always restores each project on its own),
  every other project has a version 2 lock file, the whole graph consumes CheatEngine.SDK 1.0.0 with its recorded
  content hash, no Client package comes from a feed, the Native AOT probe records its runtime packs, and each lock
  file keeps its committed final newline.
- `Workflows/WorkflowContractTests` freeze the CI contract shared with CheatEngine.SDK, over every workflow that
  exists: callers reach `ci.yml` through job `ci` named `CI`, so the only required check is `CI / Gate`; the Gate runs
  `always()` without permissions and needs every other job; the Sonar condition equals the Gate's `SONAR_EXPECTED`;
  no `pull_request_target`, `merge_group` or path filter; pinned runner labels and a timeout on every job; read-only
  top-level permissions; every action pinned to a full SHA with its version, one pin per action; no credential left
  by checkout; no NuGet cache on a release-reachable path; locked restores through the composite setup action; the
  Release leg packs before it tests and hands the packages to the consumption tests, which Debug excludes by trait;
  reserved artifact names and binary logs on failure only. `Workflows/WorkflowFile` loads the YAML with YamlDotNet.
- `Toolchain/TestProfileTests` prove that every `*.Tests` project references the Microsoft.Testing.Platform extension
  of every option the CI test command passes (a missing one fails the module with exit code 5).
- `Governance/` holds the repository governance contracts that stay meaningful without a bespoke script or a required
  check of their own (audit rows PR-CQ-08/17/23/25/37, A21-36):
  - `CodeQlWorkflowTests`, `ScorecardWorkflowTests` and `OnlineZizmorWorkflowTests` prove the advisory security
    workflows: a manual traced build of the whole shipped graph without dependency cache, the Scorecard verifier's
    restrictions, the same zizmor version as the Gate and no SARIF upload from forks;
  - `DependabotConfigurationTests` proves the cooldowns, the covered ecosystems and the ignores that protect frozen
    decisions (CheatEngine.SDK majors, Roslyn, SDK-implicit packages);
  - `CommunityHealthTests` and `IssueFormTests` prove `SECURITY.md`, `CODE_OF_CONDUCT.md`, CODEOWNERS and the issue
    forms, and that no form presents a profile as qualified.

## Run

From the repository root:

```powershell
dotnet test --project .\tests\CheatEngine.Client.Repository.Tests\CheatEngine.Client.Repository.Tests.csproj
```
