<#
.SYNOPSIS
Runs one part of the advisory "Scheduled health" workflow: strict NuGet audit, newest .NET 10 SDK canary, or the
repeat run of the threading-sensitive test modules.

.DESCRIPTION
Modes:

- Audit: restores CheatEngine.Client.slnx with --locked-mode --force into a fresh NUGET_PACKAGES folder (under
  RUNNER_TEMP on a runner), so every package is downloaded again and its content hash is checked against the committed
  lock files (this re-validates the consumed CheatEngine.SDK package weekly). The restore runs with
  -p:AuditPipeline=true, the repository's dedicated audit switch that turns every NuGet audit warning into an error
  (https://learn.microsoft.com/nuget/concepts/auditing-packages#running-nuget-audit-in-ci). --force re-resolves even an
  up-to-date graph and is compatible with --locked-mode (re-evaluating is not: NU1005). Then
  `dotnet package list --vulnerable` and `--deprecated` (they cannot be combined) print summary tables. Fails when the
  restore fails or when any vulnerable package is listed, whatever its severity; deprecated packages are warnings.
- Canary: with the SDK that global.json names (eng/ci/Select-NewestDotNetSdk.ps1 points it at the newest 10.0 SDK
  before the composite setup action installs it; that step lives in its own script because it must run before any
  SDK exists), regenerates every lock file through eng/Update-LockFiles.ps1 (the repository's only regeneration
  path), writes the global.json and lock-file diff to artifacts/health/sdk-canary.patch, then builds, packs and tests
  the solution in Release like the CI Release leg.
- Repeat: builds the threading-sensitive test modules once in Debug, runs each of them -Iterations times, and fails
  after the last iteration when any run failed, listing every test that failed at least once. No retry extension:
  a flaky test is fixed or deleted, never retried (flaky-test policy).

.PARAMETER Mode
Audit, Canary or Repeat.

.PARAMETER Iterations
Repeat mode: number of runs per module.

.PARAMETER ResultsRoot
Output folder for patches, test results and reports, relative to the repository root.

.PARAMETER Project
Repeat mode: the test projects to repeat (repository-relative).

.EXAMPLE
./eng/ci/Invoke-ScheduledHealth.ps1 -Mode Repeat -Iterations 1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Audit', 'Canary', 'Repeat')]
    [string] $Mode,

    [ValidateRange(1, 50)]
    [int] $Iterations = 5,

    [string] $ResultsRoot = 'artifacts/health',

    [string[]] $Project = @(
        'tests/CheatEngine.Client.Core.Tests/CheatEngine.Client.Core.Tests.csproj',
        'tests/CheatEngine.Client.Hosting.Tests/CheatEngine.Client.Hosting.Tests.csproj'
    )
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$Solution = Join-Path $RepositoryRoot 'CheatEngine.Client.slnx'

function Resolve-RepositoryPath {
    param([string] $Path)
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $RepositoryRoot $Path))
}

