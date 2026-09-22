#Requires -Version 7.2
<#
.SYNOPSIS
    Runs PSScriptAnalyzer, pinned by version and SHA-256, over every PowerShell file tracked by git.

.DESCRIPTION
    The module is not installed from the PowerShell Gallery with Install-Module (which would take whatever the gallery
    serves). The script downloads the exact PSScriptAnalyzer package, verifies its SHA-256, extracts it and imports it
    from that folder (https://learn.microsoft.com/powershell/gallery/how-to/working-with-packages/manual-download).

    Every *.ps1, *.psm1 and *.psd1 file of 'git ls-files' is analysed with eng/PSScriptAnalyzerSettings.psd1. Any parse
    error, and any Error or Warning record that the allowlist below does not justify, fails the run. The exit status
    does not rely on -EnableExit, which counts error records only.

.PARAMETER ModuleDirectory
    Where the module is extracted. An existing extraction of the pinned version is reused, which is how local runs avoid
    a download: ./eng/ci/Invoke-ScriptAnalysis.ps1 -ModuleDirectory $env:TEMP/psa

.EXAMPLE
    ./eng/ci/Invoke-ScriptAnalysis.ps1 -ModuleDirectory (Join-Path $env:TEMP 'psa')
#>
[CmdletBinding()]
param(
    [string] $ModuleDirectory = (Join-Path ([IO.Path]::GetTempPath()) 'PSScriptAnalyzer-1.25.0')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$moduleVersion = '1.25.0'
$moduleSha256 = '14e634c828eb98efb9f40b2918ba90f139ed5eccdf663a2a747736d996995d60'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$settings = Join-Path $repositoryRoot 'eng/PSScriptAnalyzerSettings.psd1'

# Justified exceptions: @{ Path = '<repository-relative path>'; Rule = '<rule name>'; Reason = '<why>' }. Keep it empty
# unless a finding is deliberate; an entry that no longer matches a finding fails the run so the list cannot rot.
$allowlist = @()

function Write-Finding {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [int] $Line,
        [Parameter(Mandatory)] [string] $Message
    )

    if ($env:GITHUB_ACTIONS -eq 'true') {
        Write-Host "::error file=$Path,line=$Line,title=PSScriptAnalyzer::$Message"
    }
    else {
        Write-Host "error: ${Path}:${Line}: $Message" -ForegroundColor Red
    }
}

$manifest = Join-Path $ModuleDirectory 'PSScriptAnalyzer.psd1'
if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) {
    New-Item -ItemType Directory -Force -Path $ModuleDirectory | Out-Null
    $package = Join-Path $ModuleDirectory "PSScriptAnalyzer.$moduleVersion.nupkg"
    $uri = "https://www.powershellgallery.com/api/v2/package/PSScriptAnalyzer/$moduleVersion"
    Invoke-WebRequest -Uri $uri -OutFile $package -MaximumRetryCount 3 -RetryIntervalSec 5
    $actual = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $moduleSha256) {
        throw "PSScriptAnalyzer $moduleVersion has SHA-256 $actual, expected $moduleSha256."
    }

    # Expand-Archive accepts only the .zip extension.
    $archive = [IO.Path]::ChangeExtension($package, '.zip')
    Copy-Item -LiteralPath $package -Destination $archive -Force
    Expand-Archive -LiteralPath $archive -DestinationPath $ModuleDirectory -Force
}

Import-Module $manifest -Force
$loaded = Get-Module -Name PSScriptAnalyzer
if ($null -eq $loaded -or $loaded.Version -ne [version] $moduleVersion) {
    throw "Expected PSScriptAnalyzer $moduleVersion from $ModuleDirectory."
}

$files = @(& git -C $repositoryRoot ls-files -- '*.ps1' '*.psm1' '*.psd1')
if ($LASTEXITCODE -ne 0) {
    throw "git ls-files failed with exit code $LASTEXITCODE."
}

$findings = [Collections.Generic.List[object]]::new()
$used = [Collections.Generic.HashSet[int]]::new()
foreach ($file in $files) {
    $fullPath = Join-Path $repositoryRoot $file
    $parseErrors = $null
    [void] [Management.Automation.Language.Parser]::ParseFile($fullPath, [ref] $null, [ref] $parseErrors)
    foreach ($parseError in @($parseErrors)) {
        if ($null -ne $parseError) {
            $findings.Add([pscustomobject] @{ Path = $file; Line = $parseError.Extent.StartLineNumber; Rule = 'ParseError'; Severity = 'ParseError'; Message = $parseError.Message })
        }
    }

    foreach ($record in @(Invoke-ScriptAnalyzer -Path $fullPath -Settings $settings)) {
        if ([string] $record.Severity -notin 'Error', 'Warning', 'ParseError') {
            continue
        }

        $allowed = $false
        for ($index = 0; $index -lt $allowlist.Count; $index++) {
            if ($allowlist[$index].Path -ceq $file -and $allowlist[$index].Rule -ceq $record.RuleName) {
                [void] $used.Add($index)
                $allowed = $true
            }
        }

        if (-not $allowed) {
            $findings.Add([pscustomobject] @{ Path = $file; Line = $record.Line; Rule = $record.RuleName; Severity = [string] $record.Severity; Message = $record.Message })
        }
    }
}

for ($index = 0; $index -lt $allowlist.Count; $index++) {
    if (-not $used.Contains($index)) {
        $findings.Add([pscustomobject] @{ Path = $allowlist[$index].Path; Line = 0; Rule = $allowlist[$index].Rule; Severity = 'Stale'; Message = "The allowlist entry no longer matches a finding; remove it ($($allowlist[$index].Reason))." })
    }
}

Write-Host "PSScriptAnalyzer $moduleVersion analysed $($files.Count) file(s); $($allowlist.Count) allowlisted rule(s)."
if ($findings.Count -gt 0) {
    foreach ($finding in $findings) {
        Write-Finding -Path $finding.Path -Line $finding.Line -Message "$($finding.Severity) $($finding.Rule): $($finding.Message)"
    }

    throw "PSScriptAnalyzer reported $($findings.Count) finding(s)."
}
