# CheatEngine.Client.Fluent

The fluent, composable public API of CheatEngine.Client: builders and entry points that express operations against the
`CheatEngine.Client.Abstractions` contracts.

## Rules

- References `CheatEngine.Client.Abstractions` only. It never references `CheatEngine.Client.Binding`, so it can be
  tested against fakes of the contracts.
- Public API.
