# CheatEngine.Client Roadmap

**Planning baseline: September 21, 2026; post-1.0 section written for the 1.0.0 release.** This is an outcome-based plan, not a delivery-date commitment. It does not imply product implementation, package publication, or live-host qualification.

## Operating boundary

SDK owns CE integration; Client owns developer-facing workflows. Independent reliability fixes need not wait for all SDK research. Source merged, package shipped, fixture passed and host qualified are separate gates.

## After 1.0.0

1.0.0 is the first release; it consumes CheatEngine.SDK 2.0.0. Nothing below has a date or breaks 1.x: a new public API arrives in a 1.x minor release, under the versioning rules of the [README](README.md#versioning-and-compatibility), and a new CheatEngine.SDK major means a Client 2.0.

### Domains awaiting CheatEngine.SDK primitives

1.0 has no public contract, not even a gated placeholder, for these Cheat Engine features, because no CheatEngine.SDK primitive owns them: timers; hotkeys; the debugger and breakpoints; the speed hack; target-memory and file hashing; DBVM; remote execution and DLL injection; pausing, resuming or creating a process, and attaching to the foreground process; assembly comments; detaching from a process. Each one can arrive in a 1.x minor release once the consumed CheatEngine.SDK owns it (target binding, cleanup and failure semantics) and its live qualification scenarios exist. The primitive is requested in the CheatEngine.SDK repository first.

### CheatEngine.SDK requests

| Request | What the Client needs it for | Until then |
|---|---|---|
| A child-count getter on `MemoryRecord` | `MemoryRecordStateSnapshot.ChildCount` from a typed SDK getter | `TableClient` reads the `Count` property through the untyped `CEObject.TryGetProperty`, the one `AwaitingSdkPrimitive` entry of the architecture ratchet, because `MemoryRecord.TryGetChild(int)` cannot tell an index out of range from a failed read |
| A read status for the `Script` of a memory record | `MemoryRecordContentSnapshot.Script` that tells a record without a script from a failed read | `null` covers both, and a failed `Script` read does not fail the snapshot |
| A protected chunk-execution service | Caller-supplied Lua under the SDK's protection | `UnsafeLuaClient` stays the permanent entry of the ratchet, behind the `EnableUnsafeLuaExecution` opt-in |

### Leaving experimental

Each experimental id leaves experimental once every scenario its capability requires succeeds on the exact host tuple, without a waiver ([RELEASING](RELEASING.md#qualification-gate)). An id lifted before 1.0.0 ships stable in 1.0.0; an id still experimental then is lifted in a 1.x minor release, and until that release its API can change or be removed in a minor release.

| Id | API | Scenarios |
|---|---|---|
| `CECLIENT5001` | Value scans (`IValueScanner`) | Q25, Q26 |
| `CECLIENT5002` | Target allocations (`IAllocationClient`) | Q30.a |
| `CECLIENT5003` | Single-instruction assembly and disassembly (`IAssemblyClient`) | Q32 |
| `CECLIENT5004` | Auto Assembler patches (`IAutoAssemblerClient`, registered by `EnableAutoAssemblerPatches()`) | Q35, Q44 |

### Lua operations

Generated Lua operations take scalar inputs and return one result in 1.0 (`CECLUA1103`). CheatEngine.SDK 2.0.0's `LuaOptional<T>` lets a binding omit a trailing argument (`LUA_TNONE`, not an explicit `nil`) and read the number of results Lua actually returned; the Client adopts it once it has a contract for an omitted input and an absent result.

### Benchmarks

The facade-versus-direct-SDK comparison (audit item A24-26) is deferred: `tests/CheatEngine.Client.Benchmarks` measures the Client over in-process fakes, and comparing a Client operation with the same direct CheatEngine.SDK call needs a hosted Cheat Engine.

## Milestones

These milestones and epics are the pre-1.0 plan. Their ids stay stable for the issues that reference them; the [CHANGELOG](CHANGELOG.md) says what 1.0.0 shipped.

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
| CLI-001 — Reconcile Client audit evidence with source and consumed packages | P1 | Refinement and evidence; no declared issue blocker |
| CLI-002 — Separate capability implementation, host evidence, and policy | P1 | CLI-001 |
| CLI-003 — Adopt Client governance, roadmap, and issue-to-PR workflow | P1 | Refinement and evidence; no declared issue blocker |

### CLI-E02 — Immediate reliability corrections
Repair classified failures, retained contexts, and partial activation cleanup independently of broad SDK expansion.

| Work item | Priority | Prerequisites |
|---|---|---|
| CLI-004 — Align expected process failures with production dispatcher semantics | P1 | Refinement and evidence; no declared issue blocker |
| CLI-005 — Expire memory-codec contexts after each invocation | P1 | Refinement and evidence; no declared issue blocker |
| CLI-006 — Complete all activation rollback stages and preserve original errors | P1 | Refinement and evidence; no declared issue blocker |

### CLI-E03 — SDK contract adoption
Replace duplicated built-in CE interpretation with the actual containing SDK artifacts.

| Work item | Priority | Prerequisites |
|---|---|---|
| CLI-007 — Replace built-in CE Lua declarations with SDK semantic operations | P1 | SDK-008, SDK-023 |
| CLI-008 — Preserve SDK failure provenance and partial effect information | P1 | SDK-007, SDK-023 |
| CLI-009 — Bind Client workflows to authoritative SDK target identity | P1 | SDK-010, SDK-011, SDK-023 |

### CLI-E04 — Modules, public boundaries, and activation composition
Keep application extensibility safe without leaking raw SDK state or duplicating registration mechanisms.

| Work item | Priority | Prerequisites |
|---|---|---|
| CLI-010 — Use SDK registration leases for generated Lua modules | P1 | SDK-009, SDK-022, SDK-023, CLI-006 |
| CLI-011 — Enforce recursive public and generated API type boundaries | P1 | Refinement and evidence; no declared issue blocker |
| CLI-012 — Specify activation-local DI and multi-plugin composition | P2 | CLI-006, SDK-005 |

### CLI-E05 — Memory, scan, and table workflows
Preserve cardinality, partial effects, limits and target consistency across friendly APIs.

| Work item | Priority | Prerequisites |
|---|---|---|
| CLI-013 — Make AOB cardinality and result budgets truthful | P1 | SDK-014, SDK-023, CLI-008, CLI-009 |
| CLI-014 — Report memory batch effects and cancellation milestones | P1 | SDK-013, CLI-008, CLI-009, CLI-005 |
| CLI-015 — Adopt typed record and symbol commands with safe workflow policy | P1 | SDK-021, SDK-023, CLI-008, CLI-009 |

### CLI-E06 — Qualified advanced workflow adoption
Enable scan, allocation, patch and event workflows only after their specific lower-layer gates.

| Work item | Priority | Prerequisites |
|---|---|---|
| CLI-016 — Adopt qualified SDK value-scan sessions | P2 | SDK-015, SDK-023, CLI-009, CLI-002 |
| CLI-017 — Adopt target-bound allocation and patch leases | P1 | SDK-011, SDK-017, SDK-023, CLI-009, CLI-008 |
| CLI-018 — Define gated debugger and subscription workflow adapters | P2 | SDK-018, SDK-019, CLI-019, CLI-009 |

### CLI-E07 — Developer experience and bounded observation
Define coherent lifetime, reader, completion and discoverability policies.

| Work item | Priority | Prerequisites |
|---|---|---|
| CLI-019 — Bound event consumers and specify stream completion | P2 | Refinement and evidence; no declared issue blocker |
| CLI-020 — Standardize fluent terminals and supported workflow examples | P2 | CLI-002, CLI-013 |
| CLI-021 — Unify stale-client and local-process operation semantics | P2 | Refinement and evidence; no declared issue blocker |

### CLI-E08 — Package, deployment, and release readiness
Validate actual packages, external consumers, deployment sets and measured end-to-end behavior.

| Work item | Priority | Prerequisites |
|---|---|---|
| CLI-022 — Validate minimum SDK artifacts and public compatibility | P1 | SDK-023, CLI-007, CLI-010, CLI-011 |
| CLI-023 — Verify coherent deployment sets and external plugin inheritance | P2 | CLI-022 |
| CLI-024 — Publish end-to-end qualification and performance budgets | P2 | CLI-022, CLI-023, SDK-024 |

## Execution notes

A phase is an outcome grouping, not a global lock. A research task can proceed while an unrelated reliability fix ships. The graph specifies technical prerequisites; it does not estimate capacity. Before Client adoption, identify a package containing every required SDK primitive even when a prior SDK minimum-contract release is already complete.

Create a branch only when a leaf is ready. Keep SDK and Client PRs separate; publish the containing SDK artifact before declaring dependent Client behavior supported. Parent issue closure is never inferred from a single child PR.
