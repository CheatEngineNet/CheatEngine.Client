#Requires -Version 7.2
<#
.SYNOPSIS
An in-memory stand-in for the gh CLI, used by ReleaseScriptTests to run the eng/release scripts without GitHub.

.DESCRIPTION
ReleaseScriptTests dot-sources this file before it runs a release script. It defines a gh function, which PowerShell
resolves before gh.exe, so the scripts run unchanged. It implements only the gh calls the release scripts make, with
the GitHub behaviour they rely on: release listings include drafts and return each asset's upload state, size and
SHA-256 digest; deleting a release keeps its tag; uploads with --clobber replace an asset.

State, from environment variables set by the test:
- FAKE_GH_STATE: a JSON file { nextId, immutable, releases: [ { id, tag_name, draft, assets: [ { name, state, size,
  digest } ] } ] }; the test seeds it and reads it back.
- FAKE_GH_STORE: a folder holding the content of each asset as <release id>/<asset name>.
- FAKE_GH_LOG: every call is appended as one line, the arguments joined by spaces.
Any other call fails with exit code 1.
#>

Set-StrictMode -Version Latest

function Read-FakeGitHubState {
	return Get-Content -LiteralPath $env:FAKE_GH_STATE -Raw | ConvertFrom-Json -AsHashtable
}

function Save-FakeGitHubState {
	param([System.Collections.IDictionary] $State)
	[System.IO.File]::WriteAllText($env:FAKE_GH_STATE, ($State | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
}

function Get-FakeGitHubOption {
	# Splits gh arguments into positional values and --option values; a repeated option keeps every value.
	param([object[]] $Arguments, [string[]] $ValueOptions)
	$positional = [System.Collections.Generic.List[string]]::new()
	$options = @{}
	for ($index = 0; $index -lt $Arguments.Count; $index++) {
		$argument = [string]$Arguments[$index]
		if ($ValueOptions -contains $argument) {
			if (-not $options.ContainsKey($argument)) {
				$options[$argument] = [System.Collections.Generic.List[string]]::new()
			}
			$options[$argument].Add([string]$Arguments[++$index])
		}
		elseif ($argument.StartsWith('-')) {
			$options[$argument] = [System.Collections.Generic.List[string]]::new()
		}
		else {
			$positional.Add($argument)
		}
	}
	return [pscustomobject]@{ Positional = $positional; Options = $options }
}

function Add-FakeGitHubAsset {
	# Stores a file as an asset of a release, replacing an asset of the same name.
	param([System.Collections.IDictionary] $Release, [string] $Path)
	$name = Split-Path -Leaf $Path
	$folder = Join-Path $env:FAKE_GH_STORE ([string]$Release.id)
	New-Item -ItemType Directory -Path $folder -Force | Out-Null
	Copy-Item -LiteralPath $Path -Destination (Join-Path $folder $name) -Force
	$asset = [ordered]@{
		name = $name
		state = 'uploaded'
		size = (Get-Item -LiteralPath $Path).Length
		digest = 'sha256:' + (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
	}
	$Release.assets = @(@($Release.assets) | Where-Object { $_.name -cne $name }) + @($asset)
}

function gh {
	[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidGlobalVars', '', Justification = 'gh.exe sets the global $LASTEXITCODE, which the release scripts read.')]
	param()
	Add-Content -LiteralPath $env:FAKE_GH_LOG -Value ($args -join ' ')
	$global:LASTEXITCODE = 0
	$state = Read-FakeGitHubState
	$repository = 'CheatEngineNet/CheatEngine.Client'
	$command = "$($args[0]) $($args[1])"
	$parsed = Get-FakeGitHubOption -Arguments @($args | Select-Object -Skip 2) -ValueOptions @('--method', '--jq', '-F', '--repo', '--title', '--notes-file', '--pattern', '--dir', '--signer-workflow', '--predicate-type')

	switch ($command) {
		{ $_ -like 'api *' } {
			$parsed = Get-FakeGitHubOption -Arguments @($args | Select-Object -Skip 1) -ValueOptions @('--method', '--jq', '-F')
			$method = if ($parsed.Options.ContainsKey('--method')) { $parsed.Options['--method'][0] } else { 'GET' }
			$endpoint = $parsed.Positional[0]
			if ($method -eq 'GET' -and $endpoint -eq "repos/$repository/releases?per_page=100") {
				foreach ($release in @($state.releases)) {
					$assets = @(foreach ($asset in @($release.assets)) { [ordered]@{ name = $asset.name; state = $asset.state; size = $asset.size; digest = $asset.digest } })
					[ordered]@{ id = $release.id; tag_name = $release.tag_name; draft = $release.draft; assets = $assets } | ConvertTo-Json -Compress -Depth 5
				}
				return
			}
			if ($endpoint -match "^repos/$([regex]::Escape($repository))/releases/(?<id>[0-9]+)$") {
				$id = [long]$Matches['id']
				$release = @($state.releases) | Where-Object { $_.id -eq $id } | Select-Object -First 1
				if ($null -eq $release) {
					$global:LASTEXITCODE = 1
					return
				}
				switch ($method) {
					'DELETE' {
						$state.releases = @(@($state.releases) | Where-Object { $_.id -ne $id })
						Save-FakeGitHubState -State $state
					}
					'PATCH' {
						if ($parsed.Options['-F'] -contains 'draft=false') {
							$release.draft = $false
						}
						Save-FakeGitHubState -State $state
					}
					'GET' {
						if ($state.immutable) { 'true' } else { 'false' }
					}
				}
				return
			}
		}
		'release create' {
			$tag = $parsed.Positional[0]
			$release = [ordered]@{ id = [long]$state.nextId; tag_name = $tag; draft = $parsed.Options.ContainsKey('--draft'); assets = @() }
			$state.nextId = [long]$state.nextId + 1
			foreach ($file in @($parsed.Positional | Select-Object -Skip 1)) {
				Add-FakeGitHubAsset -Release $release -Path $file
			}
			$state.releases = @($state.releases) + @($release)
			Save-FakeGitHubState -State $state
			return
		}
		'release upload' {
			$tag = $parsed.Positional[0]
			$release = @($state.releases) | Where-Object { $_.tag_name -ceq $tag } | Select-Object -First 1
			if ($null -eq $release -or -not $release.draft) {
				$global:LASTEXITCODE = 1
				return
			}
			foreach ($file in @($parsed.Positional | Select-Object -Skip 1)) {
				Add-FakeGitHubAsset -Release $release -Path $file
			}
			Save-FakeGitHubState -State $state
			return
		}
		'release download' {
			$tag = $parsed.Positional[0]
			$release = @($state.releases) | Where-Object { $_.tag_name -ceq $tag } | Select-Object -First 1
			foreach ($asset in @($release.assets)) {
				if (@($parsed.Options['--pattern'] | Where-Object { $asset.name -like $_ }).Count -gt 0) {
					Copy-Item -LiteralPath (Join-Path $env:FAKE_GH_STORE "$($release.id)/$($asset.name)") -Destination (Join-Path $parsed.Options['--dir'][0] $asset.name) -Force
				}
			}
			return
		}
		{ $_ -in @('attestation verify', 'release verify', 'release verify-asset') } {
			return
		}
	}

	Write-Error "The fake gh does not implement: gh $($args -join ' ')" -ErrorAction Continue
	$global:LASTEXITCODE = 1
}
