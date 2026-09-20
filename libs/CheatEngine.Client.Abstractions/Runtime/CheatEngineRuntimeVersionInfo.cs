using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Runtime;

/// <summary>Immutable version observations captured during one active Cheat Engine activation.</summary>
public readonly record struct CheatEngineRuntimeVersionInfo
{
	/// <summary>Creates runtime version observations from independently observed facts.</summary>
	public CheatEngineRuntimeVersionInfo(
		double? observedCheatEngineVersion,
		CheatEngineVersion qualifiedCheatEngineBaseline,
		Version clientAssemblyVersion,
		Version sdkAssemblyVersion)
	{
		if (observedCheatEngineVersion is { } observed &&
		    (!double.IsFinite(observed) || observed < 0))
		{
			throw new ArgumentOutOfRangeException(nameof(observedCheatEngineVersion), observed,
				"The observed Cheat Engine version must be a finite non-negative number when supplied.");
		}

		ObservedCheatEngineVersion = observedCheatEngineVersion;
		QualifiedCheatEngineBaseline = qualifiedCheatEngineBaseline;
		ClientAssemblyVersion = clientAssemblyVersion ?? throw new ArgumentNullException(nameof(clientAssemblyVersion));
		SdkAssemblyVersion = sdkAssemblyVersion ?? throw new ArgumentNullException(nameof(sdkAssemblyVersion));
	}

	/// <summary>Gets the coarse number returned by CE's <c>getCEVersion</c> global, when it was callable.</summary>
	public double? ObservedCheatEngineVersion
	{
		get;
	}

	/// <summary>Gets the complete CE build against which this Client release was qualified.</summary>
	public CheatEngineVersion QualifiedCheatEngineBaseline
	{
		get;
	}

	/// <summary>Gets the assembly version of this Client abstraction assembly.</summary>
	public Version ClientAssemblyVersion
	{
		get;
	}

	/// <summary>Gets the assembly version of the SDK runtime-contract assembly.</summary>
	public Version SdkAssemblyVersion
	{
		get;
	}
}
