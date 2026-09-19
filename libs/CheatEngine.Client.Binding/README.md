# CheatEngine.Client.Binding

The mapper/binder layer of CheatEngine.Client: it implements the contracts declared in `CheatEngine.Client.Abstractions`
by mapping them onto CESDK.

## Rules

- References `CheatEngine.Client.Abstractions` only. It never references `CheatEngine.Client.Fluent`.
- The only project meant to depend on CESDK. This is the default layering rule: revisit it if
  `CheatEngine.Client.Abstractions` ever reuses CESDK vocabulary.
- Internal by default: only what `CheatEngine.Client` composes is public.
