using CheatEngine.SDK.Annotations.Lua;

namespace CheatEngine.Plugin.Modules;

/// <summary>Generated SDK Lua exports available while this plugin activation is enabled.</summary>
/// <remarks>
///     A Lua global has one owner in Cheat Engine. <c>dotnet new ceplugin</c> derives the status global from the
///     project name (ASCII lower_snake_case, then <c>_status</c>), but different names can derive the same one:
///     <c>MyPlugin</c> and <c>My.Plugin</c> both give <c>my_plugin_status</c>. Rename the global when another
///     plugin exports it.
/// </remarks>
internal static partial class PluginLuaFunctions
{
	/// <summary>Returns the activation-local status text for a generated Lua export.</summary>
	[LuaFunction("cheatengine_client_plugin_status")]
	public static string Status()
	{
		return "CheatEngine.Plugin Lua module is enabled.";
	}
}
