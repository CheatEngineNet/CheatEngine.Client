#Requires -Version 7.4
<#
.SYNOPSIS
    Writes build-info.json: the identity of a Release CI build of the Client packages (eng/ci/build-info.v0.schema.json).

.DESCRIPTION
    A published package must be traceable to the exact source, toolchain and run that produced it. The Release leg runs
    this script after Pack and uploads the result as the build-info artifact; the release workflow builds the Client
    release tuple from it. The document records:

      - the source: repository, checked-out commit and its tree hash, ref, event, run id, attempt and URL, and the pull
        request number and head SHA when the event is pull_request;
      - the toolchain: the .NET SDK that ran and the SHA-256 of global.json (line endings normalised to LF, so the hash
        is the same on every checkout);
      - the runner label and image;
      - the seven packages: id, version, file name, SHA-256 (lowercase hex), SHA-512 (base64, as NuGet records a content
        hash) and the embedded SBOM entry, or null while no SBOM is embedded;
      - the consumed CheatEngine.SDK, read from libs/CheatEngine.Client.Core/packages.lock.json, never from a literal.

    The output is UTF-8 without BOM with LF line endings, holds no absolute local path, and is validated against the
    schema before the script returns.

.PARAMETER PackageDirectory
    The pack output directory (seven *.nupkg files).

.PARAMETER RunnerLabel
    The runs-on label of the job.

.PARAMETER PullRequestNumber
    github.event.pull_request.number, empty outside pull requests.

.PARAMETER PullRequestHeadSha
    github.event.pull_request.head.sha, empty outside pull requests.

.PARAMETER OutputPath
    Where to write the document.

.EXAMPLE
    ./eng/ci/New-BuildInfo.ps1 -PackageDirectory artifacts/nuget -RunnerLabel windows-2025
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PackageDirectory,
    [Parameter(Mandatory)] [string] $RunnerLabel,
    [string] $PullRequestNumber = '',
    [string] $PullRequestHeadSha = '',
    [string] $OutputPath = 'artifacts/build-info/build-info.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$schemaPath = Join-Path $PSScriptRoot 'build-info.v0.schema.json'
$consumedSdkLockFile = 'libs/CheatEngine.Client.Core/packages.lock.json'
$sbomEntry = '_manifest/spdx_2.2/manifest.spdx.json'

function Get-RequiredEnvironment {
    param([Parameter(Mandatory)] [string] $Name)

    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "The environment variable $Name is required (GitHub Actions sets it; set it by hand for a local run)."
    }

    return $value
}

function Invoke-Git {
    param([Parameter(Mandatory)] [string[]] $Arguments)

    $output = @(& git -C $repositoryRoot @Arguments)
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }

    return ($output -join "`n").Trim()
}

function Get-PackageEntry {
    param([Parameter(Mandatory)] [IO.FileInfo] $File)

    $archive = [IO.Compression.ZipFile]::OpenRead($File.FullName)
    try {
        $nuspec = @($archive.Entries | Where-Object { $_.FullName -notmatch '/' -and $_.FullName -like '*.nuspec' })
        if ($nuspec.Count -ne 1) {
            throw "$($File.Name) must contain exactly one root .nuspec."
        }

        $reader = [IO.StreamReader]::new($nuspec[0].Open())
        try {
            [xml] $manifest = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $hasSbom = $null -ne $archive.GetEntry($sbomEntry)
    }
    finally {
        $archive.Dispose()
    }

    $bytes = [IO.File]::ReadAllBytes($File.FullName)
    return [ordered] @{
        id        = [string] $manifest.package.metadata.id
        version   = [string] $manifest.package.metadata.version
        file      = $File.Name
        sha256    = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
        sha512    = [Convert]::ToBase64String([Security.Cryptography.SHA512]::HashData($bytes))
        sbomEntry = if ($hasSbom) { $sbomEntry } else { $null }
    }
}

function Get-ConsumedSdk {
    $lock = Get-Content -LiteralPath (Join-Path $repositoryRoot $consumedSdkLockFile) -Raw | ConvertFrom-Json -AsHashtable
    foreach ($section in $lock['dependencies'].GetEnumerator()) {
        if ($section.Value.ContainsKey('CheatEngine.SDK')) {
            $entry = $section.Value['CheatEngine.SDK']
            return [ordered] @{
                id          = 'CheatEngine.SDK'
                version     = [string] $entry['resolved']
                contentHash = [string] $entry['contentHash']
                lockFile    = $consumedSdkLockFile
            }
        }
    }

    throw "$consumedSdkLockFile does not resolve CheatEngine.SDK."
}

$repository = Get-RequiredEnvironment -Name 'GITHUB_REPOSITORY'
$serverUrl = Get-RequiredEnvironment -Name 'GITHUB_SERVER_URL'
$runId = [long] (Get-RequiredEnvironment -Name 'GITHUB_RUN_ID')
$eventName = Get-RequiredEnvironment -Name 'GITHUB_EVENT_NAME'

# The document describes the tree that was built, so it reads the checkout rather than trusting the event payload.
$commit = Invoke-Git -Arguments @('rev-parse', 'HEAD')
$expectedCommit = $env:GITHUB_SHA
if ($expectedCommit -and $expectedCommit -ne $commit) {
    throw "The checkout is $commit, but GITHUB_SHA is ${expectedCommit}: build-info must describe the commit the run built."
}

$pullRequest = $null
if ($eventName -eq 'pull_request') {
    if (-not ($PullRequestNumber -match '^[0-9]+$') -or -not ($PullRequestHeadSha -match '^[0-9a-f]{40}$')) {
        throw 'A pull_request build must pass -PullRequestNumber and -PullRequestHeadSha (github.event.pull_request.number and .head.sha).'
    }

    $pullRequest = [ordered] @{ number = [long] $PullRequestNumber; headSha = $PullRequestHeadSha }
}

Push-Location -LiteralPath $repositoryRoot
try {
    $dotnetSdk = (@(& dotnet --version) -join '').Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet --version failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

$globalJson = [IO.File]::ReadAllText((Join-Path $repositoryRoot 'global.json')).Replace("`r`n", "`n")
$globalJsonSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($globalJson))).ToLowerInvariant()

