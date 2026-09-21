# Architecture-Review Archive Reconciliation

## Purpose and evidence boundary

This document makes the supplied `CheatEngineNet_Architecture_Review_2026-09-21.zip` usable as a bounded, reproducible planning input. Its machine-readable counterpart is [archive-reconciliation.json](archive-reconciliation.json), which the repository validator checks.

The archive is context. It is not a source checkout, a restored NuGet package, a generated consumer, or a Cheat Engine live run. Consequently, a capability can be source-observed while still failing the package or live gate. For example, the reviewed SDK source at `aa3fcc3cdf629468e69d0c68817183d44a719894` contains the later `MemoryScanSessions.TryCreate` work, whereas published `CheatEngine.SDK` 1.0.0 does not; [ADR 0004](../adr/0004-capability-matrix.md) keeps Client publication gated accordingly.

## Archive identity and inventory

| Item | Verified value |
|---|---|
| Archive file | `CheatEngineNet_Architecture_Review_2026-09-21.zip` |
| Archive SHA-256 | `fc1f178916ec14fb993e405018112ddedd8dc316a86aa6fe9251b42c09547802` |
| Internal content manifest | `SHA256SUMS.json` |
| Internal manifest SHA-256 | `a81aa3e52362eab942a4e1d211502958d70cb240f263072d6e39b87920440680` |
| Findings register | 36 canonical IDs, `R01`–`R36` |
| Qualification plan | 80 scenarios, all specified rather than executed |
| Ownership matrix | 32 rows |
| Capability matrix | 17 rows |

The archive was independently checked with its supplied `tools/validate_package.py --require-manifest` command and Python test suite. The committed validator checks the recorded identity and complete finding crosswalk; it intentionally does not expect the archive to be present at a particular local path.

## Findings-to-Client crosswalk

The following groups cover each canonical finding exactly once. They point to the stable Client plan IDs rather than claim that a historical issue, ADR, or test ID has been recreated.

| Group | Findings | Client execution items |
|---|---|---|
| Evidence and delivery | R17, R18, R19, R31, R32, R35, R36 | CLI-001, CLI-002, CLI-003, CLI-022, CLI-023, CLI-024 |
| Immediate corrections | R02, R03, R04, R12, R21 | CLI-004, CLI-005, CLI-006, CLI-021 |
| SDK contracts | R01, R08, R09, R10, R16 | CLI-007, CLI-008, CLI-010, CLI-011 |
| Identity and owners | R05, R06, R07, R20, R27 | CLI-009, CLI-016, CLI-017 |
| Bounded workflows | R11, R13, R14, R22, R28, R29 | CLI-013, CLI-014, CLI-015, CLI-020 |
| Advanced qualification | R15, R23, R24, R25, R26, R30, R33, R34 | CLI-012, CLI-018, CLI-019, CLI-023, CLI-024 |

## Ownership and qualification sequence

The archive ownership matrix assigns CE mappings, ABI safety, Lua protection, and low-level native owners to the SDK. Client owns activation policy, DI composition, typed workflows, and fluent public ergonomics. Package compatibility and live qualification are shared: the SDK supplies a supportable artifact and the Client verifies its actual consumer and plugin-host behavior.

The planning order follows the archive roadmap: establish package and host identity; make process/dispatcher/rollback deterministic; consume the SDK contracts; add the higher-level workflows; qualify retained-resource ownership; then validate callbacks, deployment, multi-plugin behavior, and performance. The [cross-repository sequence](CROSS_REPOSITORY_SEQUENCE.md) and work-item dependency graph express the actionable edges.

## Source, package, fixture, and live evidence

| Evidence level | What it can establish | What it cannot establish |
|---|---|---|
| Source | An exact reviewed commit contains a code path or contract. | That a released package contains it or that a host can use it. |
| Package | The exact nupkg and generated consumer expose the qualified contract. | That it works with the selected Cheat Engine host. |
| Fixture | The declared controlled fixture exercised the stated behavior. | Broader host/version compatibility. |
| Live | The named CE 7.7 host and artifact passed the recorded scenario. | A universal compatibility or security guarantee. |

No level may be silently promoted. The 80 archive scenarios remain `Specified_Not_Executed` until their actual environment, command, result, and cleanup evidence are recorded. See [the validation protocol](VALIDATION_PROTOCOL.md) for the repository and live qualification gates.
