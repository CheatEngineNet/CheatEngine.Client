# CheatEngine.Client.Extensions.DependencyInjection.Tests

## Context

This project validates the dependency-injection composition layer that turns a service collection into an
activation-scoped CheatEngine.Client facade.

## Why this project exists

DI is the composition layer of Hosting: it assembles options, logging, modules, and the policy that controls optional
capabilities. The registration graph must stay deterministic and safe without a Generic Host or a process-wide service
provider.

## How it helps improve CheatEngine.Client

The suite verifies registration completeness, semantic options validation, the absence of any implicit memory codec
(A5), and module ordering. It catches accidental singleton leakage, invalid configuration defaults, or registration
changes that would make an otherwise valid plugin fail during enable.

`CompositionSurfaceTests` pins the public surface of the layer: `AllowedTableRoots` is a read-only, never-null
`IList<string>`, `MemoryResourceLimits` is never null, the options validators are internal, and no `AddMemoryCodec` or
default codec exists.
`LoggerCoreDiagnosticsTests` pins the Core diagnostic events: one logger category per domain, stable event ids
1000–1800, one capability refusal per capability and operation, standard `Logging:LogLevel` filtering, no address,
value, symbol or path in any event (Q46), and no logging-provider fault escaping.

## Run

From the repository root:

```powershell
dotnet test --project .\tests\CheatEngine.Client.Extensions.DependencyInjection.Tests\CheatEngine.Client.Extensions.DependencyInjection.Tests.csproj --configuration Release
```
