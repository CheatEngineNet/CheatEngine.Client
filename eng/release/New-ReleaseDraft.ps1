#Requires -Version 7.2
<#
.SYNOPSIS
Creates the draft GitHub release of a tag with every release asset, or completes an existing draft.

.DESCRIPTION
Immutable releases lock a release's tag and assets once it is published, so the release is always created as a draft,
filled completely, and only published by finalize-release after nuget.org serves the packages.
- No release: gh release create --draft --verify-tag with the notes and every file of the asset folder.
- A draft: uploads only the assets it does not have yet (a re-run of a failed job).
- A published release: nothing is uploaded; a published release never receives assets.
Uses the gh CLI with GH_TOKEN and GH_REPO from the environment.

.PARAMETER Tag
The release tag.

.PARAMETER AssetDirectory
The folder whose files are the release assets.

.PARAMETER NotesPath
The release notes (Export-ReleaseNotes.ps1).

.PARAMETER Prerelease
Marks the release as a prerelease.

.EXAMPLE
./eng/release/New-ReleaseDraft.ps1 -Tag v0.1.0 -AssetDirectory artifacts/release -NotesPath artifacts/release-notes/release-notes.md
#>
[CmdletBinding()]
param(
	[Parameter(Mandatory)]
	[string] $Tag,

	[Parameter(Mandatory)]
	[string] $AssetDirectory,

	[Parameter(Mandatory)]
	[string] $NotesPath,

	[switch] $Prerelease
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$files = @(Get-ChildItem -LiteralPath $AssetDirectory -File | ForEach-Object FullName)
if ($files.Count -eq 0) {
	throw "$AssetDirectory holds no release asset."
}

$view = gh release view $Tag --json isDraft,assets 2>$null
if ($LASTEXITCODE -ne 0) {
	$options = @('--draft', '--verify-tag', '--title', $Tag, '--notes-file', $NotesPath)
	if ($Prerelease) {
		$options += '--prerelease'
	}
	gh release create $Tag @files @options
	if ($LASTEXITCODE -ne 0) {
		throw "Creating the draft release $Tag failed with exit code $LASTEXITCODE."
	}
	Write-Output "Created the draft release $Tag with $($files.Count) assets."
	return
}

$state = ($view -join "`n") | ConvertFrom-Json
if (-not $state.isDraft) {
	Write-Output "::notice::Release $Tag is already published; a published release never receives assets, so nothing is uploaded."
	return
}

$existing = @($state.assets | ForEach-Object name)
$missing = @($files | Where-Object { $existing -notcontains (Split-Path -Leaf $_) })
if ($missing.Count -eq 0) {
	Write-Output "The draft release $Tag already carries every asset."
	return
}
gh release upload $Tag @missing
if ($LASTEXITCODE -ne 0) {
	throw "Uploading the missing assets of the draft release $Tag failed with exit code $LASTEXITCODE."
}
Write-Output "Uploaded $($missing.Count) missing assets to the draft release $Tag."
