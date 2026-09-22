<#
.SYNOPSIS
Evaluates the pull request title and CHANGELOG policy: the required "PR policy" check.

.DESCRIPTION
Every rule, limit, pattern and message comes from the policy data file (pr-policy.json next to this script by
default); this script holds no rule literal. The C# mirror
tests/CheatEngine.Client.Repository.Tests/Governance/PullRequestPolicyRules.cs evaluates the same file the same way
and is the executable specification of these rules.

All matching is case-sensitive and uses the .NET regular-expression engine, as the C# mirror does. User-controlled
values (title, body, author) arrive through environment variables only, and the body is never printed.

Order: exempt authors pass every rule with a notice; otherwise the title rules run in file order, then the first-word
rule, then the CHANGELOG rule. Changed paths come from the triple-dot diff base...head (merge base to head, renames
split into delete + add so a move out of a consumer-visible folder still counts) unless -ChangedPath is given.

.PARAMETER Title
Pull request title. Defaults to $env:PR_TITLE.

.PARAMETER Body
Pull request description (may be empty). Defaults to $env:PR_BODY.

.PARAMETER Author
Pull request author login. Defaults to $env:PR_AUTHOR.

.PARAMETER BaseSha
Base commit of the pull request. Defaults to $env:PR_BASE_SHA. Ignored when -ChangedPath is given.

.PARAMETER HeadSha
Head commit of the pull request. Defaults to $env:PR_HEAD_SHA. Ignored when -ChangedPath is given.

.PARAMETER ChangedPath
Repository-relative changed paths with forward slashes. Overrides the git diff (local simulation).

.PARAMETER PolicyPath
Policy data file. Defaults to pr-policy.json next to this script.

.EXAMPLE
$env:PR_TITLE = 'Add a Core option'
./eng/ci/Test-PullRequestPolicy.ps1 -ChangedPath 'libs/CheatEngine.Client.Core/X.cs'

