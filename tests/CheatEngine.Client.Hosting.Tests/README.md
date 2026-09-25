# CheatEngine.Client.Hosting.Tests

## Context

This project tests the plugin host built on `CheatEngineClientPlugin` and `CheatEnginePluginBuilder`.

## Why this project exists

Hosting translates Cheat Engine enable/disable callbacks into an ephemeral DI provider and a single Client activation.
It is responsible for configuration ordering, validation-before-use, module startup order, reverse shutdown, rollback,
and cleanup while the SDK context is still valid.

## How it helps improve CheatEngine.Client

The suite makes the lifecycle contract executable: a new activation is created per enable, stale work is rejected after
disable, modules observe a deterministic order, and cleanup is performed in reverse order. These tests guard the
boundary where a long-lived plugin host meets activation-scoped Client services.

`CheatEngineClientPluginTests` also proves that enable logs one identification event (event 20) without any path
(Q46) and that a logging provider that throws, from `IsEnabled` or `Log`, cannot fail enable or abort cleanup.

`CheatEnginePluginBuilderTests` proves that application code can neither create the builder nor build its provider,
that `PluginDirectory` is the folder of the plugin assembly (and refuses an assembly without a file location), and that
a provider added through `Logging` receives the Core diagnostic events; `CheatEngineClientPluginTests` shows the same
provider receiving the Hosting lifecycle events. The `appsettings.json` of this project is copied next to the test
assembly and read from `PluginDirectory`, the way a plugin reads its own file; a small JSON stand-in replaces
`Microsoft.Extensions.Configuration.Json`, which only the plugin project references.

## Run

From the repository root:

```powershell
dotnet test --project .\tests\CheatEngine.Client.Hosting.Tests\CheatEngine.Client.Hosting.Tests.csproj --configuration Release
```
