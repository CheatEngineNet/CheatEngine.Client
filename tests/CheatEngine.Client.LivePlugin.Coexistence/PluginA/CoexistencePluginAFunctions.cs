using CheatEngine.SDK.Annotations.Lua;

namespace LivePlugin.Coexistence.PluginA;

/// <summary>Distinct Lua exports used only by the manual coexistence protocol.</summary>
internal static partial class CoexistencePluginAFunctions
{
	private static long s_pingCount;

	/// <summary>Returns Plugin A's activation-local identity observations.</summary>
	[LuaFunction("cheatengine_client_coexistence_a_identity")]
	public static string Identity()
	{
		return CoexistenceDiagnostics.GetIdentity("A", typeof(CoexistencePluginA).Assembly);
	}

	/// <summary>Confirms that Plugin A's distinct Lua global remains callable.</summary>
	[LuaFunction("cheatengine_client_coexistence_a_ping")]
	public static long Ping()
	{
		return Interlocked.Increment(ref s_pingCount);
	}
}
