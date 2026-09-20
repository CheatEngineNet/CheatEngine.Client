using CheatEngine.Client.Lua;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.Plugin.Modules;

/// <summary>Application-owned Lua module that encapsulates the generated SDK binding calls.</summary>
/// <remarks>
///     <see cref="ILuaClient" /> invokes this module only while the activation is current and owns the lease that calls
///     <see cref="Unregister" />. The SDK state remains inside this implementation; it never crosses the public Client
///     contract or the consuming Client module.
/// </remarks>
internal sealed class PluginLuaModule : ILuaModule
{
	/// <inheritdoc />
	public void Register()
	{
		LuaStatus status = PluginLuaFunctions.RegisterLuaFunctions(LuaRuntime.AcquireState());
		if (!status.IsOk)
		{
			throw new InvalidOperationException($"Unable to register the plugin Lua module: {status}.");
		}
	}

	/// <inheritdoc />
	public void Unregister()
	{
		LuaStatus status = PluginLuaFunctions.UnregisterLuaFunctions(LuaRuntime.AcquireState());
		if (!status.IsOk)
		{
			throw new InvalidOperationException($"Unable to unregister the plugin Lua module: {status}.");
		}
	}
}
