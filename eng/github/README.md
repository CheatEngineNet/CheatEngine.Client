# Repository settings as code

`Set-RepositorySettings.ps1` compares the GitHub settings of CheatEngine.Client with the desired state committed in
this folder and, only with `-Apply`, converges them. A repository administrator runs it from a workstation; CI never
runs it (the script refuses to run when `CI` or `GITHUB_ACTIONS` is set).

## Desired state

| File | Area | What it guarantees |
|---|---|---|
| `repository.json` | Merge settings | Squash merges only, titled by the pull request title, so every subject on `main` is a title the `PR policy` check accepted. |
| `rulesets/protect-main.json` | Ruleset `Protect main` (matched by name) | No deletion, no force push, pull requests with squash merges only, no bypass actor, and the two required checks `CI / Gate` and `PR policy` from the GitHub Actions app (integration 15368). No approval and no code-owner review is required while there is a single active maintainer. |
| `rulesets/protect-release-tags.json` | Ruleset `Protect release tags` on `refs/tags/v*` | A release tag can be neither deleted nor moved. There is no creation rule, so the maintainer can still push a tag; publication stays gated by the `nuget` environment. |
| `environments/nuget.json` | Environment `nuget` | A required reviewer (`-NuGetReviewer`, default `AriusII`) approves every publication, administrators cannot bypass it, and only `v*.*.*` tags deploy. `prevent_self_review` stays false while one person both tags and approves. |
| `actions-permissions.json` | GitHub Actions | Actions must be pinned to a full commit SHA, the default token is read-only, and workflows cannot approve pull requests. |
| `security.json` | Security features (verified, never changed) | Private vulnerability reporting, Dependabot alerts and security updates, secret scanning and push protection stay on; code-scanning default setup stays off because `codeql.yml` is an advanced setup. |

Each file carries a `_comment` member that explains the choice; the script never sends it.

## Run

Prerequisites: PowerShell 7, the GitHub CLI logged in as a repository administrator (`gh auth status`).

```powershell
# 1. Offline: print the desired payloads and the calls -Apply would make (no network access).
./eng/github/Set-RepositorySettings.ps1 -PlanOnly

# 2. Read-only: GET every area and list the drift (exit code 1 when anything differs).
./eng/github/Set-RepositorySettings.ps1

# 3. Converge. Until main is green and "PR policy" has reported on a pull request, keep the required checks out,
#    otherwise every pull request is blocked.
./eng/github/Set-RepositorySettings.ps1 -Apply -SkipRequiredChecks
./eng/github/Set-RepositorySettings.ps1 -Apply

# 4. Only after the draft-first release.yml is on main.
./eng/github/Set-RepositorySettings.ps1 -Apply -EnableImmutableReleases
```

Every write goes through `Invoke-GitHubMutation`, the single place of the script that sends a non-GET request, and
only during the first pass of an `-Apply` run; the second pass re-reads everything and reports what still differs
(for example a security feature, which must be changed by hand in the repository settings). Extra deployment policies
on the `nuget` environment are reported, never deleted.

## Maintenance

The desired state is checked by `tests/CheatEngine.Client.Repository.Tests/Governance/RepositorySettingsTests.cs`
(required check names, squash-only merges, no bypass actor, release-tag protection without a creation rule, a single
mutation path). Change the JSON and the tests together; the SDK repository keeps a twin of this folder.
