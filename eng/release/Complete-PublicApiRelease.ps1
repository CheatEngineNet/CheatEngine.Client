#Requires -Version 7.2
<#
.SYNOPSIS
Moves the unshipped public API and analyzer rules of every Client project into their shipped files, for a release pull
request.

.DESCRIPTION
Run once in the pull request that prepares a release, never in CI:
- For each PublicAPI.Unshipped.txt under libs/ and src/, the entries move into PublicAPI.Shipped.txt: '*REMOVED*'
  entries delete their shipped line, the others are added; the shipped file keeps '#nullable enable' first and is
  sorted ordinally; the unshipped file keeps only '#nullable enable'.
- For each AnalyzerReleases.Unshipped.md under source-generators/, its rule tables move into a '## Release <version>'
  section of AnalyzerReleases.Shipped.md (created when missing), and the unshipped file keeps only its header lines.
Then build: RS0016/RS0017 and the release-tracking analyzers confirm the result.

.PARAMETER Version
The version being released, for example 0.1.0.

.EXAMPLE
./eng/release/Complete-PublicApiRelease.ps1 -Version 0.1.0 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param(
	[Parameter(Mandatory)]
	[ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$')]
	[string] $Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$utf8 = [System.Text.UTF8Encoding]::new($false)
$nullable = '#nullable enable'

function Write-TextFile {
	param([string] $Path, [string[]] $Lines)
	[System.IO.File]::WriteAllText($Path, (($Lines -join "`r`n") + "`r`n"), $utf8)
}

foreach ($unshippedFile in @(Get-ChildItem -Path (Join-Path $repositoryRoot 'libs'), (Join-Path $repositoryRoot 'src') -Recurse -File -Filter 'PublicAPI.Unshipped.txt')) {
	$shippedFile = Join-Path $unshippedFile.DirectoryName 'PublicAPI.Shipped.txt'
	$unshipped = @([System.IO.File]::ReadAllLines($unshippedFile.FullName) | Where-Object { $_.Trim() -and $_ -ne $nullable })
	if ($unshipped.Count -eq 0) {
		continue
	}

	$shipped = [System.Collections.Generic.List[string]]::new()
	if (Test-Path -LiteralPath $shippedFile) {
		$shipped.AddRange([string[]]@([System.IO.File]::ReadAllLines($shippedFile) | Where-Object { $_.Trim() -and $_ -ne $nullable }))
	}
	foreach ($line in $unshipped) {
		if ($line.StartsWith('*REMOVED*', [StringComparison]::Ordinal)) {
			$removed = $line.Substring('*REMOVED*'.Length)
			if (-not $shipped.Remove($removed)) {
				throw "$($unshippedFile.FullName) removes '$removed', which $shippedFile does not ship."
			}
			continue
		}
		if (-not $shipped.Contains($line)) {
			$shipped.Add($line)
		}
	}
	$shipped.Sort([System.StringComparer]::Ordinal)

	$project = [System.IO.Path]::GetRelativePath($repositoryRoot, $unshippedFile.DirectoryName)
	if ($PSCmdlet.ShouldProcess($project, "Ship $($unshipped.Count) public API entries")) {
		Write-TextFile -Path $shippedFile -Lines (@($nullable) + $shipped)
		Write-TextFile -Path $unshippedFile.FullName -Lines @($nullable)
		Write-Output "${project}: shipped $($unshipped.Count) public API entries."
	}
}

foreach ($unshippedFile in @(Get-ChildItem -Path (Join-Path $repositoryRoot 'source-generators') -Recurse -File -Filter 'AnalyzerReleases.Unshipped.md')) {
	$lines = [System.IO.File]::ReadAllLines($unshippedFile.FullName)
	$firstTable = [Array]::FindIndex($lines, [Predicate[string]] { param($line) $line.StartsWith('### ', [StringComparison]::Ordinal) })
	if ($firstTable -lt 0) {
		continue
	}
	# PowerShell ranges count down (0..-1 is 0, -1), so an unshipped file without header lines needs its own case.
	$header = if ($firstTable -gt 0) { @($lines[0..($firstTable - 1)] | Where-Object { $_.StartsWith(';', [StringComparison]::Ordinal) }) } else { @() }
	$tables = @($lines[$firstTable..($lines.Length - 1)])
	$shippedFile = Join-Path $unshippedFile.DirectoryName 'AnalyzerReleases.Shipped.md'
	$shipped = if (Test-Path -LiteralPath $shippedFile) {
		@([System.IO.File]::ReadAllLines($shippedFile))
	}
	else {
		@('; Shipped analyzer releases', '; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md')
	}
	if ($shipped -contains "## Release $Version") {
		throw "$shippedFile already has a '## Release $Version' section."
	}

	$project = [System.IO.Path]::GetRelativePath($repositoryRoot, $unshippedFile.DirectoryName)
	if ($PSCmdlet.ShouldProcess($project, "Ship the analyzer rules as release $Version")) {
		Write-TextFile -Path $shippedFile -Lines (@($shipped) + @('', "## Release $Version", '') + $tables)
		Write-TextFile -Path $unshippedFile.FullName -Lines $header
		Write-Output "${project}: shipped the analyzer rules as release $Version."
	}
}
