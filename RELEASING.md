# Releasing CheatEngine.Client

The seven Client packages are released together, with one version, from one tag:

| Package                                             | Content                                                     |
|-----------------------------------------------------|-------------------------------------------------------------|
| `CheatEngine.Client`                                | The umbrella package a plugin references                    |
| `CheatEngine.Client.Abstractions`                   | Contracts, requests, failures and value vocabulary          |
| `CheatEngine.Client.Core`                           | The SDK-facing implementation                               |
| `CheatEngine.Client.Extensions.DependencyInjection` | DI registrations, modules, codecs and options               |
| `CheatEngine.Client.Fluent`                         | Immutable fluent builders                                   |
| `CheatEngine.Client.Hosting`                        | The plugin host, the Lua generator and the consumer targets |
| `CheatEngine.Client.Templates`                      | The `dotnet new ceplugin` template                          |

No Client version has been tagged or published yet. The release workflow and this procedure were added by the
September 2026 audit remediation, which tagged and published nothing.

## One-time setup

### Trusted publishing

nuget.org accepts the packages only from the release workflow, through
[trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): the workflow exchanges a GitHub
OIDC token for a short-lived API key, and no long-lived NuGet API key is stored anywhere. Create the policy at
<https://www.nuget.org/account/trustedpublishing> with these exact values:

| Field            | Value                                  |
|------------------|----------------------------------------|
| Repository owner | `CheatEngineNet`                       |
| Repository       | `CheatEngine.Client`                   |
| Workflow file    | `release.yml`                          |
| Environment      | `nuget`                                |
| Scope            | Push new packages and package versions |
| Package glob     | `CheatEngine.Client*`                  |

The glob covers the seven package ids, including `CheatEngine.Client.Templates`. None of them exists on nuget.org
yet, so the scope must allow new packages.

The GitHub environment `nuget` holds one environment secret, `NUGET_USER`: the nuget.org profile name of the person who
created the policy, not an e-mail address and not an organization name. Restrict the environment to deployment tags
matching `v*.*.*` and add the maintainers who approve a publication as required reviewers. A key obtained through
trusted publishing is valid for one hour, and each OIDC token yields one key, so the workflow logs in once, right
before it pushes the seven packages. The login and the pushes stay in the `publish` job of `release.yml`, because the
policy names that file and that environment.

### Immutable releases

Once `release.yml` is on `main`, enable immutable releases in the repository settings. A published release then keeps
its tag and its assets forever, which is why the workflow publishes a release only after it carries every asset. The
workflow runs `gh release verify` and `gh release verify-asset` when the release is immutable, and reports a notice
otherwise.

## Prepare a release

1. Move the entries of `## [Unreleased]` in [CHANGELOG.md](CHANGELOG.md) to a `## [X.Y.Z] - YYYY-MM-DD` section. Its
   body becomes the GitHub release notes; a stable tag fails without it, a prerelease tag falls back to `[Unreleased]`.
2. Ship the public API and the analyzer rules of the release:

   ```powershell
   ./eng/release/Complete-PublicApiRelease.ps1 -Version X.Y.Z -WhatIf   # review, then run without -WhatIf
   dotnet build CheatEngine.Client.slnx -c Release
   ```

   It moves every `PublicAPI.Unshipped.txt` entry into `PublicAPI.Shipped.txt` (applying `*REMOVED*` lines) and the
   analyzer rules of `AnalyzerReleases.Unshipped.md` into a `## Release X.Y.Z` section of `AnalyzerReleases.Shipped.md`.
   It is never run by CI.
3. Set `MinVerMinimumMajorMinor` in [Directory.Build.props](Directory.Build.props) to the line being released. The
   exact version comes from the `vX.Y.Z` tag; the property is only the floor for untagged commits.
4. Check the qualification gate below.
5. Rehearse the pack locally with the version the tag will produce, and inspect the seven packages:

   ```powershell
   dotnet restore CheatEngine.Client.slnx --locked-mode
   dotnet build CheatEngine.Client.slnx -c Release --no-restore
   dotnet pack CheatEngine.Client.slnx -c Release --no-build -o artifacts/rehearsal -p:MinVerVersionOverride=X.Y.Z
   ```

   Never leave `MinVerVersionOverride` set as an environment variable: MinVer reads it from the environment too.
6. Merge the release pull request (squash) once `CI / Gate` and `PR policy` pass.

## Qualification gate

A Client release is a claim about a tuple: the Client version, the exact `CheatEngine.SDK` package it consumes, that
package's native bridge, the Cheat Engine host profile and the load profile. Before tagging, record for that tuple:

- the receipts of the Client host scenarios Q09 and Q10 (two plugins in one host) and Q43 to Q46 (cleanup with a
  faulty module, contract-only APIs, sensitive probes, log redaction), or an explicit, dated waiver for each missing
  one;
- the result of Q40 (clean installation from the packages) on the exact host profile, in addition to the package
  consumption tests that CI runs on every change;
- a green consumer-contract run against the pinned SDK (Q48 at the managed-test level);
- that audit finding F05 (Client and SDK `main` diverge) stays open while the Client consumes `CheatEngine.SDK` 1.0.0.

A CI result is never presented as a host result, and a scenario that was not run stays not run.

## Publish

The release commit must first land on the first-parent history of `main`; the workflow rejects a tag on any other
commit. From an up-to-date `main` checkout:

```powershell
git tag -a vX.Y.Z -m "Release X.Y.Z"
git push origin vX.Y.Z
```

The tag starts [`release.yml`](.github/workflows/release.yml):

```text
verify ─► ci ─► attest ─► draft-release ─► publish ─► verify-publication ─► finalize-release
```

1. `verify` ([`Test-ReleaseTag.ps1`](eng/release/Test-ReleaseTag.ps1)) checks the SemVer tag, that it points to the
   first-parent history of `main`, that none of the seven ids already has the version on nuget.org, and that the pinned
   `CheatEngine.SDK` on nuget.org has a valid repository signature and the content hash of
   [`eng/sdk/consumed-sdk.json`](eng/sdk/consumed-sdk.json). It extracts the release notes
   ([`Export-ReleaseNotes.ps1`](eng/release/Export-ReleaseNotes.ps1)).
2. `ci` runs the reusable `ci.yml` on the tag commit with the expected version: it builds and tests Debug and Release,
   packs the tested Release build, fails unless every file is named `<Id>.X.Y.Z.nupkg`, runs the package consumption
   tests on those exact files, and uploads them as the `nuget-packages` artifact with `build-info.json` (kept 90 days).
3. `attest` signs one build provenance attestation over the seven packages and one SPDX SBOM attestation per package
   (predicate `https://spdx.dev/Document/v2.2`), from the SBOM each package embeds
   ([`New-ReleaseAssets.ps1`](eng/release/New-ReleaseAssets.ps1)).
4. `draft-release` writes the `PrePublish` Client tuple ([`New-ClientTuple.ps1`](eng/release/New-ClientTuple.ps1)),
   validates it against its schema, writes `SHA256SUMS`, and creates a draft release with every asset
   ([`New-ReleaseDraft.ps1`](eng/release/New-ReleaseDraft.ps1)).
5. `publish` waits for a required reviewer to approve the `nuget` deployment, logs in through trusted publishing and
   pushes the seven packages in dependency order (Abstractions, Fluent, Core, Extensions.DependencyInjection, Hosting,
   CheatEngine.Client, Templates). Each push also sends the package's symbol package.
6. `verify-publication` ([`Test-PublishedPackages.ps1`](eng/release/Test-PublishedPackages.ps1)) waits until nuget.org
   lists the seven versions, then checks each served file: repository signature, content hash equal to the attested
   package, and every entry except `.signature.p7s` identical. It records the nuget.org hashes.
7. `finalize-release` ([`Complete-GitHubRelease.ps1`](eng/release/Complete-GitHubRelease.ps1)) replaces the tuple with
   its `Published` stage, verifies both attestations of each package with `gh attestation verify`, publishes the draft
   and, when the release is immutable, verifies it.

No job of the release path restores from or saves to a NuGet cache. Only `publish` has the `nuget` environment and
reads a secret; only `attest` and `publish` receive an OIDC token.

To rehearse the pipeline without publishing, start `Release` manually on a branch (**Actions → Release → Run
workflow**). The dry run executes `verify` and the full `ci` job, then skips every job that attests, drafts or
publishes. A dispatch requires the workflow on the default branch, so the first dry run happens after the remediation
branch is merged.

## What a release contains

| Asset                                          | Content                                                                    |
|------------------------------------------------|----------------------------------------------------------------------------|
| `<Id>.X.Y.Z.nupkg` (7)                         | The attested packages; nuget.org serves the same content, repository-signed |
| `<Id>.X.Y.Z.snupkg` (5)                        | Portable PDBs with Source Link, for the five packages with build output     |
| `<Id>.X.Y.Z.spdx.json` (7)                     | The SPDX 2.2 SBOM embedded in each package, extracted                      |
| `CheatEngine.Client.X.Y.Z.provenance.sigstore.json` | The build provenance attestation bundle                               |
| `<Id>.X.Y.Z.sbom.sigstore.json` (7)            | The SBOM attestation bundles                                               |
| `CheatEngine.Client.X.Y.Z.tuple.json`          | The Client release tuple                                                   |
| `SHA256SUMS`                                   | The SHA-256 of every other asset                                           |