function Invoke-Native {
    param([string] $FilePath, [string[]] $ArgumentList, [string] $Description)
    Write-Host "> $FilePath $($ArgumentList -join ' ')"
    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Write-Summary {
    param([string[]] $Line)
    $Line | ForEach-Object { Write-Host $_ }
    if ($env:GITHUB_STEP_SUMMARY) {
        $Line | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
    }
}

function Get-GlobalJsonSdkVersion {
    param([string] $Path)
    $json = Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
    return [string] $json['sdk']['version']
}

function Get-PackageListEntry {
    param([string] $Kind)
    $output = & dotnet package list --project $Solution --no-restore "--$Kind" --include-transitive --format json
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet package list --$Kind failed with exit code $LASTEXITCODE."
    }

    $report = ($output -join "`n") | ConvertFrom-Json
    if ($report.PSObject.Properties['problems']) {
        foreach ($problem in @($report.problems)) {
            Write-Host "::warning title=NuGet audit::$($problem.text)"
        }
    }

    foreach ($projectReport in @($report.projects)) {
        $relative = [System.IO.Path]::GetRelativePath($RepositoryRoot, $projectReport.path).Replace('\', '/')
        if (-not $projectReport.PSObject.Properties['frameworks']) {
            continue
        }

        foreach ($framework in @($projectReport.frameworks)) {
            foreach ($scope in @('topLevelPackages', 'transitivePackages')) {
                if (-not $framework.PSObject.Properties[$scope]) {
                    continue
                }

                foreach ($package in @($framework.$scope)) {
                    [pscustomobject]@{
                        Project   = $relative
                        Framework = $framework.framework
                        Package   = $package.id
                        Version   = $package.resolvedVersion
                        Scope     = if ($scope -ceq 'topLevelPackages') { 'direct' } else { 'transitive' }
                        Detail    = $package
                    }
                }
            }
        }
    }
}

function Invoke-Audit {
    $folder = "cheatengine-client-audit-$([guid]::NewGuid().ToString('N'))"
    $temporary = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [System.IO.Path]::GetTempPath() }
    $packages = Join-Path $temporary $folder
    New-Item -ItemType Directory -Path $packages | Out-Null
    $env:NUGET_PACKAGES = $packages
    Write-Host "NUGET_PACKAGES=$packages (fresh: every package is downloaded and checked against the lock files)."

    $restore = @('restore', $Solution, '--locked-mode', '--force', '-p:AuditPipeline=true')
    Invoke-Native 'dotnet' $restore 'Strict audit restore'

    $vulnerable = @(Get-PackageListEntry 'vulnerable')
    $deprecated = @(Get-PackageListEntry 'deprecated')

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('## Strict NuGet audit')
    $lines.Add('')
    $lines.Add("Locked restore with ``-p:AuditPipeline=true`` into a fresh package folder: passed.")
    $lines.Add('')
    $lines.Add("### Vulnerable packages ($($vulnerable.Count))")
    $lines.Add('')
    if ($vulnerable.Count -gt 0) {
        $lines.Add('| Project | Framework | Package | Version | Scope | Severity | Advisory |')
        $lines.Add('|---|---|---|---|---|---|---|')
        foreach ($entry in $vulnerable) {
            foreach ($advisory in @($entry.Detail.vulnerabilities)) {
                $cells = @($entry.Project, $entry.Framework, $entry.Package, $entry.Version, $entry.Scope,
                    $advisory.severity, $advisory.advisoryurl)
                $lines.Add("| $($cells -join ' | ') |")
            }
        }
    }
    else {
        $lines.Add('None.')
    }

    $lines.Add('')
    $lines.Add("### Deprecated packages ($($deprecated.Count))")
    $lines.Add('')
    if ($deprecated.Count -gt 0) {
        $lines.Add('| Project | Framework | Package | Version | Scope | Reasons | Alternative |')
        $lines.Add('|---|---|---|---|---|---|---|')
        foreach ($entry in $deprecated) {
            $reasons = @($entry.Detail.deprecationReasons) -join ', '
            $alternative = ''
            if ($entry.Detail.PSObject.Properties['alternativePackage']) {
                $alternative = $entry.Detail.alternativePackage.id
            }

            $cells = @($entry.Project, $entry.Framework, $entry.Package, $entry.Version, $entry.Scope,
                $reasons, $alternative)
            $lines.Add("| $($cells -join ' | ') |")
        }
    }
    else {
        $lines.Add('None.')
    }

    Write-Summary $lines.ToArray()
    foreach ($entry in $deprecated) {
        Write-Host "::warning title=Deprecated package::$($entry.Package) $($entry.Version) ($($entry.Project))"
    }

    if ($vulnerable.Count -gt 0) {
        throw "$($vulnerable.Count) vulnerable package reference(s) found; see the summary."
    }
}

function Invoke-Canary {
    param([string] $OutputRoot)
    $sdk = Get-GlobalJsonSdkVersion (Join-Path $RepositoryRoot 'global.json')
    $installed = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $installed -cne $sdk) {
        throw "dotnet --version is '$installed' but global.json names ${sdk}: run the composite setup action first."
    }

    $results = Resolve-RepositoryPath $OutputRoot
    $nuget = Join-Path $results 'canary/nuget'
    $tests = Join-Path $results 'canary/test-results'
    $patch = Join-Path $results 'sdk-canary.patch'
    New-Item -ItemType Directory -Force -Path $nuget, $tests | Out-Null

    $lockScript = Join-Path $RepositoryRoot 'eng/Update-LockFiles.ps1'
    Invoke-Native 'pwsh' @('-NoProfile', '-NonInteractive', '-File', $lockScript) 'eng/Update-LockFiles.ps1'

    $diff = @('-C', $RepositoryRoot, 'diff', '--binary', "--output=$patch", '--', 'global.json', '*packages.lock.json')
    Invoke-Native 'git' $diff 'git diff'
    $changed = @(& git -C $RepositoryRoot diff --name-only -- 'global.json' '*packages.lock.json')
    if ($LASTEXITCODE -ne 0) {
        throw "git diff --name-only failed with exit code $LASTEXITCODE."
    }

    $header = @('## Newest SDK canary', '', "SDK $sdk. Changed files in sdk-canary.patch: $($changed.Count).", '')
    Write-Summary ($header + @($changed | ForEach-Object { "- ``$_``" }))

    Invoke-Native 'dotnet' @('build', $Solution, '--configuration', 'Release', '--no-restore') 'Release build'
    $pack = @('pack', $Solution, '--configuration', 'Release', '--no-build', '--output', $nuget)
    Invoke-Native 'dotnet' $pack 'Release pack'

    $env:CHEATENGINE_CLIENT_PACKAGE_SOURCE = $nuget
    Invoke-Native 'dotnet' @(
        'test', '--solution', $Solution, '--configuration', 'Release', '--no-build',
        '--results-directory', $tests, '--fail-skips', 'on', '--report-trx'
    ) 'Release tests'
    Write-Summary @('', 'Release build, pack and tests passed with this SDK.')
}

