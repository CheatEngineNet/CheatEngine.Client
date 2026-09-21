# Evidence, Provenance, and Deployment Receipt

## Review inputs and limits

The architecture-review archive supplied for this bootstrap was validated with its own SHA-256, JSON, Markdown-link, and Python-tool checks. Its identity, inventory, bounded finding crosswalk, and source/package/fixture/live boundary are recorded in [the archive reconciliation](ARCHIVE_RECONCILIATION.md). It provides the decision brief, findings, ownership/capability matrices, and qualification scenarios that shaped this plan. It is context rather than a source clone, a NuGet artifact, or live-host evidence: it does not establish a byte-for-byte import, exhaustive reconciliation of every historical finding, or a verified mapping to every original ADR/test ID. SDK-001 and CLI-001 explicitly retain that continuity work. New stable planning IDs are used instead of inventing original IDs.

## Current repository observations

| Repository | Main inspected | Relevant merged work |
|---|---|---|
| CheatEngine.SDK | `aa3fcc3cdf629468e69d0c68817183d44a719894` | PR #19: owned runtime primitives and generated Lua marshalling. |
| CheatEngine.Client | `923a4ded85898f53ef4cd2ff5872d2fd9001071a` | PR #7: fluent high-level APIs and generated Lua modules. |

Both repositories reported no open issues/PRs at the initial metadata read; an organization issue search found no open issues in these two repositories. The SDK search returned eight historical issues, including closed NuGet/publication and documentation work. Those were not reopened. Main is protected. The metadata's user-level push/admin flags did not establish the active integration's effective write permissions.

SDK CONTRIBUTING and AGENTS and the Client README were read in this bootstrap. Relevant .github/root metadata was inspected to preserve existing conventions. Most deep implementation references come from the prior review; they are marked `PriorAuditReference`, not misrepresented as newly executed or fully reread. The prior official CE comparison remains pinned to `ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37`, not an inspected live CE 7.7 binary.

## Deployment receipt as of September 21, 2026

The first preparation attempt received `Resource not accessible by integration`. That historical failure is not the final deployment state. A later operator-authorized import created and populated the private [CheatEngineNet — Engineering Execution Project](https://github.com/orgs/CheatEngineNet/projects/1):

- the Client roadmap root is [#8](https://github.com/CheatEngineNet/CheatEngine.Client/issues/8), with epics [#9–#16](https://github.com/CheatEngineNet/CheatEngine.Client/issues/9) and leaves [#17–#40](https://github.com/CheatEngineNet/CheatEngine.Client/issues/17);
- the Project has the Client planning fields `CE Planning ID`, `CE Layer`, `CE Phase`, `CE Priority`, `CE Readiness`, `CE Start`, and `CE Target` in addition to GitHub built-ins;
- the root, epics, leaves, milestones, native hierarchy, and declared Client/SDK dependencies were read back after import. Their issue bodies contain stable `ce-bootstrap:<ID>` markers.

This is a metadata receipt, not a product-validation result. Project views, workflow automation, and any subsequent human edits must be inspected in GitHub before a planning state is relied upon.

## Tool coverage

GitHub metadata is verified with authenticated repository tooling after the operator has authorized it. Repository files do not contain credentials, automation for Project mutation, or a claim that a local document can replace GitHub's current state.

## Verification categories

`ArchiveValidated`, `MetadataRead`, `PriorAuditReference`, `WebPrimary`, `Proposal`, `FixtureVerified` and `LiveVerified` are distinct. Planning records carry no FixtureVerified or LiveVerified status. Archive validation covers the archive package and its Python tests only; repository product validation is recorded separately with its exact commands and results.

The archive is not a source clone. Existing implementation claims are credited, but qualification work must recheck exact source and actual containing package. Package version, source commit and assembly version remain separate identifiers.
