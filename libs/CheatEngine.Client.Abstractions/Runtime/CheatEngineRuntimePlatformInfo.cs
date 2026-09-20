using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Runtime;

/// <summary>Immutable platform observations captured during one active Cheat Engine activation.</summary>
public readonly record struct CheatEngineRuntimePlatformInfo
{
	/// <summary>Creates runtime platform observations from copied host facts.</summary>
	public CheatEngineRuntimePlatformInfo(
		CheatEngineArchitecture systemArchitecture,
		CheatEngineArchitecture targetArchitecture,
		PointerSize targetPointerSize,
		TargetAbi targetAbi)
	{
		SystemArchitecture = systemArchitecture;
		TargetArchitecture = targetArchitecture;
		TargetPointerSize = targetPointerSize;
		TargetAbi = targetAbi;
	}

	/// <summary>Gets the CE host architecture observed from CE's system-architecture global.</summary>
	public CheatEngineArchitecture SystemArchitecture
	{
		get;
	}

	/// <summary>Gets the target architecture observed by a target-specific probe, or unknown.</summary>
	public CheatEngineArchitecture TargetArchitecture
	{
		get;
	}

	/// <summary>Gets the pointer width implied by the observed target architecture, or unknown.</summary>
	public PointerSize TargetPointerSize
	{
		get;
	}

	/// <summary>Gets the target ABI observed from CE's ABI global, or unknown.</summary>
	public TargetAbi TargetAbi
	{
		get;
	}
}
