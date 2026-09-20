# CheatEngine.Client.Extensions.DependencyInjection.Tests

## Context

This project validates the dependency-injection composition layer that turns a service collection into an
activation-scoped CheatEngine.Client facade.

## Why this project exists

DI is the public assembly point for options, logging, default memory codecs, custom codecs, modules, and the policy
that controls optional capabilities. The registration graph must stay deterministic and safe without a Generic Host or
a process-wide service provider.

## How it helps improve CheatEngine.Client

The suite verifies registration completeness, semantic options validation, explicit codec selection, and module
ordering. It catches accidental singleton leakage, invalid configuration defaults, or registration changes that would
make an otherwise valid plugin fail during enable.

## Run

From the repository root:

```powershell
dotnet test --project .\tests\CheatEngine.Client.Extensions.DependencyInjection.Tests\CheatEngine.Client.Extensions.DependencyInjection.Tests.csproj --configuration Release
```
