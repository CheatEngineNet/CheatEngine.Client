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

## Run

From the repository root:

```powershell
dotnet test --project .\tests\CheatEngine.Client.Hosting.Tests\CheatEngine.Client.Hosting.Tests.csproj --configuration Release
```
