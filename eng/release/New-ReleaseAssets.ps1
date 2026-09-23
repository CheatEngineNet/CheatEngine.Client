#Requires -Version 7.2
<#
.SYNOPSIS
Extracts the SBOM of each Client package, or writes SHA256SUMS over a release asset folder.

.DESCRIPTION
-ExtractSbom: every package embeds its SPDX 2.2 document at _manifest/spdx_2.2/manifest.spdx.json. This writes it next to
the release assets as <Id>.<Version>.spdx.json, after checking that the document describes that package and version, so
it can be attested (actions/attest sbom-path) and attached to the release.

-WriteChecksums: writes SHA256SUMS (the sha256sum format, '<sha256>  <name>', sorted by name, LF) over every file of
the folder except SHA256SUMS itself.

.PARAMETER PackageDirectory
The folder holding the seven .nupkg files (the nuget-packages artifact).

.PARAMETER Version
The version of the seven packages.

.PARAMETER OutputDirectory
The release asset folder.

.EXAMPLE
./eng/release/New-ReleaseAssets.ps1 -ExtractSbom -PackageDirectory artifacts/nuget -Version 0.1.0 -OutputDirectory artifacts/release

.EXAMPLE
./eng/release/New-ReleaseAssets.ps1 -WriteChecksums -OutputDirectory artifacts/release
#>
[CmdletBinding(DefaultParameterSetName = 'Sbom')]
param(
	[Parameter(Mandatory, ParameterSetName = 'Sbom')]
	[switch] $ExtractSbom,

	[Parameter(Mandatory, ParameterSetName = 'Checksums')]
	[switch] $WriteChecksums,

	[Parameter(Mandatory, ParameterSetName = 'Sbom')]
	[string] $PackageDirectory,

	[Parameter(Mandatory, ParameterSetName = 'Sbom')]
	[string] $Version,

	[Parameter(Mandatory)]
	[string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$packageIds = @(
	'CheatEngine.Client', 'CheatEngine.Client.Abstractions', 'CheatEngine.Client.Core',
	'CheatEngine.Client.Extensions.DependencyInjection', 'CheatEngine.Client.Fluent', 'CheatEngine.Client.Hosting',
	'CheatEngine.Client.Templates')
$sbomEntry = '_manifest/spdx_2.2/manifest.spdx.json'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

if ($ExtractSbom) {
	Add-Type -AssemblyName System.IO.Compression.FileSystem
	foreach ($id in $packageIds) {
		$package = Join-Path $PackageDirectory "$id.$Version.nupkg"
		if (-not (Test-Path -LiteralPath $package -PathType Leaf)) {
			throw "The package $id.$Version.nupkg is missing from $PackageDirectory."
		}
		$archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $package).Path)
		try {
			$entry = $archive.GetEntry($sbomEntry)
			if ($null -eq $entry) {
				throw "$id.$Version.nupkg has no $sbomEntry."
			}
			$reader = [System.IO.StreamReader]::new($entry.Open())
			try {
				$text = $reader.ReadToEnd()
			}
			finally {
				$reader.Dispose()
			}
		}
		finally {
			$archive.Dispose()
		}

		$document = $text | ConvertFrom-Json
		$root = @($document.packages | Where-Object { $_.SPDXID -eq 'SPDXRef-RootPackage' })
		if ($document.spdxVersion -ne 'SPDX-2.2' -or $root.Count -ne 1 -or $root[0].name -ne $id -or $root[0].versionInfo -ne $Version) {
			throw "The SBOM inside $id.$Version.nupkg does not describe $id $Version as SPDX-2.2."
		}
		$target = Join-Path $OutputDirectory "$id.$Version.spdx.json"
		[System.IO.File]::WriteAllText($target, $text, [System.Text.UTF8Encoding]::new($false))
		Write-Output "Extracted the SBOM of $id $Version to $target."
	}
	return
}

if (-not $WriteChecksums) {
	throw 'Choose -ExtractSbom or -WriteChecksums.'
}

[string[]] $names = @(Get-ChildItem -LiteralPath $OutputDirectory -File | Where-Object Name -ne 'SHA256SUMS' | ForEach-Object Name)
[System.Array]::Sort($names, [System.StringComparer]::Ordinal)
$lines = foreach ($name in $names) {
	'{0}  {1}' -f (Get-FileHash -LiteralPath (Join-Path $OutputDirectory $name) -Algorithm SHA256).Hash.ToLowerInvariant(), $name
}
$sums = Join-Path $OutputDirectory 'SHA256SUMS'
[System.IO.File]::WriteAllText($sums, (($lines -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))
Write-Output "Wrote $sums ($(@($lines).Count) files)."
