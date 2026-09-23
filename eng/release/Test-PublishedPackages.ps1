#Requires -Version 7.2
<#
.SYNOPSIS
Verifies that nuget.org serves exactly the attested Client packages, and records their nuget.org hashes.

.DESCRIPTION
For each of the seven packages, the script polls the nuget.org flat container until it lists the version (validation
and indexing take minutes), downloads the repository-signed package, and fails unless:
- 'dotnet nuget verify --all' accepts its signature;
- the content hash it prints equals the SHA-512 of the attested, unsigned package (the value consumers' lock files
  record);
- every entry except .signature.p7s is byte-identical to the attested package.
It writes a JSON array of { id, version, contentHashSha512, nugetOrgSignedSha256, nugetOrgSignedSha512 } for
New-ClientTuple.ps1.

.PARAMETER PackageDirectory
The folder holding the attested .nupkg files (the nuget-packages artifact).

.PARAMETER Version
The released version.

.PARAMETER OutputPath
The JSON file to write.

.PARAMETER TimeoutMinutes
How long to wait for nuget.org to list every package.

.PARAMETER PollSeconds
The polling interval.

.EXAMPLE
./eng/release/Test-PublishedPackages.ps1 -PackageDirectory artifacts/nuget -Version 0.1.0 -OutputPath artifacts/publication/published-packages.json
#>
[CmdletBinding()]
param(
	[Parameter(Mandatory)]
	[string] $PackageDirectory,

	[Parameter(Mandatory)]
	[string] $Version,

	[Parameter(Mandatory)]
	[string] $OutputPath,

	[ValidateRange(1, 180)]
	[int] $TimeoutMinutes = 45,

	[ValidateRange(5, 600)]
	[int] $PollSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$packageIds = @(
	'CheatEngine.Client.Abstractions', 'CheatEngine.Client.Fluent', 'CheatEngine.Client.Core',
	'CheatEngine.Client.Extensions.DependencyInjection', 'CheatEngine.Client.Hosting', 'CheatEngine.Client',
	'CheatEngine.Client.Templates')
$flatContainer = 'https://api.nuget.org/v3-flatcontainer'
$lowerVersion = $Version.ToLowerInvariant()

function Get-EntryHash {
	param([string] $Path)
	$hashes = @{}
	$archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
	try {
		foreach ($entry in $archive.Entries) {
			if ($entry.FullName -eq '.signature.p7s' -or $entry.FullName.EndsWith('/')) {
				continue
			}
			$stream = $entry.Open()
			try {
				$hashes[$entry.FullName] = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($stream))
			}
			finally {
				$stream.Dispose()
			}
		}
	}
	finally {
		$archive.Dispose()
	}
	return $hashes
}

$deadline = [DateTime]::UtcNow.AddMinutes($TimeoutMinutes)
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('published-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$results = [System.Collections.Generic.List[object]]::new()
try {
	foreach ($id in $packageIds) {
		$attested = Join-Path $PackageDirectory "$id.$Version.nupkg"
		if (-not (Test-Path -LiteralPath $attested -PathType Leaf)) {
			throw "The attested package $id.$Version.nupkg is missing from $PackageDirectory."
		}
		$lowerId = $id.ToLowerInvariant()
		while ($true) {
			$index = Invoke-RestMethod -Uri "$flatContainer/$lowerId/index.json" -SkipHttpErrorCheck -StatusCodeVariable status -MaximumRetryCount 3 -RetryIntervalSec 5
			if ($status -eq 200 -and @($index.versions) -contains $lowerVersion) {
				break
			}
			if ([DateTime]::UtcNow -gt $deadline) {
				throw "nuget.org does not list $id $Version after $TimeoutMinutes minutes (last HTTP status $status)."
			}
			Write-Output "Waiting for nuget.org to list $id $Version (HTTP $status)."
			Start-Sleep -Seconds $PollSeconds
		}

		$signed = Join-Path $work "$lowerId.$lowerVersion.nupkg"
		Invoke-WebRequest -Uri "$flatContainer/$lowerId/$lowerVersion/$lowerId.$lowerVersion.nupkg" -OutFile $signed -MaximumRetryCount 3 -RetryIntervalSec 5
		$verification = @(dotnet nuget verify --all $signed -v n 2>&1)
		if ($LASTEXITCODE -ne 0) {
			throw "dotnet nuget verify --all failed for $id $Version with exit code ${LASTEXITCODE}:`n$($verification -join "`n")"
		}
		$line = $verification | Select-String -Pattern '^Content hash:\s*(?<hash>\S+)\s*$' | Select-Object -First 1
		$expected = [Convert]::ToBase64String([System.Security.Cryptography.SHA512]::HashData([System.IO.File]::ReadAllBytes($attested)))
		if ($null -eq $line -or $line.Matches[0].Groups['hash'].Value -ne $expected) {
			throw "$id $Version on nuget.org does not have the content hash of the attested package ($expected)."
		}

		$attestedEntries = Get-EntryHash -Path $attested
		$signedEntries = Get-EntryHash -Path $signed
		$differences = @(@($attestedEntries.Keys) + @($signedEntries.Keys) | Sort-Object -Unique | Where-Object { $attestedEntries[$_] -ne $signedEntries[$_] })
		if ($differences.Count -gt 0) {
			throw "$id $Version on nuget.org differs from the attested package in: $($differences -join ', ')."
		}

		$bytes = [System.IO.File]::ReadAllBytes($signed)
		$results.Add([ordered]@{
			id = $id
			version = $Version
			contentHashSha512 = $expected
			nugetOrgSignedSha256 = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
			nugetOrgSignedSha512 = [Convert]::ToBase64String([System.Security.Cryptography.SHA512]::HashData($bytes))
		})
		Write-Output "nuget.org serves the attested $id $Version (signed file SHA-256 $($results[-1].nugetOrgSignedSha256))."
	}
}
finally {
	Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$directory = Split-Path -Parent $OutputPath
if ($directory) {
	New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
[System.IO.File]::WriteAllText($OutputPath, (ConvertTo-Json -InputObject @($results) -Depth 4) + "`n", [System.Text.UTF8Encoding]::new($false))
