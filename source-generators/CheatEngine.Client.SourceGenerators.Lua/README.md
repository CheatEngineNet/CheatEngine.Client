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
