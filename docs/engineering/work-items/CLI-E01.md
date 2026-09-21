<!-- ce-bootstrap:CLI-E01 -->

## CLI-E01 — Governance and capability truth

**Owner:** `CheatEngineNet/CheatEngine.Client` · **Kind:** epic · **Priority:** P1 · **Milestone:** CLI-M0

**Initial status:** Needs refinement. **Runtime validation:** Not executed by this bootstrap.

### Context and evidence

This grouping coordinates the linked implementation issues. Existing capabilities are credited by each child rather than redeclared as missing.

**Evidence classification:** Proposal derived from the attached summaries and pinned source references.

### Outcome and rationale

Maintain the developer-facing support model and evidence-linked execution plan.

**Expected benefit:** Maintain the developer-facing support model and evidence-linked execution plan.

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

- https://github.com/CheatEngineNet/CheatEngine.Client/issues/17
- https://github.com/CheatEngineNet/CheatEngine.Client/issues/18
- https://github.com/CheatEngineNet/CheatEngine.Client/issues/19

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

- [C01 — Current in-process architecture, capability gates and managed deployment.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/README.md)
- [C19 — Prior audit: SDK 1.0.0 resolution; artifact bytes not independently inspected.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/libs/CheatEngine.Client.Core/packages.lock.json)
- [H02 — Merged high-level API/modules; preserve this work.](https://github.com/CheatEngineNet/CheatEngine.Client/pull/7)
- [H01 — Merged foundations; author-reported tests are not independently executed.](https://github.com/CheatEngineNet/CheatEngine.SDK/pull/19)
- [C09 — Prior audit: capability and exception-family mismatch.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/libs/CheatEngine.Client.Core/Domains/RuntimeClient.cs)
- [C14 — Prior audit: provider-local singleton services and unavailable adapters.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/libs/CheatEngine.Client.Extensions.DependencyInjection/CheatEngineClientServiceCollectionExtensions.cs)
- [G01 — Issue API; filter pull_request entries from issue lists.](https://docs.github.com/en/rest/issues/issues)
- [G02 — Repository-specific milestones.](https://docs.github.com/en/rest/issues/milestones)
- [G03 — Native hierarchy uses issue IDs, not issue numbers.](https://docs.github.com/en/rest/issues/sub-issues)
- [G04 — Native blocked-by relationship with the blocker issue_id.](https://docs.github.com/en/rest/issues/issue-dependencies)

### Maintainer notes

Keep execution updates, actual commands/results, decisions, and refinements here. The bootstrap does not overwrite an existing issue body on rerun.
