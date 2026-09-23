# CheatEngine.Client documentation

> Recreated 2026-09 from the audit, not the historical docs/ tree.

This folder indexes the repository documentation of CheatEngine.Client. Each package also carries its own `README.md`,
which is published on nuget.org; the [repository README](../README.md) is the starting point for plugin authors.

## Documents

| Document                                 | What it answers                                                                                     |
|------------------------------------------|-----------------------------------------------------------------------------------------------------|
| [CHANGELOG](../CHANGELOG.md)             | What changed in each release, separated into extensions, semantic corrections, refusals, deployment |
| [CONTRIBUTING](../CONTRIBUTING.md)       | How to build, test, pack and change the repository, and the rules a pull request follows            |
| [RELEASING](../RELEASING.md)             | How a release is built, attested, published and verified, and what it must be qualified against     |
| [ROADMAP](../ROADMAP.md)                 | The outcome-based plan, including the `CLI-0xx` work item identifiers                               |
| [The consumed SDK](../eng/sdk/README.md) | Which `CheatEngine.SDK` package the Client consumes, its hashes, its guards and how the pin moves    |
| [LICENSE](../LICENSE)                    | The MIT license of the repository and of the seven packages                                         |

## Pages that arrive later

These pages do not exist yet. They are listed so that a reader knows where the information will live; nothing links to
them until they are written.

- `docs/qualification/`: the Client support profile, the Client rows of the qualification matrix, and the receipts of
  host runs. It references the SDK profile `ce-7.7.0.10621-x64-managed-hostfxr`.
- `docs/migration/sdk-2.0.md`: what the Client must change before it can consume `CheatEngine.SDK` 2.0.
- `docs/audit-2026-09-22-traceability.md`: how each finding and qualification scenario of the September 2026 audit was
  resolved or deferred.

## Retired documentation

Commit `d06fd2e` deleted a `docs/` tree that the repository still linked. None of it is restored, and no page here
reconstructs it: the absence of these files is missing evidence, not proof that the work never happened, and any claim
that relied on them stays a declaration until new evidence exists. Old links were re-pointed or removed as follows.

| Retired path                                        | Replacement                                                                                | Reason                                                                           |
|-----------------------------------------------------|--------------------------------------------------------------------------------------------|----------------------------------------------------------------------------------|
| `docs/engineering/EVIDENCE_AND_LIMITS.md`           | Removed, not restored. The roadmap no longer mentions the deployment receipt it held.      | It recorded a repository deployment, not product evidence.                       |
| `docs/engineering/work-items/CLI-001.md` to `CLI-024.md` | Removed, not restored. The roadmap keeps each `CLI-0xx` identifier and title as plain text. | Issues are disabled, so no issue can replace the work item pages.                |
| `docs/adr/0001-layered-in-process-architecture.md`  | [Packages and direct SDK reference](../README.md#packages-and-direct-sdk-reference)       | The repository README describes the package graph and the direct SDK reference. |
| `docs/adr/0002-plugin-activation-lifecycle.md`      | [The plugin lifecycle](../README.md#the-plugin-lifecycle)                                 | The repository README describes the per-enable lifecycle.                        |
