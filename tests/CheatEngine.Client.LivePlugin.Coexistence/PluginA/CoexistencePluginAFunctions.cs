using CheatEngine.SDK.Annotations.Lua;

namespace LivePlugin.Coexistence.PluginA;

/// <summary>Distinct Lua exports used only by the manual coexistence protocol.</summary>
internal static partial class CoexistencePluginAFunctions
{
	private static long _pingCount;

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
		return Interlocked.Increment(ref _pingCount);
	}

	/// <summary>Competes with Plugin Collision for the exact same Lua global name.</summary>
	[LuaFunction("cheatengine_client_coexistence_a_collision")]
	public static string Collision()
	{
		return "CollisionOwner=A";
	}

	/// <summary>Records the active target observation without selecting a target.</summary>
	[LuaFunction("cheatengine_client_coexistence_a_target")]
	public static string ObserveTarget()
	{
		return CoexistenceDiagnostics.ObserveTarget();
	}

	/// <summary>Attempts the explicitly opt-in retained-owner probe.</summary>
	[LuaFunction("cheatengine_client_coexistence_a_retain_owner")]
	public static string RetainOwner()
	{
		return CoexistenceDiagnostics.RetainOwner();
	}

	/// <summary>Reports the retained-owner state without issuing a CE call.</summary>
	[LuaFunction("cheatengine_client_coexistence_a_owner_state")]
	public static string OwnerState()
	{
		return CoexistenceDiagnostics.GetOwnerState();
	}

	/// <summary>Releases the retained owner when the operator has completed the probe.</summary>
	[LuaFunction("cheatengine_client_coexistence_a_release_owner")]
	public static string ReleaseOwner()
	{
		return CoexistenceDiagnostics.ReleaseOwner();
	}
}
