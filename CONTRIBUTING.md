# Contributing to CheatEngine.Client

Thanks for contributing. CheatEngine.Client is a Windows x64, .NET 10 layer for in-process Cheat Engine 7.7 plugins,
built on the `CheatEngine.SDK` package. Keep changes focused, preserve existing patterns, and update the documentation
and tests that describe the behavior you change.

## Prerequisites

- Windows x64
- .NET SDK 10.0.401 exactly, as pinned in [`global.json`](global.json). `rollForward` is `disable`, so any other
  SDK, a newer one included, stops the build with the install command:
  `winget install Microsoft.DotNet.SDK.10 --version 10.0.401`
- PowerShell 7.4 or later, for the scripts under `eng/`
- Git

## Build and test

Run these commands from the repository root. Restores are locked: they fail when a project's dependencies no longer
match its committed `packages.lock.json`.

```powershell
dotnet restore CheatEngine.Client.slnx --locked-mode
dotnet build CheatEngine.Client.slnx -c Debug --no-restore
dotnet test --solution CheatEngine.Client.slnx -c Debug --no-build --fail-skips on
dotnet build CheatEngine.Client.slnx -c Release --no-restore
dotnet test --solution CheatEngine.Client.slnx -c Release --no-build --fail-skips on
```

A skipped test fails the run: an environment-dependent test is fixed or deleted, never skipped.

The package consumption tests consume the exact packages you pack. Pack to `artifacts/nuget` and point them at that
folder; without the variable they pack the repository themselves, which is slower:

```powershell
dotnet pack CheatEngine.Client.slnx -c Release --no-build -o artifacts/nuget
$env:CHEATENGINE_CLIENT_PACKAGE_SOURCE = (Resolve-Path artifacts/nuget).Path
dotnet test --project tests/CheatEngine.Client.Tests/CheatEngine.Client.Tests.csproj -c Release --no-build --fail-skips on
```

To run everything except those tests, add `--filter-not-trait "Category=PackageConsumption"`.

### Lock files

Never edit a `packages.lock.json` by hand, and never let an IDE restore rewrite them. After a dependency change,
regenerate them with `./eng/Update-LockFiles.ps1`, on Windows, with the SDK of `global.json`, no IDE open on the working
tree and `CoexistenceSdkPackageVersion` unset. The script:

1. restores the three live-plugin coexistence fixtures first, one project at a time: they stay outside Central Package
   Management and keep version 1 lock files;
2. restores every other project, one project at a time;
3. verifies the whole solution with `--locked-mode`, keeps the final-newline state each lock file has in `HEAD`, and
   prints the files that changed.

Never run `dotnet restore CheatEngine.Client.slnx --force-evaluate`: a solution-level re-evaluation rewrites the
fixtures' lock files in the Central Package Management shape. Commit the regenerated files on their own, as
`Regenerate lock files after <reason>`. On a rebase conflict in a lock file, take either side and run the script again;
never merge a lock file by hand. `./eng/Update-LockFiles.ps1 -Verify`, the `Lock files` CI job, names every lock file
that differs from a regeneration. It is the real guard: `--locked-mode` does not notice a hand-edited resolved version.

### The consumed CheatEngine.SDK

The Client consumes exactly one `CheatEngine.SDK` package, pinned in [`eng/CheatEngineSdk.props`](eng/CheatEngineSdk.props)
and identified in `eng/sdk/consumed-sdk.json`. Never write an SDK version anywhere else, and never move the pin by hand:
[`eng/sdk/README.md`](eng/sdk/README.md) explains the policy, the guards (`CHEATENGINECLIENT9016`, `9017`, `CECLIENT017`)
and `eng/sdk/Update-CheatEngineSdk.ps1`. Moving to another SDK major is a migration of the Client, not a dependency bump.

### NuGet audit and build guards

NuGet audits every package, direct and transitive, from the `low` severity up:

- high (`NU1903`) and critical (`NU1904`) advisories fail every restore and build;
- low and moderate advisories (`NU1901`, `NU1902`), an unavailable audit source (`NU1900`) and `NU1905` stay warnings in
  ordinary builds;
- `-p:AuditPipeline=true` turns every audit code into an error. The weekly strict audit of the `Scheduled health`
  workflow uses it.

