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
  `LICENSE` equal to the package license expression; a `CHANGELOG.md` whose `[Unreleased]` section separates the four
  release categories and whose releases are `## [X.Y.Z] - YYYY-MM-DD` sections with a real ISO 8601 date, newest first
  by version and date, each with an entry (`ChangelogReleasesAreDatedInIsoFormatNewestFirstAndHaveEntries`, whose
  rules `TheReleaseSectionRulesSeeOrderDatesHeadingsAndEmptySections` exercises on synthetic headings); and a
  `RELEASING.md` that documents the trusted publishing policy for `CheatEngine.Client*`, with the `CheatEngine`
  organization as policy owner and `AriusII` as `NUGET_USER`.
- `Packaging/SdkPinTests` proves that the Client consumes one reviewed `CheatEngine.SDK` package (audit ADR-10, A21-01,
  A21-02). Its documentation checks read the Markdown and C# files of `src/`, `libs/`, `source-generators/` and
  `templates/`, the template content project, the governance documents, every text file under `.github/` and the
  CHANGELOG except the sections below the MinVer floor, which are released history:
  - `SdkVersionAppearsAsALiteralOnlyInTheSdkPropsFile`: project files derive every SDK version from
    `eng/CheatEngineSdk.props`;
  - `ProseMentionsOfTheConsumedSdkEqualThePin`: documentation and diagnostics that name the SDK version name the pin;
  - `VersionRangesInTheDocumentationAreTheDeclaredSdkRange`: every two-bound version range is the range that
    `eng/CheatEngineSdk.props` declares, spaces ignored, and at least one guarded document states it;
  - `TupleLineHashesAreTheReviewedIdentityOfThePinnedSdk`: a SHA-512 or SHA-256 on a line that names CheatEngine.SDK,
    its bridge or its content hash is the reviewed identity of `PackagedClientFeedFixture.PinnedSdkIdentity` or a host
    profile hash, and the install guides state the content hash and the bridge hash;
  - `TheDocumentationChecksSeeTupleHashesRangesAndOnlyTheCurrentChangelog` proves those helpers on synthetic lines;
  - `EveryLockFileResolvesThePinnedSdkWithOneContentHash`: every lock resolves the pin with one content hash;
  - `RetiredSdkIdentityLiteralsAppearNowhere`: no text file keeps the content hash, signed-file hash, bridge hash or
    source commit of an SDK package the Client no longer consumes;
  - `SdkPinIsAStableVersionOfTheSupportedMajor`, `CoexistenceFixturesDeriveTheirSdkVersionFromThePin` and
    `ConsumerSdkMajorGuardMatchesThePinUpperBound`.
