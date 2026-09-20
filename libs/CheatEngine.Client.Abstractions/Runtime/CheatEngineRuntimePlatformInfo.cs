using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Runtime;

/// <summary>Immutable platform observations captured during one active Cheat Engine activation.</summary>
public readonly record struct CheatEngineRuntimePlatformInfo
{
	/// <summary>Creates runtime platform observations from copied host facts.</summary>
	/// <exception cref="ArgumentException">
	///     <paramref name="targetPointerSize" /> does not match the width implied by
	///     <paramref name="targetArchitecture" />.
	/// </exception>
	public CheatEngineRuntimePlatformInfo(
		CheatEngineArchitecture systemArchitecture,
		CheatEngineArchitecture targetArchitecture,
		PointerSize targetPointerSize,
		TargetAbi targetAbi)
	{
		PointerSize expectedTargetPointerSize = PointerSize.FromArchitecture(targetArchitecture);
		if (targetPointerSize != expectedTargetPointerSize)
		{
			throw new ArgumentException(
				"The target pointer size must match the target architecture.",
				nameof(targetPointerSize));
		}

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
