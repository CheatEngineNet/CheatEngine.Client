using CheatEngine.Client.Lua;

namespace CheatEngine.Client.AotProbe;

/// <summary>Provides the scalar mapping shape used by generated Lua operations.</summary>
internal readonly struct AotProbeScalarMapper : ILuaResultMapper<int, int>
{
	/// <summary>Copies the scalar value.</summary>
	public static int Map(int source)
	{
		return source;
	}
}
