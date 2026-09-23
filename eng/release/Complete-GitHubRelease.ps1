#Requires -Version 7.2
<#
.SYNOPSIS
Attaches the Published tuple to the draft release, checks that the draft carries exactly the assets of this run,
verifies the attestations, publishes the release and, when the repository has immutable releases, verifies it.

.DESCRIPTION
Runs in finalize-release, after verify-publication proved that nuget.org serves the attested packages:
1. While the release is a draft, replaces the PrePublish tuple and SHA256SUMS with their Published versions. When the
   release is already published (a re-run of this job), nothing is uploaded; its tuple and SHA256SUMS are downloaded,
   so the checks below compare the release as it was published.
2. Checks that the release carries exactly the files of the asset folder: the same names, every asset completely
   uploaded, and each asset's SHA-256 digest equal to the local file and to SHA256SUMS (Compare-ReleaseAssets.ps1).
   A draft that differs stays a draft.
3. Verifies, for each package, the build provenance attestation and the SPDX SBOM attestation
   (predicate https://spdx.dev/Document/v2.2), both signed by this repository's release workflow.
4. Publishes the checked draft, addressed by its release id.
5. When the published release is immutable, runs gh release verify and gh release verify-asset on every asset;
   otherwise reports that immutable releases are not enabled.
Uses the gh CLI with GH_TOKEN from the environment.

.PARAMETER Tag
The release tag.

.PARAMETER AssetDirectory
The release asset folder, holding the Published tuple and the regenerated SHA256SUMS.

.PARAMETER PackageDirectory
The folder holding the seven attested .nupkg files.

.PARAMETER Repository
The repository that holds the release and signed the attestations.

.PARAMETER SettleSeconds
How long to wait before comparing again when GitHub does not list the replaced assets completely yet.

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

	[string] $Repository = 'CheatEngineNet/CheatEngine.Client',

	[ValidateRange(0, 120)]
	[int] $SettleSeconds = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$version = $Tag.Substring(1)
$compare = Join-Path $PSScriptRoot 'Compare-ReleaseAssets.ps1'
$signerWorkflow = "$Repository/.github/workflows/release.yml"
$tuple = Join-Path $AssetDirectory "CheatEngine.Client.$version.tuple.json"
$sums = Join-Path $AssetDirectory 'SHA256SUMS'
foreach ($required in @($tuple, $sums)) {
	if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
		throw "$required is missing."
	}
}

$releases = @(& $compare -Tag $Tag -AssetDirectory $AssetDirectory -Repository $Repository)
if ($releases.Count -ne 1) {
	throw "Expected exactly one release of $Tag, found $($releases.Count); draft-release creates it."
}
$release = $releases[0]

# 1. The Published tuple and SHA256SUMS replace the PrePublish ones, only while the release is a draft.
if ($release.IsDraft) {
	gh release upload $Tag $tuple $sums --clobber --repo $Repository
	if ($LASTEXITCODE -ne 0) {
		throw "Uploading the Published tuple to the draft release $Tag failed with exit code $LASTEXITCODE."
	}
}
else {
	Write-Output "::notice::Release $Tag is already published; its assets are not replaced, and its tuple and SHA256SUMS are checked as published."
	gh release download $Tag --pattern (Split-Path -Leaf $tuple) --pattern 'SHA256SUMS' --dir $AssetDirectory --clobber --repo $Repository
	if ($LASTEXITCODE -ne 0) {
		throw "Downloading the tuple and SHA256SUMS of release $Tag failed with exit code $LASTEXITCODE."
	}
}

# 2. The release carries exactly the files of this run, checked by content before anything becomes public.
for ($attempt = 1; ; $attempt++) {
	$checked = @(& $compare -Tag $Tag -AssetDirectory $AssetDirectory -Repository $Repository)
	$differences = [System.Collections.Generic.List[string]]::new()
	if ($checked.Count -ne 1 -or $checked[0].Id -ne $release.Id) {
		$differences.Add("the tag now has $($checked.Count) releases instead of release $($release.Id)")
	}
	else {
		$differences.AddRange([string[]]$checked[0].Differences)
	}
	if ($differences.Count -eq 0) {
		break
	}
	if ($attempt -ge 3) {
		$state = if ($release.IsDraft) { 'The release stays a draft.' } else { 'The published release differs from the attested files.' }
		throw "Release $Tag does not carry exactly the assets of this run:`n- $($differences -join "`n- ")`n$state"
	}
	Start-Sleep -Seconds $SettleSeconds
}

# 3. Attestations, before anything becomes public.
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

# 4. Publish the release that was checked, by id: another draft of the same tag is never published by accident.
if ($release.IsDraft) {
	gh api --method PATCH "repos/$Repository/releases/$($release.Id)" -F draft=false --silent
	if ($LASTEXITCODE -ne 0) {
		throw "Publishing the release $Tag (id $($release.Id)) failed with exit code $LASTEXITCODE."
	}
}

# 5. Immutable releases are a repository setting, enabled by the maintainer; verify only when the release is immutable.
$immutable = gh api "repos/$Repository/releases/$($release.Id)" --jq '.immutable'
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
