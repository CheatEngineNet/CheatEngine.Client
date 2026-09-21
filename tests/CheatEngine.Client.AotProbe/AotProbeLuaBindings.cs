using CheatEngine.SDK.Annotations.Lua;

namespace CheatEngine.Client.AotProbe;

/// <summary>Minimal explicit SDK-shaped binding fixture consumed by the Client generator during probe compilation.</summary>
internal static partial class AotProbeLuaBindings
{
	/// <summary>Declares one generated Lua export so descriptor and registration generation are rooted.</summary>
	[LuaFunction("aot_probe_ping")]
	public static int Ping()
	{
		return 1;
	}
}
