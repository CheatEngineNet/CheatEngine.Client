#Requires -Version 7.2
<#
.SYNOPSIS
    Regenerates, or verifies, every NuGet lock file of CheatEngine.Client in the order the repository requires.

.DESCRIPTION
    Every project restores with a committed packages.lock.json, and CI restores in locked mode. The three Coexistence
    fixtures under tests/CheatEngine.Client.LivePlugin.Coexistence opt out of Central Package Management
    (CoexistencePlugin.props) and keep version 1 lock files. A solution-level re-evaluation once rewrote them with
    CentralTransitive entries, which broke every locked restore with NU1004. This script therefore never restores the
    solution with --force-evaluate. It:

      1. restores each Coexistence fixture on its own with --force-evaluate (PluginA, PluginB, PluginCollision) and
         checks that it still has a version 1 lock file without CentralTransitive entries;
      2. restores every other project on its own with --force-evaluate, then re-checks the fixtures;
      3. verifies the whole graph with 'dotnet restore CheatEngine.Client.slnx --locked-mode' (never combined with
         --force-evaluate, see NU1005);
      4. keeps each lock file's final newline as committed in HEAD (NuGet writes none), so only real changes produce a
         diff, even after a plain restore stripped it;
      5. checks the structural invariants: a lock file per project, format version 1 or 2 matching the project's
         Central Package Management mode, one CheatEngine.SDK identity across the graph, no CheatEngine.Client package
         resolved from a feed, and the runtime packs of every Native AOT project.

    Projects come from git (tracked plus untracked, non-ignored *.csproj); only the template content project is
    excluded. Run it on Windows with the exact .NET SDK of global.json: Native AOT lock sections record host-RID
    packages and the SDK-implicit packages move with the SDK.

    https://learn.microsoft.com/nuget/consume-packages/package-references-in-project-files#locking-dependencies

.PARAMETER Verify
    Regenerates in place, then fails when a packages.lock.json differs from git or an untracked lock file exists. This
    is what the CI job "Lock files" runs.

.EXAMPLE
    ./eng/Update-LockFiles.ps1

    Regenerates the lock files and lists the ones that changed.

.EXAMPLE
    ./eng/Update-LockFiles.ps1 -Verify

    Fails, naming the files, when the committed lock files are not what this script produces.
#>
[CmdletBinding()]
param(
    [switch] $Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solution = 'CheatEngine.Client.slnx'
$lockFilePattern = '*packages.lock.json'

# Built only after 'dotnet new' instantiates it (the package smoke test); it has no lock file. Same exclusion as
# tests/CheatEngine.Client.Repository.Tests/Solution/SolutionInventoryTests.cs.
$excludedProjects = @(
    'templates/CheatEngine.Client.Templates/content/CheatEngine.Plugin/CheatEngine.Plugin.csproj'
)

# The only projects allowed outside Central Package Management, restored first and one by one, in this order.
$coexistenceFixtures = @(
    'tests/CheatEngine.Client.LivePlugin.Coexistence/PluginA/CheatEngine.Client.LivePlugin.Coexistence.PluginA.csproj'
    'tests/CheatEngine.Client.LivePlugin.Coexistence/PluginB/CheatEngine.Client.LivePlugin.Coexistence.PluginB.csproj'
    'tests/CheatEngine.Client.LivePlugin.Coexistence/PluginCollision/CheatEngine.Client.LivePlugin.Coexistence.PluginCollision.csproj'
)

function Write-Failure {
    param([Parameter(Mandatory)] [string] $Message)

    if ($env:GITHUB_ACTIONS -eq 'true') {
        Write-Host "::error title=Lock files::$Message"
    }
    else {
        Write-Host "error: $Message" -ForegroundColor Red
    }
}

function Invoke-Git {
    param([Parameter(Mandatory)] [string[]] $Arguments)

    $output = @(& git -C $repositoryRoot @Arguments)
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }

    return $output
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)] [string[]] $Arguments,
        [Parameter(Mandatory)] [string] $Description
    )

    Write-Host "> dotnet $($Arguments -join ' ')"
    & dotnet @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE (dotnet $($Arguments -join ' '))."
    }
}

