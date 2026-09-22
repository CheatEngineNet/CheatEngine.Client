<#
.SYNOPSIS
Compares the GitHub settings of CheatEngine.Client with the desired state committed next to this script and, only
with -Apply, converges them. Run by a repository administrator from a workstation; never run by CI.

.DESCRIPTION
Desired state (one JSON file per area; the "_comment" members are documentation and are never sent):

- repository.json: squash merges only, titled by the pull request title that the "PR policy" check polices.
- rulesets/protect-main.json: ruleset "Protect main" (matched by name, never by id): no deletion, no force push,
  pull requests with squash merges only and no bypass actor, required checks "CI / Gate" and "PR policy" from the
  GitHub Actions app (integration 15368).
- rulesets/protect-release-tags.json: ruleset "Protect release tags" on refs/tags/v*: no deletion, no force push, no
  update; no creation rule, so the maintainer can still push a release tag.
- environments/nuget.json: the publication environment requires a reviewer (-NuGetReviewer), admins cannot bypass it,
  and only v*.*.* tags deploy.
- actions-permissions.json: actions pinned by full commit SHA, read-only default token, no pull request approval by
  workflows.
- security.json: verified only, never changed (private vulnerability reporting, Dependabot alerts and security updates,
  secret scanning and push protection on; code-scanning default setup off because codeql.yml is an advanced setup).

Modes:

- default: read-only. Every call is a GET; prints desired vs actual per setting; exit code 1 when anything drifts.
- -PlanOnly: offline. Prints the desired payloads and the calls -Apply would make; no network access.
- -Apply: the read-only comparison, then the writes for the drifting areas, then a second comparison. Every write goes
  through Invoke-GitHubMutation, the only place of this script that sends a non-GET request.

Apply the required checks only after main is green and "PR policy" has reported at least once on a pull request;
until then use -SkipRequiredChecks, otherwise every pull request is blocked. Use -EnableImmutableReleases only after the
draft-first release.yml is on main.

.PARAMETER Repository
owner/name of the repository.

.PARAMETER NuGetReviewer
Logins of the required reviewers of the 'nuget' environment.

.PARAMETER PlanOnly
Print the plan without any network access.

.PARAMETER Apply
Write the drifting settings.

.PARAMETER SkipRequiredChecks
Leave the required status checks out of the "Protect main" ruleset.

.PARAMETER EnableImmutableReleases
Also require immutable releases.

.EXAMPLE
./eng/github/Set-RepositorySettings.ps1

