# CheatEngine.Client.Tests

## Context

This is the consumer-facing smoke suite for the `CheatEngine.Client` meta-package project. It validates the public
graph that a plugin author receives through the recommended top-level package.

## Why this project exists

The meta-package must expose functional Client namespaces and fluent entry points without forcing consumers to know
the internal package layout. It must also keep SDK ownership wrappers and raw Lua handles behind the Client boundary.

## How it helps improve CheatEngine.Client

The smoke tests compile against the assembled consumer graph and inspect selected public contracts for prohibited
handle types. They catch accidental dependency omissions, namespace regressions, and public leakage of `LuaState`,
`LuaRef`, `CEObject`, `Owned<T>`, `MemScan`, or `FoundList` before package smoke tests run.

The suite is activation-independent. `PackageConsumptionSmokeTests` consumes the exact package directory supplied
through `CHEATENGINE_CLIENT_PACKAGE_SOURCE`, validates the template package, and builds isolated consumers. It does
not replace Core lifecycle tests or the opt-in live validation required for host-dependent capabilities such as value
scans.

`BuildGuardTests` run the repository's MSBuild guard targets against real projects with overridden global properties,
without restoring or building: `CommittedPinPassesTheSdkGuard`, `SdkMajorTwoPinFailsWithCHEATENGINECLIENT9016`,
`PrereleaseSdkPinFailsWithCHEATENGINECLIENT9016` and `CanarySwitchKeepsTheBuildRunningButStillBlocksPack` prove that the
consumed `CheatEngine.SDK` pin cannot move to a 2.x or prerelease package, and that the SDK-side canary build can report
breakage but never produce a package. `RoslynPinDriftFailsWithCHEATENGINECLIENT9020` proves that the Roslyn pin of the
packed Lua generator cannot drift from its declared floor, and `LockstepGuardAcceptsMinVerAndRefusesEveryOtherVersionSource`
that a package version comes from MinVer only (`CHEATENGINECLIENT9019`).

## Run

From the repository root:

```powershell
dotnet test --project .\tests\CheatEngine.Client.Tests\CheatEngine.Client.Tests.csproj --configuration Release
```
