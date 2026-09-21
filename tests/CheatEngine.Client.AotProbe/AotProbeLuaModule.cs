using CheatEngine.Client.Lua;

namespace CheatEngine.Client.AotProbe;

/// <summary>Roots the Client Lua adapter generator in the AOT compatibility probe without connecting to a live host.</summary>
[CheatEngineLuaModule(typeof(AotProbeLuaBindings), "aot-probe")]
internal sealed partial class AotProbeLuaModule : ILuaModule;
