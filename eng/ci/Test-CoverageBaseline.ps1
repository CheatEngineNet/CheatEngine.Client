#Requires -Version 7.2
<#
.SYNOPSIS
    Merges the per-module coverage reports of a test run and enforces the per-assembly line-coverage floors of
    eng/coverage-baseline.json for the shipping assemblies.

.DESCRIPTION
    Each test module of the Debug run writes one Microsoft Code Coverage XML report (the format the Sonar job imports).
    This script restores the pinned dotnet-coverage local tool (.config/dotnet-tools.json) and merges them into
    <OutputDirectory>/merged.cobertura.xml: a union of the line hits of the same build, so a line covered by any module
    counts once.

    It then measures the line coverage of every shipping assembly: the projects of CheatEngine.Client.slnx under src/,
    libs/ and source-generators/ (the Lua generator ships inside CheatEngine.Client.Hosting). Line coverage is lines
    executed at least partly divided by instrumented lines. It is the only metric the merge preserves exactly: the
    Microsoft XML format records basic blocks rather than branches (a Cobertura branch rate converted from it is always
    1.0), and dotnet-coverage keeps only line data for a module that several reports contain.

    Generated sources (*.g.cs, *.generated.cs, *.designer.cs, anything under an obj folder) are excluded; Microsoft
    Code Coverage already skips members marked [GeneratedCode] or [ExcludeFromCodeCoverage], and test assemblies
    (IncludeTestAssembly defaults to false).

    A value fails when it drops below its floor by more than the baseline tolerance (percentage points). The job summary
    shows every value and a suggested baseline (each value floored to 0.1). CI never writes the baseline: raising a floor
    is a reviewed commit. A shipping project without source files (the CheatEngine.Client package facade) is reported as
    not measurable; a shipping assembly with source that is missing from the report fails.

    https://learn.microsoft.com/dotnet/core/additional-tools/dotnet-coverage#dotnet-coverage-merge

.PARAMETER ResultsDirectory
    The --results-directory of the Debug test run; its top-level *.xml files are the per-module coverage reports.

.PARAMETER Baseline
    The floors file.

.PARAMETER OutputDirectory
    Where the merged Cobertura report and the Markdown summary are written (the coverage-report artifact).

.EXAMPLE
    ./eng/ci/Test-CoverageBaseline.ps1 -ResultsDirectory artifacts/test-results/Debug
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ResultsDirectory,
    [string] $Baseline = 'eng/coverage-baseline.json',
    [string] $OutputDirectory = 'artifacts/coverage-report'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$schema = 'cheatengine-coverage-baseline/v0'
$shippingFolders = 'src/', 'libs/', 'source-generators/'
$generatedSource = '(\.g|\.generated|\.designer)\.cs$|[\\/]obj[\\/]'

function Write-Failure {
    param([Parameter(Mandatory)] [string] $Message)

    if ($env:GITHUB_ACTIONS -eq 'true') {
        Write-Host "::error title=Coverage ratchet::$Message"
    }
    else {
        Write-Host "error: $Message" -ForegroundColor Red
    }
}

