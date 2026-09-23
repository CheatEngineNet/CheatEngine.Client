#Requires -Version 7.2
<#
.SYNOPSIS
Verifies that a release run may build and publish the seven Client packages, before anything is built.

.DESCRIPTION
Used by the verify job of .github/workflows/release.yml. Only a tag push releases: any other event, including a
workflow_dispatch started from a tag, is a dry run that writes empty outputs and a notice, so no later job attests,
drafts or publishes. A push must come from a tag, and it fails unless:
- the tag is v<major>.<minor>.<patch> with an optional SemVer prerelease and no build metadata;
- the tag commit is on the first-parent history of main (only a merged head may be released);
- none of the seven package ids already lists the version on nuget.org (a version can never be replaced);
- the pinned CheatEngine.SDK version is published, carries a valid repository signature, and has the NuGet content
  hash recorded in eng/sdk/consumed-sdk.json and in the lock file.
It then writes version=<x.y.z[-pre]> and prerelease=true|false to the GitHub output file.

.PARAMETER EventName
github.event_name: 'push' for a tag push; anything else is a dry run.

.PARAMETER RefType
github.ref_type: 'tag' or 'branch'.

.PARAMETER RefName
github.ref_name, for example v0.1.0.

.PARAMETER OutputPath
The GitHub output file (GITHUB_OUTPUT).

.PARAMETER MainRef
The ref whose first-parent history must contain the tag commit.

