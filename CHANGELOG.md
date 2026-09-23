# Changelog

All notable changes to the CheatEngine.Client packages are documented in this file. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).
The seven packages (`CheatEngine.Client`, `.Abstractions`, `.Core`, `.Extensions.DependencyInjection`, `.Fluent`,
`.Hosting` and `.Templates`) always share one version.

No version has been published yet. Earlier builds packed `0.1.0` from source, but no `CheatEngine.Client*` package
exists on nuget.org, so this file starts at the first release.

Each release separates four kinds of change, because a consumer reacts to each differently:

- **Added**: an extension, such as a new API, option or package asset. Existing code keeps its meaning.
- **Changed**: a semantic correction. An existing API returns, throws, cancels or cleans up differently, even when its
  signature is unchanged.
- **Security**: a hardening of refusals. An operation that used to proceed is now refused, or refused earlier.
- **Deployment**: a change to what is built, packed, pinned, signed or published, and to how a plugin is deployed.

## [Unreleased]

### Added

### Changed

### Security

- A plugin that references `CheatEngine.SDK` 2.0 or later directly next to this Client now fails to build with
  `CECLIENT017`. `CheatEngineClientAllowUnsupportedSdk=true` turns the error into a warning; such a plugin is
  unsupported and is expected to break at run time.

### Deployment

- Every Client package is versioned by MinVer from `v*` tags, in lockstep: untagged builds are
  `0.1.0-alpha.0.<height>`, and the assembly version keeps the major and minor numbers (`0.1.0.0`).
- The Client consumes exactly `CheatEngine.SDK` 1.0.0 and declares `[1.0.0, 2.0.0)`. The pin has a single source,
  `eng/CheatEngineSdk.props`, and a reviewed identity, `eng/sdk/consumed-sdk.json`. The build refuses a 2.x or
  prerelease pin (`CHEATENGINECLIENT9016`).
- The `ceplugin` template references the exact `CheatEngine.Client` version it was packed with and the pinned
  `CheatEngine.SDK`, and the template is validated when it is built.
- Every package embeds an SPDX 2.2 software bill of materials at `_manifest/spdx_2.2/manifest.spdx.json`, has its own
  description, and links the project, the license and this changelog. The redundant `Microsoft.SourceLink.GitHub`
  package reference is removed: the .NET SDK provides Source Link.
- A release workflow publishes the seven packages from a version tag through NuGet trusted publishing, after build
  provenance and SBOM attestations, into a draft GitHub release that carries a Client release tuple.