- `Packaging/NoStaleSdkWordingTests` refuses, in every text file of `libs/`, `src/`, `source-generators/` and
  `templates/`, a wording about another CheatEngine.SDK version or a pending migration ("SDK 1.0.0", "SDK 2.0 ...
  owners", "migration guide", "when the Client migrates", "until the SDK 2.0"), also when a comment wraps the phrase;
  `TheDetectorFindsEveryStaleFormAcrossWrappedCommentsAndSparesTheCurrentWording` pins each form.
- `Packaging/ConsumerDiagnosticCatalogTests` catalogs the `CECLIENT` build diagnostics that the Hosting package's
  `buildTransitive` targets bring to a plugin project: the codes the targets emit (MSBuild `Error` and `Warning`
  elements and the `Log.LogError` calls of their inline tasks) are exactly `CECLIENT001` to `CECLIENT017` and the rows
  of the "Build diagnostics" table of the Hosting README, each row has its anchor once, its Severity cell names how the
  targets emit the code (`Error`, `Warning`, or `Error; Warning with ...` for a code emitted as both), and every
  emission carries the help link of its row (`_CheatEngineClientHelpLink` followed by the code).
- `Packaging/PackageVersioningTests` and `Packaging/PackageMetadataTests` pin the package versioning and metadata:
  MinVer configured once for every package (`MinVerIsConfiguredOnceForEveryPackage`), the Roslyn pin of the packed
  generator equal to its floor (`RoslynPinsEqualTheDeclaredComponentFloor`,
  `TemplateSdkConstraintDoesNotExceedTheRepositorySdk`), readable template defaults
  (`TemplateProjectDefaultsMatchTheCentralVersions`), one description per package, the license holder as copyright, the
  `artifacts/nuget` output folder, Source Link from the .NET SDK, and the same SPDX SBOM settings in both profiles.
- `Packaging/PublicApiFileTests` keeps the PublicAPI baselines truthful before the first release: every shipping library
  declares both files and a project that packs no assembly declares none, entries are ordinally sorted, no `*REMOVED*`
  entry and no `PublicAPI.Shipped.txt` entry exists until the release pull request promotes a release (a
  `## Release X.Y.Z` section in `AnalyzerReleases.Shipped.md`, which must be a dated `CHANGELOG.md` release), and the
  files that still suppress RS0026/RS0027 may only shrink.
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
  every other project has a version 2 lock file, the whole graph consumes the pinned CheatEngine.SDK with its reviewed
  content hash (`EveryLockResolvesThePinnedSdkWithTheRecordedContentHash`), no Client package comes from a feed, the
  Native AOT probe records its runtime packs, and every lock file ends exactly as NuGet writes it, without a final
  newline (`LockFilesEndExactlyAsNuGetWritesThem`).
- `Workflows/WorkflowContractTests` freeze the CI contract shared with CheatEngine.SDK, over every workflow that
  exists: callers reach `ci.yml` through job `ci` named `CI`, so the only required check is `CI / Gate`; the Gate runs
  `always()` without permissions and needs every other job; the Sonar condition equals the Gate's `SONAR_EXPECTED`;
  no `pull_request_target`, `merge_group` or path filter; pinned runner labels and a timeout on every job; read-only
  top-level permissions; every action pinned to a full SHA with its version, one pin per action; no credential left
  by checkout; no NuGet cache on a release-reachable path; locked restores through the composite setup action; the
  Release leg packs before it tests and hands the packages to the consumption tests, which Debug excludes by trait;
  reserved artifact names and binary logs on failure only. `LiveQualificationTestsNeverRunInCi` proves that every
  `dotnet test` step excludes `Category=LiveQualification` by trait (in `ci.yml`, in the option array both legs share),
  with no positive filter and no workflow that sets the live qualification opt-in. `Workflows/WorkflowFile` loads the
  YAML with YamlDotNet.
- `Workflows/SonarWorkflowTests` freezes the analysis scope of `sonar.yml`: every excluded path exists, and shipping
  code leaves the coverage metric file by file (`ShippingCodeIsExcludedFromCoverageFileByFile`), never by a folder
  pattern.
- `Toolchain/TestProfileTests` prove that every `*.Tests` project references the Microsoft.Testing.Platform extension
  of every option the CI test command passes (a missing one fails the module with exit code 5).
- `Capabilities/CapabilityDocumentationTests` keeps every capability table (the Markdown table between
  `<!-- capability-table:start -->` and `<!-- capability-table:end -->`, in any README) equal to the Client's capability
  catalog: each `ClientCapabilityId` listed once, the catalog's operational adapter and experimental id in the
  Implementation column, and exactly the live scenarios its qualification gate requires in the Qualification column.
  In a table with a "1.0 status" column, a row starts with `Experimental` exactly when the catalog gives its
  capability an experimental diagnostic id, and with `Available` otherwise
  (`CapabilityTableStatusColumnsMarkExactlyTheExperimentalApis`).
  `InstallGuidesStateTheQualifiedHostProfile` proves that the three install guides state the supported host profile,
  the host executable and runtime configuration hashes and the NuGet content hash that Core's lock file records.
- `SourcePolicy/` holds the source rules no analyzer expresses:
  - `AotProbeCoverageTests`: the Native AOT probe calls every public member of CheatEngine.Client.Fluent, read from
    the Fluent PublicAPI baselines. Each public Fluent type has an `Exercise` method that `Run` calls, each member has
    at least as many call sites there as public signatures (counted as text, comments excluded, so a review keeps one
    call per overload), and the probe's entry point returns 1 when `AotProbeFluentCalls.Run()` fails;
  - `ErrorTextClassificationPolicyTests`: Client libraries never classify a failure by Cheat Engine or Lua error text
    (A07-22, A24-24), only by type and status;
  - `InterfaceStabilityRemarkTests`: every public interface states whether it is Call-only or Implementable, and the
    versioning sections of the root and `CheatEngine.Client` READMEs name exactly the Implementable ones;
  - `SingleFileSuppressionTests`: IL3000 is suppressed exactly once, on the getter of
    `CheatEnginePluginBuilder.PluginDirectory`, under ADR-02;
  - `TemplateLoggingPolicyTests`: the template's log events carry no address, value or raw failure (Q46), checked from
    the committed text because the template compiles only after `dotnet new`.
- `Qualification/QualificationEvidenceTests` keeps the evidence discipline of the live qualification:
  `NoQualificationClaimWithoutCommittedEvidence` refuses, while
  `tests/CheatEngine.Client.Tests/LiveQualification/Evidence/` holds no committed run summary, any README, CHANGELOG,
  RELEASING or capability-table line that claims a host qualification (a CHANGELOG `Qualification` section, a scenario
  id with a `Passed` or `Waived` verdict, a live run id, a sentence stating that something is qualified on the host, or
  a Qualification cell that reports a pass). `TheClaimDetectorRecognizesEveryClaimFormAndTheCurrentWording` pins those forms, and
  `EveryShippingSourceIsBoundByTheDigest` proves that the shipping source digest (`QualifiedSourceDigest`, compiled in
  from the live runner of `CheatEngine.Client.Tests`) covers every shipping source file this project sees.
- `Governance/` holds the repository governance contracts that stay meaningful without a bespoke script or a required
  check of their own (audit rows PR-CQ-08/17/23/25/37, A21-36):
  - `CodeQlWorkflowTests`, `ScorecardWorkflowTests` and `OnlineZizmorWorkflowTests` prove the advisory security
    workflows: a manual traced build of the whole shipped graph without dependency cache, the Scorecard verifier's
    restrictions, the same zizmor version as the Gate and no SARIF upload from forks;
  - `DependabotConfigurationTests` proves the cooldowns, the covered ecosystems, the ignores that protect frozen
    decisions (CheatEngine.SDK majors, Roslyn, SDK-implicit packages) and that a CheatEngine.SDK update, version or
    security, a reviewed pin move, never joins a grouped pull request (`CheatEngineSdkUpdatesAreNeverGrouped`);
  - `CommunityHealthTests` and `IssueFormTests` prove `SECURITY.md`, `CODE_OF_CONDUCT.md`, CODEOWNERS and the issue
    forms, that no form presents a profile as qualified, and that the version placeholders name a stable version of
    the Client's major line, the pinned CheatEngine.SDK and its content hash
    (`VersionPlaceholdersNameTheClientLineAndThePinnedSdk`).

## Run

From the repository root:

```powershell
dotnet test --project .\tests\CheatEngine.Client.Repository.Tests\CheatEngine.Client.Repository.Tests.csproj
```
