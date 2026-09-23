# CheatEngine.Client.SourceGenerators.Lua.Tests

This project runs `CheatEngineLuaGenerator` directly through `CSharpGeneratorDriver`, without an attached Cheat Engine
process. It validates the Client-owned source boundary: generated code keeps raw Lua values inside its implementation and
emits the contracts that Core reserves before mutation.

| Folder | What it proves |
|---|---|
| `EndToEnd/` | C1 execution of the emitted ownership-aware module algorithm (Q16): a second `partial` part of the generated module implements its private port with `FakeLuaGlobals`, a managed Lua-globals double, and the real generated `RegisterCore`/`UnregisterCore` run against it. The double replaces the SDK port, so these tests prove the algorithm around the port, not what the port decides, and they compare values by object identity, not by Lua type semantics. |
| `SdkPort/` | C0 checks of the generated SDK port, the only generated code that runs against the real Lua state: `SdkPortDecisionTests` pins the symbol-resolved decision paths of every port method (read the global, `nil` before any pin push, `RawEquals` of the global and the pinned value, `CreateRef` as the only pin, `nil` written under the export name), executes `ExportName`, and proves with `MutatedPortsAreRejected` that identity and decision mutations fail it; `SdkPortStackDisciplineTests` checks the stack restoration. A change to an expected path is a change of the Q16 contract. |
| `Architecture/` | C0 ratchet of the single registered ADR-01 exception of generated Client code: the SDK Lua members a module may use, read from the emitted image, and the confinement of `LuaState` access to the SDK port. |
| `Diagnostics/` | CECLUA1201-1204 shape diagnostics and the catalog check against `AnalyzerReleases.Unshipped.md` and the generator README (linked into the output). |
| `Validation/` | Incremental models (cached on unrelated edits, no Roslyn objects), identifier stability, and the descriptor contract, public projection and legacy-call checks kept separate from the golden snapshot. |
| `Snapshots/` | The golden text of one generated module. A formatting-only change updates it (and at most the literal-text checks of `SdkPortStackDisciplineTests` and `ModuleContractTests`); it is not the guard of the SDK port, `SdkPort/` is. |
| root | Operation adapters, mapper boundary diagnostics, and dependency-injection composition of generated modules. |

The SDK owns the live Lua 5.3 lifecycle and closure tests. No Lua runtime exists in this repository, so nothing here is a
C2 fixture result, and nothing is host-qualified. Tests that evidence Q16 carry `[Trait("Qualification", "Q16")]`:

```powershell
dotnet test --project tests/CheatEngine.Client.SourceGenerators.Lua.Tests/CheatEngine.Client.SourceGenerators.Lua.Tests.csproj --filter-trait "Qualification=Q16"
```
