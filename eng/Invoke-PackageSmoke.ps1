[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string]$PackageSource,

    [ValidatePattern('^\d+\.\d+\.\d+([-.].+)?$')]
    [string]$ClientVersion = '0.1.0'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedPackageSource = (Resolve-Path -LiteralPath $PackageSource).Path
$expectedPackage = Join-Path $resolvedPackageSource "CheatEngine.Client.$ClientVersion.nupkg"
if (-not (Test-Path -LiteralPath $expectedPackage -PathType Leaf)) {
    throw "Expected package '$expectedPackage' was not found. Run dotnet pack before the smoke test."
}

$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$smokeDirectory = [IO.Path]::GetFullPath((Join-Path $temporaryBase ("CheatEngine.Client.PackageSmoke." + [Guid]::NewGuid().ToString('N'))))
if (-not $smokeDirectory.StartsWith($temporaryBase, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use a smoke-test directory outside the system temporary directory: '$smokeDirectory'."
}

function Write-SmokeProject {
    param(
        [Parameter(Mandatory)] [string]$ProjectDirectory,
        [Parameter(Mandatory)] [bool]$IncludeSdkReference
    )

    $sdkReference = if ($IncludeSdkReference) {
        '    <PackageReference Include="CheatEngine.SDK" Version="1.0.0" />'
    }
    else {
        ''
    }

    $projectXml = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <CheatEngineClientPluginProject>true</CheatEngineClientPluginProject>
    <RestorePackagesWithLockFile>false</RestorePackagesWithLockFile>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>obj/Generated</CompilerGeneratedFilesOutputPath>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CheatEngine.Client" Version="$ClientVersion" />
$sdkReference
  </ItemGroup>
</Project>
"@

    Set-Content -LiteralPath (Join-Path $ProjectDirectory 'Smoke.Plugin.csproj') -Value $projectXml -Encoding utf8NoBOM
    $pluginSource = @'
using CheatEngine.Client;
using CheatEngine.Client.Hosting;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Annotations.Plugin;
using CheatEngine.SDK.Engine.Values;

[CheatEnginePlugin("Package smoke plugin")]
public sealed class Plugin : CheatEngineClientPlugin
{
    protected override void Configure(CheatEnginePluginBuilder builder)
    {
    }

    protected override void OnClientEnabled(ICheatEngineClient client)
    {
        _ = client.Memory.At(default(Address));
        _ = client.Patterns.Aob("00").FirstOrNone();
    }
}
'@
    Set-Content -LiteralPath (Join-Path $ProjectDirectory 'Plugin.cs') -Value $pluginSource -Encoding utf8NoBOM
}

try {
    New-Item -ItemType Directory -Path $smokeDirectory | Out-Null

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

    $positiveDirectory = Join-Path $smokeDirectory 'positive'
    New-Item -ItemType Directory -Path $positiveDirectory | Out-Null
    Write-SmokeProject -ProjectDirectory $positiveDirectory -IncludeSdkReference $true
    & dotnet restore (Join-Path $positiveDirectory 'Smoke.Plugin.csproj') --configfile $configurationPath
    if ($LASTEXITCODE -ne 0) {
        throw 'The positive isolated package restore failed.'
    }

    & dotnet build (Join-Path $positiveDirectory 'Smoke.Plugin.csproj') --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw 'The positive isolated package build failed.'
    }

    $positiveOutput = Join-Path $positiveDirectory 'bin/Release/net10.0'
    $requiredOutputFiles = @(
        'Smoke.Plugin.dll',
        'Smoke.Plugin.runtimeconfig.json',
        'cheatengine-sdk-lua-bridge.dll',
        'CheatEngine.SDK.dll',
        'CheatEngine.Client.Abstractions.dll',
        'CheatEngine.Client.Core.dll',
        'CheatEngine.Client.Fluent.dll',
        'CheatEngine.Client.Extensions.DependencyInjection.dll',
        'CheatEngine.Client.Hosting.dll'
    )
    foreach ($requiredOutputFile in $requiredOutputFiles) {
        $requiredOutputPath = Join-Path $positiveOutput $requiredOutputFile
        if (-not (Test-Path -LiteralPath $requiredOutputPath -PathType Leaf)) {
            throw "The positive isolated package output is missing '$requiredOutputFile'."
        }
    }

    $generatedEntryPoints = @(Get-ChildItem -LiteralPath (Join-Path $positiveDirectory 'obj') -Recurse -File |
        Where-Object Name -eq 'CheatEngine.SDK.EntryPoint.g.cs')
    if ($generatedEntryPoints.Count -ne 1) {
        throw "Expected exactly one generated CESDK bootstrap source, found $($generatedEntryPoints.Count)."
    }
    $generatedEntryPoint = $generatedEntryPoints[0]
    $generatedEntryPointText = Get-Content -LiteralPath $generatedEntryPoint.FullName -Raw
    if ($generatedEntryPointText -notmatch 'namespace CESDK' -or
        $generatedEntryPointText -notmatch 'CEPluginInitialize') {
        throw 'The direct SDK reference did not emit the expected CESDK bootstrap source.'
    }

    $negativeDirectory = Join-Path $smokeDirectory 'negative'
    New-Item -ItemType Directory -Path $negativeDirectory | Out-Null
    Write-SmokeProject -ProjectDirectory $negativeDirectory -IncludeSdkReference $false
    & dotnet restore (Join-Path $negativeDirectory 'Smoke.Plugin.csproj') --configfile $configurationPath
    if ($LASTEXITCODE -ne 0) {
        throw 'The negative isolated package restore failed before CECLIENT001 could be evaluated.'
    }

    $negativeOutput = & dotnet build (Join-Path $negativeDirectory 'Smoke.Plugin.csproj') --configuration Release --no-restore 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0) {
        throw 'The negative isolated plugin build unexpectedly succeeded without a direct CheatEngine.SDK reference.'
    }
    if ($negativeOutput -notmatch 'CECLIENT001') {
        throw "The negative isolated plugin build failed, but did not report CECLIENT001.`n$negativeOutput"
    }

    # The expected negative build leaves PowerShell's native-command status non-zero. Clear it only after both
    # assertions prove that the failure was the intended CECLIENT001 guard.
    $global:LASTEXITCODE = 0
    Write-Host 'Package smoke test passed: direct SDK reference accepted and CECLIENT001 enforced for marked plugin projects.'
}
finally {
    if (Test-Path -LiteralPath $smokeDirectory -PathType Container) {
        Remove-Item -LiteralPath $smokeDirectory -Recurse -Force
    }
}
