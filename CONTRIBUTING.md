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
`tests/CheatEngine.Client.Repository.Tests/LockFiles/LockFileTests.cs`) to the new package's hashes, regenerate every
`packages.lock.json` (coexistence fixtures first, one project at a time), and update the SDK version named in prose
(`SdkPinTests.ProseMentionsOfTheConsumedSdkEqualThePin` lists the files). `CHEATENGINECLIENT9016`, `9017` and
`CECLIENT017` guard the pin at build and consumption time. Moving to another SDK major is a migration of the Client,
not a dependency bump.

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
| `Build and test (Debug)`       | `windows-2025` | Locked restore, build, one `dotnet test --solution` run with coverage (package consumption tests excluded by trait)              |
| `Build and test (Release)`     | `windows-2025` | Locked restore, build, pack (exact package set, embedded SBOM), benchmark discovery, one test run against the packed packages    |
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
  requires a registered exception, in the ratchet itself, naming its own SDK 2.0 replacement as the removal reason.

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

### Pull request conventions

No required check enforces these; CodeRabbit's automatic review checks them on every push and flags a miss, but it is
advisory and never blocks a merge. Follow them anyway, since the title becomes the squash commit subject on `main`:

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
and patch updates are grouped. Dependabot ignores `CheatEngine.SDK` majors (a Client migration), the Roslyn packages
(they move with the Lua generator's compiler floor, `CHEATENGINECLIENT9020`), the SDK-implicit ILLink and ILCompiler
packages (they move with `global.json`) and .NET SDK majors ([`.github/dependabot.yml`](.github/dependabot.yml)).

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
