<!-- ce-bootstrap:CLI-E08 -->

## CLI-E08 — Package, deployment, and release readiness

**Owner:** `CheatEngineNet/CheatEngine.Client` · **Kind:** epic · **Priority:** P1 · **Milestone:** CLI-M6

**Initial status:** Needs refinement. **Runtime validation:** Not executed by this bootstrap.

### Context and evidence

This grouping coordinates the linked implementation issues. Existing capabilities are credited by each child rather than redeclared as missing.

**Evidence classification:** Proposal derived from the attached summaries and pinned source references.

### Outcome and rationale

Validate actual packages, external consumers, deployment sets and measured end-to-end behavior.

**Expected benefit:** Validate actual packages, external consumers, deployment sets and measured end-to-end behavior.

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

- https://github.com/CheatEngineNet/CheatEngine.Client/issues/38
- https://github.com/CheatEngineNet/CheatEngine.Client/issues/39
- https://github.com/CheatEngineNet/CheatEngine.Client/issues/40

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

- [C19 — Prior audit: SDK 1.0.0 resolution; artifact bytes not independently inspected.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/libs/CheatEngine.Client.Core/packages.lock.json)
- [C20 — Prior audit: approved SDK values versus broad compile/runtime dependency.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/libs/CheatEngine.Client.Abstractions/CheatEngine.Client.Abstractions.csproj)
- [C01 — Current in-process architecture, capability gates and managed deployment.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/README.md)
- [N03 — Source/binary/behavioral compatibility distinctions.](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/breaking-changes)
- [C17 — Prior audit: build-only native calls, external-base checks and per-file deployment.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/libs/CheatEngine.Client.Hosting/buildTransitive/CheatEngine.Client.Hosting.targets)
- [S11 — Protected native boundary and bridge contract reference.](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/aa3fcc3cdf629468e69d0c68817183d44a719894/native/cheatengine-sdk-lua-bridge/README.md)
- [C21 — Live gate protocol identified by current README; not independently re-read here.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/docs/live-capability-gates.md)
- [C03 — Prior audit: scoped codecs, partial batches and failure projection.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/libs/CheatEngine.Client.Core/Domains/MemoryClient.cs)
- [C15 — Prior audit: bounded event ring, pending reader list and async continuations.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/libs/CheatEngine.Client.Core/Domains/Events/BoundedEventStream.cs)
- [S13 — Live fixture entry point, identified in contribution guidance.](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/aa3fcc3cdf629468e69d0c68817183d44a719894/tests/CheatEngine.SDK.LivePlugin/README.md)

### Maintainer notes

Keep execution updates, actual commands/results, decisions, and refinements here. The bootstrap does not overwrite an existing issue body on rerun.
