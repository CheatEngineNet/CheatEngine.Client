# CheatEngine.Client

The single project a consumer references. It is the composition root of CheatEngine.Client: it wires together
`CheatEngine.Client.Core`, `CheatEngine.Client.Hosting`, and `CheatEngine.Client.Fluent` behind the
`CheatEngine.Client.Abstractions` contracts and holds no logic of its own.

## Purpose

As the final composition layer, this package:

- Depends on `CheatEngine.Client.Hosting` for the DI container and plugin lifecycle
- Depends on `CheatEngine.Client.Fluent` for builder APIs
- Transitively brings in `CheatEngine.Client.Core` (the SDK mapper) through Hosting's DI registration layer
- Provides the umbrella package for normal plugin projects

See [ADR 0001](../../docs/adr/0001-layered-in-process-architecture.md) for the complete layered architecture and
[ADR 0002](../../docs/adr/0002-plugin-activation-lifecycle.md) for the plugin lifecycle model.

## Rules

- The only public package where Hosting, Core, and Fluent meet.
- The assembly and the root namespace are both `CheatEngine.Client`, so never declare a type named `CheatEngine` or
  `Client` in it (CA1724 matches each segment of the namespace).
