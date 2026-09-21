[CmdletBinding()]
param(
    [switch]$Build,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$PluginASdkVersion = '1.0.0',

    [string]$PluginBSdkVersion = '1.0.0',

    [string]$PluginCollisionSdkVersion = '1.0.0',

    [string]$BundleRoot,

    [string]$PluginABundlePath,

    [string]$PluginBBundlePath,

    [string]$PluginCollisionBundlePath,

    [string]$ReceiptPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixtureRoot = Join-Path $repositoryRoot 'tests/CheatEngine.Client.LivePlugin.Coexistence'
$pluginAProject = Join-Path $fixtureRoot 'PluginA/CheatEngine.Client.LivePlugin.Coexistence.PluginA.csproj'
$pluginBProject = Join-Path $fixtureRoot 'PluginB/CheatEngine.Client.LivePlugin.Coexistence.PluginB.csproj'
$pluginCollisionProject = Join-Path $fixtureRoot 'PluginCollision/CheatEngine.Client.LivePlugin.Coexistence.PluginCollision.csproj'

function Resolve-ConcreteDirectory {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Label directory '$Path' does not exist."
    }

    return (Resolve-Path -LiteralPath $Path).Path
}

function Assert-FreshBundleRoot {
    param([Parameter(Mandatory)] [string]$Path)

    if (Test-Path -LiteralPath $Path) {
        throw "Refusing to reuse coexistence bundle root '$Path'. Use a newly created, empty path so stale dependencies cannot qualify as part of a closure."
    }
}

function Invoke-FixtureBuild {
    param(
        [Parameter(Mandatory)] [string]$ProjectPath,
        [Parameter(Mandatory)] [string]$SdkVersion,
        [Parameter(Mandatory)] [string]$DeploymentPath
    )

    & dotnet build $ProjectPath --configuration $Configuration `
        "-p:CoexistenceSdkPackageVersion=$SdkVersion" `
        "-p:CheatEnginePluginOutputPath=$DeploymentPath"
    if ($LASTEXITCODE -ne 0) {
        throw "Fixture build failed for '$ProjectPath' (SDK package '$SdkVersion')."
    }
}

function Get-DependencyAssets {
    param(
        [Parameter(Mandatory)] [object]$Deps,
        [Parameter(Mandatory)] [string]$BundlePath,
        [Parameter(Mandatory)] [string]$Label
    )

    $targetProperties = @($Deps.targets.PSObject.Properties)
    if ($targetProperties.Count -ne 1) {
        throw "$Label dependency manifest must contain exactly one runtime target; found $($targetProperties.Count)."
    }

    $assetPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($library in $targetProperties[0].Value.PSObject.Properties) {
        foreach ($sectionName in @('runtime', 'native')) {
            $sectionProperty = $library.Value.PSObject.Properties[$sectionName]
            if ($null -eq $sectionProperty) {
                continue
            }

            $section = $sectionProperty.Value

            foreach ($asset in $section.PSObject.Properties) {
                $relativePath = $asset.Name.Replace('/', [IO.Path]::DirectorySeparatorChar)
                $assetPath = Join-Path $BundlePath $relativePath
                if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
                    # NuGet's deps manifest names package assets (for example lib/net10.0/foo.dll), whereas a managed
                    # plugin deployment flattens its runtime closure beside the plugin DLL.
                    $assetPath = Join-Path $BundlePath ([IO.Path]::GetFileName($relativePath))
                    if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
                        throw "$Label bundle is missing dependency asset '$($asset.Name)' declared by '$($library.Name)'."
                    }
                }

                [void]$assetPaths.Add($relativePath)
            }
        }
    }

    return @($assetPaths | Sort-Object)
}

