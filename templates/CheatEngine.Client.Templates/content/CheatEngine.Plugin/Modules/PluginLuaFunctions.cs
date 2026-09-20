using CheatEngine.SDK.Annotations.Lua;

namespace CheatEngine.Plugin.Modules;

/// <summary>Generated SDK Lua exports available while this plugin activation is enabled.</summary>
internal static partial class PluginLuaFunctions
{
	/// <summary>Returns the activation-local status text for a generated Lua export.</summary>
	[LuaFunction("cheatengine_client_plugin_status")]
	public static string Status()
	{
		return "CheatEngine.Plugin Lua module is enabled.";
	}
}
