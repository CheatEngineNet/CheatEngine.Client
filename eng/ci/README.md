# CI scripts

The scripts that the CI pipeline ([`.github/workflows/ci.yml`](../../.github/workflows/ci.yml)) runs, kept outside
the YAML so they run the same way on a developer machine and are analysed by PSScriptAnalyzer. Each script fails with an
exception (and a GitHub `::error::` annotation on Actions) and prints its findings; none of them writes a committed
file. The rules they enforce are also frozen by C# tests in `tests/CheatEngine.Client.Repository.Tests`
(`Workflows/`, `Toolchain/`, `LockFiles/`).

| Script | CI job / step | What it guarantees |
| --- | --- | --- |
| [`Test-PackageSet.ps1`](Test-PackageSet.ps1) | `build-test` (Release), Pack | The pack produced exactly the seven lockstep Client packages and five symbol packages (the `CheatEngine.Client` facade and Templates have none), one version, exact names for a release, and every `.nupkg` embeds its SPDX SBOM (`-RequireSbom`); it summarises each file's SHA-256 and exports `package-source` for the package-consumption tests. |
| [`New-BuildInfo.ps1`](New-BuildInfo.ps1) | `build-test` (Release), Write build info | `build-info.json` records the commit, tree, run, SDK, runner, package hashes and the consumed CheatEngine.SDK read from the Core lock file; it is validated against [`build-info.v0.schema.json`](build-info.v0.schema.json). |
| [`Test-TestModuleInventory.ps1`](Test-TestModuleInventory.ps1) | `build-test`, Test module inventory | Every tracked `tests/**/*.Tests.csproj` produced a TRX report, executed tests and skipped none; Debug also has one coverage report per module. |
| [`Test-CoverageBaseline.ps1`](Test-CoverageBaseline.ps1) | `build-test` (Debug), Coverage ratchet | The merged line coverage of each shipping assembly stays within the tolerance of its floor in [`eng/coverage-baseline.json`](../coverage-baseline.json); the job summary suggests new floors. |
| [`Invoke-ScriptAnalysis.ps1`](Invoke-ScriptAnalysis.ps1) | `lint`, Analyse PowerShell scripts | PSScriptAnalyzer, pinned by version and SHA-256, reports no Error or Warning on any tracked PowerShell file ([settings](../PSScriptAnalyzerSettings.psd1)). |

The lock-file guard ([`eng/Update-LockFiles.ps1`](../Update-LockFiles.ps1) `-Verify`, job `lock-files`) lives one folder
up because it is also the tool developers use to regenerate lock files.

The other workflows run these scripts. None of them is part of `CI / Gate`; `PR policy` is the second required check.
Their rules are frozen by the C# tests in `Governance/`.

| Script | Workflow / job | What it guarantees |
| --- | --- | --- |
| [`Test-PullRequestPolicy.ps1`](Test-PullRequestPolicy.ps1) with [`pr-policy.json`](pr-policy.json) | `pr-policy.yml`, `PR policy` | The pull request title is an imperative sentence of at most 72 characters without a Conventional Commit prefix or a trailing period, and a change under `libs/`, `src/`, `source-generators/` or `templates/` comes with a `CHANGELOG.md` entry unless the description opts out; Dependabot pull requests are exempt. |
| [`Invoke-ScheduledHealth.ps1`](Invoke-ScheduledHealth.ps1) | `scheduled-health.yml` | `-Mode Audit`: a locked restore into a fresh package folder where every NuGet audit code is an error. `-Mode Canary`: lock files, build, pack and tests with the newest .NET 10 SDK, and the `sdk-canary-patch` artifact. `-Mode Repeat`: the threading-sensitive test modules run several times without retries. |
| [`Select-NewestDotNetSdk.ps1`](Select-NewestDotNetSdk.ps1) | `scheduled-health.yml`, SDK canary | Rewrites `global.json` to the newest .NET 10.0 SDK, `sdk.errorMessage` included, before any SDK is installed; it never runs the .NET CLI. |
| [`New-DependencySnapshot.ps1`](New-DependencySnapshot.ps1) | `dependency-submission.yml` | A pinned, hash-checked Component Detection binary turns the locked restore into a GitHub dependency snapshot that names `CheatEngine.SDK` at the locked version; a separate job submits it. |

## Run locally

From the repository root, in PowerShell 7.4 or later, with the .NET SDK of `global.json`:

```powershell
dotnet build CheatEngine.Client.slnx -c Debug
dotnet test --solution CheatEngine.Client.slnx -c Debug --no-build --results-directory artifacts/test-results/Debug `
  --fail-skips on --report-trx --coverage --coverage-output-format xml --filter-not-trait "Category=PackageConsumption"
./eng/ci/Test-TestModuleInventory.ps1 -ResultsDirectory artifacts/test-results/Debug -Configuration Debug -RequireCoverage
./eng/ci/Test-CoverageBaseline.ps1 -ResultsDirectory artifacts/test-results/Debug

dotnet build CheatEngine.Client.slnx -c Release
dotnet pack CheatEngine.Client.slnx -c Release --no-build -o artifacts/nuget
./eng/ci/Test-PackageSet.ps1 -PackageDirectory artifacts/nuget

./eng/ci/Invoke-ScriptAnalysis.ps1 -ModuleDirectory (Join-Path ([IO.Path]::GetTempPath()) 'PSScriptAnalyzer-1.25.0')
./eng/Update-LockFiles.ps1 -Verify

# PR policy for a planned title and change set (the description is read from $env:PR_BODY).
./eng/ci/Test-PullRequestPolicy.ps1 -Title 'Add a Core option' -Author $env:USERNAME -ChangedPath 'libs/CheatEngine.Client.Core/X.cs', 'CHANGELOG.md'
```

`New-BuildInfo.ps1` reads the `GITHUB_*`, `ImageOS` and `ImageVersion` variables that GitHub Actions sets; set them by
hand to try it locally. Consume a locally packed package only through an isolated `NUGET_PACKAGES` folder: every local
pack of a branch has the same version as other branches at the same height.

## Raising a coverage floor

The Debug job summary shows a suggested baseline, each value floored to 0.1. Copy the values you want to keep into
`eng/coverage-baseline.json` in a reviewed commit; CI never writes the file. Lower a floor only with a justification in
the pull request.
