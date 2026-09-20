[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string]$PackageSource,

    [ValidatePattern('^\d+\.\d+\.\d+([-.].+)?$')]
    [string]$TemplateVersion = '0.1.0'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedPackageSource = (Resolve-Path -LiteralPath $PackageSource).Path
$templatePackage = Join-Path $resolvedPackageSource "CheatEngine.Client.Templates.$TemplateVersion.nupkg"
if (-not (Test-Path -LiteralPath $templatePackage -PathType Leaf)) {
    throw "Expected template package '$templatePackage' was not found. Run dotnet pack before the smoke test."
}

$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$smokeDirectory = [IO.Path]::GetFullPath((Join-Path $temporaryBase ("CheatEngine.Client.TemplateSmoke." + [Guid]::NewGuid().ToString('N'))))
if (-not $smokeDirectory.StartsWith($temporaryBase, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use a smoke-test directory outside the system temporary directory: '$smokeDirectory'."
}

$previousDotnetCliHome = $env:DOTNET_CLI_HOME
$previousDotnetNewHome = $env:DOTNET_NEW_HOME
try {
    New-Item -ItemType Directory -Path $smokeDirectory | Out-Null
    $env:DOTNET_CLI_HOME = Join-Path $smokeDirectory '.dotnet-cli'
    $env:DOTNET_NEW_HOME = Join-Path $smokeDirectory '.template-engine'

    $configurationPath = Join-Path $smokeDirectory 'NuGet.Config'
    $escapedSource = [Security.SecurityElement]::Escape($resolvedPackageSource)
    $escapedPackageCache = [Security.SecurityElement]::Escape((Join-Path $smokeDirectory '.packages'))
    $nuGetConfiguration = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <config>
    <add key="globalPackagesFolder" value="$escapedPackageCache" />
  </config>
  <packageSources>
    <clear />
    <add key="local-client-packages" value="$escapedSource" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@
    Set-Content -LiteralPath $configurationPath -Value $nuGetConfiguration -Encoding utf8NoBOM

    & dotnet new install $templatePackage --force
    if ($LASTEXITCODE -ne 0) {
        throw 'Local template installation failed.'
    }

    & dotnet new ceplugin --dry-run --name Smoke.Plugin --output (Join-Path $smokeDirectory 'dry-run')
    if ($LASTEXITCODE -ne 0) {
        throw 'Template dry run failed.'
    }

    $instantiatedDirectory = Join-Path $smokeDirectory 'Smoke.Plugin'
    & dotnet new ceplugin --name Smoke.Plugin --output $instantiatedDirectory
    if ($LASTEXITCODE -ne 0) {
        throw 'Template instantiation failed.'
    }

    $projectPath = Join-Path $instantiatedDirectory 'Smoke.Plugin.csproj'
    & dotnet restore $projectPath --configfile $configurationPath
    if ($LASTEXITCODE -ne 0) {
        throw 'Instantiated template restore failed.'
    }

    & dotnet build $projectPath --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw 'Instantiated template build failed.'
    }

    Write-Host 'Template smoke test passed: local installation, dry run, instantiation, restore, and Release build succeeded.'
}
finally {
    if ([string]::IsNullOrEmpty($previousDotnetCliHome)) {
        Remove-Item Env:DOTNET_CLI_HOME -ErrorAction SilentlyContinue
    }
    else {
        $env:DOTNET_CLI_HOME = $previousDotnetCliHome
    }

    if ([string]::IsNullOrEmpty($previousDotnetNewHome)) {
        Remove-Item Env:DOTNET_NEW_HOME -ErrorAction SilentlyContinue
    }
    else {
        $env:DOTNET_NEW_HOME = $previousDotnetNewHome
    }

    if (Test-Path -LiteralPath $smokeDirectory -PathType Container) {
        Remove-Item -LiteralPath $smokeDirectory -Recurse -Force
    }
}
