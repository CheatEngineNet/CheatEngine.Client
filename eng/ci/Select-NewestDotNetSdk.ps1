<#
.SYNOPSIS
Points global.json at the newest .NET 10.0 SDK, for the SDK canary of the advisory "Scheduled health" workflow.

.DESCRIPTION
The canary job runs this script before the composite setup action, so that the action installs exactly the SDK the
rewritten global.json names. It therefore never invokes the .NET CLI: no SDK is installed yet when it runs
(WorkflowContractTests.EveryDotnetJobUsesTheCompositeSetupAction and
ScheduledHealthWorkflowTests.SdkSelectionNeverRunsTheDotNetCli hold it to that).

The newest SDK is the `latest-sdk` of the .NET 10.0 release metadata, or -SdkVersion when given. global.json is
rewritten in place, as text:

- sdk.version becomes the newest SDK;
- inside the sdk.errorMessage string, every occurrence of the pinned version becomes the newest one, so the message
  still names the exact SDK to install (ToolchainPinTests.GlobalJsonErrorMessageNamesThePinnedSdkVersion runs in the
  canary's Release tests, and the sdk-canary-patch artifact must leave CI green when it is applied);
- everything else stays byte-identical: rollForward, allowPrerelease, the "test" section (without test.runner the
  test command falls back to VSTest) and the formatting.

A parse-and-compare guard fails the script when the rewrite changed anything else. The two text patterns are
mirrored by tests/CheatEngine.Client.Repository.Tests/Governance/GlobalJsonSdkRewrite.cs, which runs the same rewrite
on the committed global.json.

.PARAMETER GlobalJsonPath
The global.json to rewrite (relative to the repository root, or absolute). Point it at a copy to try the script
locally without touching the working tree.

.PARAMETER SdkVersion
Use this 10.0 SDK version instead of reading the release metadata (offline tries of the rewrite).

.EXAMPLE
./eng/ci/Select-NewestDotNetSdk.ps1 -GlobalJsonPath "$env:TEMP/global.json"

.EXAMPLE
./eng/ci/Select-NewestDotNetSdk.ps1 -GlobalJsonPath "$env:TEMP/global.json" -SdkVersion 10.0.499
#>
[CmdletBinding()]
param(
    [string] $GlobalJsonPath = 'global.json',

    [ValidatePattern('^(10\.0\.[1-9][0-9]{2})?$')]
    [string] $SdkVersion = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$ReleaseMetadataUrl = 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'

# The value of "version", then of "errorMessage", inside the "sdk" object (group 2 is the JSON-escaped string value).
$SdkVersionPattern = '("sdk"\s*:\s*\{[^{}]*?"version"\s*:\s*")([^"]*)(")'
$ErrorMessagePattern = '("sdk"\s*:\s*\{[^{}]*?"errorMessage"\s*:\s*")((?:[^"\\]|\\.)*)(")'

function Write-Summary {
    param([string[]] $Line)
    $Line | ForEach-Object { Write-Host $_ }
    if ($env:GITHUB_STEP_SUMMARY) {
        $Line | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
    }
}

# Every occurrence of the pinned version as a whole version (10.0.401 never matches inside 10.0.4011 or 110.0.401).
function Get-PinnedVersionPattern {
    param([string] $Pinned)
    return '(?<![0-9.])' + [regex]::Escape($Pinned) + '(?![0-9])'
}

# Deterministic text of a parsed JSON value, used only to compare two parses.
function ConvertTo-CanonicalJson {
    param([object] $Value)
    if ($Value -is [System.Collections.IDictionary]) {
        $members = foreach ($key in @($Value.Keys | Sort-Object -CaseSensitive)) {
            "$(ConvertTo-CanonicalJson $key):$(ConvertTo-CanonicalJson $Value[$key])"
        }

        return '{' + ($members -join ',') + '}'
    }

    if ($Value -is [System.Collections.IList]) {
        return '[' + (@($Value | ForEach-Object { ConvertTo-CanonicalJson $_ }) -join ',') + ']'
    }

    if ($null -eq $Value) {
        return 'null'
    }

    return "$($Value.GetType().Name):$Value"
}

$path = if ([System.IO.Path]::IsPathRooted($GlobalJsonPath)) {
    [System.IO.Path]::GetFullPath($GlobalJsonPath)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $RepositoryRoot $GlobalJsonPath))
}

$original = [System.IO.File]::ReadAllText($path)
$before = $original | ConvertFrom-Json -AsHashtable
$pinned = [string] $before['sdk']['version']

$newest = $SdkVersion
$source = 'the -SdkVersion parameter'
if (-not $newest) {
    $metadata = Invoke-RestMethod -Uri $ReleaseMetadataUrl -MaximumRetryCount 3 -RetryIntervalSec 5
    $newest = [string] $metadata.'latest-sdk'
    $source = $ReleaseMetadataUrl
    if (-not [regex]::IsMatch($newest, '^10\.0\.[1-9][0-9]{2}$')) {
        throw "Unexpected latest-sdk '$newest' in $ReleaseMetadataUrl."
    }
}

$versionRegex = [regex]::new($SdkVersionPattern)
if ($versionRegex.Count($original) -ne 1) {
    throw "$path must contain exactly one sdk.version."
}

$updated = $versionRegex.Replace($original, { param($match) $match.Groups[1].Value + $newest + $match.Groups[3].Value }, 1)
$before['sdk']['version'] = $newest

$messageRegex = [regex]::new($ErrorMessagePattern)
$messages = $messageRegex.Count($updated)
if ($messages -gt 1) {
    throw "$path must contain at most one sdk.errorMessage."
}

if ($messages -eq 1 -and $newest -cne $pinned) {
    $pinnedRegex = [regex]::new((Get-PinnedVersionPattern $pinned))
    if (-not $pinnedRegex.IsMatch([string] $before['sdk']['errorMessage'])) {
        throw "sdk.errorMessage in $path does not name the pinned SDK $pinned; fix global.json before running the canary."
    }

    $moveMessage = {
        param($match)
        $match.Groups[1].Value + $pinnedRegex.Replace($match.Groups[2].Value, $newest) + $match.Groups[3].Value
    }
    $updated = $messageRegex.Replace($updated, $moveMessage, 1)
    $before['sdk']['errorMessage'] = $pinnedRegex.Replace([string] $before['sdk']['errorMessage'], $newest)
}

$after = $updated | ConvertFrom-Json -AsHashtable
if ((ConvertTo-CanonicalJson $before) -cne (ConvertTo-CanonicalJson $after)) {
    throw "Rewriting $path changed more than sdk.version and the SDK version named by sdk.errorMessage."
}

[System.IO.File]::WriteAllText($path, $updated, [System.Text.UTF8Encoding]::new($false))
if ($env:GITHUB_OUTPUT) {
    "sdk-version=$newest" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "pinned-version=$pinned" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}

$verdict = if ($newest -ceq $pinned) {
    'The newest 10.0 SDK equals the pinned SDK: the canary rebuilds with the pinned SDK.'
}
else {
    "A newer 10.0 SDK exists: the canary builds with $newest instead of $pinned (sdk.version and sdk.errorMessage)."
}

Write-Summary @('## Newest .NET 10 SDK', '', "Pinned: $pinned. Newest: $newest (from $source).", '', $verdict)
