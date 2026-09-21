# Organization Project Configuration

## One shared execution Project

The private organization [CheatEngineNet — Engineering Execution Project](https://github.com/orgs/CheatEngineNet/projects/1) contains the SDK and Client roadmap issues. Maintain separate SDK and Client table views rather than duplicating every issue into independently maintained planning boards. Each repository retains its own seven milestones and roadmap root.

This repository deliberately does not ship a Project-mutating script or a second Project manifest. Project administration remains an explicit operator action: read the current metadata first, make a narrow GitHub change only when authorized, and retain the resulting issue/Project receipt. The local [`backlog.json`](backlog.json) is the reviewed source for Client planning IDs, fields, hierarchy, and dependency intent; it is not a remote-state synchronizer.

## Fields

Use built-in Status and repository/milestone data. Add CE Planning ID, CE Layer, CE Phase, CE Priority, CE Readiness, CE Start and CE Target. Readiness begins at Needs refinement; having no blocker does not automatically imply Ready. Contract, artifact and live gates are tracked explicitly in issues and reviewed before changing readiness. Dates remain empty until real capacity and sequencing commitments exist.

## Saved views and remaining UI configuration

| View | Layout | Selection | Purpose |
|---|---|---|---|
| SDK Backlog | Table | SDK repository | Group by milestone; show priority, readiness and dependencies. |
| Client Backlog | Table | Client repository | Show SDK blockers and exact package gates. |
| Cross-repository Board | Board | Open issues | Group by built-in Status; do not duplicate state into several label schemes. |
| Blocking Contracts | Table | Open issues | Display native dependency fields and separate artifact blockers. |
| Roadmap | Roadmap | Epic label | Use CE Start / CE Target only after dates are accepted. |

The Project currently has the named CE fields. Grouping, sorting, roadmap date-field binding, saved-view configuration, and Project workflow automation remain explicit GitHub UI checks; they are not asserted by the local manifest validator. GitHub roadmap visualization requires date information, so an undated outcome plan must not manufacture dates just to fill a chart.

## Operating guidance

Keep completed issues in the Project unless the team approves archival behavior. Avoid automatic closure of epics or cross-repository issues on a child merge. Keep unsafe/unqualified capability work visible as deferred or gated, not silently removed. Do not add a new organization issue type globally just to represent an epic; the bootstrap uses ordinary issues, a dedicated label, and native hierarchy.

## Sources

- [Project creation](https://cli.github.com/manual/gh_project_create)
- [Field creation](https://cli.github.com/manual/gh_project_field-create)
- [Item editing](https://cli.github.com/manual/gh_project_item-edit)
- [Roadmap layout](https://docs.github.com/en/issues/planning-and-tracking-with-projects/customizing-views-in-your-project/customizing-the-roadmap-layout)
