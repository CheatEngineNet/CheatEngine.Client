# Contributing to CheatEngine.Client

Thanks for contributing. CheatEngine.Client is a Windows x64, .NET 10 layer for in-process Cheat Engine 7.7 plugins,
built on the `CheatEngine.SDK` package. Keep changes focused, preserve existing patterns, and update the documentation
and tests that describe the behavior you change.

## Prerequisites

- Windows x64
- .NET SDK 10.0.401 exactly, as pinned in [`global.json`](global.json):
  `winget install Microsoft.DotNet.SDK.10 --version 10.0.401`
- Git

## Build and test

Run these commands from the repository root. Restores are locked: they fail when a project's dependencies no longer
match its committed `packages.lock.json`.

```powershell
dotnet restore CheatEngine.Client.slnx --locked-mode
dotnet build CheatEngine.Client.slnx -c Debug --no-restore
dotnet test --solution CheatEngine.Client.slnx -c Debug --no-build --fail-skips on
dotnet build CheatEngine.Client.slnx -c Release --no-restore
dotnet test --solution CheatEngine.Client.slnx -c Release --no-build --fail-skips on
```

A skipped test fails the run: an environment-dependent test is fixed or deleted, never skipped.

The package consumption tests consume the exact packages you pack. Pack to `artifacts/nuget` and point them at that
folder; without the variable they pack the repository themselves, which is slower:

```powershell
dotnet pack CheatEngine.Client.slnx -c Release --no-build -o artifacts/nuget
$env:CHEATENGINE_CLIENT_PACKAGE_SOURCE = (Resolve-Path artifacts/nuget).Path
dotnet test --project tests/CheatEngine.Client.Tests/CheatEngine.Client.Tests.csproj -c Release --no-build --fail-skips on
```

To run everything except those tests, add `--filter-not-trait "Category=PackageConsumption"`.

### Lock files

Never edit a `packages.lock.json` by hand, and never let an IDE restore rewrite them. After a dependency change,
regenerate them with `./eng/Update-LockFiles.ps1`, which restores the live-plugin coexistence fixtures first (they stay
outside Central Package Management) and then every other project, and commit the result on its own.

### The consumed CheatEngine.SDK

The Client consumes exactly one `CheatEngine.SDK` package, pinned in [`eng/CheatEngineSdk.props`](eng/CheatEngineSdk.props)
and identified in `eng/sdk/consumed-sdk.json`. Never write an SDK version anywhere else, and never move the pin by hand:
[`eng/sdk/README.md`](eng/sdk/README.md) explains the policy, the guards (`CHEATENGINECLIENT9016`, `9017`, `CECLIENT017`)
and `eng/sdk/Update-CheatEngineSdk.ps1`. Moving to another SDK major is a migration of the Client, not a dependency bump.

## Continuous integration

Pull requests run `Pull request CI`, pushes to `main` run `Main CI`, and version tags run `Release`; all three call the
reusable `ci.yml`. Two checks are required on a pull request:

- `CI / Gate` passes only when every CI job passed (the Sonar analysis is skipped only where it cannot run, such as
  pull requests from forks or Dependabot);
- `PR policy` checks the pull request title and the changelog rule below.

Drafts do not run CI until they are marked ready for review.

## Style and analyzers

- Follow [`.editorconfig`](.editorconfig): tab-indented C#, two-space project and configuration files. Builds treat
  formatting, compiler and analyzer diagnostics as errors; run `dotnet format` on the projects you touch.
- Use file-scoped namespaces, explicit types instead of `var`, braces, explicit accessibility and `_camelCase` private
  fields. Test names are PascalCase sentences.
- Public APIs require XML documentation. A public API change in Abstractions, Fluent, Hosting, the DI extensions or the
  facade is declared in that project's `PublicAPI.Unshipped.txt` (RS0016/RS0017 are errors); `PublicAPI.Shipped.txt`
  changes only in a release pull request.
- Core implementation types stay `internal sealed`, and every Cheat Engine interaction goes through the Client's
  dispatcher and ports; the SDK remains the only native authority.

## Branches and pull requests

1. Update your local `main`, then create a focused branch from it.
2. Make the smallest change that solves the problem, with its tests.
3. Run the relevant build, test and pack commands.
4. Open a pull request against `main`; do not push directly to the protected branch.
5. Record consumer-visible changes under `## [Unreleased]` in [`CHANGELOG.md`](CHANGELOG.md), in one of its four
   categories: **Added** (an extension), **Changed** (a semantic correction), **Security** (a hardened refusal) or
   **Deployment** (what is built, packed, pinned or published). A change under `libs/`, `src/`, `source-generators/` or
   `templates/` needs an entry, unless the pull request body contains the line `<!-- changelog: not-needed -->`.

Fill in the pull request template: the problem, the resulting behavior, the validation commands and results, and any
remaining host-level limitation. Pull requests are squash-merged once the required checks pass, so the pull request
title becomes the commit subject on `main`.

## Commits

Use focused commits with an imperative subject of at most 72 characters, without a Conventional Commit prefix and
without a trailing period, such as `Harden the package consumption smoke tests`. Explain why in the body. Do not add
`Co-authored-by` trailers.

## Releases

Versions come from MinVer and the nearest `v*` tag; the seven packages always share one version. Releases are built,
attested and published by `release.yml` through NuGet trusted publishing, only from a tag on `main`. See
[`RELEASING.md`](RELEASING.md).
