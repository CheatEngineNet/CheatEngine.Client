#Requires -Version 7.2
<#
.SYNOPSIS
Attaches the Published tuple to the draft release, verifies the attestations, publishes the release and, when the
repository has immutable releases, verifies the published release.

.DESCRIPTION
Runs in finalize-release, after verify-publication proved that nuget.org serves the attested packages:
1. While the release is still a draft, replaces the PrePublish tuple and SHA256SUMS with their Published versions.
2. Verifies, for each package, the build provenance attestation and the SPDX SBOM attestation
   (predicate https://spdx.dev/Document/v2.2), both signed by this repository's release workflow.
3. Publishes the draft.
4. When the published release is immutable, runs gh release verify and gh release verify-asset on every asset;
   otherwise reports that immutable releases are not enabled.
Uses the gh CLI with GH_TOKEN from the environment.

.PARAMETER Tag
The release tag.

.PARAMETER AssetDirectory
The release asset folder, holding the Published tuple and the regenerated SHA256SUMS.

.PARAMETER PackageDirectory
The folder holding the seven attested .nupkg files.

.PARAMETER Repository
The repository that signed the attestations.

.EXAMPLE
./eng/release/Complete-GitHubRelease.ps1 -Tag v0.1.0 -AssetDirectory artifacts/release -PackageDirectory artifacts/nuget
#>
[CmdletBinding()]
param(
	[Parameter(Mandatory)]
	[string] $Tag,

	[Parameter(Mandatory)]
	[string] $AssetDirectory,

	[Parameter(Mandatory)]
	[string] $PackageDirectory,

	[string] $Repository = 'CheatEngineNet/CheatEngine.Client'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$version = $Tag.Substring(1)
$signerWorkflow = "$Repository/.github/workflows/release.yml"
$tuple = Join-Path $AssetDirectory "CheatEngine.Client.$version.tuple.json"
$sums = Join-Path $AssetDirectory 'SHA256SUMS'
foreach ($required in @($tuple, $sums)) {
	if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
		throw "$required is missing."
	}
}

$state = (gh release view $Tag --repo $Repository --json isDraft) -join "`n"
if ($LASTEXITCODE -ne 0) {
	throw "Release $Tag does not exist (exit code $LASTEXITCODE); draft-release creates it."
}
$isDraft = ($state | ConvertFrom-Json).isDraft

# 1. The Published tuple replaces the PrePublish one, only while the release is a draft.
if ($isDraft) {
	gh release upload $Tag $tuple $sums --clobber --repo $Repository
	if ($LASTEXITCODE -ne 0) {
		throw "Uploading the Published tuple to the draft release $Tag failed with exit code $LASTEXITCODE."
	}
}
else {
	Write-Output "::notice::Release $Tag is already published; its assets are not replaced."
}

# 2. Attestations, before anything becomes public.
$packages = @(Get-ChildItem -LiteralPath $PackageDirectory -File -Filter "*.$version.nupkg")
if ($packages.Count -ne 7) {
	throw "Expected the seven attested packages in $PackageDirectory, found $($packages.Count)."
}
foreach ($package in $packages) {
	gh attestation verify $package.FullName --repo $Repository --signer-workflow $signerWorkflow
	if ($LASTEXITCODE -ne 0) {
		throw "The build provenance attestation of $($package.Name) did not verify (exit code $LASTEXITCODE)."
	}
	gh attestation verify $package.FullName --repo $Repository --signer-workflow $signerWorkflow --predicate-type 'https://spdx.dev/Document/v2.2'
	if ($LASTEXITCODE -ne 0) {
		throw "The SBOM attestation of $($package.Name) did not verify (exit code $LASTEXITCODE)."
	}
}

# 3. Publish.
if ($isDraft) {
	gh release edit $Tag --draft=false --repo $Repository
	if ($LASTEXITCODE -ne 0) {
		throw "Publishing the release $Tag failed with exit code $LASTEXITCODE."
	}
}

# 4. Immutable releases are a repository setting, enabled by the maintainer; verify only when the release is immutable.
$immutable = gh api "repos/$Repository/releases/tags/$Tag" --jq '.immutable'
if ($LASTEXITCODE -ne 0) {
	throw "Reading release $Tag failed with exit code $LASTEXITCODE."
}
if ($immutable -ne 'true') {
	Write-Output "::notice::Release $Tag is not immutable: immutable releases are not enabled for $Repository, so gh release verify is skipped."
	return
}
gh release verify $Tag --repo $Repository
if ($LASTEXITCODE -ne 0) {
	throw "gh release verify $Tag failed with exit code $LASTEXITCODE."
}
foreach ($asset in Get-ChildItem -LiteralPath $AssetDirectory -File) {
	gh release verify-asset $Tag $asset.FullName --repo $Repository
	if ($LASTEXITCODE -ne 0) {
		throw "gh release verify-asset $Tag $($asset.Name) failed with exit code $LASTEXITCODE."
	}
}
Write-Output "Release $Tag is published, immutable and verified."
