using CheatEngine.Client.Core.Infrastructure;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Production runtime probe; it calls only the read-only <see cref="ClientLuaGlobals" /> observations.</summary>
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

	public bool TargetIsX86()
	{
		return ClientLuaGlobals.TargetIsX86();
	}

	public bool TargetIsArm()
	{
		return ClientLuaGlobals.TargetIsArm();
	}

	public int GetConfiguredPointerSize()
	{
		return ClientLuaGlobals.GetConfiguredPointerSize();
	}
}
