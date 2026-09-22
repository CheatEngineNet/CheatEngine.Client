#Requires -Version 7.2
<#
.SYNOPSIS
    Asserts that the Release pack produced exactly the Client package set, and exports it for the package-consumption
    tests.

.DESCRIPTION
    The Release leg packs before it tests, and the package-consumption smoke tests install exactly these files
    (CHEATENGINE_CLIENT_PACKAGE_SOURCE), so the packages CI tests are the packages it publishes. This script requires the
    pack output directory to hold exactly:

      - one *.nupkg for each of the seven lockstep Client packages, all with one version;
      - one *.snupkg for each of them except CheatEngine.Client.Templates (a template package has no symbols);
      - nothing else.

    Package identities are read from each package's .nuspec, never parsed from file names, and every file name must be
    <id>.<version>.nupkg or .snupkg. With -PackageVersion (a release) the version must match exactly.

    The script writes a table (file, SHA-256, embedded SBOM) to the console and, on GitHub Actions, to the job summary,
    and sets the step outputs package-source (the absolute directory) and package-version.

.PARAMETER PackageDirectory
    The dotnet pack --output directory.

.PARAMETER PackageVersion
    The exact version a release requires; empty for ordinary runs.

.PARAMETER RequireSbom
    Also require the SPDX software bill of materials (_manifest/spdx_2.2/manifest.spdx.json) in every .nupkg.

.EXAMPLE
    ./eng/ci/Test-PackageSet.ps1 -PackageDirectory artifacts/nuget
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PackageDirectory,
    [string] $PackageVersion = '',
    [switch] $RequireSbom
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem

# The seven lockstep packages. CheatEngine.Client.Templates ships content only, so it has no symbol package.
$packageIds = @(
    'CheatEngine.Client'
    'CheatEngine.Client.Abstractions'
    'CheatEngine.Client.Core'
    'CheatEngine.Client.Extensions.DependencyInjection'
    'CheatEngine.Client.Fluent'
    'CheatEngine.Client.Hosting'
    'CheatEngine.Client.Templates'
)
$packagesWithoutSymbols = @('CheatEngine.Client.Templates')
$sbomEntry = '_manifest/spdx_2.2/manifest.spdx.json'

function Write-Failure {
    param([Parameter(Mandatory)] [string] $Message)

    if ($env:GITHUB_ACTIONS -eq 'true') {
        Write-Host "::error title=Package set::$Message"
    }
    else {
        Write-Host "error: $Message" -ForegroundColor Red
    }
}

function Read-PackageIdentity {
    param([Parameter(Mandatory)] [IO.FileInfo] $File)

    $archive = [IO.Compression.ZipFile]::OpenRead($File.FullName)
    try {
        $nuspecs = @($archive.Entries | Where-Object { $_.FullName -notmatch '/' -and $_.FullName -like '*.nuspec' })
        if ($nuspecs.Count -ne 1) {
            throw "$($File.Name) must contain exactly one root .nuspec, found $($nuspecs.Count)."
        }

        $reader = [IO.StreamReader]::new($nuspecs[0].Open())
        try {
            [xml] $nuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        return [pscustomobject] @{
            Id      = [string] $nuspec.package.metadata.id
            Version = [string] $nuspec.package.metadata.version
            HasSbom = $null -ne $archive.GetEntry($sbomEntry)
        }
    }
    finally {
        $archive.Dispose()
    }
}

$directory = [IO.Path]::GetFullPath($PackageDirectory, (Get-Location).Path)
if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
    throw "The package directory '$PackageDirectory' does not exist: dotnet pack produced nothing."
}

$failures = [Collections.Generic.List[string]]::new()
$files = @(Get-ChildItem -LiteralPath $directory -Force | Sort-Object -Property Name -CaseSensitive)
$packages = @{}
$symbols = @{}
$rows = [Collections.Generic.List[string]]::new()

foreach ($file in $files) {
    if ($file.PSIsContainer -or $file.Extension -notin '.nupkg', '.snupkg') {
        $failures.Add("Unexpected entry '$($file.Name)' in $PackageDirectory; the pack output holds only the Client .nupkg and .snupkg files.")
        continue
    }

    $identity = Read-PackageIdentity -File $file
    $expectedName = "$($identity.Id).$($identity.Version)$($file.Extension)"
    if ($file.Name -cne $expectedName) {
        $failures.Add("$($file.Name) contains $($identity.Id) $($identity.Version); expected the file name $expectedName.")
    }

    if ($identity.Id -cnotin $packageIds) {
        $failures.Add("$($file.Name) is $($identity.Id), which is not one of the seven Client packages.")
    }

    $target = if ($file.Extension -eq '.nupkg') { $packages } else { $symbols }
    if ($target.ContainsKey($identity.Id)) {
        $failures.Add("$PackageDirectory holds more than one $($file.Extension) for $($identity.Id).")
    }

    $target[$identity.Id] = $identity
    $sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $sbom = if ($file.Extension -ne '.nupkg') { 'n/a' } elseif ($identity.HasSbom) { 'yes' } else { 'no' }
    $rows.Add("| ``$($file.Name)`` | ``$sha256`` | $sbom |")

    if ($RequireSbom -and $file.Extension -eq '.nupkg' -and -not $identity.HasSbom) {
        $failures.Add("$($file.Name) has no embedded SBOM ($sbomEntry).")
    }
}

foreach ($id in $packageIds) {
    if (-not $packages.ContainsKey($id)) {
        $failures.Add("$PackageDirectory has no $id .nupkg.")
    }

    $needsSymbols = $id -notin $packagesWithoutSymbols
    if ($needsSymbols -and -not $symbols.ContainsKey($id)) {
        $failures.Add("$PackageDirectory has no $id .snupkg.")
    }

    if (-not $needsSymbols -and $symbols.ContainsKey($id)) {
        $failures.Add("$id must not ship a symbol package.")
    }
}

$versions = @(@($packages.Values) + @($symbols.Values) | ForEach-Object Version | Sort-Object -Unique -CaseSensitive)
if ($versions.Count -gt 1) {
    $failures.Add("The Client packages are released in lockstep, but the pack produced several versions: $($versions -join ', ').")
}

if ($PackageVersion -and $versions.Count -eq 1 -and $versions[0] -cne $PackageVersion) {
    $failures.Add("The release requires version $PackageVersion, but the pack produced $($versions[0]).")
}

$version = if ($versions.Count -eq 1) { $versions[0] } else { '' }
$summary = @(
    "### Client packages $version"
    ''
    "$($packages.Count) packages and $($symbols.Count) symbol packages; these exact files feed the package-consumption tests."
    ''
    '| File | SHA-256 | Embedded SBOM |'
    '| --- | --- | --- |'
) + $rows
$summary | Out-Host
if ($env:GITHUB_STEP_SUMMARY) {
    $summary + '' | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        Write-Failure -Message $failure
    }

    throw "The package set is not the Client release set ($($failures.Count) finding(s))."
}

if ($env:GITHUB_OUTPUT) {
    "package-source=$directory", "package-version=$version" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}
else {
    Write-Host "package-source=$directory"
    Write-Host "package-version=$version"
}
