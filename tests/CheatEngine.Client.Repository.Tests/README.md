# CheatEngine.Client.Repository.Tests

## Context

Repository policy tests for CheatEngine.Client: the solution inventory today, and the documentation, workflow and
qualification contracts added by the audit remediation work.

## Why this project exists

Repository rules must not live in scripts that CI can silently stop running (the deleted `docs/` tree left dead links,
including in the README packed on nuget.org). They are enforced by C# tests that only read committed files: the
project never builds, packs, restores or starts a process, so it runs in seconds.

## How it helps improve CheatEngine.Client

- `Solution/SolutionInventoryTests` proves that every `*.csproj` on disk is built by CI through
  `CheatEngine.Client.slnx`, unless an explicit, reasoned exclusion says otherwise (the template content project).
- `Release/RepositoryDocumentsTests` proves that the repository carries what a published package links to: an MIT
  `LICENSE` equal to the package license expression, a `CHANGELOG.md` whose `[Unreleased]` section separates the four
  release categories, and a `RELEASING.md` that documents the trusted publishing policy for `CheatEngine.Client*`.
- Later work adds one folder per contract (for example `Documentation/`, `Workflows/`, `Qualification/`).
- `Toolchain/ToolchainPinTests` keeps the build reproducible from the commit alone: `global.json` pins the exact .NET
  SDK (`rollForward: disable`, with an `errorMessage` naming the install command), `AnalysisLevel` is a numbered
  release rather than `latest`, and the NuGet audit policy blocks high and critical advisories in every build. The
  build-time guards `CHEATENGINECLIENT9030`-`9032` (`Directory.Build.targets`) catch per-project or command-line
  overrides of the same settings.
- `LockFiles/LockFileTests` mirror the structural checks of `eng/Update-LockFiles.ps1` offline: every project has a
  lock file, the Coexistence fixtures keep version 1 lock files without `CentralTransitive` entries (the failure a
  solution-level `--force-evaluate` caused), every other project has a version 2 lock file, the whole graph consumes
  CheatEngine.SDK 1.0.0 with its recorded content hash, no Client package comes from a feed, the Native AOT probe
  records its runtime packs, and each lock file keeps its committed final newline.
- `Workflows/CoverageBaselineTests` keep `eng/coverage-baseline.json` equal to the shipping assemblies that compile
  source (the `CheatEngine.Client` package facade has none), its line floors valid percentages with an explicit
  tolerance, and the `dotnet-coverage` merge tool pinned to the collector's version in `.config/dotnet-tools.json`.
- `Workflows/BuildInfoSchemaTests` keep `eng/ci/build-info.v0.schema.json`, the contract fields of `build-info.json`
  and its writer `eng/ci/New-BuildInfo.ps1` in step, and every object of the schema closed to unknown properties.
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
- `Governance/` holds the repository governance contracts (audit rows PR-CQ-08/17/21/23/25/31/35/37/46/56, A21-36,
  A22-44):
  - `PullRequestPolicyTests` (with `PullRequestPolicyRules`, the executable specification of `eng/ci/pr-policy.json`)
    proves the required `PR policy` check: imperative title of at most 72 characters without a Conventional-Commit
    prefix, a CHANGELOG entry for consumer-visible paths unless the opt-out marker is on its own line, Dependabot
    exempt, user text reaching `eng/ci/Test-PullRequestPolicy.ps1` only through `env:`, case-sensitive matching only;
  - `CodeQlWorkflowTests`, `ScorecardWorkflowTests` and `OnlineZizmorWorkflowTests` prove the advisory security
    workflows: a manual traced build of the whole shipped graph without dependency cache, the Scorecard verifier's
    restrictions, the same zizmor version as the Gate and no SARIF upload from forks;
  - `DependabotConfigurationTests` proves the cooldowns, the covered ecosystems and the ignores that protect frozen
    decisions (CheatEngine.SDK majors, Roslyn, SDK-implicit packages);
  - `ScheduledHealthWorkflowTests` and `DependencySubmissionWorkflowTests` prove the scheduled audit, canary and repeat
    run and the split read/write dependency submission with a pinned, hash-verified detector. `GlobalJsonSdkRewrite`
    mirrors the canary's global.json rewrite (`eng/ci/Select-NewestDotNetSdk.ps1`, which never runs the .NET CLI):
    sdk.version and the version named by sdk.errorMessage move together, every other byte stays. The submit job
    refuses a snapshot that names another commit, ref or correlator than its own run;
  - `CommunityHealthTests` and `IssueFormTests` prove `SECURITY.md`, `CODE_OF_CONDUCT.md`, CODEOWNERS and the issue
    forms, including a compatibility form that requires the full release tuple (audit Checkpoint F) and never presents
    a profile as qualified;
  - `RepositorySettingsTests` proves the desired state of `eng/github/` (the two frozen required checks, squash-only
    merges, no bypass actor, release tags protected without blocking their creation, a single guarded write path).

## Run

From the repository root:

```powershell
dotnet test --project .\tests\CheatEngine.Client.Repository.Tests\CheatEngine.Client.Repository.Tests.csproj
```
