# CheatEngine.Client.SourceGenerators.Lua

This Roslyn component turns explicit `CheatEngine.Client.Lua` declarations into activation-safe,
handle-free Client adapters. It is an analyzer asset consumed by plugin projects; it is not a runtime
dependency and never discovers application code through reflection.

`[CheatEngineLuaModule]` generates an `IDescribedLuaModule` adapter around the SDK-generated Lua
registration pair. It performs a protected, stack-balanced preflight of every exported global before
it writes the first one, then attempts rollback inside the same admitted `LuaRuntimeOperation` if a
registration fails.

`[CheatEngineLuaOperation]` turns a scalar `[LuaGlobal]` declaration into a readonly value operation
and strongly typed factory. Mapper calls use static abstract interface dispatch so the generated
runtime path stays trim- and AOT-friendly.
