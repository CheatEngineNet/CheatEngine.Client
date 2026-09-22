# CheatEngine.Client.Repository.Tests

## Context

Repository policy tests for CheatEngine.Client: the solution inventory today, and the documentation, workflow and
qualification contracts added by the audit remediation work.

## Why this project exists

Repository rules must not live in scripts that CI can silently stop running (the deleted `docs/` tree left dead links,
including in the README packed on nuget.org). They are enforced by C# tests that only read committed files: the
project never builds, packs, restores or starts a process, so it runs in seconds.

## How it helps improve CheatEngine.Client

- `Solution/SolutionInventoryTests` proves that every `*.csproj` on disk is built by CI through
  `CheatEngine.Client.slnx`, unless an explicit, reasoned exclusion says otherwise (the template content project).
- Later work adds one folder per contract (for example `Documentation/`, `Workflows/`, `Qualification/`).
- `Toolchain/ToolchainPinTests` keeps the build reproducible from the commit alone: `global.json` pins the exact .NET
  SDK (`rollForward: disable`, with an `errorMessage` naming the install command), `AnalysisLevel` is a numbered
  release rather than `latest`, and the NuGet audit policy blocks high and critical advisories in every build. The
  build-time guards `CHEATENGINECLIENT9030`-`9032` (`Directory.Build.targets`) catch per-project or command-line
  overrides of the same settings.

## Run

From the repository root:

```powershell
dotnet test --project .\tests\CheatEngine.Client.Repository.Tests\CheatEngine.Client.Repository.Tests.csproj
```
