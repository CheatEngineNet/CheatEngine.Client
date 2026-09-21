using CheatEngine.Client.Lua;

namespace CheatEngine.Client.AotProbe;

/// <summary>Exercises static abstract scalar mapper dispatch in the AOT reachability graph.</summary>
internal static class AotProbeMapperInvocation
{
	/// <summary>Maps one scalar through a compile-time mapper without interface boxing.</summary>
	public static int Map<TMapper>(int source)
		where TMapper : ILuaResultMapper<int, int>
	{
		return TMapper.Map(source);
	}
}
