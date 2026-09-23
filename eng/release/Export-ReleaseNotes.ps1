#Requires -Version 7.2
<#
.SYNOPSIS
Extracts the release notes of one version from CHANGELOG.md.

.DESCRIPTION
The GitHub release notes are the CHANGELOG.md section of the version: '## [X.Y.Z] - <date>'. A prerelease may ship
before its entries leave '## [Unreleased]', so it falls back to that section; a stable release may not. Empty category
headings (### Added, ### Changed, ### Security, ### Deployment) are dropped, and the notes end with the nuget.org links
of the seven packages.

.PARAMETER Version
The version being released, without the v prefix.

.PARAMETER Prerelease
Allows the [Unreleased] section when the version has no section of its own.

.PARAMETER OutputPath
The Markdown file to write.

.PARAMETER ChangelogPath
The changelog to read; CHANGELOG.md at the repository root by default.

.EXAMPLE
./eng/release/Export-ReleaseNotes.ps1 -Version 0.1.0 -OutputPath artifacts/release-notes/release-notes.md
#>
[CmdletBinding()]
param(
	[Parameter(Mandatory)]
	[string] $Version,

	[switch] $Prerelease,

	[Parameter(Mandatory)]
	[string] $OutputPath,

	[string] $ChangelogPath = (Join-Path $PSScriptRoot '../../CHANGELOG.md')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$changelog = (Get-Content -LiteralPath $ChangelogPath -Raw).Replace("`r`n", "`n")
$sections = @($Version)
if ($Prerelease) {
	$sections += 'Unreleased'
}

$notes = ''
foreach ($section in $sections) {
	$pattern = '(?ms)^## \[' + [regex]::Escape($section) + '\][^\n]*\n(?<body>.*?)(?=^## \[|^\[[^\]]+\]:|\z)'
	$match = [regex]::Match($changelog, $pattern)
	if (-not $match.Success) {
		continue
	}

	# Drop category headings with no entry under them.
	$kept = [System.Collections.Generic.List[string]]::new()
	$lines = $match.Groups['body'].Value.Trim().Split("`n")
	for ($index = 0; $index -lt $lines.Length; $index++) {
		if ($lines[$index] -match '^### ') {
			$next = $index + 1
			while ($next -lt $lines.Length -and -not $lines[$next].Trim()) {
				$next++
			}
			if ($next -ge $lines.Length -or $lines[$next] -match '^#{2,3} ') {
				continue
			}
		}
		$kept.Add($lines[$index])
	}

	$body = (($kept -join "`n") -replace "`n{3,}", "`n`n").Trim()
	if ($body) {
		$notes = $body
		break
	}
}

if (-not $notes) {
	throw "CHANGELOG.md has no non-empty section for $($sections -join ' or '). Move the release entries under '## [$Version] - <date>' before tagging."
}

$packages = @(
	'CheatEngine.Client', 'CheatEngine.Client.Abstractions', 'CheatEngine.Client.Core',
	'CheatEngine.Client.Extensions.DependencyInjection', 'CheatEngine.Client.Fluent', 'CheatEngine.Client.Hosting',
	'CheatEngine.Client.Templates') | ForEach-Object { "- [$_ $Version](https://www.nuget.org/packages/$_/$Version)" }

$directory = Split-Path -Parent $OutputPath
if ($directory) {
	New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
$text = (@($notes, '', '## Packages', '') + @($packages) + @('',
	"The release assets include SHA256SUMS, the SPDX SBOM of each package, their sigstore attestation bundles and the Client release tuple CheatEngine.Client.$Version.tuple.json. RELEASING.md explains how to verify them.")) -join "`n"
[System.IO.File]::WriteAllText($OutputPath, $text + "`n", [System.Text.UTF8Encoding]::new($false))
Write-Output "Wrote the release notes of $Version to $OutputPath."