Simulates a pull request that changes a consumer-visible file without a CHANGELOG entry (exit code 1).
#>
[CmdletBinding()]
param(
    [string] $Title = $env:PR_TITLE,
    [string] $Body = $env:PR_BODY,
    [string] $Author = $env:PR_AUTHOR,
    [string] $BaseSha = $env:PR_BASE_SHA,
    [string] $HeadSha = $env:PR_HEAD_SHA,
    [string[]] $ChangedPath,
    [string] $PolicyPath = (Join-Path $PSScriptRoot 'pr-policy.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RegexTimeout = [TimeSpan]::FromSeconds(2)
$script:NoOptions = [System.Text.RegularExpressions.RegexOptions]::None

function Test-Pattern {
    param([string] $Value, [string] $Pattern)
    return [regex]::IsMatch($Value, $Pattern, $script:NoOptions, $script:RegexTimeout)
}

function Get-OptionalProperty {
    param([object] $Object, [string] $Name)
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function ConvertTo-RuleResult {
    param([string] $Rule, [bool] $Passed, [string] $Detail)
    return [pscustomobject]@{ Rule = $Rule; Passed = $Passed; Detail = $Detail }
}

# Workflow-command data escaping (https://github.com/actions/toolkit/blob/main/packages/core/src/command.ts).
function ConvertTo-CommandData {
    param([string] $Value)
    return $Value.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
}

# Changed paths are pull-request controlled: keep them on one line and out of the Markdown table syntax.
function ConvertTo-SingleLine {
    param([string] $Value)
    return $Value.Replace("`r", ' ').Replace("`n", ' ').Replace('|', '\|')
}

function Get-ChangedPath {
    param([string] $Base, [string] $Head)
    foreach ($sha in @($Base, $Head)) {
        if (-not (Test-Pattern $sha '^[0-9a-f]{40}([0-9a-f]{24})?$')) {
            throw "Base and head must be full lowercase commit ids (PR_BASE_SHA and PR_HEAD_SHA); got '$sha'."
        }
    }

    $output = & git -c core.quotePath=false diff --name-only --no-renames -z "$Base...$Head"
    if ($LASTEXITCODE -ne 0) {
        throw "git diff $Base...$Head failed with exit code $LASTEXITCODE (the checkout needs fetch-depth: 0)."
    }

    $joined = @($output) -join "`n"
    return @($joined.Split([char] 0, [System.StringSplitOptions]::RemoveEmptyEntries))
}

function Test-TitleLength {
    param([object] $Rule, [string] $Value)
    $length = [System.Globalization.StringInfo]::new($Value).LengthInTextElements
    $limit = [int] $Rule.limit
    if ($length -le $limit) {
        return ConvertTo-RuleResult $Rule.id $true "$length of $limit characters."
    }

    return ConvertTo-RuleResult $Rule.id $false "$($Rule.message) ($length of $limit characters)"
}

function Test-TitleRule {
    param([object] $Rule, [string] $Value)
    $mustMatch = Get-OptionalProperty $Rule 'mustMatch'
    $mustNotMatch = Get-OptionalProperty $Rule 'mustNotMatch'
    if ($null -ne $mustMatch) {
        $passed = Test-Pattern $Value $mustMatch
    }
    elseif ($null -ne $mustNotMatch) {
        $passed = -not (Test-Pattern $Value $mustNotMatch)
    }
    else {
        throw "Title rule '$($Rule.id)' has neither mustMatch nor mustNotMatch."
    }

    if ($passed) {
        return ConvertTo-RuleResult $Rule.id $true 'Passed.'
    }

    return ConvertTo-RuleResult $Rule.id $false $Rule.message
}

function Test-FirstWord {
    param([object] $Rule, [string] $Value)
    $match = [regex]::Match($Value, $Rule.pattern, $script:NoOptions, $script:RegexTimeout)
    if (-not $match.Success) {
        return ConvertTo-RuleResult $Rule.id $true 'No leading word; the other title rules apply.'
    }

    $word = $match.Value
    $denied = @($Rule.denied) -ccontains $word
    $nonImperative = (Test-Pattern $word $Rule.nonImperativePattern) -and -not (@($Rule.allowed) -ccontains $word)
    if ($denied -or $nonImperative) {
        return ConvertTo-RuleResult $Rule.id $false "$($Rule.message) (first word '$word')"
    }

    return ConvertTo-RuleResult $Rule.id $true "First word '$word'."
}

function Test-Changelog {
    param([object] $Rule, [string[]] $Path, [string] $Description)
    $visible = @($Path | Where-Object {
            $candidate = $_
            (Test-Pattern $candidate $Rule.consumerVisiblePathPattern) -and
            -not @($Rule.excludedPathPatterns | Where-Object { Test-Pattern $candidate $_ })
        })

    if ($visible.Count -eq 0) {
        return ConvertTo-RuleResult $Rule.id $true 'No consumer-visible path changed.'
    }

    if (@($Path) -ccontains $Rule.file) {
        return ConvertTo-RuleResult $Rule.id $true "$($Rule.file) changed."
    }

    if (Test-Pattern $Description $Rule.optOutMarkerPattern) {
        return ConvertTo-RuleResult $Rule.id $true 'The description opts out on its own line.'
    }

    $shown = @($visible | Select-Object -First 10) -join ', '
    $more = if ($visible.Count -gt 10) { " and $($visible.Count - 10) more" } else { '' }
    return ConvertTo-RuleResult $Rule.id $false "$($Rule.message) Paths: $shown$more."
}

function Write-PolicyReport {
    param([object[]] $Result, [string] $Heading)
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("## $Heading")
    $lines.Add('')
    $lines.Add('| Rule | Result | Detail |')
    $lines.Add('|---|---|---|')
    foreach ($item in $Result) {
        $verdict = if ($item.Passed) { 'Pass' } else { 'Fail' }
        $lines.Add("| $($item.Rule) | $verdict | $(ConvertTo-SingleLine $item.Detail) |")
        Write-Host "$($verdict.ToUpperInvariant()) $($item.Rule): $(ConvertTo-SingleLine $item.Detail)"
    }

    if ($env:GITHUB_STEP_SUMMARY) {
        $lines | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
    }
}

[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$policy = Get-Content -LiteralPath $PolicyPath -Raw -Encoding utf8 | ConvertFrom-Json
if ($policy.schema -cne 'cheatengine-pr-policy/v0') {
    throw "Unsupported policy schema '$($policy.schema)' in $PolicyPath."
}

if (@($policy.exemptAuthors) -ccontains $Author) {
    $detail = "Pull request by $Author`: the title and CHANGELOG rules are exempt."
    Write-Host "::notice title=PR policy::$(ConvertTo-CommandData $detail)"
    Write-PolicyReport @(ConvertTo-RuleResult 'ExemptAuthor' $true $detail) 'PR policy'
    exit 0
}

$results = [System.Collections.Generic.List[object]]::new()
$results.Add((Test-TitleLength $policy.title.maxLength $Title))
foreach ($rule in @($policy.title.rules)) {
    $results.Add((Test-TitleRule $rule $Title))
}

$results.Add((Test-FirstWord $policy.title.firstWord $Title))

if ($PSBoundParameters.ContainsKey('ChangedPath')) {
    $paths = @($ChangedPath | Where-Object { $_ })
}
else {
    $paths = Get-ChangedPath $BaseSha $HeadSha
}

$results.Add((Test-Changelog $policy.changelog $paths $Body))

Write-PolicyReport $results.ToArray() 'PR policy'
$failures = @($results | Where-Object { -not $_.Passed })
foreach ($failure in $failures) {
    Write-Host "::error title=PR policy::$(ConvertTo-CommandData "$($failure.Rule): $($failure.Detail)")"
}

if ($failures.Count -gt 0) {
    exit 1
}

exit 0
