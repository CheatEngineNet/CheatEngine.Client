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

- `CheatEngineFailure.HostEffect` states how far the Cheat Engine primitive got before a failure: `NotStarted`,
  `Started` (effects may persist), `Completed`, `CleanupUnconfirmed` (a resource or change may remain) or `Unknown`.
  `CheatEngineFailureKind.IndeterminateHostResult` reports a Cheat Engine result that the Client cannot classify.
- `IPatternScanOutcomeClient.ScanDetailed` returns the classification of `TryScan` with `PatternScanMetrics`: the
  number of matches Cheat Engine found, the examined, filtered-out and copied counts, the `PatternScanScope`, and the
  Cheat Engine scan time separately from the copy time.

### Changed

- An AOB scan for which Cheat Engine returns no result list fails with `IndeterminateHostResult` instead of
  `OperationRejected`: the Client's scan route calls the boolean `AobScanner.TryScan`, so zero matches and a host
  failure cannot be told apart. It is never reported as `null` or `NotFound`.
- `Try...` methods no longer let a CheatEngine.SDK exception escape. An SDK fault raised by Client-internal work
  becomes a `CheatEngineFailure` chosen by exception type, and an SDK fault after the activation ended throws
  `CheatEngineActivationExpiredException`. Exceptions from your own callbacks, codecs and Lua operations are still
  rethrown unchanged.
- A batch write cancelled before dispatch reports `MemoryBatchWriteEffectState.NotStarted`. The cancellation
  documentation now says what a token does: it is observed before dispatch and between Client-managed steps, and it
  never interrupts a Cheat Engine call that has started.
- An AOB result list is released exactly once on every path. When the release fails, the copied results are discarded
  and the scan fails with `InvalidState` and `CleanupUnconfirmed`.
- Core cleanup attempts every release in reverse order and reports every failure: a single failure is rethrown as the
  same instance, several are aggregated. A failed Address List record creation destroys the partial record once and
  reports `CleanupUnconfirmed` when that rollback is not confirmed. Hosting logs each failed cleanup stage.
- `CheatEngineFailure.ToString()` returns only the kind, the operation and the host effect; it no longer includes
  `Message` or `Exception`, which can carry addresses, expressions, paths or Lua text.
- The documentation of `InModule`, `InRange`, `Take`, `FirstOrNone` and `RequireSingle` states that they filter and
  bound the copy of one global Cheat Engine scan; they never reduce the scan itself.

### Security

- The `ceplugin` template no longer logs the AOB match address or whole failures; it logs the failure kind, operation
  and host effect only. A test rejects any logging event of the Client packages whose parameters could carry an
  address, expression, path, script, message, exception or failure.
- A plugin that references `CheatEngine.SDK` 3.0 or later directly next to this Client now fails to build with
  `CECLIENT017`. `CheatEngineClientAllowUnsupportedSdk=true` turns the error into a warning; such a plugin is
  unsupported and is expected to break at run time. A direct reference below 2.0.0 fails the restore with `NU1605`.

### Deployment

- Every Client package is versioned by MinVer from `v*` tags, in lockstep: untagged builds are
  `1.0.0-alpha.0.<height>`, and the assembly version carries the major number only (`1.0.0.0`).
- The Client consumes exactly `CheatEngine.SDK` 2.0.0 and declares `[2.0.0, 3.0.0)`. The pin has a single source,
  `eng/CheatEngineSdk.props`. The build refuses a pin outside 2.x or a prerelease pin (`CHEATENGINECLIENT9016`).
- The `ceplugin` template references the exact `CheatEngine.Client` version it was packed with and the pinned
  `CheatEngine.SDK`, and the template is validated when it is built.
- Every package embeds an SPDX 2.2 software bill of materials at `_manifest/spdx_2.2/manifest.spdx.json`, has its own
  description, and links the project, the license and this changelog. The redundant `Microsoft.SourceLink.GitHub`
  package reference is removed: the .NET SDK provides Source Link.
- A release workflow publishes the seven packages from a version tag through NuGet trusted publishing, after build
  provenance and SBOM attestations, into a draft GitHub release that carries a Client release tuple.
- CI builds and tests every change in Debug and Release with exactly .NET SDK 10.0.401 and locked restores, tests the
  packages that its Release leg packs (and that a release publishes) instead of packing them again, and requires the
  lock-file guard, the NuGet audit policy (high and critical advisories fail every build), formatting and the workflow
  security checks to pass before `CI / Gate` does.
