# The consumed CheatEngine.SDK

The Client consumes exactly one published `CheatEngine.SDK` package: **1.0.0**. Its packages declare the dependency
range `[1.0.0, 2.0.0)`. This folder holds the reviewed identity of that package and the only supported way to change
it.

| File                                                       | Role                                                                         |
|------------------------------------------------------------|------------------------------------------------------------------------------|
| [`eng/CheatEngineSdk.props`](../CheatEngineSdk.props)      | The single source of the pin: version, upper bound, range, supported major   |
| [`consumed-sdk.json`](consumed-sdk.json)                   | The reviewed identity of the pinned package (hashes, bridge, source commit)  |
| [`consumed-sdk.v0.schema.json`](consumed-sdk.v0.schema.json) | The schema of that identity (`cheatengine-consumed-sdk/v0`)                |
| [`Update-CheatEngineSdk.ps1`](Update-CheatEngineSdk.ps1)   | Verifies the current pin, or moves it to another published version           |

## Why the Client stays on 1.0.0

A feature exists for the Client only when it is in the package the Client consumes, not when it is in the SDK
repository (audit ADR-10). The SDK `main` branch already contains APIs that 1.0.0 lacks, such as the value-scan owner
factory; the Client does not use them, says so in its diagnostics ("CheatEngine.SDK 1.0.0 does not provide …"), and
keeps audit finding F05 open until a qualified SDK release contains them. The next SDK release is a major version
(2.0.0) with intentional breaking changes, so moving to it is a migration of the Client, not a dependency bump; the
changes it needs will be collected in `docs/migration/sdk-2.0.md`.

## What uses the pin

Every MSBuild usage derives from `eng/CheatEngineSdk.props`, which `Directory.Packages.props` imports for every
project:

- the central `PackageVersion` of `CheatEngine.SDK`;
- the `VersionOverride="$(CheatEngineSdkVersionRange)"` of the three SDK-facing libraries (Abstractions, Core, Hosting),
  which becomes the dependency range of their packages;
- the default `CoexistenceSdkPackageVersion` of the live-plugin coexistence fixtures, which stay outside Central
  Package Management;
- the `CheatEngine.SDK` version stamped into the packed `ceplugin` template.

The repository tests `tests/CheatEngine.Client.Repository.Tests/Packaging/SdkPinTests.cs` keep the pin, this identity,
every lock file, the project files and the documentation that names the SDK version in agreement.

## One package, four hashes

The same package has four different digests. Each field of `consumed-sdk.json` names the one it records:

| Field                  | What it hashes                                                                                                      | Where a consumer finds it                                           |
|------------------------|---------------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------|
| `contentHashSha512`    | The NuGet SHA-512 base64 content hash of the package content without its signature, read from the lock file         | `packages.lock.json` (`contentHash`) and `.nupkg.metadata`           |
| `nugetOrgSignedSha512` | SHA-512 (base64) of the repository-signed `.nupkg` file served by nuget.org                                         | `<id>.<version>.nupkg.sha512` in the global packages folder          |
| `nugetOrgSignedSha256` | SHA-256 of the same repository-signed file                                                                          | `Get-FileHash` on the downloaded `.nupkg`                            |
| `attestedAssetSha256`  | SHA-256 of the unsigned `.nupkg` attached to the SDK GitHub release and covered by its provenance attestation       | the SDK release asset and `gh attestation verify`                    |

`contentHashSha512` is the value to compare with a plugin's own lock file: it is not a SHA-256 of any file. The
`nativeBridge` object records the SHA-256 of `build/native/cheatengine-sdk-lua-bridge.dll` inside the package and the
bridge source fingerprint the SDK embeds, so a deployed bridge can be tied to this package.

## Guards

| Diagnostic              | Where                                        | What it refuses                                                                                         |
|-------------------------|----------------------------------------------|---------------------------------------------------------------------------------------------------------|
| `CHEATENGINECLIENT9016` | every restore and build of this repository   | a pin that is a prerelease, of another major than `_CheatEngineClientSupportedSdkMajor`, or outside its range; packing with such a pin |
| `CHEATENGINECLIENT9017` | every restore and build of this repository   | a `CheatEngine.SDK` version written anywhere but `eng/CheatEngineSdk.props`                             |

## Move the pin

Only a published, stable version of the supported major can be pinned:

```powershell
./eng/sdk/Update-CheatEngineSdk.ps1 -Version 1.0.0 -WhatIf     # verifies the current pin against nuget.org; changes nothing
./eng/sdk/Update-CheatEngineSdk.ps1 -Version 1.0.1 `
    -AttestedAssetSha256 <sha256> -SourceCommit <commit> -SourceTreeHash <tree> -BridgeSourceFingerprint <c>:<xmake>
```

The script refuses a version that nuget.org does not list, a prerelease, and another major (unless `-AllowMajor`,
which is the start of the SDK 2.0 migration, never a dependency bump). It downloads the package into a temporary
folder, checks its repository signature with `dotnet nuget verify --all`, reads the content hash that command prints,
hashes the signed file and the packed bridge, and only then updates `eng/CheatEngineSdk.props`, the template default
and `consumed-sdk.json`. The fields that nuget.org cannot provide (attested asset, source commit and tree, bridge
fingerprint) come from the SDK release tuple `CheatEngine.SDK.<version>.tuple.json` when that asset exists, otherwise
from the parameters; they are never guessed. It then regenerates the lock files with `./eng/Update-LockFiles.ps1` and
fails unless every lock resolves the new version with the verified content hash and nothing else changed.

Dependabot must not update `CheatEngine.SDK`: the repository's Dependabot configuration ignores the package, so every
change goes through this script and a reviewed pull request.

## Canary builds

The SDK repository can build this Client against an unreleased SDK to detect breaking changes early (audit Q48). That
advisory job overrides the pin with global properties and a temporary NuGet configuration:

```powershell
dotnet build CheatEngine.Client.slnx -c Release `
  -p:CheatEngineSdkVersion=2.0.0-alpha.0.42 -p:CheatEngineSdkUpperBound=3.0.0 `
  -p:CheatEngineSdkCanary=true -p:RestorePackagesWithLockFile=false
```

The `NuGet.Config` of that job maps the package id `CheatEngine.SDK` to the folder of the candidate package and every
other id to nuget.org. `CheatEngineSdkCanary=true` turns the `CHEATENGINECLIENT9016` errors into messages so the build
reports what breaks; `dotnet pack` still fails, so a canary build never produces a Client package.