function Get-ProjectEvaluation {
    param([Parameter(Mandatory)] [string] $Project)

    $names = 'ManagePackageVersionsCentrally', 'PublishAot', 'RuntimeIdentifier', 'TargetFramework', 'NuGetLockFilePath'
    $arguments = @('msbuild', $Project) + @($names | ForEach-Object { "-getProperty:$_" })
    $output = @(& dotnet @arguments)
    if ($LASTEXITCODE -ne 0) {
        throw "Evaluating $Project failed with exit code $LASTEXITCODE.`n$($output -join "`n")"
    }

    # -getProperty prints one JSON document; skip anything MSBuild might print before it.
    $text = $output -join "`n"
    $start = $text.IndexOf('{')
    if ($start -lt 0) {
        throw "Evaluating $Project printed no property document:`n$text"
    }

    $properties = ($text.Substring($start) | ConvertFrom-Json -AsHashtable)['Properties']
    $lockFile = if ([string]::IsNullOrEmpty($properties['NuGetLockFilePath'])) {
        Join-Path (Split-Path -Parent $Project) 'packages.lock.json'
    }
    else {
        Join-Path (Split-Path -Parent $Project) $properties['NuGetLockFilePath']
    }

    return [pscustomobject] @{
        Project                  = $Project
        CentralPackageManagement = $properties['ManagePackageVersionsCentrally'] -eq 'true'
        PublishAot               = $properties['PublishAot'] -eq 'true'
        RuntimeIdentifier        = [string] $properties['RuntimeIdentifier']
        TargetFramework          = [string] $properties['TargetFramework']
        LockFile                 = [IO.Path]::GetRelativePath($repositoryRoot, [IO.Path]::GetFullPath((Join-Path $repositoryRoot $lockFile))).Replace('\', '/')
    }
}

function Read-LockFile {
    param([Parameter(Mandatory)] [string] $Path)

    $fullPath = Join-Path $repositoryRoot $Path
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        return $null
    }

    try {
        return Get-Content -LiteralPath $fullPath -Raw | ConvertFrom-Json -AsHashtable
    }
    catch {
        throw "$Path is not valid JSON: $($_.Exception.Message)"
    }
}

function Get-LockViolation {
    param(
        [Parameter(Mandatory)] [pscustomobject] $Evaluation,
        [AllowNull()] [hashtable] $Lock
    )

    $path = $Evaluation.LockFile
    if ($null -eq $Lock) {
        return , "$path is missing: every project restores with a committed lock file."
    }

    $violations = [Collections.Generic.List[string]]::new()
    $version = $Lock['version']
    if ($version -notin 1, 2) {
        $violations.Add("$path declares lock format version '$version'; only 1 and 2 are supported.")
    }

    if ($Evaluation.CentralPackageManagement -and $version -ne 2) {
        $violations.Add("$path is version $version, but $($Evaluation.Project) uses Central Package Management (version 2).")
    }

    if (-not $Evaluation.CentralPackageManagement -and $version -ne 1) {
        $violations.Add("$path is version $version, but $($Evaluation.Project) opts out of Central Package Management (version 1).")
    }

    $sections = $Lock['dependencies']
    if ($sections -isnot [hashtable]) {
        $violations.Add("$path has no dependencies object.")
        return $violations.ToArray()
    }

    foreach ($section in $sections.GetEnumerator()) {
        foreach ($dependency in $section.Value.GetEnumerator()) {
            $type = [string] $dependency.Value['type']
            if ($version -eq 1 -and $type -eq 'CentralTransitive') {
                $violations.Add("$path is a version 1 lock file with a CentralTransitive entry ($($dependency.Key)); a solution-level --force-evaluate rewrote it.")
            }

            if ($dependency.Key -like 'CheatEngine.Client*' -and $type -ne 'Project') {
                $violations.Add("$path resolves $($dependency.Key) as '$type' from a feed; Client projects are always project references.")
            }
        }
    }

    if ($Evaluation.PublishAot) {
        $ridSection = "$($Evaluation.TargetFramework)/$($Evaluation.RuntimeIdentifier)"
        $runtimePack = "runtime.$($Evaluation.RuntimeIdentifier).Microsoft.DotNet.ILCompiler"
        if ([string]::IsNullOrEmpty($Evaluation.RuntimeIdentifier)) {
            $violations.Add("$($Evaluation.Project) publishes Native AOT without a RuntimeIdentifier, so its lock file cannot record the ILCompiler runtime pack.")
        }
        elseif (-not $sections.ContainsKey($ridSection) -or -not $sections[$ridSection].ContainsKey($runtimePack)) {
            $violations.Add("$path has no '$ridSection' section with $runtimePack; 'dotnet publish --no-restore' would fail.")
        }
    }

    return $violations.ToArray()
}

function Get-SdkIdentity {
    param([Parameter(Mandatory)] [hashtable] $Lock)

    foreach ($section in $Lock['dependencies'].GetEnumerator()) {
        foreach ($dependency in $section.Value.GetEnumerator()) {
            if ($dependency.Key -eq 'CheatEngine.SDK') {
                return "$($dependency.Value['resolved']) (contentHash $($dependency.Value['contentHash']))"
            }
        }
    }

    return $null
}

function Get-CommittedContent {
    param([Parameter(Mandatory)] [string] $Path)

    # Raw blob bytes: PowerShell splits native output into lines and would lose the final newline.
    $startInfo = [Diagnostics.ProcessStartInfo]::new('git')
    foreach ($argument in @('-C', $repositoryRoot, 'show', "HEAD:$Path")) {
        $startInfo.ArgumentList.Add($argument)
    }

    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    $process = [Diagnostics.Process]::Start($startInfo)
    try {
        $errorTask = $process.StandardError.ReadToEndAsync()
        $buffer = [IO.MemoryStream]::new()
        $process.StandardOutput.BaseStream.CopyTo($buffer)
        $process.WaitForExit()
        $null = $errorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            return $null
        }

        return , $buffer.ToArray()
    }
    finally {
        $process.Dispose()
    }
}

