using CheatEngine.SDK.Annotations.Lua;

namespace LivePlugin.Coexistence.PluginCollision;

/// <summary>Exports the same global as Plugin A so the host-visible collision path can be observed.</summary>
internal static partial class CoexistencePluginCollisionFunctions
{
	/// <summary>Must never replace Plugin A's existing collision marker.</summary>
	[LuaFunction("cheatengine_client_coexistence_a_collision")]
	public static string Collision()
	{
		return "CollisionOwner=Collision";
	}
}