To accept one advisory, add `<NuGetAuditSuppress Include="<advisory URL>"/>` with a comment that gives the
justification and an expiry date. Never suppress an advisory on the release path. Never lower the policy itself:
`CHEATENGINECLIENT9030` refuses an analysis level other than the pinned `10.0-recommended`, `CHEATENGINECLIENT9031`
refuses a weakened audit (off, a mode other than `all`, a level other than `low`, or `NU1903`/`NU1904` in `NoWarn` or
`WarningsNotAsErrors`), and `CHEATENGINECLIENT9032` refuses a project without a lock file or outside Central Package
Management (the coexistence fixtures excepted).

## Continuous integration

Pull requests run `Pull request CI`, pushes to `main` run `Main CI`, and version tags run `Release`; all three call the
reusable `ci.yml` through a job named `CI`. There is no merge queue. Two checks are required on a pull request, and no
other:

- `CI / Gate` requires every other `ci.yml` job to succeed. The only exception is `Sonar`: it must succeed when the
  analysis is expected (`SONAR_EXPECTED`: a pull request from this repository, a push to `main`, a manual run) and must be
  skipped otherwise (pull requests from forks or Dependabot, which receive no secrets, and the release run). A Sonar run
  that was not expected fails the Gate too: it means that the job condition and the Gate's copy of it drifted apart. The
  Gate's job summary lists every job with its result, the required result and the reason.
