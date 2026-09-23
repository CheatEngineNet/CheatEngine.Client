#Requires -Version 7.2
<#
.SYNOPSIS
Compares every GitHub release of a tag with a local release asset folder: asset names, upload state, size and SHA-256.

.DESCRIPTION
A release asset is identified by its content, never by its name alone. Every pack of the Client produces different
.nupkg bytes (the embedded SBOM has a unique namespace and a creation time), so an asset left by another run of the
same tag carries the expected name but not this run's package. GitHub computes the SHA-256 of every uploaded asset and
returns it as the asset digest ('sha256:<hex>').

The script lists the releases of the repository through the REST API (draft releases are listed for a token with push
access; the get-by-tag endpoint returns published releases only) and returns, for each release of the tag, how it
differs from the folder:
- a file of the folder that the release lacks, or an asset of the release that the folder lacks;
- an asset that is not completely uploaded (state other than 'uploaded');
- an asset whose size or digest differs from the local file, or that has no digest.
When the folder holds SHA256SUMS, the script first checks that it lists exactly the other files of the folder with
their SHA-256, so a release that matches the folder also matches its SHA256SUMS.

Uses the gh CLI with GH_TOKEN from the environment.

.PARAMETER Tag
The release tag.

.PARAMETER AssetDirectory
The folder whose files are the release assets of this run.

.PARAMETER Repository
The repository that holds the release.

.OUTPUTS
One object per release of the tag: Id (the REST release id), IsDraft, and Differences (the problems as sentences; empty
when the release carries exactly the files of the folder).

.EXAMPLE
./eng/release/Compare-ReleaseAssets.ps1 -Tag v0.1.0 -AssetDirectory artifacts/release
#>
[CmdletBinding()]
[OutputType([pscustomobject])]
param(
	[Parameter(Mandatory)]
	[string] $Tag,

	[Parameter(Mandatory)]
	[string] $AssetDirectory,

	[string] $Repository = 'CheatEngineNet/CheatEngine.Client'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The files of this run.
$local = [System.Collections.Generic.SortedDictionary[string, object]]::new([System.StringComparer]::Ordinal)
foreach ($file in Get-ChildItem -LiteralPath $AssetDirectory -File) {
	$local[$file.Name] = [pscustomobject]@{
		Sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
		Length = $file.Length
	}
}
if ($local.Count -eq 0) {
	throw "$AssetDirectory holds no release asset."
}

# SHA256SUMS, when present, describes exactly the other files of the folder.
if ($local.ContainsKey('SHA256SUMS')) {
	$problems = [System.Collections.Generic.List[string]]::new()
	$listed = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
	foreach ($line in [System.IO.File]::ReadAllLines((Join-Path $AssetDirectory 'SHA256SUMS'))) {
		if ($line -cmatch '^(?<hash>[0-9a-f]{64})  (?<name>\S.*)$') {
			$listed[$Matches['name']] = $Matches['hash']
		}
		elseif ($line.Length -gt 0) {
			$problems.Add("malformed line '$line'")
		}
	}
	foreach ($name in $local.Keys) {
		if ($name -ceq 'SHA256SUMS') {
			continue
		}
		if (-not $listed.ContainsKey($name)) {
			$problems.Add("$name is not listed")
		}
		elseif ($listed[$name] -cne $local[$name].Sha256) {
			$problems.Add("$name is listed with $($listed[$name]), but the file has $($local[$name].Sha256)")
		}
	}
	foreach ($name in $listed.Keys) {
		if (-not $local.ContainsKey($name)) {
			$problems.Add("$name is listed but is not a file of the folder")
		}
	}
	if ($problems.Count -gt 0) {
		throw "SHA256SUMS does not describe the files of ${AssetDirectory}:`n- $($problems -join "`n- ")"
	}
}

# The releases of the tag, drafts included, with the digest GitHub computed for each asset.
$filter = '.[] | {id, tag_name, draft, assets: [.assets[] | {name, state, size, digest}]} | tojson'
$lines = @(gh api "repos/$Repository/releases?per_page=100" --paginate --jq $filter)
if ($LASTEXITCODE -ne 0) {
	throw "Listing the releases of $Repository failed with exit code $LASTEXITCODE."
}

foreach ($line in $lines) {
	$release = $line | ConvertFrom-Json
	if ($release.tag_name -cne $Tag) {
		continue
	}

	$differences = [System.Collections.Generic.List[string]]::new()
	$remote = [System.Collections.Generic.SortedDictionary[string, object]]::new([System.StringComparer]::Ordinal)
	foreach ($asset in @($release.assets)) {
		$remote[$asset.name] = $asset
	}
	foreach ($name in $local.Keys) {
		if (-not $remote.ContainsKey($name)) {
			$differences.Add("$name is a file of this run but not an asset of the release")
		}
	}
	foreach ($name in $remote.Keys) {
		$asset = $remote[$name]
		if (-not $local.ContainsKey($name)) {
			$differences.Add("$name is an asset of the release but not a file of this run")
			continue
		}
		if ($asset.state -cne 'uploaded') {
			$differences.Add("$name is not completely uploaded (state '$($asset.state)')")
			continue
		}
		$expected = "sha256:$($local[$name].Sha256)"
		if ([string]::IsNullOrEmpty($asset.digest)) {
			$differences.Add("$name has no digest, so its content cannot be compared with this run's file")
		}
		elseif ($asset.digest -cne $expected -or [long]$asset.size -ne $local[$name].Length) {
			$differences.Add("$name is $($asset.digest) ($($asset.size) bytes) on the release, but this run's file is $expected ($($local[$name].Length) bytes)")
		}
	}

	[pscustomobject]@{
		Id = [long]$release.id
		IsDraft = [bool]$release.draft
		Differences = [string[]]$differences.ToArray()
	}
}
