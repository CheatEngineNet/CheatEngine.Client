#Requires -Version 7.2
<#
.SYNOPSIS
Regenerates the induced Client break list of docs/migration/sdk-2.0.md from the SDK ApiCompat suppressions.

.DESCRIPTION
The CheatEngine.SDK 2.0 package validation baseline is 1.0.0; every intentional break is listed in the SDK file
src/CheatEngine.SDK/CompatibilitySuppressions.xml. This script reads that file, the Client's consumed SDK surface
(tests/CheatEngine.Client.Tests/SdkContract/ConsumedSdkSurface.cs) and the allowlist of SDK types allowed in public
Client signatures (source-generators/CheatEngine.Client.SourceGenerators.Lua/ApprovedSdkClientTypes.cs), and rewrites
only the block between '<!-- generated:sdk-breaks:start -->' and '<!-- generated:sdk-breaks:end -->' of
docs/migration/sdk-2.0.md. Nothing else in the page changes.

A suppression is listed when its declaring type is allowlisted (Public API: a Client public break) or referenced by a
consumed-surface line (Consumed); the other suppressions are counted, not listed. Rows are sorted ordinally by target,
then by diagnostic id. The block starts with a provenance line: the SDK commit, the SHA-256 of the suppression file,
the client-canary report and the generation date. The page is written in UTF-8 without a byte order mark, with CRLF
line endings in the block. When only the date would change, the page is left untouched, so a second run with the same
inputs changes nothing.

This is a generator, not a gate: tests/CheatEngine.Client.Repository.Tests/Migration/SdkMigrationGuideTests.cs checks
the block. The script reads the SDK file it is given and never contacts the SDK repository.

.PARAMETER SuppressionFile
The SDK src/CheatEngine.SDK/CompatibilitySuppressions.xml, for example extracted with
'git -C <sdk> show <commit>:src/CheatEngine.SDK/CompatibilitySuppressions.xml'.

.PARAMETER SdkCommit
The 40-character SDK commit the suppression file was read from.

.PARAMETER CanaryReport
Optional client-canary-report.json (schema cheatengine-client-canary-report/v0) of the SDK's advisory client-canary job.
Without it, the canary section stays a placeholder.

.PARAMETER CanaryRunUrl
Optional https URL of the workflow run that produced the canary report.

.EXAMPLE
git -C ..\CheatEngine.SDK show feat/audit-remediation-cicd:src/CheatEngine.SDK/CompatibilitySuppressions.xml > $env:TEMP\sdk-suppressions.xml
./eng/migration/Update-SdkMigrationBreakList.ps1 -SuppressionFile $env:TEMP\sdk-suppressions.xml -SdkCommit (git -C ..\CheatEngine.SDK rev-parse feat/audit-remediation-cicd)

Regenerates the break list from the SDK branch head.

.EXAMPLE
./eng/migration/Update-SdkMigrationBreakList.ps1 -SuppressionFile .\CompatibilitySuppressions.xml -SdkCommit <sha> -CanaryReport .\client-canary-report.json -CanaryRunUrl https://github.com/CheatEngineNet/CheatEngine.SDK/actions/runs/<id> -WhatIf

Shows what would change with a canary report, without writing.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
	[Parameter(Mandatory)]
	[string] $SuppressionFile,

	[Parameter(Mandatory)]
	[ValidatePattern('^[0-9a-f]{40}$')]
	[string] $SdkCommit,

	[string] $CanaryReport,

	[ValidatePattern('^https://')]
	[string] $CanaryRunUrl
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$guidePath = Join-Path $repositoryRoot 'docs/migration/sdk-2.0.md'
$surfacePath = Join-Path $repositoryRoot 'tests/CheatEngine.Client.Tests/SdkContract/ConsumedSdkSurface.cs'
$allowlistPath = Join-Path $repositoryRoot 'source-generators/CheatEngine.Client.SourceGenerators.Lua/ApprovedSdkClientTypes.cs'
$startMarker = '<!-- generated:sdk-breaks:start -->'
$endMarker = '<!-- generated:sdk-breaks:end -->'
$canaryPlaceholder = "Status: placeholder $([char] 0x2014) content arrives with DOCS-FINAL (V4c)"
$utf8 = [Text.UTF8Encoding]::new($false)

# The action for a break is chosen by its declaring type; any other type gets the default review action.
$actions = @{
	'CheatEngine.SDK.Engine.Inspection.AddressResolutionOptions' = 'Client public break: replace the UseHostSymbolTable path by EngineInspection.ResolveHostAddress, add the `*REMOVED*` PublicAPI lines, and review every `new AddressResolutionOptions(true)`, which binds Shallow on 2.0.'
	'CheatEngine.SDK.Engine.Memory.MemoryAccessFailure'          = 'Recompile against 2.0 (a 1.0.0 binary reads the renumbered values under the old names); the Client only formats the value, then maps PartialRead, PointerWidthUnknown and PointerValueExceedsTargetWidth.'
}
$defaultAction = 'Review the Client usage against the 2.0 member before the bump.'