Each SBOM describes its package (name and version equal to the nuspec) and hashes every file in it. It also lists the
package's resolved NuGet graph from the build's `project.assets.json`: runtime dependencies such as `CheatEngine.SDK`
and `Microsoft.Extensions.*`, and build-only tools such as MinVer, the analyzers, `Microsoft.Sbom.Targets` and the
template tasks, which ship nothing into the package. Because the SBOM carries a unique namespace and a creation time,
a `.nupkg` is not byte-reproducible; reproducibility is promised for the assemblies it contains.

## The Client release tuple

`CheatEngine.Client.X.Y.Z.tuple.json` (schema [`client-tuple.v0.schema.json`](eng/release/client-tuple.v0.schema.json),
example [`client-tuple.example.json`](eng/release/client-tuple.example.json)) ties the release to what a compatibility
report needs:

- `source`: tag, commit, tree, the pull request whose squash merge produced the commit, and the run URLs;
- `build`: .NET SDK, `global.json` hash, runner image, Roslyn floor and analysis level;
- `client.packages`: for each package, the SHA-256 of the attested file, its NuGet content hash (the SHA-512 a
  consumer's lock file records), the SHA-256 of the file nuget.org serves (`Published` stage) and of its symbol package;
- `consumedSdk`: the reviewed `CheatEngine.SDK` identity, with its content hash read from
  `libs/CheatEngine.Client.Core/packages.lock.json`, and its native bridge;
- `ceProfile` and `qualification`: the Cheat Engine profile (`ce-7.7.0.10621-x64-managed-hostfxr`, which records the
  Lua module, runtime configuration and load profile), the SHA-256 of the Client support profile and qualification
  matrix once they exist, and the committed host receipts;
- `sbom`, `attestations` and `assets`: what the release carries.

## Verify a release

```powershell
gh release download vX.Y.Z --repo CheatEngineNet/CheatEngine.Client --dir release
cd release
Get-Content SHA256SUMS | ForEach-Object { $hash, $name = $_ -split '  '; if ((Get-FileHash $name -Algorithm SHA256).Hash -ne $hash) { throw "$name" } }
foreach ($package in Get-ChildItem *.nupkg) {
    gh attestation verify $package --repo CheatEngineNet/CheatEngine.Client --signer-workflow CheatEngineNet/CheatEngine.Client/.github/workflows/release.yml
    gh attestation verify $package --repo CheatEngineNet/CheatEngine.Client --predicate-type https://spdx.dev/Document/v2.2
}
gh release verify vX.Y.Z --repo CheatEngineNet/CheatEngine.Client   # once immutable releases are enabled
```

To tie a plugin to a release, compare its `packages.lock.json` (or the `sha512-…` values of the `CheatEngine.Client*`
and `CheatEngine.SDK` libraries in its deployed `.deps.json`) with `client.packages[].contentHashSha512` and
`consumedSdk.contentHashSha512` of the tuple. `dotnet nuget verify --all <package>` prints the same content hash for a
package downloaded from nuget.org. [`eng/sdk/README.md`](eng/sdk/README.md) explains the different hashes of one
package.

## Re-run a release

Use **Re-run failed jobs** only. Completed jobs are not repeated and a re-run reuses the artifacts of the original
attempt, so the pushed packages, their attestations and the release assets stay the same files. A push of an existing
version is skipped as a duplicate, a draft release receives only its missing assets, and a published release never
receives assets. **Re-run all jobs** after a successful publication stops in `verify`, because the version is already
on nuget.org.

## After a release

1. Raise `MinVerMinimumMajorMinor` to the next development line, so later untagged builds become
   `X.(Y+1).0-alpha.0.N`. The evaluation-time `VersionPrefix` follows it and NuGet records it in the project-reference
   entries of the lock files, so regenerate them in the same pull request with `./eng/Update-LockFiles.ps1`.
2. Set `CheatEngineClientPackageValidationBaselineVersion` in `Directory.Build.props` to the released version, so
   package validation reports breaking changes against it (the hook is in `eng/Shipping.props`).
3. Check the [NuGet packages](https://www.nuget.org/packages/CheatEngine.Client) and the GitHub release.
