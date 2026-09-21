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

$expectedHostingPackage = Join-Path $resolvedPackageSource "CheatEngine.Client.Hosting.$ClientVersion.nupkg"
if (-not (Test-Path -LiteralPath $expectedHostingPackage -PathType Leaf)) {
    throw "Expected package '$expectedHostingPackage' was not found. Run dotnet pack before the smoke test."
}

$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$smokeDirectory = [IO.Path]::GetFullPath((Join-Path $temporaryBase ("CheatEngine.Client.PackageSmoke." + [Guid]::NewGuid().ToString('N'))))
if (-not $smokeDirectory.StartsWith($temporaryBase, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use a smoke-test directory outside the system temporary directory: '$smokeDirectory'."
}

function Write-SmokeProject {
    param(
        [Parameter(Mandatory)] [string]$ProjectDirectory,
        [Parameter(Mandatory)] [bool]$IncludeClientReference,
        [Parameter(Mandatory)] [bool]$IncludeSdkReference,
        [bool]$IncludeHostingReference = $false,
        [ValidateRange(0, 2)] [int]$PluginCount = 1,
        [string]$LanguageVersion = '14.0',
        [string]$PlatformTarget = 'x64',
        [bool]$GenerateEntryPoint = $true,
        [bool]$ManualBootstrap = $false
    )

    $clientReference = if ($IncludeClientReference) {
        '    <PackageReference Include="CheatEngine.Client" Version="' + $ClientVersion + '" />'
    }
    else {
        ''
    }

    $sdkReference = if ($IncludeSdkReference) {
        '    <PackageReference Include="CheatEngine.SDK" Version="1.0.0" />'
    }
    else {
        ''
    }

    $hostingReference = if ($IncludeHostingReference) {
        '    <PackageReference Include="CheatEngine.Client.Hosting" Version="' + $ClientVersion + '" />'
    }
    else {
        ''
    }

    $projectXml = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>$LanguageVersion</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <PlatformTarget>$PlatformTarget</PlatformTarget>
    <CheatEngineClientPluginProject>true</CheatEngineClientPluginProject>
    <CheatEngineSdkGenerateEntryPoint>$GenerateEntryPoint</CheatEngineSdkGenerateEntryPoint>
    <CheatEngineClientManualBootstrap>$ManualBootstrap</CheatEngineClientManualBootstrap>
    <RestorePackagesWithLockFile>false</RestorePackagesWithLockFile>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>obj/Generated</CompilerGeneratedFilesOutputPath>
  </PropertyGroup>
  <ItemGroup>
$clientReference
$hostingReference
$sdkReference
  </ItemGroup>
</Project>
"@

    Set-Content -LiteralPath (Join-Path $ProjectDirectory 'Smoke.Plugin.csproj') -Value $projectXml -Encoding utf8NoBOM
    $pluginClasses = for ($index = 1; $index -le $PluginCount; $index++) {
        @"
[CheatEnginePlugin("Package smoke plugin $index")]
public sealed class Plugin$index : CheatEngineClientPlugin
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
"@
    }

    if ($PluginCount -eq 0) {
        $pluginClasses = @'
public sealed class NotAPlugin;
'@
    }

    $manualBootstrapSource = if ($ManualBootstrap) {
        @'
namespace CESDK
{
    public static class CESDK
    {
        public static int CEPluginInitialize(IntPtr initialization, int version)
        {
            return 1;
        }
    }
}
'@
    }
    else {
        ''
    }

    $pluginSource = @"
using System;
using CheatEngine.Client;
using CheatEngine.Client.Hosting;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Annotations.Plugin;
using CheatEngine.SDK.Engine.Values;

$pluginClasses
$manualBootstrapSource
"@
    Set-Content -LiteralPath (Join-Path $ProjectDirectory 'Plugin.cs') -Value $pluginSource -Encoding utf8NoBOM
}

function Assert-ExpectedBuildFailure {
    param(
        [Parameter(Mandatory)] [string]$ProjectPath,
        [Parameter(Mandatory)] [string]$ExpectedDiagnostic,
        [string[]]$AdditionalArguments = @()
    )

    $output = & dotnet build $ProjectPath --configuration Release --no-restore @AdditionalArguments 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0) {
        throw "The negative isolated plugin build unexpectedly succeeded; expected $ExpectedDiagnostic."
    }
    if ($output -notmatch $ExpectedDiagnostic) {
        throw "The negative isolated plugin build failed, but did not report $ExpectedDiagnostic.`n$output"
    }

    # A diagnosed negative build is expected. Clear the native command status only after asserting its exact diagnostic.
    $global:LASTEXITCODE = 0
}