function Invoke-Repeat {
    param([string[]] $TestProject, [int] $Count, [string] $OutputRoot)
    $results = Join-Path (Resolve-RepositoryPath $OutputRoot) 'repeat'
    $testsProps = [System.IO.File]::ReadAllText((Join-Path $RepositoryRoot 'eng/Tests.props'))
    $hangDump = $testsProps.Contains('Microsoft.Testing.Extensions.HangDump', [System.StringComparison]::Ordinal)

    foreach ($path in $TestProject) {
        $build = @('build', (Resolve-RepositoryPath $path), '--configuration', 'Debug', '--no-restore')
        Invoke-Native 'dotnet' $build "Debug build of $path"
    }

    $runs = [System.Collections.Generic.List[object]]::new()
    for ($iteration = 1; $iteration -le $Count; $iteration++) {
        foreach ($path in $TestProject) {
            $module = [System.IO.Path]::GetFileNameWithoutExtension($path)
            $directory = Join-Path $results "$iteration/$module"
            $arguments = @(
                'test', '--project', (Resolve-RepositoryPath $path), '--configuration', 'Debug', '--no-build',
                '--fail-skips', 'on', '--report-trx', '--results-directory', $directory
            )
            if ($hangDump) {
                $arguments += @('--hangdump', '--hangdump-timeout', '10m')
            }

            Write-Host "> dotnet $($arguments -join ' ')"
            & dotnet @arguments
            $runs.Add([pscustomobject]@{
                    Iteration = $iteration
                    Module    = $module
                    ExitCode  = $LASTEXITCODE
                    Directory = $directory
                })
        }
    }

    $failedTests = @{}
    foreach ($trx in @(Get-ChildItem -LiteralPath $results -Filter '*.trx' -Recurse -File)) {
        [xml] $document = Get-Content -LiteralPath $trx.FullName -Raw -Encoding utf8
        $namespace = [System.Xml.XmlNamespaceManager]::new($document.NameTable)
        $namespace.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
        foreach ($result in @($document.SelectNodes('//t:UnitTestResult[@outcome="Failed"]', $namespace))) {
            $name = $result.GetAttribute('testName')
            $failedTests[$name] = 1 + [int] ($failedTests[$name] ?? 0)
        }
    }

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("## Repeat run ($Count iterations)")
    $lines.Add('')
    $lines.Add('| Iteration | Module | Exit code |')
    $lines.Add('|---|---|---|')
    foreach ($run in $runs) {
        $lines.Add("| $($run.Iteration) | $($run.Module) | $($run.ExitCode) |")
    }

    if ($failedTests.Count -gt 0) {
        $lines.Add('')
        $lines.Add('| Test that failed at least once | Failures |')
        $lines.Add('|---|---|')
        foreach ($name in @($failedTests.Keys | Sort-Object)) {
            $lines.Add("| $name | $($failedTests[$name]) |")
        }
    }

    Write-Summary $lines.ToArray()
    $failedRuns = @($runs | Where-Object { $_.ExitCode -ne 0 })
    if ($failedRuns.Count -gt 0) {
        foreach ($name in @($failedTests.Keys | Sort-Object)) {
            $failures = $failedTests[$name]
            Write-Host "::error title=Flaky or failing test::$name failed in $failures of $Count iteration(s)."
        }

        throw "$($failedRuns.Count) of $($runs.Count) test runs failed; fix or delete the unstable tests (no retries)."
    }
}

# ValidateSet accepts any casing of the mode names, so the dispatch ignores case too.
switch ($Mode) {
    'Audit' { Invoke-Audit }
    'Canary' { Invoke-Canary -OutputRoot $ResultsRoot }
    'Repeat' { Invoke-Repeat -TestProject $Project -Count $Iterations -OutputRoot $ResultsRoot }
}