Read-only comparison; exit code 1 lists the drift.
#>
[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$')]
    [string] $Repository = 'CheatEngineNet/CheatEngine.Client',

    [ValidateNotNullOrEmpty()]
    [string[]] $NuGetReviewer = @('AriusII'),

    [switch] $PlanOnly,

    [switch] $Apply,

    [switch] $SkipRequiredChecks,

    [switch] $EnableImmutableReleases
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($env:CI -ceq 'true' -or $env:GITHUB_ACTIONS) {
    throw 'Set-RepositorySettings.ps1 is run by a repository administrator, never by CI.'
}

if ($PlanOnly -and $Apply) {
    throw '-PlanOnly and -Apply are mutually exclusive.'
}

$script:Rows = [System.Collections.Generic.List[object]]::new()
# True only during the first comparison of an -Apply run; the verification pass after the writes is read-only.
$script:Writing = $false

function Read-DesiredState {
    param([string] $RelativePath)
    $path = Join-Path $PSScriptRoot $RelativePath
    $value = Get-Content -LiteralPath $path -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
    $value.Remove('_comment')
    return $value
}

function ConvertTo-Text {
    param([object] $Value)
    if ($null -eq $Value) {
        return '(absent)'
    }

    if ($Value -is [string] -or $Value -is [ValueType]) {
        return [string] $Value
    }

    return (ConvertTo-Json -InputObject $Value -Depth 10 -Compress)
}

# Differences between a desired value and the actual one, where the desired value lists only the settings this script
# owns: objects compare the desired members, arrays must have the same length and every desired element must match one
# actual element (order-insensitive), scalars compare as text.
function Compare-DesiredState {
    param([object] $Desired, [object] $Actual, [string] $Path)
    if ($Desired -is [System.Collections.IDictionary]) {
        if ($Actual -isnot [System.Collections.IDictionary]) {
            return @("${Path}: expected an object, found $(ConvertTo-Text $Actual)")
        }

        $differences = foreach ($key in @($Desired.Keys)) {
            if (-not $Actual.Contains($key)) {
                "$Path.${key}: expected $(ConvertTo-Text $Desired[$key]), found (absent)"
            }
            else {
                Compare-DesiredState $Desired[$key] $Actual[$key] "$Path.$key"
            }
        }

        return @($differences)
    }

    if ($Desired -is [System.Collections.IList]) {
        $actualItems = @($Actual)
        if ($Actual -isnot [System.Collections.IList] -or $actualItems.Count -ne $Desired.Count) {
            return @("${Path}: expected $(ConvertTo-Text $Desired), found $(ConvertTo-Text $Actual)")
        }

        $differences = foreach ($item in $Desired) {
            $matched = @($actualItems | Where-Object { @(Compare-DesiredState $item $_ "$Path[]").Count -eq 0 })
            if ($matched.Count -eq 0) {
                "${Path}: no element matches $(ConvertTo-Text $item) in $(ConvertTo-Text $Actual)"
            }
        }

        return @($differences)
    }

    if ((ConvertTo-Text $Desired) -cne (ConvertTo-Text $Actual)) {
        return @("${Path}: expected $(ConvertTo-Text $Desired), found $(ConvertTo-Text $Actual)")
    }

    return @()
}

function Add-Row {
    param([string] $Area, [AllowNull()] [string[]] $Difference, [string] $Remedy)
    # An empty result arrives as $null: a function's empty array is unrolled by the pipeline.
    $items = @($Difference | Where-Object { $_ })
    $script:Rows.Add([pscustomobject]@{
            Area       = $Area
            InSync     = $items.Count -eq 0
            Difference = $items
            Remedy     = $Remedy
        })
}

# GET only: gh api uses GET when no method and no field is given.
function Invoke-GitHubRead {
    param([string] $Path, [switch] $AllowNotFound)
    $output = & gh api --include $Path 2>$null
    $lines = @($output)
    $status = if ($lines.Count -gt 0) { [string] $lines[0] } else { '' }
    if ($AllowNotFound -and [regex]::IsMatch($status, '^HTTP/\S+ 404')) {
        return $null
    }

    if ($LASTEXITCODE -ne 0) {
        throw "GET $Path failed with exit code $LASTEXITCODE ($status)."
    }

    # --include prints the status line and the headers, a blank line, then the body (absent for 204 No Content).
    $separator = [array]::IndexOf($lines, '')
    $body = if ($separator -ge 0 -and $separator -lt $lines.Count - 1) {
        ($lines[($separator + 1)..($lines.Count - 1)] -join "`n").Trim()
    }
    else {
        ''
    }
    if ($body.Length -eq 0) {
        return @{}
    }

    return ($body | ConvertFrom-Json -AsHashtable)
}

# The single write path of this script.
function Invoke-GitHubMutation {
    param(
        [ValidateSet('PUT', 'POST', 'PATCH')]
        [string] $Method,
        [string] $Path,
        [object] $Body
    )
    if (-not ($Apply -and $script:Writing)) {
        throw "Refusing $Method $Path without -Apply."
    }

    $payload = New-TemporaryFile
    try {
        $json = if ($null -eq $Body) { '{}' } else { $Body | ConvertTo-Json -Depth 20 }
        [System.IO.File]::WriteAllText($payload.FullName, $json, [System.Text.UTF8Encoding]::new($false))
        Write-Host "$Method $Path"
        & gh api --method $Method $Path --input $payload.FullName | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "$Method $Path failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Remove-Item -LiteralPath $payload.FullName -Force
    }
}

function Get-DesiredMainRuleset {
    param([bool] $WithoutRequiredChecks)
    $ruleset = Read-DesiredState 'rulesets/protect-main.json'
    if ($WithoutRequiredChecks) {
        $ruleset['rules'] = @($ruleset['rules'] | Where-Object { $_['type'] -cne 'required_status_checks' })
    }

    return $ruleset
}

function Sync-Repository {
    $desired = Read-DesiredState 'repository.json'
    $actual = Invoke-GitHubRead "repos/$Repository"
    $difference = @(Compare-DesiredState $desired $actual 'repository')
    if ($difference.Count -gt 0 -and $script:Writing) {
        Invoke-GitHubMutation 'PATCH' "repos/$Repository" $desired
    }

    return $difference
}

function Sync-ActionsPermission {
    $desired = Read-DesiredState 'actions-permissions.json'
    $actual = Invoke-GitHubRead "repos/$Repository/actions/permissions"
    $difference = @(Compare-DesiredState $desired['permissions'] $actual 'actions.permissions')
    if ($difference.Count -gt 0 -and $script:Writing) {
        Invoke-GitHubMutation 'PUT' "repos/$Repository/actions/permissions" $desired['permissions']
    }

    $actualWorkflow = Invoke-GitHubRead "repos/$Repository/actions/permissions/workflow"
    $workflowDifference = @(Compare-DesiredState $desired['workflow'] $actualWorkflow 'actions.workflow')
    if ($workflowDifference.Count -gt 0 -and $script:Writing) {
        Invoke-GitHubMutation 'PUT' "repos/$Repository/actions/permissions/workflow" $desired['workflow']
    }

    return @($difference + $workflowDifference)
}

function Sync-Ruleset {
    param([System.Collections.IDictionary] $Desired)
    $rulesets = Invoke-GitHubRead "repos/$Repository/rulesets?includes_parents=false"
    $summary = @($rulesets | Where-Object { $_['name'] -ceq $Desired['name'] })
    if ($summary.Count -gt 1) {
        throw "Several rulesets are named '$($Desired['name'])'; remove the duplicates by hand."
    }

    if ($summary.Count -eq 0) {
        if ($script:Writing) {
            Invoke-GitHubMutation 'POST' "repos/$Repository/rulesets" $Desired
        }

        return @("ruleset '$($Desired['name'])': missing")
    }

    $id = $summary[0]['id']
    $actual = Invoke-GitHubRead "repos/$Repository/rulesets/$id"
    $difference = @(Compare-DesiredState $Desired $actual "ruleset '$($Desired['name'])'")
    if ($difference.Count -gt 0 -and $script:Writing) {
        Invoke-GitHubMutation 'PUT' "repos/$Repository/rulesets/$id" $Desired
    }

    return $difference
}

function Sync-NuGetEnvironment {
    $desired = Read-DesiredState 'environments/nuget.json'
    $name = $desired['name']
    $actual = Invoke-GitHubRead "repos/$Repository/environments/$name" -AllowNotFound
    $observed = [ordered]@{
        wait_timer               = 0
        prevent_self_review      = $null
        reviewers                = @()
        can_admins_bypass        = $null
        deployment_branch_policy = $null
    }
    if ($null -ne $actual) {
        $observed['can_admins_bypass'] = $actual['can_admins_bypass']
        $observed['deployment_branch_policy'] = $actual['deployment_branch_policy']
        foreach ($rule in @($actual['protection_rules'])) {
            if ($rule['type'] -ceq 'required_reviewers') {
                $observed['prevent_self_review'] = $rule['prevent_self_review']
                $logins = $rule['reviewers'] | ForEach-Object { $_['reviewer']['login'] }
                $observed['reviewers'] = @($logins | Sort-Object)
            }
            elseif ($rule['type'] -ceq 'wait_timer') {
                $observed['wait_timer'] = $rule['wait_timer']
            }
        }
    }

    $wanted = [ordered]@{
        wait_timer               = $desired['wait_timer']
        prevent_self_review      = $desired['prevent_self_review']
        reviewers                = @($NuGetReviewer | Sort-Object)
        can_admins_bypass        = $desired['can_admins_bypass']
        deployment_branch_policy = $desired['deployment_branch_policy']
    }
    $difference = @(Compare-DesiredState $wanted $observed "environment '$name'")
    if ($difference.Count -gt 0 -and $script:Writing) {
        $reviewers = @($NuGetReviewer | ForEach-Object {
                [ordered]@{ type = 'User'; id = (Invoke-GitHubRead "users/$_")['id'] }
            })
        Invoke-GitHubMutation 'PUT' "repos/$Repository/environments/$name" ([ordered]@{
                wait_timer               = $desired['wait_timer']
                prevent_self_review      = $desired['prevent_self_review']
                reviewers                = $reviewers
                can_admins_bypass        = $desired['can_admins_bypass']
                deployment_branch_policy = $desired['deployment_branch_policy']
            })
    }

    $policies = Invoke-GitHubRead "repos/$Repository/environments/$name/deployment-branch-policies" -AllowNotFound
    $existing = @()
    if ($null -ne $policies) {
        $existing = @($policies['branch_policies'] | ForEach-Object { "$($_['type']):$($_['name'])" })
    }
    foreach ($policy in @($desired['deployment_policies'])) {
        if (-not ($existing -ccontains "$($policy['type']):$($policy['name'])")) {
            $difference += "environment '$name': deployment policy $($policy['type']) '$($policy['name'])' missing"
            if ($script:Writing) {
                Invoke-GitHubMutation 'POST' "repos/$Repository/environments/$name/deployment-branch-policies" $policy
            }
        }
    }

    $expected = @($desired['deployment_policies'] | ForEach-Object { "$($_['type']):$($_['name'])" })
    foreach ($extra in @($existing | Where-Object { -not ($expected -ccontains $_) })) {
        $difference += "environment '$name': unexpected deployment policy $extra (remove it by hand)"
    }

    return $difference
}

function Sync-ImmutableRelease {
    $actual = Invoke-GitHubRead "repos/$Repository/immutable-releases"
    if (-not $EnableImmutableReleases) {
        Write-Host "Immutable releases: enabled=$($actual['enabled']) (not managed without -EnableImmutableReleases)."
        return @()
    }

    if ($actual['enabled'] -eq $true) {
        return @()
    }

    if ($script:Writing) {
        Invoke-GitHubMutation 'PUT' "repos/$Repository/immutable-releases" $null
    }

    return @('immutable releases: expected enabled, found disabled')
}

function Test-SecurityFeature {
    $desired = Read-DesiredState 'security.json'
    $analysis = (Invoke-GitHubRead "repos/$Repository")['security_and_analysis']
    $alerts = Invoke-GitHubRead "repos/$Repository/vulnerability-alerts" -AllowNotFound
    $reporting = Invoke-GitHubRead "repos/$Repository/private-vulnerability-reporting"
    $observed = [ordered]@{
        private_vulnerability_reporting = $reporting['enabled']
        vulnerability_alerts            = $null -ne $alerts
        dependabot_security_updates     = $analysis['dependabot_security_updates']['status']
        secret_scanning                 = $analysis['secret_scanning']['status']
        secret_scanning_push_protection = $analysis['secret_scanning_push_protection']['status']
        code_scanning_default_setup     = (Invoke-GitHubRead "repos/$Repository/code-scanning/default-setup")['state']
    }

    return @(Compare-DesiredState $desired $observed 'security')
}

function Invoke-Comparison {
    $script:Rows.Clear()
    Add-Row 'Repository merge settings' (Sync-Repository) 'PATCH repository'
    Add-Row 'Actions permissions' (Sync-ActionsPermission) 'PUT actions permissions'
    Add-Row 'Ruleset Protect main' (Sync-Ruleset (Get-DesiredMainRuleset $SkipRequiredChecks)) 'PUT or POST ruleset'
    $tags = Read-DesiredState 'rulesets/protect-release-tags.json'
    Add-Row 'Ruleset Protect release tags' (Sync-Ruleset $tags) 'PUT or POST ruleset'
    Add-Row 'Environment nuget' (Sync-NuGetEnvironment) 'PUT environment, POST deployment policy'
    Add-Row 'Immutable releases' (Sync-ImmutableRelease) 'PUT immutable-releases (-EnableImmutableReleases)'
    Add-Row 'Security features (verify only)' (Test-SecurityFeature) 'Repository settings > Code security (manual)'

    foreach ($row in $script:Rows) {
        $state = if ($row.InSync) { 'in sync' } else { "DRIFT ($($row.Remedy))" }
        Write-Host "$($row.Area): $state"
        foreach ($difference in @($row.Difference)) {
            Write-Host "    $difference"
        }
    }

    return @($script:Rows | Where-Object { -not $_.InSync }).Count
}

if ($PlanOnly) {
    $actions = Read-DesiredState 'actions-permissions.json'
    $plan = [ordered]@{
        'PATCH repos/{repository}'                            = Read-DesiredState 'repository.json'
        'PUT repos/{repository}/actions/permissions'          = $actions['permissions']
        'PUT repos/{repository}/actions/permissions/workflow' = $actions['workflow']
        'PUT|POST ruleset Protect main'                       = Get-DesiredMainRuleset $SkipRequiredChecks
        'PUT|POST ruleset Protect release tags'               = Read-DesiredState 'rulesets/protect-release-tags.json'
        'PUT repos/{repository}/environments/nuget'           = Read-DesiredState 'environments/nuget.json'
        'verify only'                                         = Read-DesiredState 'security.json'
    }
    $reviewers = $NuGetReviewer -join ', '
    $immutable = [bool] $EnableImmutableReleases
    Write-Host "Plan for $Repository ('nuget' reviewers: $reviewers; immutable releases: $immutable)."
    foreach ($entry in $plan.GetEnumerator()) {
        Write-Host ''
        Write-Host "# $($entry.Key)"
        Write-Host ($entry.Value | ConvertTo-Json -Depth 20)
    }

    exit 0
}

& gh auth status *> $null
if ($LASTEXITCODE -ne 0) {
    throw 'gh is not authenticated: run gh auth login first.'
}

if ($Apply -and (Invoke-GitHubRead "repos/$Repository")['permissions']['admin'] -ne $true) {
    throw "-Apply needs administrator access to $Repository."
}

$script:Writing = [bool] $Apply
$drift = Invoke-Comparison
$script:Writing = $false
if ($Apply -and $drift -gt 0) {
    Write-Host ''
    Write-Host 'Re-reading after the writes.'
    $drift = Invoke-Comparison
}

if ($drift -gt 0) {
    Write-Host "$drift area(s) differ from the desired state."
    exit 1
}

Write-Host 'Every area matches the desired state.'
exit 0
