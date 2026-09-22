#Requires -Version 7.2
<#
.SYNOPSIS
Verifies the consumed CheatEngine.SDK pin, or moves it to another published version.

.DESCRIPTION
The Client consumes exactly one CheatEngine.SDK package, pinned in eng/CheatEngineSdk.props and identified in
eng/sdk/consumed-sdk.json (see eng/sdk/README.md). This script is the only supported way to change that pin.

Before it touches any file, the script refuses a version that is a prerelease, of another major than
_CheatEngineClientSupportedSdkMajor (unless -AllowMajor), or absent from the nuget.org flat container. It then downloads
the package into a temporary folder, verifies its repository signature with 'dotnet nuget verify --all', reads the
NuGet content hash that command prints, hashes the signed file and the packed native bridge, and takes the fields that
nuget.org cannot provide (attested GitHub asset, source commit and tree, bridge source fingerprint) from the SDK
release tuple asset or from the parameters. It never guesses them.

With the version that is already pinned, the script only verifies consumed-sdk.json and the lock files and changes
nothing. With another version, it updates eng/CheatEngineSdk.props, the default of the template project and
consumed-sdk.json, regenerates the lock files with eng/Update-LockFiles.ps1, and fails unless every lock resolves the
new version with the verified content hash and no other lock entry changed.

.PARAMETER Version
The CheatEngine.SDK version to pin, for example 1.0.1.

.PARAMETER AllowMajor
Allows another major version. That is the SDK major migration described in docs/migration/sdk-2.0.md, not a
dependency bump: _CheatEngineClientSupportedSdkMajor and the Client code must change in the same pull request.

.PARAMETER AttestedAssetSha256
SHA-256 of the unsigned package attached to the SDK GitHub release. Required when the release has no tuple asset.

.PARAMETER SourceCommit
The SDK commit the release tag points to. Required when the release has no tuple asset.

.PARAMETER SourceTreeHash
The git tree of that commit. Required when the release has no tuple asset.

.PARAMETER BridgeSourceFingerprint
The native bridge source fingerprint ('<c-sha256>:<xmake-sha256>'). Required when the release has no tuple asset.

.EXAMPLE
./eng/sdk/Update-CheatEngineSdk.ps1 -Version 1.0.0 -WhatIf

Verifies the current pin against nuget.org and the lock files; changes nothing.

.EXAMPLE
./eng/sdk/Update-CheatEngineSdk.ps1 -Version 1.0.1 -AttestedAssetSha256 <sha256> -SourceCommit <sha> -SourceTreeHash <sha> -BridgeSourceFingerprint <c>:<xmake>

