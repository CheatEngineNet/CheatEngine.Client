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
2. Ship the public API and the analyzer rules of the release, by hand, in every project that changed:
   - move every entry of each project's `PublicAPI.Unshipped.txt` into its `PublicAPI.Shipped.txt` (keep `*REMOVED*`
     lines), leaving `#nullable enable` first and the file empty otherwise;
   - move the rows of `AnalyzerReleases.Unshipped.md` into a new `## Release X.Y.Z` section of
     `AnalyzerReleases.Shipped.md`.

   Then confirm the move compiles clean (RS0016/RS0017/RS0025, RS2000–RS2008):

   ```powershell
   dotnet build CheatEngine.Client.slnx -c Release
   ```
3. Set `MinVerMinimumMajorMinor` in [Directory.Build.props](Directory.Build.props) to the line being released. The
   exact version comes from the `vX.Y.Z` tag; the property is only the floor for untagged commits. When the value
   changes, the evaluation-time `VersionPrefix` follows it and NuGet records it in the project-reference entries of the
   lock files (the three coexistence fixture locks included), so regenerate them in the same pull request with
   `dotnet restore <project> --force-evaluate` for each affected project.
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

1. `verify` checks the SemVer tag, that it points to the first-parent history of `main`, and that none of the seven ids
   already has the version on nuget.org (a version can never be replaced). It extracts the release notes from
   `CHANGELOG.md`.
2. `ci` runs the reusable `ci.yml` on the tag commit with the expected version: it builds and tests Debug and Release,
   packs the tested Release build, fails unless every file is named `<Id>.X.Y.Z.nupkg`, runs the package consumption
   tests on those exact files, and uploads them as the `nuget-packages` artifact.
3. `attest` signs one build provenance attestation over the seven packages and one SPDX SBOM attestation per package
   (predicate `https://spdx.dev/Document/v2.2`), from the SBOM each package embeds.
4. `draft-release` writes `SHA256SUMS` over the release assets and creates a draft release with every asset. Any draft
   already on the tag (an interrupted run or an earlier build) is deleted first and the draft is created again from
   this run's files, so the release always carries exactly the packages `publish` pushes, never a patched-over asset
   set (that path breaks immutable releases).
5. `publish` waits for a required reviewer to approve the `nuget` deployment, logs in through trusted publishing and
   pushes the seven packages in dependency order (Abstractions, Fluent, Core, Extensions.DependencyInjection, Hosting,
   CheatEngine.Client, Templates). Each push also sends the package's symbol package.
6. `verify-publication` waits until nuget.org lists the seven versions, then checks each served file: repository
   signature, content hash equal to the attested package, and every entry except `.signature.p7s` identical.
7. `finalize-release` verifies both attestations of each package with `gh attestation verify`, publishes the draft,
   addressed by its release id, and, when the release is immutable, verifies it with `gh release verify`.

No job of the release path restores from or saves to a NuGet cache. Only `publish` has the `nuget` environment and
reads a secret; only `attest` and `publish` receive an OIDC token; the `contents: write` token of `draft-release`
and `finalize-release` reaches only their `gh` steps, never the restore and test steps.

To rehearse the pipeline without publishing, start `Release` manually (**Actions → Release → Run workflow**), from a
branch or from a tag. Only a tag push of `CheatEngineNet/CheatEngine.Client` releases: for a `workflow_dispatch`,
even one started from a tag, `verify` writes no version, and `attest`, `draft-release` and `publish` (which share one
condition: a push, a tag, this repository and a verified version) are skipped, with every job after them. The dry run
executes `verify` and the full `ci` job only. A dispatch requires the workflow on the default branch, so the first dry
run happens after the remediation branch is merged.

## What a release contains

| Asset                                          | Content                                                                    |
|------------------------------------------------|----------------------------------------------------------------------------|
| `<Id>.X.Y.Z.nupkg` (7)                         | The attested packages; nuget.org serves the same content, repository-signed |
| `<Id>.X.Y.Z.snupkg` (5)                        | Portable PDBs with Source Link, for the five packages with build output     |
| `<Id>.X.Y.Z.spdx.json` (7)                     | The SPDX 2.2 SBOM embedded in each package, extracted                      |
| `CheatEngine.Client.X.Y.Z.provenance.sigstore.json` | The build provenance attestation bundle                               |
| `<Id>.X.Y.Z.sbom.sigstore.json` (7)            | The SBOM attestation bundles                                               |
| `SHA256SUMS`                                   | The SHA-256 of every other asset                                           |

Each SBOM describes its package (name and version equal to the nuspec) and hashes every file in it. It also lists the
package's resolved NuGet graph from the build's `project.assets.json`: runtime dependencies such as `CheatEngine.SDK`
and `Microsoft.Extensions.*`, and build-only tools such as MinVer, the analyzers, `Microsoft.Sbom.Targets` and the
template tasks, which ship nothing into the package. Because the SBOM carries a unique namespace and a creation time,
a `.nupkg` is not byte-reproducible; reproducibility is promised for the assemblies it contains.

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
and `CheatEngine.SDK` libraries in its deployed `.deps.json`) with the SHA-256 of the attested `.nupkg` and the NuGet
content hash nuget.org serves. `dotnet nuget verify --all <package>` prints the same content hash for a package
downloaded from nuget.org.

## Re-run a release

Use **Re-run failed jobs**. Completed jobs are not repeated and a re-run reuses the artifacts of the original attempt,
so the pushed packages and their attestations stay the same files. `draft-release` always deletes any draft already on
the tag and creates it again from the run's artifacts (deleting a draft release keeps the git tag, and nothing of a
draft is public), so a re-run's draft always matches that run's files. A push of an existing version is skipped as a
duplicate, and a published release never receives assets: `draft-release` fails before anything is created if the
release is already published. **Re-run all jobs** after any package reached nuget.org stops in `verify`, because the
version is already there.

A new build of the same tag produces different package bytes (the SBOM of each package has a unique namespace and
creation time). This happens when the maintainer rejects or cancels `publish` and then re-pushes the tag, after moving
it or not, or uses **Re-run all jobs** before any package reached nuget.org: the earlier build's draft is deleted and
replaced by `draft-release`, so the release always carries the packages `publish` pushes and `SHA256SUMS` lists.

## After a release

1. Raise `MinVerMinimumMajorMinor` to the next development line, so later untagged builds become
   `X.(Y+1).0-alpha.0.N`. The evaluation-time `VersionPrefix` follows it and NuGet records it in the project-reference
   entries of the lock files, so regenerate them in the same pull request with `dotnet restore <project> --force-evaluate`
   for each affected project.
2. Set `CheatEngineClientPackageValidationBaselineVersion` in `Directory.Build.props` to the released version, so
   package validation reports breaking changes against it (the hook is in `eng/Shipping.props`).
3. Check the [NuGet packages](https://www.nuget.org/packages/CheatEngine.Client) and the GitHub release.
