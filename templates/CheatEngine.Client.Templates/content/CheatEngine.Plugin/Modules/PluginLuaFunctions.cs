using CheatEngine.SDK.Annotations.Lua;

namespace CheatEngine.Plugin.Modules;

/// <summary>Generated SDK Lua exports available while this plugin activation is enabled.</summary>
internal static partial class PluginLuaFunctions
{
	[LuaFunction("cheatengine_client_plugin_status")]
	public static string Status()
	{
		return "CheatEngine.Plugin Lua module is enabled.";
	}
}
