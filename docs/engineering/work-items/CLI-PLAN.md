<!-- ce-bootstrap:CLI-PLAN -->

## CLI-PLAN — Establish the Client developer-workflow roadmap and execution baseline

**Owner:** `CheatEngineNet/CheatEngine.Client` · **Kind:** roadmap · **Priority:** P1 · **Milestone:** Cross-phase navigation

**Initial status:** Needs refinement. **Runtime validation:** Not executed by this bootstrap.

### Context and evidence

This is the navigation root for the engineering bootstrap, not an implemented feature or a GitHub Project object.

**Evidence classification:** `MetadataRead` + `Proposal`: the initial GitHub preparation attempt was denied (`Resource not accessible by integration`). A later operator-authorized import created and populated the private CheatEngineNet — Engineering Execution Project #1 and created Client issues [#8–#40](https://github.com/CheatEngineNet/CheatEngine.Client/issues/8). This is a planning-metadata receipt only; it does not establish implementation, package, fixture, or live-host qualification.

### Outcome and rationale

Connect outcome milestones, owned epics, implementable issues, source evidence, package gates and focused PRs.

**Expected benefit:** Connect outcome milestones, owned epics, implementable issues, source evidence, package gates and focused PRs.

### Architectural responsibility

SDK owns CE mappings, native safety, factual outcomes and low-level owners. Client owns application policy, typed workflows, composition, and developer experience.

### Technical requirements and task checklist

- [ ] Keep one owning repository per implementation task.
- [ ] Keep hierarchy, blocking dependencies, package gates and release qualification distinct.
- [ ] Never close this root automatically from one child implementation PR.

### Acceptance criteria

- [ ] Every work item links to its owning epic and verified sources.
- [ ] Native GitHub relationships and deployment status are recorded accurately.
- [ ] No speculative due dates, assignees, release versions or live-verification results are assigned.

### Required validation

- [ ] Manifest integrity, hierarchy and cross-repository dependency validation.

### Scope exclusions

- No automatic implementation branch farm, auto-merge, release or protection change.

### Parent and children

Repository roadmap root; not a GitHub Projects object.

- https://github.com/CheatEngineNet/CheatEngine.Client/issues/9
- https://github.com/CheatEngineNet/CheatEngine.Client/issues/10
- https://github.com/CheatEngineNet/CheatEngine.Client/issues/11
- https://github.com/CheatEngineNet/CheatEngine.Client/issues/12
- https://github.com/CheatEngineNet/CheatEngine.Client/issues/13
- https://github.com/CheatEngineNet/CheatEngine.Client/issues/14
- https://github.com/CheatEngineNet/CheatEngine.Client/issues/15
- https://github.com/CheatEngineNet/CheatEngine.Client/issues/16

### Blocked by

None declared. This does not waive evidence, policy, or package requirements.

### Blocks

None declared. This does not waive evidence, policy, or package requirements.

### Package and readiness gate

Identify the first containing artifact before describing new source as consumable support. No version is invented by this plan.

### Proposed branch and PR

No epic-sized implementation branch. Children have focused branch/PR proposals. The bootstrap creates only one documentation/tooling branch and draft PR per repository.

### Risks and compatibility

An attractive board cannot substitute for precise contracts, acceptance evidence, and maintainable issue scopes.

**Long-term impact:** One traceable owner, stable acceptance criteria, and explicit compatibility evidence reduce repeated integration fixes and make future API changes reviewable.

### Definition of done

- [ ] Navigable roadmap, detailed backlog and verified deployment receipt.

Governance root; close only when the defined roadmap scope is accepted or deliberately superseded.

### Sources

- [S01 — Existing build/test, focused branches and release rules; preserve rather than overwrite.](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/aa3fcc3cdf629468e69d0c68817183d44a719894/CONTRIBUTING.md)
- [C01 — Current in-process architecture, capability gates and managed deployment.](https://github.com/CheatEngineNet/CheatEngine.Client/blob/923a4ded85898f53ef4cd2ff5872d2fd9001071a/README.md)
- [G01 — Issue API; filter pull_request entries from issue lists.](https://docs.github.com/en/rest/issues/issues)
- [G02 — Repository-specific milestones.](https://docs.github.com/en/rest/issues/milestones)
- [G03 — Native hierarchy uses issue IDs, not issue numbers.](https://docs.github.com/en/rest/issues/sub-issues)
- [G04 — Native blocked-by relationship with the blocker issue_id.](https://docs.github.com/en/rest/issues/issue-dependencies)

### Maintainer notes

Keep execution updates, actual commands/results, decisions, and refinements here. The bootstrap does not overwrite an existing issue body on rerun.
