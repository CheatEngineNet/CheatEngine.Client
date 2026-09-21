# CheatEngine.Client Roadmap

**Planning baseline: September 21, 2026.** This is an outcome-based plan, not a delivery-date commitment. The initial preparation attempt was denied GitHub writes; the later operator-authorized deployment is recorded in [the engineering evidence receipt](docs/engineering/EVIDENCE_AND_LIMITS.md). The roadmap still does not imply product implementation, package publication, or live-host qualification.

## Operating boundary

SDK owns CE integration; Client owns developer-facing workflows. Independent reliability fixes need not wait for all SDK research. Source merged, package shipped, fixture passed and host qualified are separate gates.

## Milestones

| Phase | Outcome | Exit evidence |
|---|---|---|
| CLI-M0 | Governance and capability truth | Public status separates implementation, artifact, host qualification and policy. |
| CLI-M1 | Reliability and activation integrity | No-target, retained-codec and construction-rollback tests match production semantics. |
| CLI-M2 | SDK boundary adoption | Built-in integration delegates to shipped SDK contracts with preserved outcomes. |
| CLI-M3 | Modules and public API boundaries | Generated public contracts contain no raw state/owner escape; activation/module lifetime tested. |
| CLI-M4 | Workflow and fluent correctness | Cardinality, partial effects, budgets and fluent names are truthful and documented. |
| CLI-M5 | Evidence-gated advanced workflows | Only workflows with target/ownership/cleanup evidence are enabled. |
| CLI-M6 | Packaging and developer readiness | Clean consumer/template/deployment/live results independently recorded. |

## Epics and implementable work

### CLI-E01 — Governance and capability truth
Maintain the developer-facing support model and evidence-linked execution plan.

| Work item | Priority | Prerequisites |
|---|---|---|
| [CLI-001](docs/engineering/work-items/CLI-001.md) — Reconcile Client audit evidence with source and consumed packages | P1 | Refinement and evidence; no declared issue blocker |
| [CLI-002](docs/engineering/work-items/CLI-002.md) — Separate capability implementation, host evidence, and policy | P1 | CLI-001 |
| [CLI-003](docs/engineering/work-items/CLI-003.md) — Adopt Client governance, roadmap, and issue-to-PR workflow | P1 | Refinement and evidence; no declared issue blocker |

### CLI-E02 — Immediate reliability corrections
Repair classified failures, retained contexts, and partial activation cleanup independently of broad SDK expansion.

| Work item | Priority | Prerequisites |
|---|---|---|
| [CLI-004](docs/engineering/work-items/CLI-004.md) — Align expected process failures with production dispatcher semantics | P1 | Refinement and evidence; no declared issue blocker |
| [CLI-005](docs/engineering/work-items/CLI-005.md) — Expire memory-codec contexts after each invocation | P1 | Refinement and evidence; no declared issue blocker |
| [CLI-006](docs/engineering/work-items/CLI-006.md) — Complete all activation rollback stages and preserve original errors | P1 | Refinement and evidence; no declared issue blocker |

### CLI-E03 — SDK contract adoption
Replace duplicated built-in CE interpretation with the actual containing SDK artifacts.

| Work item | Priority | Prerequisites |
|---|---|---|
| [CLI-007](docs/engineering/work-items/CLI-007.md) — Replace built-in CE Lua declarations with SDK semantic operations | P1 | SDK-008, SDK-023 |
| [CLI-008](docs/engineering/work-items/CLI-008.md) — Preserve SDK failure provenance and partial effect information | P1 | SDK-007, SDK-023 |
| [CLI-009](docs/engineering/work-items/CLI-009.md) — Bind Client workflows to authoritative SDK target identity | P1 | SDK-010, SDK-011, SDK-023 |

### CLI-E04 — Modules, public boundaries, and activation composition
Keep application extensibility safe without leaking raw SDK state or duplicating registration mechanisms.