.EXAMPLE
./eng/release/Test-ReleaseTag.ps1 -EventName push -RefType tag -RefName v0.1.0 -OutputPath $env:GITHUB_OUTPUT
#>
[CmdletBinding()]
param(
	[Parameter(Mandatory)]
	[string] $EventName,

	[Parameter(Mandatory)]
	[string] $RefType,

	[Parameter(Mandatory)]
	[string] $RefName,

	[Parameter(Mandatory)]
	[string] $OutputPath,

	[string] $MainRef = 'refs/remotes/origin/main'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$packageIds = @(
	'CheatEngine.Client', 'CheatEngine.Client.Abstractions', 'CheatEngine.Client.Core',
	'CheatEngine.Client.Extensions.DependencyInjection', 'CheatEngine.Client.Fluent', 'CheatEngine.Client.Hosting',
	'CheatEngine.Client.Templates')
$flatContainer = 'https://api.nuget.org/v3-flatcontainer'

function Write-ReleaseOutput {
	param([string] $Path, [string] $Version, [string] $Prerelease)
	"version=$Version" | Out-File -FilePath $Path -Append -Encoding utf8
	"prerelease=$Prerelease" | Out-File -FilePath $Path -Append -Encoding utf8
}

function Get-PublishedVersionList {
	param([string] $PackageId)
	$uri = "$flatContainer/$($PackageId.ToLowerInvariant())/index.json"
	$index = Invoke-RestMethod -Uri $uri -SkipHttpErrorCheck -StatusCodeVariable status -MaximumRetryCount 3 -RetryIntervalSec 5
	if ($status -eq 404) {
		return @()
	}
	if ($status -ne 200) {
		throw "Reading $uri returned HTTP $status."
	}
	return @($index.versions)
}

# Only a tag push releases. A dispatch started from a tag has ref type 'tag' too, and must stay a dry run: with empty
# outputs, attest, draft-release and publish are skipped.
if ($EventName -cne 'push') {
	Write-Output "::notice::Dry run ($EventName on $RefType ${RefName}): the run builds and tests, and never attests, drafts a release or publishes."
	Write-ReleaseOutput -Path $OutputPath -Version '' -Prerelease ''
	return
}
if ($RefType -cne 'tag') {
	throw "A release run started by a push must come from a v*.*.* tag; $RefType $RefName is not a tag."
}

# 1. The tag.
$semver = '^v(?<version>(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?)$'
if ($RefName -cnotmatch $semver) {
	throw "Tag $RefName is not a release tag. Use v<major>.<minor>.<patch> with an optional SemVer prerelease such as -rc.1, and no build metadata."
}
$version = $Matches['version']

# 2. Only a commit on the first-parent line of main, the merged head, may be released: a squash merge gives the pull
#    request new commits, so a tag on a branch commit would release something main never contained.
$commit = git -C $repositoryRoot rev-parse --verify "refs/tags/$RefName^{commit}"
if ($LASTEXITCODE -ne 0) {
	throw "Tag $RefName does not resolve to a commit (exit code $LASTEXITCODE)."
}
$landed = @(git -C $repositoryRoot rev-list --first-parent $MainRef)
if ($LASTEXITCODE -ne 0) {
	throw "The checkout has no $MainRef to compare the tag with (exit code $LASTEXITCODE); check out with fetch-depth 0."
}
if ($landed -notcontains $commit) {
	throw "Tag $RefName points to $commit, which is not on the first-parent history of main. Tag the merged head of main."
}

# 3. None of the seven versions exists: a full re-run after a publication would build different files for an immutable
#    version. Re-run only the failed jobs of the original run; they reuse its artifacts.
foreach ($id in $packageIds) {
	if ((Get-PublishedVersionList -PackageId $id) -contains $version.ToLowerInvariant()) {
		throw "$id $version is already on nuget.org. Re-run only the failed jobs of the original run, or tag a new version."
	}
}

# 4. The consumed SDK is the reviewed package.
[xml] $pin = Get-Content -LiteralPath (Join-Path $repositoryRoot 'eng/CheatEngineSdk.props') -Raw
$sdkVersion = $pin.SelectSingleNode('/Project/PropertyGroup/CheatEngineSdkVersion').InnerText.Trim()
$identity = Get-Content -LiteralPath (Join-Path $repositoryRoot 'eng/sdk/consumed-sdk.json') -Raw | ConvertFrom-Json
$lock = Get-Content -LiteralPath (Join-Path $repositoryRoot $identity.lockFile) -Raw | ConvertFrom-Json -AsHashtable
$lockEntry = $lock['dependencies']['net10.0']['CheatEngine.SDK']
if ($identity.version -ne $sdkVersion -or $lockEntry['resolved'] -ne $sdkVersion -or $lockEntry['contentHash'] -ne $identity.contentHashSha512) {
	throw "eng/CheatEngineSdk.props ($sdkVersion), eng/sdk/consumed-sdk.json ($($identity.version)) and $($identity.lockFile) ($($lockEntry['resolved'])) disagree on the consumed CheatEngine.SDK."
}
if ((Get-PublishedVersionList -PackageId 'CheatEngine.SDK') -notcontains $sdkVersion.ToLowerInvariant()) {
	throw "The pinned CheatEngine.SDK $sdkVersion is not on nuget.org."
}
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('release-sdk-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
	$lowerSdk = $sdkVersion.ToLowerInvariant()
	$nupkg = Join-Path $work "cheatengine.sdk.$lowerSdk.nupkg"
	Invoke-WebRequest -Uri "$flatContainer/cheatengine.sdk/$lowerSdk/cheatengine.sdk.$lowerSdk.nupkg" -OutFile $nupkg -MaximumRetryCount 3 -RetryIntervalSec 5
	$verification = @(dotnet nuget verify --all $nupkg -v n 2>&1)
	if ($LASTEXITCODE -ne 0) {
		throw "dotnet nuget verify --all failed for CheatEngine.SDK $sdkVersion with exit code ${LASTEXITCODE}:`n$($verification -join "`n")"
	}
	$line = $verification | Select-String -Pattern '^Content hash:\s*(?<hash>\S+)\s*$' | Select-Object -First 1
	if ($null -eq $line -or $line.Matches[0].Groups['hash'].Value -ne $identity.contentHashSha512) {
		throw "CheatEngine.SDK $sdkVersion on nuget.org does not have the content hash of eng/sdk/consumed-sdk.json ($($identity.contentHashSha512))."
	}
}
finally {
	Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$prerelease = if ($version.Contains('-')) { 'true' } else { 'false' }
Write-Output "Releasing $version (prerelease: $prerelease) from $commit against CheatEngine.SDK $sdkVersion."
Write-ReleaseOutput -Path $OutputPath -Version $version -Prerelease $prerelease
