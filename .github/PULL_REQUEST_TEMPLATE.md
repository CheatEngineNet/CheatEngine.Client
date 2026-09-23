## Outcome and linked issue

Closes <!-- link only the issue actually completed by this PR -->

<!-- The title becomes the squash commit subject: an imperative sentence of at most 72 characters, without a prefix
such as "feat:" and without a trailing period (checked by "PR policy"). -->

## Scope and architectural ownership

Describe resulting behavior, affected contracts, and exclusions. SDK owns CE integration; Client owns workflows and policy.

## Dependencies and containing artifacts

Link upstream prerequisites without closing them. Identify the SDK package containing every consumed primitive.

## Validation actually performed

| Check | Command, run link or artifact | Qualification level and Q-IDs | Actual result |
|---|---|---|---|
| CI / Gate | | | Pending |
| Unit / fixture | | | Not executed |
| Packed consumer | | | Not executed |
| Live host | | | Not executed |
| AOT publication | | | Not executed |

Qualification levels: C0 static contract · C1 managed tests or doubles · C2 native fixture · C3 exact Cheat Engine host
with a loaded plugin · C4 several components (two plugins, a target switch). A C1 or C2 result, a CI run or a Native AOT
publication is never Cheat Engine host qualification.

## API and compatibility

- Public API: <!-- the PublicAPI.Unshipped.txt entries, or "none" -->
- Behavior, ownership, lifetime, cleanup, cancellation and partial effects: <!-- what changes for an existing caller -->
- CheatEngine.SDK pin and lock files: <!-- unchanged, or how they moved -->
- Migration: <!-- what a consumer must change, or "none" -->

## Evidence

<!-- Test reports, logs, run links, receipts or artifacts that back the results above. State what was not verified and
every remaining host-level limitation. -->

## Documentation and review checklist

- [ ] Scope is focused; existing repository style and contribution rules are preserved.
- [ ] Relevant regression evidence is attached; pending gates remain explicit.
- [ ] No raw state/owner escapes the normal Client boundary.
- [ ] Capability and artifact claims match actual results and state their qualification level.
- [ ] `CHANGELOG.md` has an entry under `[Unreleased]`, or nothing consumer-visible changed and the description contains the opt-out marker `<!-- changelog: not-needed -->` on its own line.
- [ ] The CheatEngine.SDK pin is unchanged or was moved with `eng/sdk/Update-CheatEngineSdk.ps1`; lock files were regenerated with `eng/Update-LockFiles.ps1`, never edited by hand.
- [ ] Documentation and release impact are recorded.
- [ ] No automatic merge, release, protection change or unsupported capability activation is requested.