| Work item | Priority | Prerequisites |
|---|---|---|
| [CLI-010](docs/engineering/work-items/CLI-010.md) — Use SDK registration leases for generated Lua modules | P1 | SDK-009, SDK-022, SDK-023, CLI-006 |
| [CLI-011](docs/engineering/work-items/CLI-011.md) — Enforce recursive public and generated API type boundaries | P1 | Refinement and evidence; no declared issue blocker |
| [CLI-012](docs/engineering/work-items/CLI-012.md) — Specify activation-local DI and multi-plugin composition | P2 | CLI-006, SDK-005 |

### CLI-E05 — Memory, scan, and table workflows
Preserve cardinality, partial effects, limits and target consistency across friendly APIs.

| Work item | Priority | Prerequisites |
|---|---|---|
| [CLI-013](docs/engineering/work-items/CLI-013.md) — Make AOB cardinality and result budgets truthful | P1 | SDK-014, SDK-023, CLI-008, CLI-009 |
| [CLI-014](docs/engineering/work-items/CLI-014.md) — Report memory batch effects and cancellation milestones | P1 | SDK-013, CLI-008, CLI-009, CLI-005 |
| [CLI-015](docs/engineering/work-items/CLI-015.md) — Adopt typed record and symbol commands with safe workflow policy | P1 | SDK-021, SDK-023, CLI-008, CLI-009 |

### CLI-E06 — Qualified advanced workflow adoption
Enable scan, allocation, patch and event workflows only after their specific lower-layer gates.

| Work item | Priority | Prerequisites |
|---|---|---|
| [CLI-016](docs/engineering/work-items/CLI-016.md) — Adopt qualified SDK value-scan sessions | P2 | SDK-015, SDK-023, CLI-009, CLI-002 |
| [CLI-017](docs/engineering/work-items/CLI-017.md) — Adopt target-bound allocation and patch leases | P1 | SDK-011, SDK-017, SDK-023, CLI-009, CLI-008 |
| [CLI-018](docs/engineering/work-items/CLI-018.md) — Define gated debugger and subscription workflow adapters | P2 | SDK-018, SDK-019, CLI-019, CLI-009 |

### CLI-E07 — Developer experience and bounded observation
Define coherent lifetime, reader, completion and discoverability policies.

| Work item | Priority | Prerequisites |
|---|---|---|
| [CLI-019](docs/engineering/work-items/CLI-019.md) — Bound event consumers and specify stream completion | P2 | Refinement and evidence; no declared issue blocker |
| [CLI-020](docs/engineering/work-items/CLI-020.md) — Standardize fluent terminals and supported workflow examples | P2 | CLI-002, CLI-013 |
| [CLI-021](docs/engineering/work-items/CLI-021.md) — Unify stale-client and local-process operation semantics | P2 | Refinement and evidence; no declared issue blocker |

### CLI-E08 — Package, deployment, and release readiness
Validate actual packages, external consumers, deployment sets and measured end-to-end behavior.

| Work item | Priority | Prerequisites |
|---|---|---|
| [CLI-022](docs/engineering/work-items/CLI-022.md) — Validate minimum SDK artifacts and public compatibility | P1 | SDK-023, CLI-007, CLI-010, CLI-011 |
| [CLI-023](docs/engineering/work-items/CLI-023.md) — Verify coherent deployment sets and external plugin inheritance | P2 | CLI-022 |
| [CLI-024](docs/engineering/work-items/CLI-024.md) — Publish end-to-end qualification and performance budgets | P2 | CLI-022, CLI-023, SDK-024 |

## Execution notes

A phase is an outcome grouping, not a global lock. A research task can proceed while an unrelated reliability fix ships. The graph specifies technical prerequisites; it does not estimate capacity. Before Client adoption, identify a package containing every required SDK primitive even when a prior SDK minimum-contract release is already complete.

Create a branch only when a leaf is ready. Keep SDK and Client PRs separate; publish the containing SDK artifact before declaring dependent Client behavior supported. Parent issue closure is never inferred from a single child PR.