function Get-DeclaringType {
	param([Parameter(Mandatory)] [string] $DocId)

	$name = $DocId.Substring(2)
	if ($DocId.StartsWith('T:', [StringComparison]::Ordinal)) {
		return $name
	}

	$parenthesis = $name.IndexOf('(')
	if ($parenthesis -ge 0) {
		$name = $name.Substring(0, $parenthesis)
	}

	return $name.Substring(0, $name.LastIndexOf('.'))
}

function Get-MemberName {
	param([Parameter(Mandatory)] [string] $DocId)

	if ($DocId.StartsWith('T:', [StringComparison]::Ordinal)) {
		return $null
	}

	$name = $DocId.Substring(2)
	$parenthesis = $name.IndexOf('(')
	if ($parenthesis -ge 0) {
		$name = $name.Substring(0, $parenthesis)
	}

	$member = $name.Substring($name.LastIndexOf('.') + 1)
	if ($member -eq '#ctor') {
		return '.ctor'
	}

	return $member
}

function Get-ClientExposure {
	param(
		[Parameter(Mandatory)] [string] $DocId,
		[Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Allowlist,
		[Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $SurfaceLines
	)

	$type = Get-DeclaringType -DocId $DocId
	if ($Allowlist -ccontains $type) {
		return "Public API (``$type``)"
	}

	$member = Get-MemberName -DocId $DocId
	$candidates = [System.Collections.Generic.List[string]]::new()
	if ($null -ne $member) {
		foreach ($line in $SurfaceLines) {
			if ($line.Contains(" M ${type}::${member}(", [StringComparison]::Ordinal)) {
				$candidates.Add($line)
			}
		}
	}

	if ($candidates.Count -eq 0) {
		foreach ($line in $SurfaceLines) {
			if ($line.EndsWith(" T $type", [StringComparison]::Ordinal)) {
				$candidates.Add($line)
			}
		}
	}

	if ($candidates.Count -eq 0) {
		foreach ($line in $SurfaceLines) {
			if ($line.Contains(" ${type}::", [StringComparison]::Ordinal)) {
				$candidates.Add($line)
			}
		}
	}

	if ($candidates.Count -eq 0) {
		return $null
	}

	$candidates.Sort([StringComparer]::Ordinal)
	return "Consumed (``$($candidates[0])``)"
}

function Read-QuotedLine {
	param(
		[Parameter(Mandatory)] [string] $Path,
		[Parameter(Mandatory)] [string] $Pattern
	)

	$values = [System.Collections.Generic.List[string]]::new()
	foreach ($line in [IO.File]::ReadAllLines($Path)) {
		$match = [regex]::Match($line, $Pattern)
		if ($match.Success) {
			$values.Add($match.Groups['value'].Value)
		}
	}

	if ($values.Count -eq 0) {
		throw "No entry matching '$Pattern' was found in '$Path'."
	}

	return , $values.ToArray()
}

function ConvertTo-CanarySection {
	param([string] $ReportPath)

	if ([string]::IsNullOrEmpty($ReportPath)) {
		return , @($canaryPlaceholder)
	}

	$report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json
	if ($report.schema -ne 'cheatengine-client-canary-report/v0') {
		throw "'$ReportPath' is not a cheatengine-client-canary-report/v0 document."
	}

	$lines = [System.Collections.Generic.List[string]]::new()
	$lines.Add("Outcome ``$($report.outcome)``: $($report.errorCount) error(s) when CheatEngine.Client ``$($report.client.commit)`` was built against CheatEngine.SDK ``$($report.sdkPackage.version)`` (package SHA-256 ``$($report.sdkPackage.sha256)``), report created $($report.createdUtc). The messages are in the report artifact.")
	if (@($report.errors).Count -gt 0) {
		$lines.Add('')
		$lines.Add('| Code | File | Line |')
		$lines.Add('|---|---|---|')
		$rows = [System.Collections.Generic.List[string]]::new()
		foreach ($canaryError in @($report.errors)) {
			$file = if ($null -eq $canaryError.file) { 'n/a' } else { "``$($canaryError.file)``" }
			$line = if ($null -eq $canaryError.line) { 'n/a' } else { [string] $canaryError.line }
			$rows.Add("| ``$($canaryError.code)`` | $file | $line |")
		}

		$rows.Sort([StringComparer]::Ordinal)
		foreach ($row in ($rows | Select-Object -Unique)) {
			$lines.Add($row)
		}
	}

	return , $lines.ToArray()
}

foreach ($path in @($SuppressionFile, $guidePath, $surfacePath, $allowlistPath)) {
	if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
		throw "Required file '$path' does not exist."
	}
}

if (-not [string]::IsNullOrEmpty($CanaryReport) -and -not (Test-Path -LiteralPath $CanaryReport -PathType Leaf)) {
	throw "Canary report '$CanaryReport' does not exist."
}

[xml] $suppressionXml = Get-Content -LiteralPath $SuppressionFile -Raw
$suppressions = [System.Collections.Generic.List[pscustomobject]]::new()
$seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($suppression in @($suppressionXml.Suppressions.Suppression)) {
	$id = [string] $suppression.DiagnosticId
	$target = [string] $suppression.Target
	if ($id -cnotmatch '^CP\d{4}$') {
		throw "Unexpected diagnostic id '$id' in '$SuppressionFile'."
	}

	if ($target -cnotmatch '^[TMFPE]:[A-Za-z_]') {
		throw "Unexpected suppression target '$target' in '$SuppressionFile'."
	}

	if ($seen.Add("$id|$target")) {
		$suppressions.Add([pscustomobject] @{ Id = $id; Target = $target })
	}
}

$allowlist = Read-QuotedLine -Path $allowlistPath -Pattern '^\s*"(?<value>CheatEngine\.SDK\.[^"]+)",?\s*$'
$surfaceLines = Read-QuotedLine -Path $surfacePath -Pattern '^\s*"(?<value>CheatEngine\.Client[^"]*)",?\s*$'

$sorted = [System.Collections.Generic.List[pscustomobject]]::new($suppressions)
$sorted.Sort([Comparison[pscustomobject]] {
		param($left, $right)
		$byTarget = [string]::CompareOrdinal($left.Target, $right.Target)
		if ($byTarget -ne 0) {
			return $byTarget
		}

		return [string]::CompareOrdinal($left.Id, $right.Id)
	})

$rows = [System.Collections.Generic.List[string]]::new()
foreach ($suppression in $sorted) {
	$exposure = Get-ClientExposure -DocId $suppression.Target -Allowlist $allowlist -SurfaceLines $surfaceLines
	if ($null -eq $exposure) {
		continue
	}

	$type = Get-DeclaringType -DocId $suppression.Target
	$action = if ($actions.ContainsKey($type)) { $actions[$type] } else { $defaultAction }
	$rows.Add("| $($suppression.Id) | ``$($suppression.Target)`` | $exposure | $action |")
}

$suppressionHash = (Get-FileHash -LiteralPath $SuppressionFile -Algorithm SHA256).Hash.ToLowerInvariant()
$canarySource = if ([string]::IsNullOrEmpty($CanaryReport)) {
	'none'
}
elseif (-not [string]::IsNullOrEmpty($CanaryRunUrl)) {
	$CanaryRunUrl
}
else {
	"local report SHA-256 $((Get-FileHash -LiteralPath $CanaryReport -Algorithm SHA256).Hash.ToLowerInvariant())"
}

$date = [DateTime]::UtcNow.ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
$unlisted = $sorted.Count - $rows.Count

$block = [System.Collections.Generic.List[string]]::new()
$block.Add($startMarker)
$block.Add("Provenance: SDK commit ``$SdkCommit``, CompatibilitySuppressions.xml SHA-256 ``$suppressionHash``, client-canary report: $canarySource, generated $date.")
$block.Add('')
$block.Add('| ApiCompat id | SDK member (DocId) | Client exposure | Action |')
$block.Add('|---|---|---|---|')
foreach ($row in $rows) {
	$block.Add($row)
}

$block.Add('')
$block.Add("Suppressions listed: $($rows.Count). Suppressions without Client exposure (counted, not listed): $unlisted of $($sorted.Count).")
$block.Add('')
$block.Add('### Client canary')
$block.Add('')
foreach ($line in (ConvertTo-CanarySection -ReportPath $CanaryReport)) {
	$block.Add($line)
}

$block.Add($endMarker)
$newBlock = [string]::Join("`r`n", $block)

$guide = [IO.File]::ReadAllText($guidePath, $utf8)
$start = $guide.IndexOf($startMarker, [StringComparison]::Ordinal)
$end = $guide.IndexOf($endMarker, [StringComparison]::Ordinal)
if ($start -lt 0 -or $end -lt $start -or $guide.IndexOf($startMarker, $start + 1, [StringComparison]::Ordinal) -ge 0) {
	throw "'$guidePath' must contain exactly one '$startMarker' followed by '$endMarker'."
}

$oldBlock = $guide.Substring($start, $end + $endMarker.Length - $start)
$oldBlockToday = [regex]::Replace($oldBlock, 'generated \d{4}-\d{2}-\d{2}\.', "generated $date.")
if ($oldBlockToday -ceq $newBlock) {
	Write-Output "The SDK break list is up to date: $($rows.Count) listed, $unlisted counted of $($sorted.Count) suppression(s)."
	return
}

$updated = $guide.Substring(0, $start) + $newBlock + $guide.Substring($end + $endMarker.Length)
if ($PSCmdlet.ShouldProcess($guidePath, 'Rewrite the generated SDK break list')) {
	[IO.File]::WriteAllText($guidePath, $updated, $utf8)
	Write-Output "Rewrote the SDK break list: $($rows.Count) listed, $unlisted counted of $($sorted.Count) suppression(s)."
}
