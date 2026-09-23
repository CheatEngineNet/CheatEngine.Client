#Requires -Version 7.2
<#
.SYNOPSIS
Creates the draft GitHub release of a tag with exactly the release assets of this run.

.DESCRIPTION
Immutable releases lock a release's tag and assets once it is published, so the release is always created as a draft,
filled completely, and only published by finalize-release after nuget.org serves the packages. Assets are compared by
content (Compare-ReleaseAssets.ps1: name, upload state, size and SHA-256), never by name alone, because every build of
the tag produces different package bytes:
- No release: gh release create --draft --verify-tag with the notes and every file of the asset folder.
- One draft that carries exactly the files of the folder (a re-run of this job with the same artifacts): kept.
- Any other draft (left by an interrupted upload or by an earlier run of the tag, such as a re-pushed tag or
  "Re-run all jobs" after a rejected publication): deleted and created again from this run's files. Deleting a draft
  release keeps the git tag, and nothing of a draft is public.
- A published release: never receives assets. The job succeeds only when the release already carries exactly the files
  of the folder; otherwise it fails before the packages can be pushed.
The new draft is then compared with the folder again, and the job fails unless it matches.
Uses the gh CLI with GH_TOKEN from the environment.

.PARAMETER Tag
The release tag.

.PARAMETER AssetDirectory
The folder whose files are the release assets.

.PARAMETER NotesPath
The release notes (Export-ReleaseNotes.ps1).

.PARAMETER Prerelease
Marks the release as a prerelease.

.PARAMETER Repository
The repository that holds the release.

.PARAMETER SettleSeconds
How long to wait before comparing the new draft again when GitHub does not list it completely yet.

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

	[switch] $Prerelease,

	[string] $Repository = 'CheatEngineNet/CheatEngine.Client',

	[ValidateRange(0, 120)]
	[int] $SettleSeconds = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$compare = Join-Path $PSScriptRoot 'Compare-ReleaseAssets.ps1'
$files = @(Get-ChildItem -LiteralPath $AssetDirectory -File | ForEach-Object FullName)
if ($files.Count -eq 0) {
	throw "$AssetDirectory holds no release asset."
}

function Format-Difference {
	param([object[]] $Release)
	$lines = [System.Collections.Generic.List[string]]::new()
	foreach ($entry in $Release) {
		foreach ($difference in $entry.Differences) {
			$lines.Add("release $($entry.Id): $difference")
		}
	}
	if ($lines.Count -eq 0) {
		return '- no asset difference'
	}
	return "- $($lines -join "`n- ")"
}

$releases = @(& $compare -Tag $Tag -AssetDirectory $AssetDirectory -Repository $Repository)
$published = @($releases | Where-Object { -not $_.IsDraft })
$drafts = @($releases | Where-Object { $_.IsDraft })

if ($published.Count -gt 0) {
	if ($releases.Count -eq 1 -and $published[0].Differences.Count -eq 0) {
		Write-Output "::notice::Release $Tag is already published with exactly the assets of this run; nothing is uploaded."
		return
	}
	throw "Release $Tag is already published, and a published release never receives assets, but its releases do not carry exactly the assets of this run:`n$(Format-Difference -Release $releases)`nTag a new version."
}

if ($drafts.Count -eq 1 -and $drafts[0].Differences.Count -eq 0) {
	Write-Output "The draft release $Tag already carries exactly the assets of this run."
	return
}

foreach ($draft in $drafts) {
	Write-Output "::warning::The draft release $Tag (id $($draft.Id)) does not carry exactly the assets of this run, so it is deleted and created again; the tag stays."
	Write-Output (Format-Difference -Release @($draft))
	gh api --method DELETE "repos/$Repository/releases/$($draft.Id)" --silent
	if ($LASTEXITCODE -ne 0) {
		throw "Deleting the draft release $Tag (id $($draft.Id)) failed with exit code $LASTEXITCODE."
	}
}

$options = @('--draft', '--verify-tag', '--title', $Tag, '--notes-file', $NotesPath, '--repo', $Repository)
if ($Prerelease) {
	$options += '--prerelease'
}
gh release create $Tag @files @options
if ($LASTEXITCODE -ne 0) {
	throw "Creating the draft release $Tag failed with exit code $LASTEXITCODE."
}

# What GitHub recorded, not what was sent: every asset uploaded, with this run's SHA-256.
for ($attempt = 1; ; $attempt++) {
	$created = @(& $compare -Tag $Tag -AssetDirectory $AssetDirectory -Repository $Repository)
	if ($created.Count -eq 1 -and $created[0].IsDraft -and $created[0].Differences.Count -eq 0) {
		break
	}
	if ($attempt -ge 3) {
		throw "After creation, the releases of $Tag are not one draft carrying exactly the assets of this run ($($created.Count) releases):`n$(Format-Difference -Release $created)"
	}
	Start-Sleep -Seconds $SettleSeconds
}
Write-Output "Created the draft release $Tag with $($files.Count) assets, each checked by name, size and SHA-256."
