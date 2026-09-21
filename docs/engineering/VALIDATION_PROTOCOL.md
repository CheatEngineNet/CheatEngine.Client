# Validation and Release Protocol

## Package validation versus product validation

Run the repository-owned structural gate for the delivered Client manifest and work-item documents:

```powershell
python eng/Validate-EngineeringManifest.py
python -m unittest discover -s eng/tests -v
```

The supplied architecture-review archive has its own independent integrity check; run it from the extracted archive root:

```powershell
python tools/validate_package.py . --require-manifest
python -m unittest discover -s tools/tests -v
```

The repository validator also checks the committed archive-reconciliation inventory: the archive identity, 36 finding IDs, 80 specified scenarios, ownership summary, and references to known Client items. It does not read an archive from a machine-specific path. These checks validate documentation/data/tool integrity only. They do not establish that either .NET repository builds or that a Cheat Engine plugin works. Product validation uses the commands in the repository [README](../../README.md), and acceptance records begin with Not executed until their actual evidence is attached.

## Source and fixture gates

Recheck the exact call path before implementing an inherited audit observation. The process Try failure test must compose the production dispatcher behavior; a fake that catches a different exception cannot validate the seam. Runtime probes must distinguish missing global from a present throwing function. AOB must distinguish a qualified no-match shape from Lua/global/userdata failures. Retained codec contexts must fail before SDK entry after the invocation expires.

Inject failures before native mutation, after native success, before owner publication, during each cleanup stage and after mutation before snapshot refresh. Report partial effects and uncertain cleanup; do not assume false means nothing happened. Two target fixtures must prove that old owners never release into a newly selected process.

## Package and generated consumer gates

Use shipped packages in clean consumers, not sibling project references. Record content hashes and bridge/generator identity. Test minimum and selected later SDK versions; the version range is not a claim that every possible intermediate package was tested. Compile generated code and inspect public nested types rather than relying only on emitted text or shallow reflection tests.

## Live and performance gates

Record host executable, Lua/bridge identity, OS/architecture, target profile and exact plugin artifacts. Include enable/failure/disable/re-enable, target changes and simultaneous plugins. A standalone AOT executable publish is not a native plugin load/unload test. Explicitly retain unavailable support when the live gate is missing.

Benchmark direct SDK, immediate/queued Client, primitive/batch/codec/snapshot/AOB/event and cleanup paths separately. Record managed bytes, native/Lua measures when available, boundary-call counts and latency distributions. Preserve target, cancellation, outcome and ownership semantics. No performance values are supplied by this bootstrap.

## GitHub deployment verification

Read back each issue marker and milestone, all native parents and blocking edges, the two documentation branch trees and draft PRs. The optional Project verifier checks membership, configured fields and saved-view names/layouts/filters. Grouping, roadmap date-field binding and automation remain manual checks. Preserve the receipt when a run stops. A partial run is not transactionally rolled back or relabeled successful.