function Get-ResolvedSdkPackage {
    param(
        [Parameter(Mandatory)] [string]$ProjectPath,
        [Parameter(Mandatory)] [string]$Label,
        [Parameter(Mandatory)] [string]$SdkAssemblyPath,
        [Parameter(Mandatory)] [string]$RequestedSdkVersion
    )

    $projectName = [IO.Path]::GetFileNameWithoutExtension($ProjectPath)
    $assetsPath = Join-Path $repositoryRoot "artifacts/obj/$projectName/project.assets.json"
    if (-not (Test-Path -LiteralPath $assetsPath -PathType Leaf)) {
        throw "$Label has no restored project.assets.json at '$assetsPath'."
    }

    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    $sdkLibrary = @($assets.libraries.PSObject.Properties | Where-Object Name -like 'CheatEngine.SDK/*')
    if ($sdkLibrary.Count -ne 1) {
        throw "$Label must resolve exactly one CheatEngine.SDK package; found $($sdkLibrary.Count)."
    }

    $resolvedVersion = $sdkLibrary[0].Name.Substring('CheatEngine.SDK/'.Length)
    if ($resolvedVersion -ne $RequestedSdkVersion) {
        throw "$Label resolved CheatEngine.SDK '$resolvedVersion', not requested '$RequestedSdkVersion'."
    }

    $targetProperties = @($assets.targets.PSObject.Properties)
    if ($targetProperties.Count -ne 1) {
        throw "$Label restored project.assets.json must contain exactly one runtime target; found $($targetProperties.Count)."
    }

    $sdkTarget = $targetProperties[0].Value.PSObject.Properties[$sdkLibrary[0].Name]
    if ($null -eq $sdkTarget -or $null -eq $sdkTarget.Value.runtime) {
        throw "$Label restored CheatEngine.SDK package has no runtime assets."
    }

    $runtimeAssemblyPaths = @(
        $sdkTarget.Value.runtime.PSObject.Properties.Name |
            Where-Object { $_ -match '^lib/[^/]+/CheatEngine\.SDK\.dll$' }
    )
    if ($runtimeAssemblyPaths.Count -ne 1) {
        throw "$Label restored CheatEngine.SDK package must expose exactly one CheatEngine.SDK runtime assembly; found $($runtimeAssemblyPaths.Count)."
    }

    $bundleAssemblyHash = (Get-FileHash -LiteralPath $SdkAssemblyPath -Algorithm SHA256).Hash
    $packageRelativePath = $sdkLibrary[0].Value.path.Replace('/', [IO.Path]::DirectorySeparatorChar)
    $runtimeAssemblyRelativePath = $runtimeAssemblyPaths[0].Replace('/', [IO.Path]::DirectorySeparatorChar)
    foreach ($packageFolder in $assets.packageFolders.PSObject.Properties.Name) {
        $packageAssemblyPath = Join-Path (Join-Path $packageFolder $packageRelativePath) $runtimeAssemblyRelativePath
        if ((Test-Path -LiteralPath $packageAssemblyPath -PathType Leaf) -and
            (Get-FileHash -LiteralPath $packageAssemblyPath -Algorithm SHA256).Hash -eq $bundleAssemblyHash) {
            return [ordered]@{
                Id = 'CheatEngine.SDK'
                Version = $resolvedVersion
                ContentHash = $sdkLibrary[0].Value.sha512
                Verification = 'VerifiedPackageAssemblyMatch'
                BundleAssemblySha256 = $bundleAssemblyHash
            }
        }
    }

    return [ordered]@{
        Id = 'CheatEngine.SDK'
        Verification = 'UnverifiedBundleAssembly'
        BundleAssemblySha256 = $bundleAssemblyHash
        Reason = 'The supplied bundle CheatEngine.SDK.dll did not match the runtime assembly from the resolved approved package.'
    }
}

