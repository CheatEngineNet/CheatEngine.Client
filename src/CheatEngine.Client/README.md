# CheatEngine.Client

The single project a consumer references. It is the composition root of CheatEngine.Client: it wires
`CheatEngine.Client.Fluent` and `CheatEngine.Client.Binding` behind the `CheatEngine.Client.Abstractions` contracts and
holds no logic of its own.

## Rules

- The only place where `CheatEngine.Client.Fluent` and `CheatEngine.Client.Binding` meet.
- The assembly and the root namespace are both `CheatEngine.Client`, so never declare a type named `CheatEngine` or
  `Client` in it (CA1724 matches each segment of the namespace).