function Assert-PackageEntries {
    param(
        [Parameter(Mandatory)] [string]$PackagePath,
        [Parameter(Mandatory)] [string[]]$ExpectedEntries
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $entries = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $archive.Entries) {
            [void]$entries.Add($entry.FullName)
        }

        foreach ($expectedEntry in $ExpectedEntries) {
            if (-not $entries.Contains($expectedEntry)) {
                throw "Package '$PackagePath' is missing '$expectedEntry'."
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

try {
    New-Item -ItemType Directory -Path $smokeDirectory | Out-Null

    Assert-PackageEntries -PackagePath $expectedHostingPackage -ExpectedEntries @(
        'analyzers/dotnet/cs/CheatEngine.Client.SourceGenerators.Lua.dll',
        'buildTransitive/CheatEngine.Client.Hosting.props',
        'buildTransitive/CheatEngine.Client.Hosting.targets'
    )

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
    Write-SmokeProject -ProjectDirectory $positiveDirectory -IncludeClientReference $true -IncludeSdkReference $true
    & dotnet restore (Join-Path $positiveDirectory 'Smoke.Plugin.csproj') --configfile $configurationPath
    if ($LASTEXITCODE -ne 0) {
        throw 'The positive isolated package restore failed.'
    }

    $deploymentDirectory = Join-Path $smokeDirectory 'deployment'
    & dotnet build (Join-Path $positiveDirectory 'Smoke.Plugin.csproj') --configuration Release --no-restore `
        "-p:CheatEnginePluginOutputPath=$deploymentDirectory"
    if ($LASTEXITCODE -ne 0) {
        throw 'The positive isolated package build failed.'
    }

    # Reuse the destination so the deployment task must replace every staged file, not only create a fresh layout.
    & dotnet build (Join-Path $positiveDirectory 'Smoke.Plugin.csproj') --configuration Release --no-restore `
        "-p:CheatEnginePluginOutputPath=$deploymentDirectory"
    if ($LASTEXITCODE -ne 0) {
        throw 'The managed deployment could not atomically replace an existing plugin layout.'
    }

    $positiveOutput = Join-Path $positiveDirectory 'bin/Release/net10.0'
    $requiredOutputFiles = @(
        'Smoke.Plugin.dll',
        'Smoke.Plugin.deps.json',
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

    foreach ($requiredDeploymentFile in $requiredOutputFiles) {
        $requiredDeploymentPath = Join-Path $deploymentDirectory $requiredDeploymentFile
        if (-not (Test-Path -LiteralPath $requiredDeploymentPath -PathType Leaf)) {
            throw "The prepared deployment is missing '$requiredDeploymentFile'."
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
    Write-SmokeProject -ProjectDirectory $negativeDirectory -IncludeClientReference $true -IncludeSdkReference $false
    & dotnet restore (Join-Path $negativeDirectory 'Smoke.Plugin.csproj') --configfile $configurationPath
    if ($LASTEXITCODE -ne 0) {
        throw 'The negative isolated package restore failed before CECLIENT001 could be evaluated.'
    }

    Assert-ExpectedBuildFailure -ProjectPath (Join-Path $negativeDirectory 'Smoke.Plugin.csproj') -ExpectedDiagnostic 'CECLIENT001'

    $missingClientDirectory = Join-Path $smokeDirectory 'missing-client'
    New-Item -ItemType Directory -Path $missingClientDirectory | Out-Null
    Write-SmokeProject -ProjectDirectory $missingClientDirectory -IncludeClientReference $false -IncludeSdkReference $true `
        -IncludeHostingReference $true
    & dotnet restore (Join-Path $missingClientDirectory 'Smoke.Plugin.csproj') --configfile $configurationPath
    if ($LASTEXITCODE -ne 0) {
        throw 'The missing-Client isolated package restore failed before CECLIENT002 could be evaluated.'
    }
    Assert-ExpectedBuildFailure -ProjectPath (Join-Path $missingClientDirectory 'Smoke.Plugin.csproj') -ExpectedDiagnostic 'CECLIENT002'

    $zeroPluginDirectory = Join-Path $smokeDirectory 'zero-plugin'
    New-Item -ItemType Directory -Path $zeroPluginDirectory | Out-Null
    Write-SmokeProject -ProjectDirectory $zeroPluginDirectory -IncludeClientReference $true -IncludeSdkReference $true -PluginCount 0
    & dotnet restore (Join-Path $zeroPluginDirectory 'Smoke.Plugin.csproj') --configfile $configurationPath
    if ($LASTEXITCODE -ne 0) {
        throw 'The zero-plugin isolated package restore failed before CECLIENT003 could be evaluated.'
    }
    Assert-ExpectedBuildFailure -ProjectPath (Join-Path $zeroPluginDirectory 'Smoke.Plugin.csproj') -ExpectedDiagnostic 'CECLIENT003'

    $multiplePluginDirectory = Join-Path $smokeDirectory 'multiple-plugin'
    New-Item -ItemType Directory -Path $multiplePluginDirectory | Out-Null
    Write-SmokeProject -ProjectDirectory $multiplePluginDirectory -IncludeClientReference $true -IncludeSdkReference $true `
        -PluginCount 2 -GenerateEntryPoint $false -ManualBootstrap $true
    & dotnet restore (Join-Path $multiplePluginDirectory 'Smoke.Plugin.csproj') --configfile $configurationPath
    if ($LASTEXITCODE -ne 0) {
        throw 'The multiple-plugin isolated package restore failed before CECLIENT004 could be evaluated.'
    }
    Assert-ExpectedBuildFailure -ProjectPath (Join-Path $multiplePluginDirectory 'Smoke.Plugin.csproj') -ExpectedDiagnostic 'CECLIENT004'

    $manualBootstrapDirectory = Join-Path $smokeDirectory 'manual-bootstrap'
    New-Item -ItemType Directory -Path $manualBootstrapDirectory | Out-Null
    Write-SmokeProject -ProjectDirectory $manualBootstrapDirectory -IncludeClientReference $true -IncludeSdkReference $true `
        -GenerateEntryPoint $false
    & dotnet restore (Join-Path $manualBootstrapDirectory 'Smoke.Plugin.csproj') --configfile $configurationPath
    if ($LASTEXITCODE -ne 0) {
        throw 'The manual-bootstrap isolated package restore failed before CECLIENT008 could be evaluated.'
    }
    Assert-ExpectedBuildFailure -ProjectPath (Join-Path $manualBootstrapDirectory 'Smoke.Plugin.csproj') -ExpectedDiagnostic 'CECLIENT008'

    $languageDirectory = Join-Path $smokeDirectory 'language'
    New-Item -ItemType Directory -Path $languageDirectory | Out-Null
    Write-SmokeProject -ProjectDirectory $languageDirectory -IncludeClientReference $true -IncludeSdkReference $true -LanguageVersion '13.0'
    & dotnet restore (Join-Path $languageDirectory 'Smoke.Plugin.csproj') --configfile $configurationPath
    if ($LASTEXITCODE -ne 0) {
        throw 'The language-version isolated package restore failed before CECLIENT006 could be evaluated.'
    }
    Assert-ExpectedBuildFailure -ProjectPath (Join-Path $languageDirectory 'Smoke.Plugin.csproj') -ExpectedDiagnostic 'CECLIENT006'

    $platformDirectory = Join-Path $smokeDirectory 'platform'
    New-Item -ItemType Directory -Path $platformDirectory | Out-Null
    Write-SmokeProject -ProjectDirectory $platformDirectory -IncludeClientReference $true -IncludeSdkReference $true -PlatformTarget 'x86'
    & dotnet restore (Join-Path $platformDirectory 'Smoke.Plugin.csproj') --configfile $configurationPath
    if ($LASTEXITCODE -ne 0) {
        throw 'The platform isolated package restore failed before CECLIENT007 could be evaluated.'
    }
    Assert-ExpectedBuildFailure -ProjectPath (Join-Path $platformDirectory 'Smoke.Plugin.csproj') -ExpectedDiagnostic 'CECLIENT007'

    $frameworkDirectory = Join-Path $smokeDirectory 'framework'
    New-Item -ItemType Directory -Path $frameworkDirectory | Out-Null
    Write-SmokeProject -ProjectDirectory $frameworkDirectory -IncludeClientReference $true -IncludeSdkReference $true
    & dotnet restore (Join-Path $frameworkDirectory 'Smoke.Plugin.csproj') --configfile $configurationPath
    if ($LASTEXITCODE -ne 0) {
        throw 'The framework isolated package restore failed before CECLIENT005 could be evaluated.'
    }
    Assert-ExpectedBuildFailure -ProjectPath (Join-Path $frameworkDirectory 'Smoke.Plugin.csproj') -ExpectedDiagnostic 'CECLIENT005' `
        -AdditionalArguments @('-p:TargetFramework=net9.0')

    $anyCpuDirectory = Join-Path $smokeDirectory 'anycpu'
    New-Item -ItemType Directory -Path $anyCpuDirectory | Out-Null
    Write-SmokeProject -ProjectDirectory $anyCpuDirectory -IncludeClientReference $true -IncludeSdkReference $true -PlatformTarget 'AnyCPU'
    & dotnet restore (Join-Path $anyCpuDirectory 'Smoke.Plugin.csproj') --configfile $configurationPath
    if ($LASTEXITCODE -ne 0) {
        throw 'The AnyCPU isolated package restore failed.'
    }
    & dotnet build (Join-Path $anyCpuDirectory 'Smoke.Plugin.csproj') --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw 'The AnyCPU isolated plugin build failed.'
    }

    $manualBootstrapSuccessDirectory = Join-Path $smokeDirectory 'manual-bootstrap-success'
    New-Item -ItemType Directory -Path $manualBootstrapSuccessDirectory | Out-Null
    Write-SmokeProject -ProjectDirectory $manualBootstrapSuccessDirectory -IncludeClientReference $true -IncludeSdkReference $true `
        -GenerateEntryPoint $false -ManualBootstrap $true
    & dotnet restore (Join-Path $manualBootstrapSuccessDirectory 'Smoke.Plugin.csproj') --configfile $configurationPath
    if ($LASTEXITCODE -ne 0) {
        throw 'The manual-bootstrap-success isolated package restore failed.'
    }
    & dotnet build (Join-Path $manualBootstrapSuccessDirectory 'Smoke.Plugin.csproj') --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw 'The explicit manual bootstrap isolated plugin build failed.'
    }

    Write-Host 'Package smoke test passed: direct package references, Client plugin profile diagnostics, generated SDK bootstrap, and managed deployment layout are verified.'
}
finally {
    if (Test-Path -LiteralPath $smokeDirectory -PathType Container) {
        Remove-Item -LiteralPath $smokeDirectory -Recurse -Force
    }
}