function Get-BundleReceipt {
    param(
        [Parameter(Mandatory)] [string]$Label,
        [Parameter(Mandatory)] [string]$BundlePath,
        [Parameter(Mandatory)] [string]$ProjectPath,
        [Parameter(Mandatory)] [string]$PluginAssemblyName,
        [Parameter(Mandatory)] [string]$RequestedSdkVersion
    )

    $resolvedBundle = Resolve-ConcreteDirectory -Path $BundlePath -Label $Label
    $pluginAssemblyPath = Join-Path $resolvedBundle "$PluginAssemblyName.dll"
    $depsPath = Join-Path $resolvedBundle "$PluginAssemblyName.deps.json"
    $runtimeConfigPath = Join-Path $resolvedBundle "$PluginAssemblyName.runtimeconfig.json"
    $requiredFiles = @(
        $pluginAssemblyPath,
        $depsPath,
        $runtimeConfigPath,
        (Join-Path $resolvedBundle 'CheatEngine.SDK.dll'),
        (Join-Path $resolvedBundle 'CheatEngine.Client.Abstractions.dll'),
        (Join-Path $resolvedBundle 'CheatEngine.Client.Core.dll'),
        (Join-Path $resolvedBundle 'CheatEngine.Client.Extensions.DependencyInjection.dll'),
        (Join-Path $resolvedBundle 'CheatEngine.Client.Hosting.dll'),
        (Join-Path $resolvedBundle 'cheatengine-sdk-lua-bridge.dll')
    )
    foreach ($requiredFile in $requiredFiles) {
        if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
            throw "$Label bundle is missing required deployment file '$requiredFile'."
        }
    }

    $deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json
    $dependencyAssets = Get-DependencyAssets -Deps $deps -BundlePath $resolvedBundle -Label $Label
    $sdkAssemblyPath = Join-Path $resolvedBundle 'CheatEngine.SDK.dll'
    $sdkPackage = Get-ResolvedSdkPackage -ProjectPath $ProjectPath -Label $Label -SdkAssemblyPath $sdkAssemblyPath `
        -RequestedSdkVersion $RequestedSdkVersion

    $fileRecords = @(
        Get-ChildItem -LiteralPath $resolvedBundle -File -Recurse |
            Sort-Object FullName |
            ForEach-Object {
                [ordered]@{
                    Path = [IO.Path]::GetRelativePath($resolvedBundle, $_.FullName).Replace('\', '/')
                    Length = $_.Length
                    Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
                }
            }
    )

    $sdkAssembly = [Reflection.AssemblyName]::GetAssemblyName($sdkAssemblyPath)
    return [ordered]@{
        Label = $Label
        BundlePath = $resolvedBundle
        PluginAssembly = [ordered]@{
            Name = $PluginAssemblyName
            Sha256 = (Get-FileHash -LiteralPath $pluginAssemblyPath -Algorithm SHA256).Hash
        }
        SdkPackage = $sdkPackage
        SdkAssemblyIdentity = $sdkAssembly.FullName
        DependencyAssets = $dependencyAssets
        Files = $fileRecords
    }
}

if ($Build) {
    if ([string]::IsNullOrWhiteSpace($BundleRoot)) {
        $BundleRoot = Join-Path $repositoryRoot ("artifacts/live-plugin-coexistence/" + [Guid]::NewGuid().ToString('N'))
    }

    $resolvedBundleRoot = [IO.Path]::GetFullPath($BundleRoot)
    Assert-FreshBundleRoot -Path $resolvedBundleRoot
    $PluginABundlePath = Join-Path $resolvedBundleRoot 'PluginA'
    $PluginBBundlePath = Join-Path $resolvedBundleRoot 'PluginB'
    $PluginCollisionBundlePath = Join-Path $resolvedBundleRoot 'PluginCollision'

    Invoke-FixtureBuild -ProjectPath $pluginAProject -SdkVersion $PluginASdkVersion -DeploymentPath $PluginABundlePath
    Invoke-FixtureBuild -ProjectPath $pluginBProject -SdkVersion $PluginBSdkVersion -DeploymentPath $PluginBBundlePath
    Invoke-FixtureBuild -ProjectPath $pluginCollisionProject -SdkVersion $PluginCollisionSdkVersion -DeploymentPath $PluginCollisionBundlePath
}
else {
    if ([string]::IsNullOrWhiteSpace($PluginABundlePath) -or [string]::IsNullOrWhiteSpace($PluginBBundlePath) -or
        [string]::IsNullOrWhiteSpace($PluginCollisionBundlePath)) {
        throw 'Supply -Build or all three existing bundle paths.'
    }
}

$pluginAReceipt = Get-BundleReceipt -Label 'PluginA' -BundlePath $PluginABundlePath -ProjectPath $pluginAProject `
    -PluginAssemblyName 'CheatEngine.Client.LivePlugin.Coexistence.PluginA' -RequestedSdkVersion $PluginASdkVersion
