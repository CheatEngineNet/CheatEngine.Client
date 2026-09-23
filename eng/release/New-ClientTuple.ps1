#Requires -Version 7.2
<#
.SYNOPSIS
Writes the Client release tuple (cheatengine-client-tuple/v0) of one release.

.DESCRIPTION
The tuple ties a Client release to everything a compatibility report needs: the seven packages and their hashes, the
exact CheatEngine.SDK package they consume, the Cheat Engine profile, the build environment, the attestations and the
release assets (schema: eng/release/client-tuple.v0.schema.json; RELEASING.md explains the fields).

Every value is read, never typed: package hashes from the files, the build environment from the CI build-info.json,
the consumed SDK from eng/sdk/consumed-sdk.json with its content hash read from the lock file, the Roslyn floor and
analysis level as MSBuild evaluates them for a shipped project (dotnet msbuild -getProperty, so a property that
Directory.Build.props defines through another property is recorded by value, never as an unexpanded $(...) text),
qualification documents from docs/qualification when they exist (their SHA-256 is computed after CRLF-to-LF
normalization, as for every committed JSON document), and the nuget.org hashes of the Published stage from the output
of Test-PublishedPackages.ps1. The script therefore needs the .NET SDK of global.json.

.PARAMETER Stage
PrePublish (the draft release) or Published (after nuget.org serves the packages).

.PARAMETER Tag
The release tag, for example v0.1.0.

.PARAMETER AssetDirectory
The release asset folder: the seven .nupkg and their .snupkg files, the extracted <Id>.<Version>.spdx.json documents and
the *.sigstore.json attestation bundles.

.PARAMETER BuildInfoPath
The build-info.json of the CI Release leg (artifact build-info).

.PARAMETER ReleaseRunUrl
The URL of the release workflow run.

.PARAMETER PublishedPackagesPath
The JSON written by Test-PublishedPackages.ps1; required for the Published stage.

.PARAMETER PullRequestNumber
The pull request whose squash merge produced the tag commit, when known.

.PARAMETER PullRequestHeadSha
The head commit of that pull request.

.PARAMETER OutputPath
The tuple file; <AssetDirectory>/CheatEngine.Client.<Version>.tuple.json by default.

.PARAMETER AllowMissingAttestations
For a local rehearsal only: writes the expected bundle names and sbom.attested=false when the bundles are absent.

