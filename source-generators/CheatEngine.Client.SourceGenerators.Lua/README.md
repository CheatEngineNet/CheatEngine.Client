# CheatEngine.Client.SourceGenerators.Lua

This Roslyn component turns explicit `CheatEngine.Client.Lua` declarations into activation-safe,
handle-free Client adapters. It is an analyzer asset consumed by plugin projects; it is not a runtime
dependency and never discovers application code through reflection.

`[CheatEngineLuaModule]` generates an `IDescribedLuaModule` and `IOwnershipAwareLuaModule` adapter around
the SDK-generated `RegisterLuaFunctions`. It refuses, before the first write, to replace a global that is
already defined, pins the value it published under each export, and at release clears a global only
while it still holds that value, so a third-party replacement survives (F12, Q16). It never calls the
legacy `UnregisterLuaFunctions`, which writes `nil` unconditionally.

`[CheatEngineLuaOperation]` turns a scalar `[LuaGlobal]` declaration into a readonly value operation
and strongly typed factory. Mapper calls use static abstract interface dispatch so the generated
runtime path stays trim- and AOT-friendly.

## Diagnostics

Every generator diagnostic is an error. Ids are allocated per range and never renumbered or reused: 1001-1006 module
shape, 1101-1106 operation shape, 1201-1209 module ownership (Q16; 1205-1209 unused).

| Id | Title | Reported when |
|---|---|---|
| CECLUA1001 | Lua module must be a non-static partial class | The module is not a top-level, concrete, non-static, non-generic, non-file-local partial class. |
| CECLUA1002 | Lua module requires a static SDK bindings type | The bindings type is not a non-generic, non-file-local static class. |
| CECLUA1003 | Lua module bindings export nothing | The bindings type declares no `[LuaFunction]` export. |
| CECLUA1004 | Lua module exports must have unique names | An export name is missing, blank, or declared twice. |
| CECLUA1005 | Lua module name cannot be blank | The explicit module name is empty or whitespace. |
| CECLUA1006 | Lua module needs a public constructor | Only non-public explicit constructors exist, so dependency injection cannot create the module. |
| CECLUA1101 | Lua operation requires a supported SDK global declaration | The operation is not a static partial `[LuaGlobal]` method of a top-level static partial class. |
| CECLUA1102 | Lua operation method cannot be overloaded | Several `[CheatEngineLuaOperation]` methods share a name. |
| CECLUA1103 | Lua operation has an unsupported result shape | Inputs are not scalar, or the result is not one return value or one trailing `out` value. |
| CECLUA1104 | Lua operation result requires a mapper | A non-scalar SDK result has no `ILuaResultMapper`. |
| CECLUA1105 | Lua operation mapper does not match the SDK result | The mapper does not implement `ILuaResultMapper` for that SDK result. |
| CECLUA1106 | Lua operation mapper must project a safe Client result | The mapped graph exposes an SDK lifetime, interop, callback, or opaque framework type. |
| CECLUA1201 | Lua export is owned by more than one Lua module | Two modules of one compilation export the same Lua global; reported on the later module (file path, then position). |
| CECLUA1202 | Lua module declares a member reserved by the generated registration | The module declares `Register`, `Unregister`, `Descriptor`, `LastReleaseOutcome`, `s_descriptor`, a member starting with `__CheatEngineLua`, or an explicit implementation of the module contract. No source is generated. |
| CECLUA1203 | Lua module inherits a Lua module implementation | A base type already implements `ILuaModule` or is itself a `[CheatEngineLuaModule]`. No source is generated. |
| CECLUA1204 | Lua module annotation is not the contract type | `[CheatEngineLuaModule]` does not come from `CheatEngine.Client.Abstractions`, or a `[LuaFunction]` on the bindings type does not come from `CheatEngine.SDK.Annotations`. Look-alike exports are never counted; no source is generated. |
