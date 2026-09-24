# CheatEngine.Client.SourceGenerators.Lua.Tests

This project runs `CheatEngineLuaGenerator` directly through `CSharpGeneratorDriver`, without an attached Cheat Engine
process. It validates the Client-owned source boundary: generated code keeps raw Lua values inside its implementation and
emits the contracts that Core reserves before mutation.

| Folder | What it proves |
|---|---|
| `EndToEnd/` | C1 execution of the generated module and registrar around the CheatEngine.SDK registration leases (Q16): the test compilation replaces only the generated SDK adapter with one that drives `FakeLuaGlobals`, a managed double of the SDK registration set, and calls the module's public `Register`/`Unregister`. `GeneratedRegistrarMappingTests` proves that the registrar maps every SDK admission status, registration result and release kind, and fails closed. The double compares values by object identity, not by Lua type semantics. |
| `Composition/` | CRIT-07: the real CheatEngine.SDK LuaBindings generator of the pinned package, loaded from the restored package folder the test project passes as assembly metadata, runs next to the Client generator; both outputs must compile together, the module must publish through `LuaRegistrationSet`, and integer results must use the refusing SDK marshallers. |
| `Architecture/` | C0 ratchet of the SDK-imposed registration API: the exact list of CheatEngine.SDK members the generated adapter uses, read from the emitted image, and the proof that only the adapter calls CheatEngine.SDK. |
| `Diagnostics/` | CECLUA1201-1204 shape diagnostics and the catalog check against `AnalyzerReleases.Unshipped.md` and the generator README (linked into the output). |
| `Validation/` | Incremental models (cached on unrelated edits, no Roslyn objects), identifier stability, and the descriptor contract, public projection, registration-call and no-runtime checks kept separate from the golden snapshot. |
| `Snapshots/` | The golden text of one generated module, and the constant text of the registrar and adapter. A formatting-only change updates it (and at most the literal-text checks of `ModuleContractTests`). |
| root | Operation adapters, mapper boundary diagnostics, and dependency-injection composition of generated modules. |

The SDK owns the live Lua 5.3 lifecycle and closure tests. No Lua runtime exists in this repository, so nothing here is a
C2 fixture result, and nothing is host-qualified. Tests that evidence Q16 carry `[Trait("Qualification", "Q16")]`:

```powershell
dotnet test --project tests/CheatEngine.Client.SourceGenerators.Lua.Tests/CheatEngine.Client.SourceGenerators.Lua.Tests.csproj --filter-trait "Qualification=Q16"
```
