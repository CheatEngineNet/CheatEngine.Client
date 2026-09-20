using CheatEngine.Client.Core.Infrastructure;

namespace CheatEngine.Client.Core.Domains;

internal sealed class LuaRuntimeProbe : IRuntimeProbe
{
	public double GetCheatEngineVersion()
	{
		return ClientLuaGlobals.GetCheatEngineVersion();
	}

	public int GetSystemArchitecture()
	{
		return ClientLuaGlobals.GetSystemArchitecture();
	}

	public int GetTargetAbi()
	{
		return ClientLuaGlobals.GetTargetAbi();
	}

	public long GetOpenedProcessId()
	{
		return ClientLuaGlobals.GetOpenedProcessId();
	}

	public bool TargetIs64Bit()
	{
		return ClientLuaGlobals.TargetIs64Bit();
	}
}
