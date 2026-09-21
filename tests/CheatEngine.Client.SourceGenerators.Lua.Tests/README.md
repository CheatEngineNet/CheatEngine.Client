# CheatEngine.Client.SourceGenerators.Lua.Tests

This project runs `CheatEngineLuaGenerator` directly through `CSharpGeneratorDriver`. It locks down the
generated module descriptor and registration adapter, typed operation factory, invalid declaration diagnostics,
and incremental re-run behavior without requiring an attached Cheat Engine process.

The SDK itself owns live Lua 5.3 lifecycle and closure tests. These tests validate only the Client-owned
source boundary: generated code must keep raw Lua values inside its implementation and emit the contracts
that Core can reserve before mutation.