function Get-NormalizedText {
    param([AllowEmptyString()] [string] $Text)

    return $Text.Replace("`r`n", "`n").TrimEnd()
}

Push-Location -LiteralPath $repositoryRoot
try {
    if (-not $IsWindows) {
        throw 'Run eng/Update-LockFiles.ps1 on Windows: Native AOT lock sections record host-RID ILCompiler packages.'
    }

    if (-not [string]::IsNullOrEmpty($env:CoexistenceSdkPackageVersion)) {
        throw "Unset the environment variable CoexistenceSdkPackageVersion ('$env:CoexistenceSdkPackageVersion') first: MSBuild reads it as a property, and any value other than 1.0.0 turns lock files off for the Coexistence fixtures."
    }

    $globalJson = Get-Content -LiteralPath (Join-Path $repositoryRoot 'global.json') -Raw | ConvertFrom-Json -AsHashtable
    $pinnedSdk = [string] $globalJson['sdk']['version']
    $activeSdk = @(& dotnet --version 2>&1) -join ' '
    if ($LASTEXITCODE -ne 0 -or $activeSdk.Trim() -ne $pinnedSdk) {
        throw ".NET SDK $pinnedSdk is required exactly (global.json), found '$($activeSdk.Trim())'. Install it with: winget install Microsoft.DotNet.SDK.10 --version $pinnedSdk"
    }

    $projects = @(
        Invoke-Git -Arguments @('ls-files', '--', '*.csproj')
        Invoke-Git -Arguments @('ls-files', '--others', '--exclude-standard', '--', '*.csproj')
    ) | Where-Object { $_ -and $_ -notin $excludedProjects } | Sort-Object -Unique -CaseSensitive

    Write-Host "Evaluating $($projects.Count) projects with .NET SDK $pinnedSdk..."
    $evaluations = @($projects | ForEach-Object { Get-ProjectEvaluation -Project $_ })

    $outsideCentralManagement = @($evaluations | Where-Object { -not $_.CentralPackageManagement } | ForEach-Object Project)
    if ((($outsideCentralManagement | Sort-Object -CaseSensitive) -join '|') -cne (($coexistenceFixtures | Sort-Object -CaseSensitive) -join '|')) {
        throw "Projects outside Central Package Management must be exactly the Coexistence fixtures ($($coexistenceFixtures -join ', ')); evaluation found: $($outsideCentralManagement -join ', '). Review the change before regenerating lock files."
    }

    # Snapshot every existing lock file so an unchanged graph keeps its bytes, and record whether the committed (HEAD)
    # version ends with a newline: NuGet never writes one, so without this every regeneration would churn those files.
    $snapshots = @{}
    $committedNewline = @{}
    foreach ($evaluation in $evaluations) {
        $fullPath = Join-Path $repositoryRoot $evaluation.LockFile
        if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
            $snapshots[$evaluation.LockFile] = [IO.File]::ReadAllBytes($fullPath)
        }

        $committed = Get-CommittedContent -Path $evaluation.LockFile
        if ($null -ne $committed) {
            $committedNewline[$evaluation.LockFile] = $committed.Length -gt 0 -and $committed[-1] -eq 10
        }
    }

    $fixtureEvaluations = @($evaluations | Where-Object { $_.Project -in $coexistenceFixtures })
    Write-Host 'Step 1: Coexistence fixtures, one by one.'
    foreach ($fixture in $coexistenceFixtures) {
        Invoke-DotNet -Arguments @('restore', $fixture, '--force-evaluate') -Description "Restoring $fixture"
    }

    foreach ($evaluation in $fixtureEvaluations) {
        $violations = @(Get-LockViolation -Evaluation $evaluation -Lock (Read-LockFile -Path $evaluation.LockFile))
        if ($violations.Count -gt 0) {
            throw ($violations -join [Environment]::NewLine)
        }
    }

    Write-Host 'Step 2: every other project, one by one (never the solution with --force-evaluate).'
    foreach ($evaluation in @($evaluations | Where-Object { $_.Project -notin $coexistenceFixtures })) {
        Invoke-DotNet -Arguments @('restore', $evaluation.Project, '--force-evaluate') -Description "Restoring $($evaluation.Project)"
    }

    foreach ($evaluation in $fixtureEvaluations) {
        $violations = @(Get-LockViolation -Evaluation $evaluation -Lock (Read-LockFile -Path $evaluation.LockFile))
        if ($violations.Count -gt 0) {
            throw "Restoring the other projects changed a Coexistence fixture lock file: $($violations -join ' ')"
        }
    }

    Write-Host 'Step 3: locked restore of the solution.'
    Invoke-DotNet -Arguments @('restore', $solution, '--locked-mode') -Description "Locked restore of $solution"

    Write-Host 'Step 4: keep the committed final newline of every lock file.'
    $utf8 = [Text.UTF8Encoding]::new($false)
    foreach ($evaluation in $evaluations) {
        $fullPath = Join-Path $repositoryRoot $evaluation.LockFile
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            continue
        }

        # New lock files keep NuGet's format (no final newline).
        $wantsNewline = $committedNewline.ContainsKey($evaluation.LockFile) -and $committedNewline[$evaluation.LockFile]
        $afterText = [IO.File]::ReadAllText($fullPath, $utf8)
        if ($snapshots.ContainsKey($evaluation.LockFile)) {
            $before = $snapshots[$evaluation.LockFile]
            $beforeText = $utf8.GetString($before)
            $sameGraph = (Get-NormalizedText -Text $afterText) -ceq (Get-NormalizedText -Text $beforeText)
            if ($sameGraph -and $beforeText.EndsWith("`n") -eq $wantsNewline) {
                [IO.File]::WriteAllBytes($fullPath, $before)
                continue
            }
        }

        $lineEnding = if ($afterText.Contains("`r`n")) { "`r`n" } else { "`n" }
        $rewritten = $afterText.TrimEnd([char[]] "`r`n") + $(if ($wantsNewline) { $lineEnding } else { '' })
        if ($rewritten -cne $afterText) {
            [IO.File]::WriteAllText($fullPath, $rewritten, $utf8)
        }
    }

    Write-Host 'Step 5: structural checks.'
    $violations = [Collections.Generic.List[string]]::new()
    $sdkIdentities = @{}
    foreach ($evaluation in $evaluations) {
        $lock = Read-LockFile -Path $evaluation.LockFile
        foreach ($violation in @(Get-LockViolation -Evaluation $evaluation -Lock $lock)) {
            $violations.Add($violation)
        }

        if ($null -ne $lock -and $lock['dependencies'] -is [hashtable]) {
            $identity = Get-SdkIdentity -Lock $lock
            if ($null -ne $identity) {
                if (-not $sdkIdentities.ContainsKey($identity)) {
                    $sdkIdentities[$identity] = [Collections.Generic.List[string]]::new()
                }

                $sdkIdentities[$identity].Add($evaluation.LockFile)
            }
        }
    }

    if ($sdkIdentities.Count -gt 1) {
        $details = @($sdkIdentities.GetEnumerator() | ForEach-Object { "$($_.Key): $($_.Value -join ', ')" })
        $violations.Add("CheatEngine.SDK resolves to more than one identity: $($details -join '; ').")
    }

    if ($violations.Count -gt 0) {
        foreach ($violation in $violations) {
            Write-Failure -Message $violation
        }

        throw "$($violations.Count) lock file invariant(s) failed."
    }

    foreach ($identity in $sdkIdentities.Keys) {
        Write-Host "CheatEngine.SDK $identity in $($sdkIdentities[$identity].Count) lock files."
    }

    if (-not $Verify) {
        $status = @(Invoke-Git -Arguments @('status', '--porcelain', '--', $lockFilePattern))
        if ($status.Count -eq 0) {
            Write-Host 'Lock files are up to date.'
        }
        else {
            Write-Host "Changed lock files ($($status.Count)); review and commit them in their own commit:"
            $status | ForEach-Object { Write-Host "  $_" }
        }

        return
    }

    $changed = @(Invoke-Git -Arguments @('diff', '--name-only', '--', $lockFilePattern))
    $untracked = @(Invoke-Git -Arguments @('ls-files', '--others', '--exclude-standard', '--', $lockFilePattern))
    if ($changed.Count -gt 0 -or $untracked.Count -gt 0) {
        $files = @($changed) + @($untracked | ForEach-Object { "$_ (untracked)" })
        Write-Failure -Message "Lock files differ from what ./eng/Update-LockFiles.ps1 produces: $($files -join ', '). Run ./eng/Update-LockFiles.ps1 on Windows with .NET SDK $pinnedSdk and commit the result."
        & git -C $repositoryRoot --no-pager diff --stat -- $lockFilePattern | Out-Host
        throw "$($files.Count) lock file(s) are out of date."
    }

    Write-Host "All $($evaluations.Count) lock files are up to date."
}
finally {
    Pop-Location
}