Moves the pin to 1.0.1.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
	[Parameter(Mandatory)]
	[ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$')]
	[string] $Version,

	[switch] $AllowMajor,

	[ValidatePattern('^[0-9a-f]{64}$')]
	[string] $AttestedAssetSha256,

	[ValidatePattern('^[0-9a-f]{40}$')]
	[string] $SourceCommit,

	[ValidatePattern('^[0-9a-f]{40}$')]
	[string] $SourceTreeHash,

	[ValidatePattern('^[0-9a-f]{64}:[0-9a-f]{64}$')]
	[string] $BridgeSourceFingerprint
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$packageId = 'CheatEngine.SDK'
$flatContainer = 'https://api.nuget.org/v3-flatcontainer/cheatengine.sdk'
$bridgeEntry = 'build/native/cheatengine-sdk-lua-bridge.dll'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$propsPath = Join-Path $repositoryRoot 'eng/CheatEngineSdk.props'
$identityPath = Join-Path $repositoryRoot 'eng/sdk/consumed-sdk.json'
$templateProjectPath = Join-Path $repositoryRoot 'templates/CheatEngine.Client.Templates/content/CheatEngine.Plugin/CheatEngine.Plugin.csproj'
$lockScript = Join-Path $repositoryRoot 'eng/Update-LockFiles.ps1'
$utf8 = [System.Text.UTF8Encoding]::new($false)

function Get-PinProperty {
	param([string] $Text, [string] $Name)
	$match = [regex]::Match($Text, "<$Name>(?<value>[^<]*)</$Name>")
	if (-not $match.Success) {
		throw "eng/CheatEngineSdk.props does not define $Name."
	}
	return $match.Groups['value'].Value
}

function Get-SdkLockEntry {
	param([string] $Path)
	$lock = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable
	foreach ($framework in $lock['dependencies'].Keys) {
		$packages = $lock['dependencies'][$framework]
		foreach ($name in $packages.Keys) {
			if ($name -ieq $packageId -and $packages[$name] -is [System.Collections.IDictionary]) {
				[pscustomobject]@{ Path = $Path; Framework = $framework; Resolved = $packages[$name]['resolved']; ContentHash = $packages[$name]['contentHash'] }
			}
		}
	}
}

function Get-LockWithoutSdk {
	param([string] $Json)
	$lock = $Json | ConvertFrom-Json -AsHashtable
	foreach ($framework in @($lock['dependencies'].Keys)) {
		$packages = $lock['dependencies'][$framework]
		foreach ($name in @($packages.Keys)) {
			if ($name -ieq $packageId) {
				$packages.Remove($name)
				continue
			}
			$entry = $packages[$name]
			if ($entry -is [System.Collections.IDictionary] -and $entry.Contains('dependencies')) {
				foreach ($dependency in @($entry['dependencies'].Keys)) {
					if ($dependency -ieq $packageId) {
						$entry['dependencies'].Remove($dependency)
					}
				}
			}
		}
	}
	return $lock | ConvertTo-Json -Depth 64 -Compress
}

function Test-LockFile {
	param([string] $ExpectedVersion, [string] $ExpectedContentHash, [switch] $CompareWithHead)
	$problems = [System.Collections.Generic.List[string]]::new()
	$locks = @(git -C $repositoryRoot ls-files '*packages.lock.json')
	if ($LASTEXITCODE -ne 0) {
		throw "git ls-files failed with exit code $LASTEXITCODE."
	}
	foreach ($relative in $locks) {
		$path = Join-Path $repositoryRoot $relative
		foreach ($entry in @(Get-SdkLockEntry -Path $path)) {
			if ($entry.Resolved -ne $ExpectedVersion -or $entry.ContentHash -ne $ExpectedContentHash) {
				$problems.Add("$relative ($($entry.Framework)) resolves $($entry.Resolved) with content hash $($entry.ContentHash).")
			}
		}
		if ($CompareWithHead) {
			$before = git -C $repositoryRoot show "HEAD:$relative" 2>$null
			if ($LASTEXITCODE -ne 0) {
				throw "git show HEAD:$relative failed with exit code $LASTEXITCODE."
			}
			if ((Get-LockWithoutSdk -Json ($before -join "`n")) -ne (Get-LockWithoutSdk -Json (Get-Content -LiteralPath $path -Raw))) {
				$problems.Add("$relative changed outside its $packageId entries.")
			}
		}
	}
	if ($problems.Count -gt 0) {
		throw "The lock files do not consume $packageId $ExpectedVersion with content hash ${ExpectedContentHash}:`n$($problems -join "`n")"
	}
	Write-Output "Every lock file resolves $packageId $ExpectedVersion with content hash $ExpectedContentHash."
}

# 1. Refusals that need nothing but the arguments and the committed pin.
$propsText = [System.IO.File]::ReadAllText($propsPath)
$currentVersion = Get-PinProperty -Text $propsText -Name 'CheatEngineSdkVersion'
$supportedMajor = [int](Get-PinProperty -Text $propsText -Name '_CheatEngineClientSupportedSdkMajor')
if ($Version.Contains('-')) {
	throw "$packageId $Version is a prerelease. The Client pins stable releases only; an SDK prerelease is tried through the canary build described in eng/sdk/README.md."
}
$major = [int]$Version.Split('.')[0]
if ($major -ne $supportedMajor) {
	if (-not $AllowMajor) {
		throw "$packageId $Version is major $major, but this Client supports major $supportedMajor. Moving to another major is the migration described in docs/migration/sdk-2.0.md; rerun with -AllowMajor only as part of that migration."
	}
	Write-Warning "Major $major is a migration, not a dependency bump: follow docs/migration/sdk-2.0.md and change _CheatEngineClientSupportedSdkMajor in eng/CheatEngineSdk.props in the same pull request. Until then CHEATENGINECLIENT9016 fails every build."
}

# 2. The version must be published.
$lowerVersion = $Version.ToLowerInvariant()
$index = Invoke-RestMethod -Uri "$flatContainer/index.json" -MaximumRetryCount 3 -RetryIntervalSec 5
if (@($index.versions) -notcontains $lowerVersion) {
	throw "$packageId $Version is not published on nuget.org (flat container $flatContainer/index.json)."
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("cheatengine-sdk-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -WhatIf:$false | Out-Null
try {
	# 3. Download and verify the package nuget.org serves.
	$nupkg = Join-Path $work "cheatengine.sdk.$lowerVersion.nupkg"
	Invoke-WebRequest -Uri "$flatContainer/$lowerVersion/cheatengine.sdk.$lowerVersion.nupkg" -OutFile $nupkg -MaximumRetryCount 3 -RetryIntervalSec 5
	$verification = @(dotnet nuget verify --all $nupkg -v n 2>&1)
	if ($LASTEXITCODE -ne 0) {
		throw "dotnet nuget verify --all failed with exit code ${LASTEXITCODE}:`n$($verification -join "`n")"
	}
	$contentHashLine = $verification | Select-String -Pattern '^Content hash:\s*(?<hash>\S+)\s*$' | Select-Object -First 1
	if ($null -eq $contentHashLine) {
		throw "dotnet nuget verify did not print the package content hash; use the .NET 10 SDK pinned by global.json."
	}
	$contentHash = $contentHashLine.Matches[0].Groups['hash'].Value
	$signedBytes = [System.IO.File]::ReadAllBytes($nupkg)
	$signedSha256 = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($signedBytes)).ToLowerInvariant()
	$signedSha512 = [Convert]::ToBase64String([System.Security.Cryptography.SHA512]::HashData($signedBytes))

	Add-Type -AssemblyName System.IO.Compression.FileSystem
	$archive = [System.IO.Compression.ZipFile]::OpenRead($nupkg)
	try {
		$bridge = $archive.GetEntry($bridgeEntry)
		if ($null -eq $bridge) {
			throw "$packageId $Version does not contain $bridgeEntry."
		}
		$stream = $bridge.Open()
		try {
			$bridgeSha256 = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()
		}
		finally {
			$stream.Dispose()
		}
	}
	finally {
		$archive.Dispose()
	}

	# 4. The fields nuget.org cannot provide: the SDK release tuple, else the parameters. Never guessed.
	$sources = @{
		attestedAssetSha256 = $AttestedAssetSha256
		sourceCommit = $SourceCommit
		sourceTreeHash = $SourceTreeHash
		sourceFingerprint = $BridgeSourceFingerprint
	}
	$tupleUri = "https://github.com/CheatEngineNet/CheatEngine.SDK/releases/download/v$Version/$packageId.$Version.tuple.json"
	try {
		$tuple = Invoke-RestMethod -Uri $tupleUri -MaximumRetryCount 2 -RetryIntervalSec 5
	}
	catch {
		$tuple = $null
		Write-Verbose "No SDK release tuple at ${tupleUri}: $($_.Exception.Message)"
	}
	if ($null -ne $tuple) {
		if ($tuple.package.contentHashSha512 -ne $contentHash -or $tuple.nativeBridge.sha256 -ne $bridgeSha256) {
			throw "The SDK release tuple $tupleUri does not describe the package nuget.org serves (content hash or bridge differs)."
		}
		$fromTuple = @{
			attestedAssetSha256 = $tuple.package.attestedAssetSha256
			sourceCommit = $tuple.source.commit
			sourceTreeHash = $tuple.source.treeHash
			sourceFingerprint = $tuple.nativeBridge.sourceFingerprint
		}
		foreach ($name in @($fromTuple.Keys)) {
			if ($sources[$name] -and $sources[$name] -ne $fromTuple[$name]) {
				throw "-$name '$($sources[$name])' contradicts the SDK release tuple ('$($fromTuple[$name])')."
			}
			$sources[$name] = $fromTuple[$name]
		}
	}

	$current = Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json -AsHashtable
	if ($Version -eq $currentVersion) {
		# 5. Already pinned: verify, change nothing.
		$expected = [ordered]@{
			contentHashSha512 = $contentHash
			nugetOrgSignedSha256 = $signedSha256
			nugetOrgSignedSha512 = $signedSha512
			'nativeBridge.sha256' = $bridgeSha256
			attestedAssetSha256 = $sources.attestedAssetSha256
			sourceCommit = $sources.sourceCommit
			sourceTreeHash = $sources.sourceTreeHash
			'nativeBridge.sourceFingerprint' = $sources.sourceFingerprint
		}
		$recorded = @{
			contentHashSha512 = $current.contentHashSha512
			nugetOrgSignedSha256 = $current.nugetOrgSignedSha256
			nugetOrgSignedSha512 = $current.nugetOrgSignedSha512
			'nativeBridge.sha256' = $current.nativeBridge.sha256
			attestedAssetSha256 = $current.attestedAssetSha256
			sourceCommit = $current.sourceCommit
			sourceTreeHash = $current.sourceTreeHash
			'nativeBridge.sourceFingerprint' = $current.nativeBridge.sourceFingerprint
		}
		$mismatches = @()
		foreach ($name in $expected.Keys) {
			if (-not $expected[$name]) {
				Write-Warning "$name was not re-verified: neither an SDK release tuple nor a parameter provides it."
			}
			elseif ($expected[$name] -ne $recorded[$name]) {
				$mismatches += "$name is '$($recorded[$name])' in consumed-sdk.json but '$($expected[$name])' for the published package."
			}
		}
		if ($mismatches.Count -gt 0) {
			throw "eng/sdk/consumed-sdk.json does not describe $packageId ${Version}:`n$($mismatches -join "`n")"
		}
		Write-Output "$packageId $Version is already pinned; eng/sdk/consumed-sdk.json matches the package nuget.org serves."
		Test-LockFile -ExpectedVersion $Version -ExpectedContentHash $contentHash
		return
	}

	$missing = @($sources.Keys | Where-Object { -not $sources[$_] } | Sort-Object)
	if ($missing.Count -gt 0) {
		throw "The SDK release has no tuple asset ($tupleUri), so pass these values from the SDK release and its attestation: $($missing -join ', ')."
	}

	# 6. Write the new pin.
	$identity = [ordered]@{
		schema = 'cheatengine-consumed-sdk/v0'
		id = $packageId
		version = $Version
		range = ''
		contentHashSha512 = $contentHash
		attestedAssetSha256 = $sources.attestedAssetSha256
		nugetOrgSignedSha256 = $signedSha256
		nugetOrgSignedSha512 = $signedSha512
		sourceCommit = $sources.sourceCommit
		sourceTreeHash = $sources.sourceTreeHash
		nativeBridge = [ordered]@{
			packagePath = $bridgeEntry
			sha256 = $bridgeSha256
			sourceFingerprint = $sources.sourceFingerprint
		}
		lockFile = $current.lockFile
	}
	$upperBound = Get-PinProperty -Text $propsText -Name 'CheatEngineSdkUpperBound'
	if ($major -ne $supportedMajor) {
		$upperBound = "$($major + 1).0.0"
	}
	$identity.range = "[$Version, $upperBound)"

	if ($PSCmdlet.ShouldProcess('eng/CheatEngineSdk.props, the template project and eng/sdk/consumed-sdk.json', "Pin $packageId $Version")) {
		$propsText = $propsText -replace '<CheatEngineSdkVersion>[^<]*</CheatEngineSdkVersion>', "<CheatEngineSdkVersion>$Version</CheatEngineSdkVersion>"
		$propsText = $propsText -replace '<CheatEngineSdkUpperBound>[^<]*</CheatEngineSdkUpperBound>', "<CheatEngineSdkUpperBound>$upperBound</CheatEngineSdkUpperBound>"
		[System.IO.File]::WriteAllText($propsPath, $propsText, $utf8)

		$templateText = [System.IO.File]::ReadAllText($templateProjectPath)
		$templateText = $templateText -replace '(<PackageReference Include="CheatEngine\.SDK" Version=")[^"]*(")', "`${1}$Version`${2}"
		[System.IO.File]::WriteAllText($templateProjectPath, $templateText, $utf8)

		$json = ($identity | ConvertTo-Json -Depth 8) -replace "`r?`n", "`r`n"
		[System.IO.File]::WriteAllText($identityPath, $json + "`r`n", $utf8)
		Write-Output "Pinned $packageId $Version ($($identity.range)); content hash $contentHash, bridge $bridgeSha256."

		# 7. Lock files: regenerated by the repository script only, then checked.
		if (Test-Path -LiteralPath $lockScript) {
			& $lockScript
			Test-LockFile -ExpectedVersion $Version -ExpectedContentHash $contentHash -CompareWithHead
		}
		else {
			Write-Warning ('eng/Update-LockFiles.ps1 is not in this checkout. Regenerate the lock files per project, fixtures first: ' +
				'dotnet restore <fixture> --force-evaluate for PluginA, PluginB and PluginCollision under tests/CheatEngine.Client.LivePlugin.Coexistence, ' +
				'then every other project except the template content, then dotnet restore CheatEngine.Client.slnx --locked-mode. ' +
				"Rerun this script with -Version $Version to verify the result.")
		}
	}
}
finally {
	Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue -WhatIf:$false
}
