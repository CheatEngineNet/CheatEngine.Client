using CheatEngine.SDK.Annotations.Lua;

namespace LivePlugin.Coexistence.PluginB;

/// <summary>Distinct Lua exports used only by the manual coexistence protocol.</summary>
internal static partial class CoexistencePluginBFunctions
{
	private static long s_pingCount;

	/// <summary>Returns Plugin B's activation-local identity observations.</summary>
	[LuaFunction("cheatengine_client_coexistence_b_identity")]
	public static string Identity()
	{
		return CoexistenceDiagnostics.GetIdentity("B", typeof(CoexistencePluginB).Assembly);
	}

	/// <summary>Confirms that Plugin B's distinct Lua global remains callable.</summary>
	[LuaFunction("cheatengine_client_coexistence_b_ping")]
	public static long Ping()
	{
		return Interlocked.Increment(ref s_pingCount);
	}

	/// <summary>Records the active target observation without selecting a target.</summary>
	[LuaFunction("cheatengine_client_coexistence_b_target")]
	public static string ObserveTarget()
	{
		return CoexistenceDiagnostics.ObserveTarget();
	}
}