- `PR policy` checks the pull request title and the changelog rule of [Pull request policy](#pull-request-policy).

Drafts do not run CI until they are marked ready for review, so `CI / Gate` stays pending; `PR policy` runs on drafts
and again after every title or description edit.

| Check (`CI / ...`)             | Runner         | What it does                                                                                                                                                 |
|--------------------------------|----------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Build and test (Debug)`       | `windows-2025` | Locked restore, build, one `dotnet test --solution` run with coverage (package consumption tests excluded by trait), test module inventory, coverage ratchet |
| `Build and test (Release)`     | `windows-2025` | Locked restore, build, pack (exact package set, embedded SBOM), `build-info.json`, benchmark discovery, one test run against the packed packages             |
| `Native AOT publication probe` | `windows-2025` | Publishes and runs `tests/CheatEngine.Client.AotProbe`: trim and Native AOT compatibility of the Client graph, not a Cheat Engine load                       |
| `Sonar / Analyze`              | `windows-2025` | SonarQube Cloud CI-based analysis with the Debug coverage; waits for the quality gate except on pushes to `main`                                             |
| `Lint`                         | `ubuntu-24.04` | actionlint and the offline zizmor audits over the workflows, PSScriptAnalyzer over every tracked PowerShell file                                             |
| `Format`                       | `ubuntu-24.04` | `dotnet format whitespace . --folder --verify-no-changes --exclude artifacts`                                                                                |
| `Dependency review`            | `ubuntu-24.04` | On pull requests, reviews dependency changes against `.github/dependency-review-config.yml`; a notice on other events                                        |
| `Lock files`                   | `windows-2025` | `./eng/Update-LockFiles.ps1 -Verify`                                                                                                                         |
| `Gate`                         | `ubuntu-24.04` | The required check described above                                                                                                                           |

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

These workflows are advisory, never required: CodeQL, OpenSSF Scorecard, the online zizmor audits, `Scheduled health`
(strict NuGet audit, newest .NET 10 SDK canary, repeat runs of the threading-sensitive tests) and dependency submission.
A maintainer can add the `dry-run` label to a pull request to run `Scheduled health` and the dependency detection once
on it.

### Run the CI checks locally

The build already enforces formatting, compiler and analyzer rules. The remaining checks run from the repository root;
actionlint 1.7.12 and zizmor 1.30.1 are the versions CI pins:

```powershell
dotnet format whitespace . --folder --verify-no-changes --exclude artifacts
actionlint
zizmor --offline .github
./eng/ci/Invoke-ScriptAnalysis.ps1 -ModuleDirectory (Join-Path ([IO.Path]::GetTempPath()) 'PSScriptAnalyzer-1.25.0')
./eng/Update-LockFiles.ps1 -Verify
```

[`eng/ci/README.md`](eng/ci/README.md) describes every CI script and how to run it locally.

### Coverage floors

The Debug leg merges the coverage of every test module and fails when the line coverage of a shipping assembly falls
more than the tolerance below its floor in [`eng/coverage-baseline.json`](eng/coverage-baseline.json). Its job summary
suggests new floors. Raise a floor by copying the suggested value into that file in a reviewed commit; CI never writes
it. Lower a floor only with a justification in the pull request.

### Runner labels

Jobs run on `windows-2025` and `ubuntu-24.04`, never on a moving `-latest` label, and Dependabot does not update
`runs-on`. To move to a new runner image, change the label in every workflow, in `_pinnedRunners` of
`WorkflowContractTests`, and in the runner label list of [`eng/ci/build-info.v0.schema.json`](eng/ci/build-info.v0.schema.json),
in one pull request.

### Flaky tests

No required run retries a test, and `--fail-skips on` rules out skipping one. A flaky test is fixed or deleted in the
pull request that finds it. `Scheduled health` repeats the threading-sensitive test modules every day without retries
and opens an issue when a scheduled run fails.

## Style and analyzers

- Follow [`.editorconfig`](.editorconfig): tab-indented C#, two-space project and configuration files. Builds treat
  formatting, compiler and analyzer diagnostics as errors; run `dotnet format` on the projects you touch. A commit that
  only reformats code is listed in [`.git-blame-ignore-revs`](.git-blame-ignore-revs).
- Use file-scoped namespaces, explicit types instead of `var`, braces, explicit accessibility and `_camelCase` private
  fields. Test names are PascalCase sentences.
- Public APIs require XML documentation. A public API change in Abstractions, Fluent, Hosting, the DI extensions or the
  facade is declared in that project's `PublicAPI.Unshipped.txt` (RS0016/RS0017 are errors); `PublicAPI.Shipped.txt`
  changes only in a release pull request.
- Core implementation types stay `internal sealed`, and every Cheat Engine interaction goes through the Client's
  dispatcher and ports; the SDK remains the only native authority.
- The architecture ratchet in `tests/CheatEngine.Client.Tests/Architecture` freezes the Client's remaining ADR-01 debt:
  the Lua globals it binds itself and its direct Lua and owner usages. Shrinking a list is always allowed; growing it
  requires a registered exception with a removal entry in the SDK 2.0 migration guide (`docs/migration/sdk-2.0.md`).

## Evidence and qualification levels

State the qualification level of every validation you report: **C0** static contract, **C1** managed tests or test
doubles, **C2** native fixture, **C3** the exact Cheat Engine host with a loaded plugin, **C4** several components (two
plugins, a target switch). A C1 or C2 success is never presented as host qualification, and a Native AOT publication is
never a Cheat Engine load. CI runs static and managed tests only; the Cheat Engine 7.7 x64 live suite is opt-in and never
runs in CI.

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
squash-merged once the required checks pass, so the pull request title becomes the commit subject on `main`.

### Pull request policy

`PR policy` applies the rules of [`eng/ci/pr-policy.json`](eng/ci/pr-policy.json):

- **Title:** an imperative sentence (`Add`, `Fix`, `Keep`...) that starts with an uppercase letter, has at most 72
  characters, no Conventional Commit prefix such as `feat:` and no trailing period.
- **Changelog:** a change under `libs/`, `src/`, `source-generators/` or `templates/` (lock files excepted) needs an entry
  under `## [Unreleased]` in `CHANGELOG.md`. When nothing consumer-visible changes, put the line
  `<!-- changelog: not-needed -->` on its own line in the description instead.
- **Dependabot:** its pull requests are exempt. Rename their `chore(deps): ...` squash subject to an imperative sentence
  when merging.

Check a planned title and change set before opening the pull request (the description is read from `$env:PR_BODY`):

```powershell
./eng/ci/Test-PullRequestPolicy.ps1 -Title 'Add a Core option' -Author $env:USERNAME -ChangedPath 'libs/CheatEngine.Client.Core/X.cs', 'CHANGELOG.md'
```

## Dependency updates

Dependabot opens weekly pull requests for NuGet packages, GitHub Actions (the workflows and `.github/actions`) and the
.NET SDK of `global.json`. A new release waits 7 days (30 for a NuGet major); security updates are not delayed. Minor
and patch updates are grouped. Dependabot ignores `CheatEngine.SDK` majors (a Client migration), the Roslyn packages
(they move with the Lua generator's compiler floor, `CHEATENGINECLIENT9020`), the SDK-implicit ILLink and ILCompiler
packages (they move with `global.json`) and .NET SDK majors ([`.github/dependabot.yml`](.github/dependabot.yml)).

Dependabot does not regenerate every lock file. When `Lock files` fails on a Dependabot pull request, check the branch
out, run `./eng/Update-LockFiles.ps1`, commit `Regenerate lock files after <update>` and push; Dependabot then stops
rebasing that pull request. A .NET SDK update also needs the new version in `sdk.errorMessage`: apply the latest
`sdk-canary-patch` artifact of `Scheduled health` instead, after restoring the `global.json` of `main`
(`git checkout origin/main -- global.json`, then `git apply sdk-canary.patch`).

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
