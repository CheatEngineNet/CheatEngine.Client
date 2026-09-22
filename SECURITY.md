# Security policy

## Supported versions

No CheatEngine.Client package has been published yet. After the first release, the latest published minor line of the
seven `CheatEngine.Client*` packages receives security fixes; earlier lines do not. All seven packages share one
version, so a fix ships as a new version of every package.

CheatEngine.SDK, which the Client consumes, follows its own policy:
<https://github.com/CheatEngineNet/CheatEngine.SDK/security/policy>.

## Reporting a vulnerability

Report vulnerabilities privately through GitHub private vulnerability reporting:
<https://github.com/CheatEngineNet/CheatEngine.Client/security/advisories/new>.

Never report a vulnerability in a public issue, discussion or pull request.

Include what is needed to reproduce the problem against an exact release tuple:

- the CheatEngine.Client version (all `CheatEngine.Client*` packages share it);
- the CheatEngine.SDK version and its `contentHash`, both read from the plugin's `packages.lock.json`;
- the SHA-256 of `cheatengine-sdk-lua-bridge.dll` in the plugin output folder;
- the Cheat Engine version, the name and SHA-256 of the executable you started, and the plugin load profile;
- the impact (what an attacker controls and what they gain) and a minimal reproduction.

Remove user names and private paths. Never attach Cheat Engine binaries, target binaries, authorization manifests or
raw DebugView dumps.

## What to expect

- Acknowledgement within 7 days, on a best-effort basis: the project has a single active maintainer.
- Triage and a coordinated fix through a GitHub Security Advisory on this repository. The advisory credits the reporter
  unless they ask otherwise.
- The fix ships as a new package version with a `Security` entry in `CHANGELOG.md`, and the advisory is published when
  the fixed packages are available on nuget.org.

## Scope

In scope:

- the seven `CheatEngine.Client*` packages (`CheatEngine.Client`, `.Abstractions`, `.Core`,
  `.Extensions.DependencyInjection`, `.Fluent`, `.Hosting`, `.Templates`);
- the content of the `ceplugin` template;
- this repository's build, test and release workflows.

Out of scope:

- CheatEngine.SDK: report it privately at
  <https://github.com/CheatEngineNet/CheatEngine.SDK/security/advisories/new>;
- Cheat Engine itself: report it upstream at <https://github.com/cheat-engine/cheat-engine>;
- use against processes you are not authorized to inspect or modify. The Client is for local processes you are
  authorized to inspect or modify; it adds no network control, no remote transport and no mechanism to bypass Cheat
  Engine or host protections;
- behaviour the plugin author opted into: table loading can execute Lua in the host (`AllowedTableRoots` stays empty
  unless the plugin has an explicit, trusted import/export location) and arbitrary Lua source is a separate opt-in.
  A way to reach either without the opt-in is in scope.
