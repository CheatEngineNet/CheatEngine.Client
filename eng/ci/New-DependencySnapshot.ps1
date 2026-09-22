<#
.SYNOPSIS
Detects the NuGet dependency graph of the locked restore with a pinned Component Detection binary and writes a GitHub
dependency snapshot (https://docs.github.com/en/rest/dependency-graph/dependency-submission). It never submits it.

.DESCRIPTION
1. Downloads component-detection-win-x64.exe of microsoft/component-detection v8.0.1 (tag commit
   a35446f34c87640d220e14111582b9250f8bc8f3) into a temporary folder outside the repository and verifies its SHA-256
   before running it. The official submission action is not used: it downloads the latest release at run time.
2. Re-runs the locked restore of CheatEngine.Client.slnx with a binary log under artifacts/logs, so the default-on
   MSBuildBinaryLog detector can mark the packages of test projects as development dependencies. The binary log can
   contain environment variables: it stays on the machine and is never uploaded.
3. Scans the repository with the NuGet detectors (artifacts/ stays in the scan: project.assets.json lives under
   artifacts/obj because of ArtifactsPath).
4. Converts the scan manifest into the snapshot body: one manifest per project (repository-relative, forward slashes),
   `direct` for packages the project references explicitly, `development` for the detector's development dependencies,
   and the exact dependency edges of each project's graph. Non-NuGet components are skipped.
5. Sanity gate: at least 20 distinct NuGet packages, and CheatEngine.SDK at the version resolved in
   libs/CheatEngine.Client.Core/packages.lock.json (the consumed SDK of the release tuple).

.PARAMETER OutputPath
Where the snapshot JSON is written (relative to the repository root, or absolute).

.PARAMETER Sha
Commit the snapshot describes (40 or 64 lowercase hex). Defaults to $env:SNAPSHOT_SHA, else the local HEAD.

.PARAMETER Ref
Git ref of the snapshot (refs/...). Defaults to $env:SNAPSHOT_REF, else the local symbolic HEAD.

.PARAMETER Correlator
Groups the snapshots of this workflow over time. Defaults to $env:SNAPSHOT_CORRELATOR, else 'local_nuget'.

.PARAMETER JobId
Run identifier. Defaults to $env:SNAPSHOT_JOB_ID, else 'local'.

.PARAMETER JobUrl
Run URL (optional). Defaults to $env:SNAPSHOT_JOB_URL.

.EXAMPLE
./eng/ci/New-DependencySnapshot.ps1 -OutputPath "$env:TEMP/snapshot.json"
#>
[CmdletBinding()]
param(
    [string] $OutputPath = 'artifacts/dependency-snapshot/snapshot.json',
    [string] $Sha = $env:SNAPSHOT_SHA,
    [string] $Ref = $env:SNAPSHOT_REF,
    [string] $Correlator = $env:SNAPSHOT_CORRELATOR,
    [string] $JobId = $env:SNAPSHOT_JOB_ID,
    [string] $JobUrl = $env:SNAPSHOT_JOB_URL
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Pinned detector: bump the values together after reading the release and its asset digest with
# gh api repos/microsoft/component-detection/releases/tags/<tag>.
$DetectorVersion = '8.0.1'
$DetectorAsset = 'component-detection-win-x64.exe'
$DetectorSha256 = '9539f792cd2ae7d719922db45df763ec4454cc380fbfcbe685da9a90c1b39cf2'
$DetectorUrl = "https://github.com/microsoft/component-detection/releases/download/v$DetectorVersion/$DetectorAsset"
$MinimumPackageCount = 20
$InvariantCulture = [System.Globalization.CultureInfo]::InvariantCulture

$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$Solution = Join-Path $RepositoryRoot 'CheatEngine.Client.slnx'

function Invoke-Native {
    param([string] $FilePath, [string[]] $ArgumentList, [string] $Description)
    Write-Host "> $FilePath $($ArgumentList -join ' ')"
    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Get-GitValue {
    param([string[]] $ArgumentList)
    $value = & git -C $RepositoryRoot @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "git $($ArgumentList -join ' ') failed with exit code $LASTEXITCODE."
    }

    return ([string] $value).Trim()
}

function Get-ConsumedSdkVersion {
    $lockPath = Join-Path $RepositoryRoot 'libs/CheatEngine.Client.Core/packages.lock.json'
    $lock = Get-Content -LiteralPath $lockPath -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
    foreach ($framework in $lock['dependencies'].Values) {
        if ($framework.Contains('CheatEngine.SDK')) {
            return [string] $framework['CheatEngine.SDK']['resolved']
        }
    }

    throw "CheatEngine.SDK is not resolved in $lockPath."
}

function ConvertTo-OrdinalSet {
    param([object] $Item)
    $set = [System.Collections.Generic.HashSet[string]]::new([string[]] @($Item), [System.StringComparer]::Ordinal)
    # The unary comma keeps the set whole: PowerShell would otherwise enumerate it (an empty set would become $null).
    return , $set
}

function Get-PackageUrl {
    param([object] $Component)
    return "pkg:nuget/$($Component.name.Replace('@', '%40'))@$($Component.version)"
}

if (-not $Sha) { $Sha = Get-GitValue @('rev-parse', 'HEAD') }
if (-not $Ref) { $Ref = Get-GitValue @('symbolic-ref', '--quiet', 'HEAD') }
if (-not $Correlator) { $Correlator = 'local_nuget' }
if (-not $JobId) { $JobId = 'local' }
if (-not [regex]::IsMatch($Sha, '^[0-9a-f]{40}([0-9a-f]{24})?$')) {
    throw "The snapshot commit must be a full lowercase commit id; got '$Sha'."
}

if (-not $Ref.StartsWith('refs/', [System.StringComparison]::Ordinal)) {
    throw "The snapshot ref must start with refs/; got '$Ref'."
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) "cheatengine-client-snapshot-$([guid]::NewGuid().ToString('N'))"
$logs = Join-Path $work 'logs'
New-Item -ItemType Directory -Path $logs | Out-Null
$detector = Join-Path $work $DetectorAsset
$manifest = Join-Path $work 'component-detection-manifest.json'

Write-Host "Downloading Component Detection v$DetectorVersion."
Invoke-WebRequest -Uri $DetectorUrl -OutFile $detector -MaximumRetryCount 3 -RetryIntervalSec 5
$actualSha256 = (Get-FileHash -LiteralPath $detector -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualSha256 -cne $DetectorSha256) {
    throw "$DetectorAsset v$DetectorVersion has SHA-256 $actualSha256, expected $DetectorSha256. Refusing to run it."
}

$binaryLog = Join-Path $RepositoryRoot 'artifacts/logs/dependency-restore.binlog'
Invoke-Native 'dotnet' @('restore', $Solution, '--locked-mode', "-bl:$binaryLog") 'Locked restore with a binary log'

Invoke-Native $detector @(
    'scan', '--SourceDirectory', $RepositoryRoot, '--ManifestFile', $manifest,
    '--DetectorCategories', 'NuGet', '--Output', $logs, '--LogLevel', 'Warning'
) 'Component Detection'

$scan = Get-Content -LiteralPath $manifest -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
$components = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
foreach ($found in @($scan['componentsFound'])) {
    $component = $found['component']
    if ($component['type'] -ceq 'NuGet') {
        $components[[string] $component['id']] = [pscustomobject]@{
            name    = $component['name']
            version = $component['version']
        }
    }
}

$manifests = [ordered]@{}
foreach ($location in @($scan['dependencyGraphs'].Keys | Sort-Object)) {
    $relative = [System.IO.Path]::GetRelativePath($RepositoryRoot, $location).Replace('\', '/')
    if ($relative.StartsWith('../', [System.StringComparison]::Ordinal) -or [System.IO.Path]::IsPathRooted($relative)) {
        continue
    }

    $graph = $scan['dependencyGraphs'][$location]
    $explicit = ConvertTo-OrdinalSet $graph['explicitlyReferencedComponentIds']
    $development = ConvertTo-OrdinalSet $graph['developmentDependencies']
    $resolved = [ordered]@{}
    foreach ($id in @($graph['graph'].Keys | Sort-Object)) {
        if (-not $components.ContainsKey($id)) {
            continue
        }

        $children = @(@($graph['graph'][$id]) |
                Where-Object { $_ -and $components.ContainsKey($_) } |
                ForEach-Object { Get-PackageUrl $components[$_] } |
                Sort-Object -Unique)
        $packageUrl = Get-PackageUrl $components[$id]
        $resolved[$packageUrl] = [ordered]@{
            package_url  = $packageUrl
            relationship = if ($explicit.Contains($id)) { 'direct' } else { 'indirect' }
            scope        = if ($development.Contains($id)) { 'development' } else { 'runtime' }
            dependencies = [string[]] $children
        }
    }

    if ($resolved.Count -gt 0) {
        $manifests[$relative] = [ordered]@{
            name     = $relative
            file     = [ordered]@{ source_location = $relative }
            resolved = $resolved
        }
    }
}

$job = [ordered]@{ correlator = $Correlator; id = $JobId }
if ($JobUrl) { $job['html_url'] = $JobUrl }
$snapshot = [ordered]@{
    version   = 0
    sha       = $Sha
    ref       = $Ref
    job       = $job
    detector  = [ordered]@{
        name    = 'Microsoft Component Detection'
        version = $DetectorVersion
        url     = 'https://github.com/microsoft/component-detection'
    }
    scanned   = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ', $InvariantCulture)
    manifests = $manifests
}

$packageUrls = @($manifests.Values | ForEach-Object { $_.resolved.Keys } | Sort-Object -Unique)
$entries = @($manifests.Values | ForEach-Object { $_.resolved.Values })
$direct = @($entries | Where-Object { $_.relationship -ceq 'direct' })
$developmentEntries = @($entries | Where-Object { $_.scope -ceq 'development' })
$sdkUrl = "pkg:nuget/CheatEngine.SDK@$(Get-ConsumedSdkVersion)"
$lines = @(
    '## Dependency snapshot',
    '',
    "Component Detection v$DetectorVersion (SHA-256 verified). Commit ``$Sha``, ref ``$Ref``.",
    '',
    '| Manifests | Distinct NuGet packages | Direct references | Development entries |',
    '|---|---|---|---|',
    "| $($manifests.Count) | $($packageUrls.Count) | $($direct.Count) | $($developmentEntries.Count) |"
)
$lines | ForEach-Object { Write-Host $_ }
if ($env:GITHUB_STEP_SUMMARY) {
    $lines | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}

if ($packageUrls.Count -lt $MinimumPackageCount) {
    $count = $packageUrls.Count
    throw "Only $count NuGet packages detected (at least $MinimumPackageCount): the scan missed the restore."
}

if (-not ($packageUrls -ccontains $sdkUrl)) {
    throw "The snapshot lacks $sdkUrl, the CheatEngine.SDK of libs/CheatEngine.Client.Core/packages.lock.json."
}

$output = $OutputPath
if (-not [System.IO.Path]::IsPathRooted($output)) {
    $output = Join-Path $RepositoryRoot $output
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $output) | Out-Null
$json = $snapshot | ConvertTo-Json -Depth 20
[System.IO.File]::WriteAllText($output, $json, [System.Text.UTF8Encoding]::new($false))
Remove-Item -LiteralPath $work -Recurse -Force
Write-Host "Snapshot written to $output."