$pluginBReceipt = Get-BundleReceipt -Label 'PluginB' -BundlePath $PluginBBundlePath -ProjectPath $pluginBProject `
    -PluginAssemblyName 'CheatEngine.Client.LivePlugin.Coexistence.PluginB' -RequestedSdkVersion $PluginBSdkVersion
$pluginCollisionReceipt = Get-BundleReceipt -Label 'PluginCollision' -BundlePath $PluginCollisionBundlePath `
    -ProjectPath $pluginCollisionProject -PluginAssemblyName 'CheatEngine.Client.LivePlugin.Coexistence.PluginCollision' `
    -RequestedSdkVersion $PluginCollisionSdkVersion

$bundlePaths = @($pluginAReceipt.BundlePath, $pluginBReceipt.BundlePath, $pluginCollisionReceipt.BundlePath)
for ($first = 0; $first -lt $bundlePaths.Count; $first++) {
    for ($second = $first + 1; $second -lt $bundlePaths.Count; $second++) {
        if ($bundlePaths[$first] -eq $bundlePaths[$second] -or
            $bundlePaths[$first].StartsWith($bundlePaths[$second] + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase) -or
            $bundlePaths[$second].StartsWith($bundlePaths[$first] + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Coexistence bundles must be disjoint directories; found '$($bundlePaths[$first])' and '$($bundlePaths[$second])'."
        }
    }
}

if ([string]::IsNullOrWhiteSpace($ReceiptPath)) {
    $receiptBase = if ($Build) { Split-Path -Parent $PluginABundlePath } else { [IO.Path]::GetTempPath() }
    $ReceiptPath = Join-Path $receiptBase 'coexistence-build-receipt.json'
}

$resolvedReceiptPath = [IO.Path]::GetFullPath($ReceiptPath)
$receiptDirectory = Split-Path -Parent $resolvedReceiptPath
if (-not [string]::IsNullOrWhiteSpace($receiptDirectory)) {
    [IO.Directory]::CreateDirectory($receiptDirectory) | Out-Null
}

$receipt = [ordered]@{
    Schema = 'CheatEngine.Client.LivePlugin.Coexistence.Receipt/v1'
    EvidenceState = 'BuildPrepared_NotLiveQualified'
    GeneratedAtUtc = [DateTime]::UtcNow.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
    Runner = [ordered]@{
        Path = $PSCommandPath
        BuildPerformed = [bool]$Build
        Configuration = $Configuration
    }
    Qualification = [ordered]@{
        HostRun = 'Not executed by this runner'
        Collision = 'Specified; requires controlled-host transcript'
        DisableOneSurvivesOther = 'Specified; requires controlled-host transcript'
        TargetSwitch = 'Specified; requires two authorized disposable targets and controlled-host transcript'
        RetainedOwner = 'Specified; current Client tuple can report capability unavailable and must not be counted as a pass'
        SideBySideSdk = 'Specified; package tuples are recorded but loader isolation remains host-qualified'
    }
    Bundles = @($pluginAReceipt, $pluginBReceipt, $pluginCollisionReceipt)
}

$receipt | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $resolvedReceiptPath -Encoding utf8NoBOM
Write-Host "Prepared and verified three isolated plugin bundles. Receipt: $resolvedReceiptPath"
Write-Host 'No Cheat Engine process was started, inspected, attached, configured, or modified. This is build/package-layout evidence only.'