.EXAMPLE
./eng/release/New-ClientTuple.ps1 -Stage PrePublish -Tag v0.1.0 -AssetDirectory artifacts/release -BuildInfoPath artifacts/build-info/build-info.json -ReleaseRunUrl $runUrl
#>
[CmdletBinding()]
param(
	[Parameter(Mandatory)]
	[ValidateSet('PrePublish', 'Published')]
	[string] $Stage,

	[Parameter(Mandatory)]
	[ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$')]
	[string] $Tag,

	[Parameter(Mandatory)]
	[string] $AssetDirectory,

	[Parameter(Mandatory)]
	[string] $BuildInfoPath,

	[Parameter(Mandatory)]
	[string] $ReleaseRunUrl,

	[string] $PublishedPackagesPath,

	[int] $PullRequestNumber,

	[string] $PullRequestHeadSha,

	[string] $OutputPath,

	[switch] $AllowMissingAttestations
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$version = $Tag.Substring(1)
$packageIds = @(
	'CheatEngine.Client', 'CheatEngine.Client.Abstractions', 'CheatEngine.Client.Core',
	'CheatEngine.Client.Extensions.DependencyInjection', 'CheatEngine.Client.Fluent', 'CheatEngine.Client.Hosting',
	'CheatEngine.Client.Templates')
$symbolIds = @(
	'CheatEngine.Client.Abstractions', 'CheatEngine.Client.Core', 'CheatEngine.Client.Extensions.DependencyInjection',
	'CheatEngine.Client.Fluent', 'CheatEngine.Client.Hosting')
if (-not $OutputPath) {
	$OutputPath = Join-Path $AssetDirectory "CheatEngine.Client.$version.tuple.json"
}

function Get-Sha256 {
	param([string] $Path)
	return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-Sha512Base64 {
	param([string] $Path)
	return [Convert]::ToBase64String([System.Security.Cryptography.SHA512]::HashData([System.IO.File]::ReadAllBytes($Path)))
}

function Get-DocumentSha256 {
	# Committed JSON documents are hashed with LF line endings, whatever the checkout's line endings are.
	param([string] $RelativePath)
	$path = Join-Path $repositoryRoot $RelativePath
	if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
		return $null
	}
	$bytes = [System.Text.UTF8Encoding]::new($false).GetBytes([System.IO.File]::ReadAllText($path).Replace("`r`n", "`n"))
	return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Get-AssetPath {
	param([string] $Name)
	$path = Join-Path $AssetDirectory $Name
	if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
		throw "The release asset $Name is missing from $AssetDirectory."
	}
	return $path
}

# Build environment: the CI build-info.json of the Release leg, cross-checked with the files.
$buildInfo = Get-Content -LiteralPath $BuildInfoPath -Raw | ConvertFrom-Json
if ($buildInfo.schema -ne 'cheatengine-build-info/v0' -or $buildInfo.repository -ne 'CheatEngineNet/CheatEngine.Client') {
	throw "$BuildInfoPath is not the build-info.json of CheatEngineNet/CheatEngine.Client."
}
[string[]] $listedIds = @(@($buildInfo.packages) | ForEach-Object { [string]$_.id })
[System.Array]::Sort($listedIds, [System.StringComparer]::Ordinal)
[string[]] $expectedIds = $packageIds.Clone()
[System.Array]::Sort($expectedIds, [System.StringComparer]::Ordinal)
if (($listedIds -join ';') -cne ($expectedIds -join ';')) {
	throw "$BuildInfoPath lists the packages '$($listedIds -join ', ')', not exactly the seven Client packages '$($expectedIds -join ', ')'."
}
foreach ($entry in @($buildInfo.packages)) {
	$file = Get-AssetPath -Name "$($entry.id).$version.nupkg"
	if ($entry.version -ne $version -or $entry.sha256 -ne (Get-Sha256 -Path $file)) {
		throw "build-info.json records $($entry.id) $($entry.version) $($entry.sha256), which is not the release asset $file."
	}
}

# Build options as MSBuild evaluates them for a shipped project, never the raw text of Directory.Build.props: the
# analysis level, for example, is defined through another property. Evaluation only, from the repository root so that
# global.json selects the SDK; nothing is restored or built
# (https://learn.microsoft.com/visualstudio/msbuild/evaluate-items-and-properties).
$buildOptionsProject = 'libs/CheatEngine.Client.Core/CheatEngine.Client.Core.csproj'
Push-Location -LiteralPath $repositoryRoot
try {
	$evaluation = & dotnet msbuild $buildOptionsProject -nologo -getProperty:AnalysisLevel -getProperty:CheatEngineClientRoslynComponentFloor
	if ($LASTEXITCODE -ne 0) {
		throw "Evaluating the build options of $buildOptionsProject failed with exit code $LASTEXITCODE."
	}
}
finally {
	Pop-Location
}
$evaluationText = $evaluation -join "`n"
$jsonStart = $evaluationText.IndexOf('{', [StringComparison]::Ordinal)
if ($jsonStart -lt 0) {
	throw "dotnet msbuild -getProperty printed no JSON for $($buildOptionsProject): $evaluationText"
}
$buildOptions = $evaluationText.Substring($jsonStart) | ConvertFrom-Json
$analysisLevel = [string]$buildOptions.Properties.AnalysisLevel
$roslynFloor = [string]$buildOptions.Properties.CheatEngineClientRoslynComponentFloor
if ($analysisLevel -cnotmatch '^([Ll]atest|[Pp]review|[0-9]+(\.[0-9]+)?)(-[A-Za-z]+)?$') {
	throw "$buildOptionsProject evaluates AnalysisLevel to '$analysisLevel', which is not an analysis level."
}
if ($roslynFloor -cnotmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
	throw "$buildOptionsProject evaluates CheatEngineClientRoslynComponentFloor to '$roslynFloor', which is not a version."
}

# The consumed SDK: the reviewed identity, with the content hash read from the lock file.
$identity = Get-Content -LiteralPath (Join-Path $repositoryRoot 'eng/sdk/consumed-sdk.json') -Raw | ConvertFrom-Json
$lock = Get-Content -LiteralPath (Join-Path $repositoryRoot $identity.lockFile) -Raw | ConvertFrom-Json -AsHashtable
$lockEntry = $lock['dependencies']['net10.0']['CheatEngine.SDK']
if ($lockEntry['resolved'] -ne $identity.version -or $lockEntry['contentHash'] -ne $identity.contentHashSha512) {
	throw "$($identity.lockFile) resolves CheatEngine.SDK $($lockEntry['resolved']) with $($lockEntry['contentHash']), which is not eng/sdk/consumed-sdk.json."
}

$published = @{}
if ($Stage -eq 'Published') {
	if (-not $PublishedPackagesPath) {
		throw 'The Published stage needs -PublishedPackagesPath (the output of Test-PublishedPackages.ps1).'
	}
	foreach ($entry in @(Get-Content -LiteralPath $PublishedPackagesPath -Raw | ConvertFrom-Json)) {
		$published[$entry.id] = $entry
	}
}

$packages = foreach ($id in $packageIds) {
	$nupkg = Get-AssetPath -Name "$id.$version.nupkg"
	$contentHash = Get-Sha512Base64 -Path $nupkg
	$signedSha256 = $null
	if ($Stage -eq 'Published') {
		$record = $published[$id]
		if ($null -eq $record -or $record.contentHashSha512 -ne $contentHash) {
			throw "Test-PublishedPackages.ps1 did not confirm that nuget.org serves $id $version with content hash $contentHash."
		}
		$signedSha256 = $record.nugetOrgSignedSha256
	}
	$snupkg = if ($symbolIds -contains $id) { Get-Sha256 -Path (Get-AssetPath -Name "$id.$version.snupkg") } else { $null }
	[ordered]@{
		id = $id
		version = $version
		attestedAssetSha256 = Get-Sha256 -Path $nupkg
		contentHashSha512 = $contentHash
		nugetOrgSignedSha256 = $signedSha256
		snupkgSha256 = $snupkg
	}
}

$provenanceBundle = "CheatEngine.Client.$version.provenance.sigstore.json"
$sbomBundles = foreach ($id in $packageIds) { [ordered]@{ id = $id; bundle = "$id.$version.sbom.sigstore.json" } }
$attested = $true
foreach ($name in @($provenanceBundle) + @($sbomBundles | ForEach-Object { $_.bundle })) {
	if (-not (Test-Path -LiteralPath (Join-Path $AssetDirectory $name) -PathType Leaf)) {
		if (-not $AllowMissingAttestations) {
			throw "The attestation bundle $name is missing from $AssetDirectory."
		}
		$attested = $false
	}
}
$sbomDocuments = foreach ($id in $packageIds) { [ordered]@{ id = $id; sha256 = Get-Sha256 -Path (Get-AssetPath -Name "$id.$version.spdx.json") } }

[string[]] $assetNames = @(Get-ChildItem -LiteralPath $AssetDirectory -File |
	Where-Object { $_.Name -ne 'SHA256SUMS' -and -not $_.Name.EndsWith('.tuple.json', [StringComparison]::Ordinal) } |
	ForEach-Object Name)
[System.Array]::Sort($assetNames, [System.StringComparer]::Ordinal)
$assets = foreach ($name in $assetNames) { [ordered]@{ name = $name; sha256 = Get-Sha256 -Path (Join-Path $AssetDirectory $name) } }

$receipts = @(
	Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'docs/qualification/receipts') -Recurse -File -Filter 'R-*.json' -ErrorAction SilentlyContinue |
		Where-Object { -not $_.Name.EndsWith('.events.json', [StringComparison]::Ordinal) } |
		ForEach-Object {
			$receipt = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
			if ($receipt.level -in @('C3', 'C4')) {
				[ordered]@{
					receiptId = $receipt.receiptId
					qualificationId = $receipt.qualificationId
					level = $receipt.level
					status = $receipt.status
					sha256 = Get-DocumentSha256 -RelativePath ([System.IO.Path]::GetRelativePath($repositoryRoot, $_.FullName))
				}
			}
		})

$pullRequest = $null
if ($PullRequestNumber -gt 0 -and $PullRequestHeadSha) {
	$pullRequest = [ordered]@{ number = $PullRequestNumber; headSha = $PullRequestHeadSha }
}
elseif ($null -ne $buildInfo.pullRequest) {
	$pullRequest = [ordered]@{ number = [int]$buildInfo.pullRequest.number; headSha = $buildInfo.pullRequest.headSha }
}

$tuple = [ordered]@{
	schema = 'cheatengine-client-tuple/v0'
	stage = $Stage
	source = [ordered]@{
		repository = 'CheatEngineNet/CheatEngine.Client'
		tag = $Tag
		commit = $buildInfo.commit
		treeHash = $buildInfo.treeHash
		pullRequest = $pullRequest
		releaseRunUrl = $ReleaseRunUrl
		ciRunUrl = $buildInfo.runUrl
	}
	build = [ordered]@{
		dotnetSdk = $buildInfo.dotnetSdk
		globalJsonSha256 = $buildInfo.globalJsonSha256
		runner = [ordered]@{
			label = $buildInfo.runner.label
			imageOs = $buildInfo.runner.imageOs
			imageVersion = $buildInfo.runner.imageVersion
		}
		roslynFloor = $roslynFloor
		analysisLevel = $analysisLevel
	}
	ceProfile = [ordered]@{
		profileId = 'ce-7.7.0.10621-x64-managed-hostfxr'
		supportProfileSha256 = Get-DocumentSha256 -RelativePath 'docs/qualification/support-profile.json'
	}
	qualification = [ordered]@{
		matrixSha256 = Get-DocumentSha256 -RelativePath 'docs/qualification/matrix.json'
		receipts = $receipts
	}
	sbom = [ordered]@{
		entry = '_manifest/spdx_2.2/manifest.spdx.json'
		spdxVersion = 'SPDX-2.2'
		attested = $attested
		documents = @($sbomDocuments)
	}
	attestations = [ordered]@{
		provenanceBundle = $provenanceBundle
		sbomBundles = @($sbomBundles)
	}
	assets = @($assets)
	client = [ordered]@{
		version = $version
		packages = @($packages)
	}
	consumedSdk = [ordered]@{
		id = $identity.id
		version = $identity.version
		range = $identity.range
		contentHashSha512 = $lockEntry['contentHash']
		attestedAssetSha256 = $identity.attestedAssetSha256
		nugetOrgSignedSha256 = $identity.nugetOrgSignedSha256
		sourceCommit = $identity.sourceCommit
		nativeBridge = [ordered]@{
			sha256 = $identity.nativeBridge.sha256
			sourceFingerprint = $identity.nativeBridge.sourceFingerprint
		}
		lockFile = $identity.lockFile
	}
	createdUtc = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ', [cultureinfo]::InvariantCulture)
}

$directory = Split-Path -Parent $OutputPath
if ($directory) {
	New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
[System.IO.File]::WriteAllText($OutputPath, ($tuple | ConvertTo-Json -Depth 16) + "`n", [System.Text.UTF8Encoding]::new($false))
Write-Output "Wrote the $Stage tuple of CheatEngine.Client $version to $OutputPath."
