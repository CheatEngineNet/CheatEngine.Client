# CheatEngine.Client.Abstractions

The base layer of CheatEngine.Client: the strongly-typed public vocabulary (contracts, value types, enums, options,
error types) and the contracts that separate the fluent surface from the binding layer.

## Rules

- References no other project. Everything above depends on it, nothing below it exists.
- Public API: what lives here is what consumers and the other layers agree on.
