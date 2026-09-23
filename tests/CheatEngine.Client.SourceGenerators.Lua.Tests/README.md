# CheatEngine.Client.SourceGenerators.Lua.Tests

This project runs `CheatEngineLuaGenerator` directly through `CSharpGeneratorDriver`, without an attached Cheat Engine
process. It validates the Client-owned source boundary: generated code keeps raw Lua values inside its implementation and
emits the contracts that Core reserves before mutation.

| Folder | What it proves |
|---|---|
| `EndToEnd/` | C1 execution of the emitted ownership-aware module algorithm (Q16): a second `partial` part of the generated module implements its private port with `FakeLuaGlobals`, a managed Lua-globals double, and the real generated `RegisterCore`/`UnregisterCore` run against it. `SdkPortStackDisciplineTests` checks the stack discipline of the SDK port on the emitted syntax. |
| `Architecture/` | C0 ratchet of the single registered ADR-01 exception of generated Client code: the SDK Lua members a module may use, read from the emitted image, and the confinement of `LuaState` access to the SDK port. |
| `Diagnostics/` | CECLUA1201-1204 shape diagnostics and the catalog check against `AnalyzerReleases.Unshipped.md` and the generator README (linked into the output). |
| `Validation/` | Incremental models (cached on unrelated edits, no Roslyn objects), identifier stability, and the descriptor contract, public projection and legacy-call checks kept separate from the golden snapshot. |
| `Snapshots/` | The golden text of one generated module: the only test that changes when the emitted formatting changes. |
| root | Operation adapters, mapper boundary diagnostics, and dependency-injection composition of generated modules. |

The SDK owns the live Lua 5.3 lifecycle and closure tests. No Lua runtime exists in this repository, so nothing here is a
C2 fixture result, and nothing is host-qualified. Tests that evidence Q16 carry `[Trait("Qualification", "Q16")]`:

```powershell
dotnet test --project tests/CheatEngine.Client.SourceGenerators.Lua.Tests/CheatEngine.Client.SourceGenerators.Lua.Tests.csproj --filter-trait "Qualification=Q16"
```
