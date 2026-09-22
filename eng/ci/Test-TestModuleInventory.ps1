#Requires -Version 7.2
<#
.SYNOPSIS
    Proves that every test module of the repository ran: the *.Tests projects tracked by git must equal the TRX reports
    of the solution test run, and every module must have executed tests.

.DESCRIPTION
    'dotnet test --solution' runs each *.Tests project as its own Microsoft.Testing.Platform module. A module that a
    solution edit, a build condition or a crash drops would otherwise vanish silently from a green run. The TRX
    extension (2.3.0 and later) names each report {asm}_{tfm}_{arch}.trx, so the set of assembly prefixes of the
    top-level reports must equal the set of *.Tests project names. MTP 2.4 may also write consolidated reports under
    <results>/merged; only the top level is read.

    The script prints a per-module table (passed, failed, skipped) from each report's ResultSummary/Counters to the
    console and, on GitHub Actions, to the job summary. With -RequireCoverage it also requires one top-level coverage
    XML report per module (the Debug leg).

    https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-test-reports#report-file-names

.PARAMETER ResultsDirectory
    The --results-directory of the test run.

.PARAMETER Configuration
    The build configuration of the run, printed in the summary.

.PARAMETER RequireCoverage
    Also require one top-level *.xml coverage report per module.

.EXAMPLE
    ./eng/ci/Test-TestModuleInventory.ps1 -ResultsDirectory artifacts/test-results/Debug -Configuration Debug -RequireCoverage
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ResultsDirectory,
    [Parameter(Mandatory)] [string] $Configuration,
    [switch] $RequireCoverage
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

function Write-Failure {
    param([Parameter(Mandatory)] [string] $Message)

    if ($env:GITHUB_ACTIONS -eq 'true') {
        Write-Host "::error title=Test module inventory::$Message"
    }
    else {
        Write-Host "error: $Message" -ForegroundColor Red
    }
}

$resultsPath = if ([IO.Path]::IsPathRooted($ResultsDirectory)) { $ResultsDirectory } else { Join-Path (Get-Location) $ResultsDirectory }
if (-not (Test-Path -LiteralPath $resultsPath -PathType Container)) {
    throw "The results directory '$ResultsDirectory' does not exist: the test step produced nothing."
}

$trackedProjects = @(& git -C $repositoryRoot ls-files -- 'tests/*.Tests.csproj')
if ($LASTEXITCODE -ne 0) {
    throw "git ls-files failed with exit code $LASTEXITCODE."
}

$expectedModules = @($trackedProjects | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) } | Sort-Object -Unique -CaseSensitive)
if ($expectedModules.Count -eq 0) {
    throw 'git lists no tests/**/*.Tests.csproj project.'
}

$reports = @{}
foreach ($report in @(Get-ChildItem -LiteralPath $resultsPath -Filter '*.trx' -File)) {
    if ($report.Name -notmatch '^(?<assembly>.+)_(?<tfm>[^_]+)_(?<arch>[^_]+)\.trx$') {
        throw "Unexpected TRX report name '$($report.Name)'; the TrxReport extension writes {asm}_{tfm}_{arch}.trx."
    }

    $reports[$Matches['assembly']] = $report.FullName
}

$failures = [Collections.Generic.List[string]]::new()
foreach ($module in $expectedModules) {
    if (-not $reports.ContainsKey($module)) {
        $failures.Add("$module did not produce a TRX report: the solution run did not execute it.")
    }
}

foreach ($module in @($reports.Keys | Sort-Object -CaseSensitive)) {
    if ($module -notin $expectedModules) {
        $failures.Add("$([IO.Path]::GetFileName($reports[$module])) belongs to no tracked tests/**/*.Tests.csproj project.")
    }
}

$rows = [Collections.Generic.List[string]]::new()
$totals = @{ Passed = 0; Failed = 0; Skipped = 0 }
foreach ($module in $expectedModules) {
    if (-not $reports.ContainsKey($module)) {
        $rows.Add("| $module | missing | | | |")
        continue
    }

    [xml] $trx = Get-Content -LiteralPath $reports[$module] -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    # GetAttribute returns '' for an attribute the writer omitted, which [int] turns into 0.
    $passed = [int] $counters.GetAttribute('passed')
    $failed = [int] $counters.GetAttribute('failed') + [int] $counters.GetAttribute('error') +
        [int] $counters.GetAttribute('timeout') + [int] $counters.GetAttribute('aborted')
    $skipped = [int] $counters.GetAttribute('notExecuted')
    $executed = [int] $counters.GetAttribute('executed')
    $totals.Passed += $passed
    $totals.Failed += $failed
    $totals.Skipped += $skipped
    $rows.Add("| $module | $($trx.TestRun.ResultSummary.outcome) | $passed | $failed | $skipped |")

    if ($executed -eq 0) {
        $failures.Add("$module executed no test: a filter emptied it or discovery failed.")
    }

    if ($skipped -gt 0) {
        $failures.Add("$module skipped $skipped test(s); CI runs with --fail-skips on and never hides a test behind Skip.")
    }
}

if ($RequireCoverage) {
    $coverageReports = @(Get-ChildItem -LiteralPath $resultsPath -Filter '*.xml' -File)
    if ($coverageReports.Count -ne $expectedModules.Count) {
        $failures.Add("Expected one coverage report per test module ($($expectedModules.Count)), found $($coverageReports.Count) at the top of $ResultsDirectory.")
    }
}

$summary = @(
    "### Test modules ($Configuration)"
    ''
    "$($reports.Count) of $($expectedModules.Count) tracked test modules reported: $($totals.Passed) passed, $($totals.Failed) failed, $($totals.Skipped) skipped."
    ''
    '| Module | Outcome | Passed | Failed | Skipped |'
    '| --- | --- | ---: | ---: | ---: |'
) + $rows
$summary | Out-Host
if ($env:GITHUB_STEP_SUMMARY) {
    $summary + '' | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        Write-Failure -Message $failure
    }

    throw "The test module inventory failed ($($failures.Count) finding(s))."
}