function Get-ShippingAssembly {
    [xml] $solution = Get-Content -LiteralPath (Join-Path $repositoryRoot 'CheatEngine.Client.slnx') -Raw
    foreach ($project in @($solution.SelectNodes('//Project'))) {
        $path = $project.GetAttribute('Path').Replace('\', '/')
        if (-not @($shippingFolders | Where-Object { $path.StartsWith($_, [StringComparison]::Ordinal) })) {
            continue
        }

        $folder = Join-Path $repositoryRoot (Split-Path -Parent $path)
        $sources = @(Get-ChildItem -LiteralPath $folder -Filter '*.cs' -File -Recurse |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
        [pscustomobject] @{
            Name      = [IO.Path]::GetFileNameWithoutExtension($path)
            HasSource = $sources.Count -gt 0
        }
    }
}

function Get-LineCoverage {
    param([Parameter(Mandatory)] [xml] $Cobertura)

    $result = @{}
    foreach ($package in @($Cobertura.coverage.packages.package)) {
        $lines = @{}
        foreach ($class in @($package.classes.class)) {
            $file = $class.GetAttribute('filename')
            if ($file -match $generatedSource) {
                continue
            }

            foreach ($line in @($class.lines.line)) {
                $key = "$file|$($line.GetAttribute('number'))"
                $hit = [long] $line.GetAttribute('hits') -gt 0
                $lines[$key] = ($lines.ContainsKey($key) -and $lines[$key]) -or $hit
            }
        }

        $covered = @($lines.Values | Where-Object { $_ }).Count
        $rate = if ($lines.Count -eq 0) { 100.0 } else { [Math]::Round(100.0 * $covered / $lines.Count, 2) }
        $result[$package.GetAttribute('name')] = [pscustomobject] @{ Rate = $rate; Covered = $covered; Total = $lines.Count }
    }

    return $result
}

$location = (Get-Location).Path
$resultsPath = [IO.Path]::GetFullPath($ResultsDirectory, $location)
$baselinePath = [IO.Path]::GetFullPath($Baseline, $location)
$outputPath = [IO.Path]::GetFullPath($OutputDirectory, $location)

$reports = @(Get-ChildItem -LiteralPath $resultsPath -Filter '*.xml' -File | ForEach-Object FullName)
if ($reports.Count -eq 0) {
    throw "No coverage report at the top of '$ResultsDirectory'; run the tests with --coverage --coverage-output-format xml."
}

$env:DOTNET_COVERAGE_TELEMETRY_OPTOUT = '1'
$env:DOTNET_COVERAGE_NOLOGO = '1'
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$cobertura = Join-Path $outputPath 'merged.cobertura.xml'

Push-Location -LiteralPath $repositoryRoot
try {
    & dotnet tool restore | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet tool restore failed with exit code $LASTEXITCODE."
    }

    & dotnet tool run dotnet-coverage merge --nologo --output $cobertura --output-format cobertura @reports | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet-coverage merge failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

$coverage = Get-LineCoverage -Cobertura ([xml] (Get-Content -LiteralPath $cobertura -Raw))
$floors = Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json -AsHashtable
if ($floors['schema'] -ne $schema) {
    throw "$Baseline must declare schema '$schema'."
}

$tolerance = [double] $floors['tolerance']
$failures = [Collections.Generic.List[string]]::new()
$rows = [Collections.Generic.List[string]]::new()
$suggested = [ordered] @{}
$shipping = @(Get-ShippingAssembly | Sort-Object -Property Name -CaseSensitive)
$measured = @($shipping | Where-Object HasSource | ForEach-Object Name)

foreach ($name in @($floors['assemblies'].Keys | Sort-Object -CaseSensitive)) {
    if ($name -notin $measured) {
        $failures.Add("$Baseline lists $name, which is not a shipping assembly with source; remove it.")
    }
}

foreach ($assembly in $shipping) {
    $name = $assembly.Name
    if (-not $assembly.HasSource) {
        $rows.Add("| $name | n/a | | | not measurable: no source file (package facade) |")
        continue
    }

    if (-not $coverage.ContainsKey($name)) {
        $failures.Add("$name is a shipping assembly with source but is absent from the merged coverage report: no test module loaded it.")
        $rows.Add("| $name | missing | | | absent from the report |")
        continue
    }

    $actual = $coverage[$name]
    $suggested[$name] = [ordered] @{ line = [Math]::Floor($actual.Rate * 10.0) / 10.0 }
    $lines = "$($actual.Covered) / $($actual.Total)"
    $assemblyFloors = $floors['assemblies'][$name]
    if ($null -eq $assemblyFloors) {
        $failures.Add("$Baseline has no floor for the shipping assembly $name; add the suggested value.")
        $rows.Add("| $name | $($actual.Rate) | $lines | none | no floor |")
        continue
    }

    $floor = [double] $assemblyFloors['line']
    $status = 'ok'
    if ($actual.Rate + $tolerance -lt $floor) {
        $status = 'below floor'
        $failures.Add("$name line coverage is $($actual.Rate)%, below its floor of $floor% (tolerance $tolerance points). Add tests, or lower the floor in $Baseline with a reviewed justification.")
    }

    $rows.Add("| $name | $($actual.Rate) | $lines | $floor | $status |")
}

$suggestion = [ordered] @{ schema = $schema; tolerance = $tolerance; assemblies = $suggested } | ConvertTo-Json -Depth 4
$summary = @(
    '### Line coverage of the shipping assemblies (Debug, merged)'
    ''
    "A value fails more than $tolerance points below its floor in ``$Baseline``."
    ''
    '| Assembly | Line % | Lines | Floor % | Status |'
    '| --- | ---: | ---: | ---: | --- |'
) + $rows + @(
    ''
    '<details><summary>Suggested baseline (CI never writes it; commit it to raise the floors)</summary>'
    ''
    '```json'
    $suggestion
    '```'
    ''
    '</details>'
    ''
)
$summary | Out-File -FilePath (Join-Path $outputPath 'coverage-summary.md') -Encoding utf8
$summary | Out-Host
if ($env:GITHUB_STEP_SUMMARY) {
    $summary | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        Write-Failure -Message $failure
    }

    throw "The coverage ratchet failed ($($failures.Count) finding(s))."
}
