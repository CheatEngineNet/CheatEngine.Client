<!-- ce-bootstrap:CLI-E05 -->

## CLI-E05 — Memory, scan, and table workflows

**Owner:** `CheatEngineNet/CheatEngine.Client` · **Kind:** epic · **Priority:** P1 · **Milestone:** CLI-M4

**Initial status:** Needs refinement. **Runtime validation:** Not executed by this bootstrap.

### Context and evidence

This grouping coordinates the linked implementation issues. Existing capabilities are credited by each child rather than redeclared as missing.

**Evidence classification:** Proposal derived from the attached summaries and pinned source references.

### Outcome and rationale

Preserve cardinality, partial effects, limits and target consistency across friendly APIs.

**Expected benefit:** Preserve cardinality, partial effects, limits and target consistency across friendly APIs.

### Architectural responsibility

SDK owns CE mappings, native safety, factual outcomes and low-level owners. Client owns application policy, typed workflows, composition, and developer experience.

### Technical requirements and task checklist

- [ ] Review child source evidence and preserve the SDK/Client responsibility boundary.
- [ ] Sequence only real dependencies; do not block a child on closing its own parent.
- [ ] Require an explicit decision and evidence for any deferred capability.

### Acceptance criteria

- [ ] Each child is completed with evidence or explicitly deferred by an approved decision.
- [ ] Cross-repository artifact gates have named versions and hashes when adopted.
- [ ] Compatibility and support documentation match actual delivery, not interface count.

### Required validation

- [ ] Review the acceptance evidence of each child; do not count parent closure as an additional runtime test.

### Scope exclusions

- No monolithic epic implementation PR.

### Parent and children

Parent: https://github.com/CheatEngineNet/CheatEngine.Client/issues/8

- https://github.com/CheatEngineNet/CheatEngine.Client/issues/29
- https://github.com/CheatEngineNet/CheatEngine.Client/issues/30
- https://github.com/CheatEngineNet/CheatEngine.Client/issues/31

### Blocked by

None declared. This does not waive evidence, policy, or package requirements.

### Blocks

None declared. This does not waive evidence, policy, or package requirements.

### Package and readiness gate

Identify the first containing artifact before describing new source as consumable support. No version is invented by this plan.

### Proposed branch and PR

No epic-sized implementation branch. Children have focused branch/PR proposals. The bootstrap creates only one documentation/tooling branch and draft PR per repository.

### Risks and compatibility

A grouping can span several phases. Its completion milestone is not a reason to delay an earlier independent child.

**Long-term impact:** One traceable owner, stable acceptance criteria, and explicit compatibility evidence reduce repeated integration fixes and make future API changes reviewable.

### Definition of done

- [ ] Linked child PRs, tests, and a final scope/deferral review.

Outcome group; optional scope remains explicitly gated or deferred.

### Sources

- [C10 — Prior audit: whole scan precedes module lookup and post-filtering.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/libs/CheatEngine.Client.Core/Domains/PatternScanner.cs)
- [C11 — Prior audit: SDK delegation but ambiguous false becomes rejection.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/libs/CheatEngine.Client.Core/Domains/SdkAobScanPort.cs)
- [C16 — Prior audit: immutable terminals, post-filter limits, readability naming.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/libs/CheatEngine.Client.Fluent/Scanning/AobScanBuilder.cs)
- [C03 — Prior audit: scoped codecs, partial batches and failure projection.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/libs/CheatEngine.Client.Core/Domains/MemoryClient.cs)
- [C05 — Prior audit: production adapter rethrows callback exceptions.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/libs/CheatEngine.Client.Core/Dispatching/SdkMainThreadDispatcher.cs)
- [C01 — Current in-process architecture, capability gates and managed deployment.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/README.md)
- [C12 — Prior audit: Parent/destroy semantics and mutation/snapshot ambiguity.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/libs/CheatEngine.Client.Core/Domains/SdkTableRecordMutationPort.cs)
- [C18 — Prior audit: table-root allowlist and documented path TOCTOU limitation.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/libs/CheatEngine.Client.Core/Infrastructure/CoreClientPolicy.cs)
- [C02 — Prior audit: built-in CE Lua declarations remain Client-owned.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/libs/CheatEngine.Client.Core/Infrastructure/ClientLuaGlobals.cs)

### Maintainer notes

Keep execution updates, actual commands/results, decisions, and refinements here. The bootstrap does not overwrite an existing issue body on rerun.
