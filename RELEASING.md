# Releasing CheatEngine.Client

The seven Client packages are released together, with one version, from one tag:

| Package                                             | Content                                                     |
|-----------------------------------------------------|-------------------------------------------------------------|
| `CheatEngine.Client`                                | The umbrella package a plugin references                    |
| `CheatEngine.Client.Abstractions`                   | Contracts, requests, failures and value vocabulary          |
| `CheatEngine.Client.Core`                           | The SDK-facing implementation                               |
| `CheatEngine.Client.Extensions.DependencyInjection` | Hosting's composition layer: DI registrations and options   |
| `CheatEngine.Client.Fluent`                         | Immutable fluent builders                                   |
| `CheatEngine.Client.Hosting`                        | The plugin host, the Lua generator and the consumer targets |
| `CheatEngine.Client.Templates`                      | The `dotnet new ceplugin` template                          |

1.0.0 is the first release: no Client version was tagged or published before it. It consumes `CheatEngine.SDK` 2.0.0
and declares `[2.0.0, 3.0.0)`. The release workflow and this procedure come from the September 2026 audit remediation,
whose pull request (#59) is also the 1.0.0 release pull request.

## One-time setup

Every step of this section is a maintainer action in the nuget.org and GitHub settings; nothing in the repository can
perform it, and the first release needs all of it.

### Trusted publishing

nuget.org accepts the packages only from the release workflow, through
[trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): the workflow exchanges a GitHub
OIDC token for a short-lived API key, and no long-lived NuGet API key is stored anywhere. Create the policy at
<https://www.nuget.org/account/trustedpublishing> with these exact values:

| Field            | Value                                  |
|------------------|----------------------------------------|
| Policy owner     | `CheatEngine` (organization)           |
| Repository owner | `CheatEngineNet`                       |
| Repository       | `CheatEngine.Client`                   |
| Workflow file    | `release.yml`                          |
| Environment      | `nuget`                                |
| Scope            | Push new packages and package versions |
| Package glob     | `CheatEngine.Client*`                  |

The policy belongs to the `CheatEngine` organization of nuget.org, not to a person, so it covers the packages that
organization owns. Its creator must stay an active member of the organization: nuget.org deactivates a policy whose
creator leaves it. The glob covers the seven package ids, including `CheatEngine.Client.Templates`. None of them
exists on nuget.org before 1.0.0, so the scope must allow new packages.

### The `nuget` environment

The GitHub environment `nuget` gates the `publish` job:

- **Deployment branches and tags:** only tags matching `v*.*.*`.
- **Required reviewers:** the maintainers who approve a publication; `publish` waits until one of them approves the
  deployment. While a single maintainer publishes, leave **Prevent self-review** off: the maintainer who pushed the
  tag must be able to approve its deployment.
- **Environment secret `NUGET_USER`:** `AriusII`, the nuget.org profile name of the organization member who created
  the policy (the `user` input of `NuGet/login`), not an e-mail address and not the organization name.

A key obtained through trusted publishing is valid for one hour, and each OIDC token yields one key, so the workflow
logs in once, right before it pushes the seven packages. The login and the pushes stay in the `publish` job of
`release.yml`, because the policy names that file and that environment.

### Tag protection

Add a repository ruleset that targets the tags matching `v*`, with **Restrict updates**, **Restrict deletions** and
**Block force pushes**, so that a pushed release tag keeps its commit. Without a bypass actor, nobody can move, delete
or re-push a release tag, not even to retry a publication that was rejected before any package reached nuget.org
([Re-run a release](#re-run-a-release) describes that case): either add the repository administrators as bypass actors
for it, or release the fix as a new version.

### Immutable releases

Once `release.yml` is on `main`, enable immutable releases in the repository settings. A published release then keeps
its tag and its assets forever, which is why the workflow publishes a release only after it carries every asset. The
workflow runs `gh release verify` and `gh release verify-asset` when the release is immutable, and emits a warning
otherwise.

## Prepare a release

1. Move the entries of `## [Unreleased]` in [CHANGELOG.md](CHANGELOG.md) to a `## [X.Y.Z] - YYYY-MM-DD` section. Its
   body becomes the GitHub release notes; a stable tag fails without it, a prerelease tag falls back to `[Unreleased]`.
   `## [Unreleased]` keeps its four category headings, empty. For 1.0.0, the section summarizes the initial release by
   feature and contract instead of listing every change of the remediation.
2. Ship the public API and the analyzer rules of the release, by hand, in every project that changed:
   - move every entry of each project's `PublicAPI.Unshipped.txt` into its `PublicAPI.Shipped.txt` (keep `*REMOVED*`
     lines), leaving `#nullable enable` first and the file empty otherwise;
   - move the rows of `AnalyzerReleases.Unshipped.md` into a new `## Release X.Y.Z` section of
     `AnalyzerReleases.Shipped.md`.

   Experimental entries keep their `[CECLIENT500x]` prefix in `PublicAPI.Shipped.txt`. For 1.0.0, every Shipped file
   starts empty and receives the whole 1.0.0 surface, as the last API commit of the release pull request.

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
5. Rehearse the pack locally with the version the tag will produce, and inspect the seven packages. The build takes the
   override too, so that the assemblies carry the version of their packages; these are the first commands of the
   qualification run below, and they produce the same rehearsal build:

   ```powershell
   dotnet restore CheatEngine.Client.slnx --locked-mode
   dotnet build CheatEngine.Client.slnx -c Release --no-restore -p:MinVerVersionOverride=X.Y.Z
   Remove-Item artifacts/rehearsal -Recurse -Force -ErrorAction SilentlyContinue
   dotnet pack CheatEngine.Client.slnx -c Release --no-build -o artifacts/rehearsal -p:MinVerVersionOverride=X.Y.Z
   ```

   Never leave `MinVerVersionOverride` set as an environment variable: MinVer reads it from the environment too.
6. Merge the release pull request once `CI / Gate` passes, as the
   [merge policy](CONTRIBUTING.md#merge-policy) says: a squash merge, unless the pull request contains a commit that
   [`.git-blame-ignore-revs`](.git-blame-ignore-revs) lists, which takes a merge commit. The 1.0.0 release pull request
   (#59) contains such commits: merge it with a merge commit, never squash or rebase it, and tag that merge commit.
7. Start a dry run of `release.yml` on the updated `main` (see the end of [Publish](#publish)) and wait until `verify`,
   `ci` and `stage` pass before you push the tag.

## Qualification gate

A Client release is a claim about a tuple: the Client version, the exact `CheatEngine.SDK` package it consumes, that
package's native bridge, the Cheat Engine host profile and the load profile. For 1.0.0 the tuple is the seven 1.0.0
packages, `CheatEngine.SDK` 2.0.0 with the content hash and bridge SHA-256 that the install guides state, and Cheat
Engine 7.7.0.10621 x64 (`cheatengine-x86_64.exe`) loading a managed plugin through hostfxr
(`ce-7.7.0.10621-x64-managed-hostfxr`).

The gate goes through the live qualification runner of
[`tests/CheatEngine.Client.Tests`](tests/CheatEngine.Client.Tests/README.md#live-qualification). It builds the plugins
from the packed release candidates, loads them into a sandboxed copy of Cheat Engine that drives disposable gtutorial
targets, restores the user's Cheat Engine state, and writes redacted receipts and a run summary. That README gives the
prerequisites; the whole run, from the repository root with Cheat Engine, every gtutorial and DebugView closed, is:

```powershell
dotnet restore CheatEngine.Client.slnx --locked-mode
dotnet build CheatEngine.Client.slnx -c Release --no-restore -p:MinVerVersionOverride=X.Y.Z
Remove-Item artifacts/rehearsal -Recurse -Force -ErrorAction SilentlyContinue
dotnet pack CheatEngine.Client.slnx -c Release --no-build -o artifacts/rehearsal -p:MinVerVersionOverride=X.Y.Z
$env:CHEATENGINE_CLIENT_PACKAGE_SOURCE = (Resolve-Path artifacts/rehearsal).Path
$env:CHEATENGINE_CLIENT_LIVE_QUALIFICATION = 'I_AUTHORIZE_CE77_LIVE_PROBES_ON_A_DISPOSABLE_TARGET'
dotnet test --project tests/CheatEngine.Client.Tests/CheatEngine.Client.Tests.csproj -c Release --no-build --filter-trait Category=LiveQualification --filter-not-trait Session=S0
Remove-Item Env:CHEATENGINE_CLIENT_LIVE_QUALIFICATION
```

The command runs the sessions S1 to S6 and leaves out the S0 spike (`Session=S0`), whose receipts are never committed.
The operator stays at the workstation for the Settings > Plugins toggles the runner asks for. A first run finds the
defects; after their fixes, a second run on the committed, clean tree is the one recorded. Its redacted receipts, its
summary and any dated waiver are committed under `tests/CheatEngine.Client.Tests/LiveQualification/Evidence/`. The
summary binds them to the shipping sources through `QualifiedSourceDigest`, so a change to any digest input after that
run requires a new run. The receipts cover the rehearsal build, whose sources have the same digest as the release
commit; they do not carry the hashes of the packages the release workflow builds. Before tagging, the evidence must
show for the tuple:

- the release gate: Q09 (two Client plugins in one Cheat Engine process), Q10 (next to a CheatEngine.SDK 1.x plugin;
  a failure is published as an unsupported mix of SDK majors), Q40 (clean installation of the packages and the
  template on the exact host profile, in addition to the package consumption tests that CI runs on every change), Q43
  (the aggregated cleanup when the operator disables a plugin with a faulty module), Q44 (the policy refusal of an API
  that needs an opt-in), Q45 (sensitive probes change no target byte, process or module) and Q46 (log redaction), each
  with its receipts or an explicit waiver, dated, for what cannot run on the host;
- a green consumer-contract run against the pinned SDK (Q48 at the managed-test level, which CI runs on every change);
- the scenarios of each capability (`ClientCapabilityCatalog`): the qualification gate of a capability requires
  committed evidence that each of its scenarios succeeded without a waiver, and stays `Unknown` without it; an
  experimental id is lifted only when every scenario of its capability succeeds without a waiver (`CECLIENT5001`: Q25
  and Q26; `CECLIENT5002`: Q30.a; `CECLIENT5003`: Q32; `CECLIENT5004`: Q35 and Q44);
- that audit finding F05 (Client and SDK diverge) stays open until `Client.ValueScanning` has the Q25 and Q26
  receipts: the Client composes the consumed `CheatEngine.SDK` value-scan sessions through an experimental adapter
  (`CECLIENT5001`), and closing F05 takes those receipts.

The recorded run's id is written here, and its scenario results go to the `CHANGELOG.md` section of the release, only
from that evidence. Until it is committed, no document claims a host qualification (`QualificationEvidenceTests`). A CI
result is never presented as a host result, and a scenario that was not run stays not run.

## Publish

The release commit must first land on the first-parent history of `main`; the workflow rejects a tag on any other
commit. From an up-to-date `main` checkout:

```powershell
git tag -a vX.Y.Z -m "Release X.Y.Z"
git push origin vX.Y.Z
```

The tag starts [`release.yml`](.github/workflows/release.yml):

```text
verify ─► ci ─► stage ─► attest ─► draft-release ─► publish ─► verify-publication ─► finalize-release
```

The workflow declares the seven package ids once, as `PACKAGE_IDS` in dependency order, and every job derives its list
of packages from it.

1. `verify` checks the SemVer tag, that it points to the first-parent history of `main`, and that none of the seven ids
   already has the version on nuget.org (a version can never be replaced). It extracts the release notes from
   `CHANGELOG.md`.
2. `ci` runs the reusable `ci.yml` on the tag commit with the expected version: it builds and tests Debug and Release,
   packs the tested Release build, fails unless every file is named `<Id>.X.Y.Z.nupkg`, runs the package consumption
   tests on those exact files, and uploads them as the `nuget-packages` artifact.
3. `stage`, with a read-only token and no OIDC token, checks that `nuget-packages` holds exactly the seven packages
   and their symbol packages, extracts the exact bytes of the SPDX 2.2 SBOM each package embeds (after checking that
   it describes that package and version), writes `SHA256SUMS` over the packages, symbol packages and SBOMs, and
   uploads them as the `release-staging` artifact.
4. `attest` checks every staged file against the staged `SHA256SUMS`, then signs one build provenance attestation over
   the seven packages and the five symbol packages, and one SPDX SBOM attestation per package (predicate
   `https://spdx.dev/Document/v2.2`). Before anything is drafted or pushed, it verifies each attestation against its
   bundle and in the repository with the identity of [Verify a release](#verify-a-release), then writes the
   `SHA256SUMS` of the release over every asset.
5. `draft-release` creates a draft release with every asset. Every draft already on the tag (an interrupted run or an
   earlier build) is deleted first with `gh release delete`, which finds a draft by its tag and keeps the git tag, and
   the draft is created again from this run's files, so the release always carries exactly the packages `publish`
   pushes, never a patched-over asset set (that path breaks immutable releases).
6. `publish` waits for a required reviewer to approve the `nuget` deployment. Before it logs in, it checks every
   `.nupkg` and `.snupkg` against `SHA256SUMS`: each one must have the listed SHA-256, and every package the file lists
   must be there. It then logs in through trusted publishing and pushes the seven packages in dependency order
   (Abstractions, Fluent, Core, Extensions.DependencyInjection, Hosting, CheatEngine.Client, Templates) without their
   symbols, and only then the five symbol packages, all with `--skip-duplicate`. A failed symbol push never leaves a
   package unpublished; [Re-run a release](#re-run-a-release) gives the recovery.
7. `verify-publication` resolves the package base address (`PackageBaseAddress/3.0.0`) from the nuget.org service
   index, waits until nuget.org lists the seven versions, then checks each served file: the nuget.org repository
   signature (`dotnet nuget verify --all`), a content hash equal to that of the attested package, and exactly the
   attested entries, byte for byte, plus `.signature.p7s`.
8. `finalize-release` reads the draft with `gh release view` and publishes it with `gh release edit --draft=false`. It
   then downloads the published assets, checks every one of them against `SHA256SUMS`, runs `gh release verify` and
   `gh release verify-asset` when the release is immutable, verifies the provenance and SBOM attestations of the
   downloaded packages with the same identity, and lists the asset hashes in the job summary.

No job of the release path restores from or saves to a NuGet cache. Only `publish` has the `nuget` environment and
reads a secret; only `attest` and `publish` receive an OIDC token; the `contents: write` token of `draft-release`
and `finalize-release` reaches only their `gh` steps, never the restore and test steps.

To rehearse the pipeline without publishing, start `Release` manually (**Actions → Release → Run workflow**), from a
branch or from a tag. Only a tag push of `CheatEngineNet/CheatEngine.Client` releases: for a `workflow_dispatch`,
even one started from a tag, `verify` writes no version, and `attest`, `draft-release` and `publish` (which share one
condition: a push, a tag, this repository and a verified version) are skipped, with every job after them. The dry run
executes `verify`, the full `ci` job and `stage`, which extracts the SBOMs and writes `SHA256SUMS` for the version
MinVer gave the packages, with a read-only token and no OIDC token: the `stage` job summary lists the hashes, and the
`release-staging` artifact holds the files. A dispatch requires the workflow on the default branch, so the first dry
run happens after the remediation branch is merged.

## What a release contains

| Asset                                               | Content                                                                     |
|-----------------------------------------------------|-----------------------------------------------------------------------------|
| `<Id>.X.Y.Z.nupkg` (7)                              | The attested packages; nuget.org serves the same content, repository-signed |
| `<Id>.X.Y.Z.snupkg` (5)                             | Portable PDBs with Source Link, for the five packages with build output     |
| `<Id>.X.Y.Z.spdx.json` (7)                          | The SPDX 2.2 SBOM embedded in each package, extracted byte for byte         |
| `CheatEngine.Client.X.Y.Z.provenance.sigstore.json` | The build provenance bundle of the 7 packages and the 5 symbol packages     |
| `<Id>.X.Y.Z.sbom.sigstore.json` (7)                 | The SBOM attestation bundle of each package                                 |
| `SHA256SUMS`                                        | The SHA-256 of every other asset, in `sha256sum` format                     |

The attestations are also stored in the repository, so `gh attestation verify` needs no bundle. Once immutable releases
are enabled, GitHub adds a release attestation over the tag, its commit and every asset, which `gh release verify` and
`gh release verify-asset` check.

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
$identity = @(
    '--repo', 'CheatEngineNet/CheatEngine.Client',
    '--signer-workflow', 'CheatEngineNet/CheatEngine.Client/.github/workflows/release.yml',
    '--source-ref', 'refs/tags/vX.Y.Z',
    '--deny-self-hosted-runners'
)
foreach ($package in Get-ChildItem *.nupkg, *.snupkg) {
    gh attestation verify $package @identity --predicate-type https://slsa.dev/provenance/v1
}
foreach ($package in Get-ChildItem *.nupkg) {
    gh attestation verify $package @identity --predicate-type https://spdx.dev/Document/v2.2
}

# Once immutable releases are enabled:
gh release verify vX.Y.Z --repo CheatEngineNet/CheatEngine.Client
foreach ($asset in Get-ChildItem *.nupkg, *.snupkg, SHA256SUMS) {
    gh release verify-asset vX.Y.Z $asset --repo CheatEngineNet/CheatEngine.Client
}
```

The identity flags matter: `--signer-workflow` and `--source-ref` accept only an attestation that `release.yml` signed
for that exact tag, and `--deny-self-hosted-runners` only one made on a GitHub-hosted runner, so an attestation made by
another workflow, branch or runner of the repository does not verify. The first loop checks the build provenance of
every package and symbol package; the second checks that the SPDX SBOM attestation (predicate
`https://spdx.dev/Document/v2.2`) belongs to that package. `gh release verify` checks the release attestation of the
tag, and `gh release verify-asset` that each file is an asset of that release; `SHA256SUMS` ties the other assets to
it. The workflow runs the same attestation checks twice: in `attest`, before anything is public, and in
`finalize-release`, on the assets it downloads from the published release.

To tie a plugin to a release, compare its `packages.lock.json` (or the `sha512-…` values of the `CheatEngine.Client*`
and `CheatEngine.SDK` libraries in its deployed `.deps.json`) with the SHA-256 of the attested `.nupkg` and the NuGet
content hash nuget.org serves. `dotnet nuget verify --all <package>` prints the same content hash for a package
downloaded from nuget.org.

## Re-run a release

Use **Re-run failed jobs**. Completed jobs are not repeated and a re-run reuses the artifacts of the original attempt,
so the pushed packages and their attestations stay the same files. `draft-release` always deletes every draft already
on the tag with `gh release delete <tag> --yes` and creates it again from the run's artifacts (gh finds a draft by its
tag, deleting a draft release keeps the git tag, and nothing of a draft is public), so a re-run's draft always matches
that run's files. A push of an existing version is skipped as a duplicate, and a published release never receives
assets: `draft-release` fails before anything is created if the release is already published. `finalize-release`
publishes the draft only if it is still a draft; re-run after the publication, it verifies the published release again.
**Re-run all jobs** after any package reached nuget.org stops in `verify`, because the version is already there.

If `publish` fails while it pushes the symbol packages, the seven packages are already live, and a version on nuget.org
can never be replaced. Re-run only the failed `publish` job (the `nuget` environment asks for approval again): its
`SHA256SUMS` check passes on the same artifacts, the package pushes are skipped as duplicates, and the symbol push runs
again with `--skip-duplicate`, which skips the symbol packages nuget.org already accepted. Never repair symbols with a
new build or a new tag: a new build has different bytes than the packages nuget.org serves.

A new build of the same tag produces different package bytes (the SBOM of each package has a unique namespace and
creation time). This happens when the maintainer rejects or cancels `publish` and then re-pushes the tag, after moving
it or not, or uses **Re-run all jobs** before any package reached nuget.org: the earlier build's draft is deleted and
replaced by `draft-release`, so the release always carries the packages `publish` pushes and `SHA256SUMS` lists.

## Next change after the release

Once the release is public, check the [NuGet packages](https://www.nuget.org/packages/CheatEngine.Client) and the
GitHub release. Then open the next line in one pull request of its own, never in the release pull request (after
1.0.0: the 1.0.0 baseline and the 1.1 floor):

1. Set `CheatEngineClientPackageValidationBaselineVersion` in [Directory.Build.props](Directory.Build.props) to the
   released version (`1.0.0` after the first release), and update the comment of the baseline hook in
   `eng/Shipping.props`, which says that nothing is published yet. Package validation then compares every package with
   the published package of that version and fails the pack on a breaking change. Two consequences are decided in that
   pull request:
   - `EnableStrictModeForBaselineValidation` is on (`eng/Shipping.props`), which makes the comparison an equality
     check: an API that a 1.1 change adds fails the pack too. Either keep strict mode and record each reviewed addition
     in the project's `CompatibilitySuppressions.xml`, or turn strict mode off for the baseline, so that only breaking
     changes fail.
   - The 1.x policy of the [README](README.md#versioning-and-compatibility) allows changes that package validation
     reports even without strict mode: a member added to a call-only interface (`CP0006`), and the removal or change
     of an experimental API (`CP0001`, `CP0002`). Each one carries a reviewed suppression, generated with
     `-p:GenerateCompatibilitySuppressionFile=true`, in the pull request that makes the change.
2. Raise `MinVerMinimumMajorMinor` to the next line (`1.1` after 1.0.0), so later untagged builds become
   `X.(Y+1).0-alpha.0.N`. The evaluation-time `VersionPrefix` follows it and NuGet records it in the project-reference
   entries of the lock files, the three coexistence fixture locks included, so regenerate every lock file in the same
   pull request as [CONTRIBUTING](CONTRIBUTING.md#lock-files) describes: `dotnet restore <project> --force-evaluate`,
   the coexistence fixtures first, one project at a time, never the solution.
3. Reopen `## [Unreleased]` in [CHANGELOG.md](CHANGELOG.md): the changes of the next line go under its four headings,
   and the section of the released version stays as it was released.
4. Switch the NuGet badge at the top of [README.md](README.md) from `nuget/vpre` to `nuget/v`, so that it shows the
   latest stable version.
