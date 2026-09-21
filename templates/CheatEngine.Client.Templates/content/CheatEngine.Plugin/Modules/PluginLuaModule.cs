using CheatEngine.Client.Lua;

namespace CheatEngine.Plugin.Modules;

/// <summary>
///     Declares the application Lua module. CheatEngine.Client generates the descriptor and the activation-safe
///     register/unregister adapter from <see cref="PluginLuaFunctions" /> at compile time.
/// </summary>
/// <remarks>
///     Application code deliberately contains no Lua state, operation, lease, or SDK ownership handle. Register the
///     module through <c>builder.Client.AddLuaModule&lt;PluginLuaModule&gt;()</c> so the Client owns its activation-scoped
///     lifecycle and checks global-name collisions before it mutates Lua.
/// </remarks>
[CheatEngineLuaModule(typeof(PluginLuaFunctions), "plugin")]
internal sealed partial class PluginLuaModule : ILuaModule;