$packageFiles = @(Get-ChildItem -LiteralPath ([IO.Path]::GetFullPath($PackageDirectory, (Get-Location).Path)) -Filter '*.nupkg' -File |
    Sort-Object -Property Name -CaseSensitive)
$packages = @($packageFiles | ForEach-Object { Get-PackageEntry -File $_ } | Sort-Object -Property { $_.id } -CaseSensitive)

$document = [ordered] @{
    schema           = 'cheatengine-build-info/v0'
    repository       = $repository
    commit           = $commit
    treeHash         = Invoke-Git -Arguments @('rev-parse', 'HEAD^{tree}')
    ref              = Get-RequiredEnvironment -Name 'GITHUB_REF'
    event            = $eventName
    runId            = $runId
    runAttempt       = [int] (Get-RequiredEnvironment -Name 'GITHUB_RUN_ATTEMPT')
    runUrl           = "$serverUrl/$repository/actions/runs/$runId"
    pullRequest      = $pullRequest
    dotnetSdk        = $dotnetSdk
    globalJsonSha256 = $globalJsonSha256
    runner           = [ordered] @{
        label        = $RunnerLabel
        imageOs      = Get-RequiredEnvironment -Name 'ImageOS'
        imageVersion = Get-RequiredEnvironment -Name 'ImageVersion'
    }
    packages         = $packages
    consumedSdk      = Get-ConsumedSdk
    createdUtc       = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture)
}

$json = ($document | ConvertTo-Json -Depth 6).Replace("`r`n", "`n") + "`n"
$errors = $null
if (-not (Test-Json -Json $json -SchemaFile $schemaPath -ErrorVariable errors -ErrorAction SilentlyContinue)) {
    throw "build-info.json does not match $([IO.Path]::GetFileName($schemaPath)): $(@($errors | ForEach-Object { $_.ToString() }) -join '; ')"
}

$fullOutputPath = [IO.Path]::GetFullPath($OutputPath, (Get-Location).Path)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $fullOutputPath) | Out-Null
[IO.File]::WriteAllText($fullOutputPath, $json, [Text.UTF8Encoding]::new($false))

$summary = @(
    '### Build info'
    ''
    '| Field | Value |'
    '| --- | --- |'
    "| Commit / tree | ``$commit`` / ``$($document.treeHash)`` |"
    "| .NET SDK | $dotnetSdk (global.json SHA-256 ``$globalJsonSha256``) |"
    "| Runner | $RunnerLabel, $($document.runner.imageOs) $($document.runner.imageVersion) |"
    "| Packages | $($packages.Count) at version $(@($packages | ForEach-Object { $_.version } | Sort-Object -Unique) -join ', ') |"
    "| Consumed SDK | $($document.consumedSdk.id) $($document.consumedSdk.version) (contentHash ``$($document.consumedSdk.contentHash)``) |"
    ''
)
$summary | Out-Host
if ($env:GITHUB_STEP_SUMMARY) {
    $summary | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}

Write-Host "Wrote $OutputPath."
