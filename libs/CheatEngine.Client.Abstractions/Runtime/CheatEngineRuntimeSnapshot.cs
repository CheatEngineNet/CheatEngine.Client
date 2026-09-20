using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Runtime;

/// <summary>Immutable runtime observations captured during one active Cheat Engine activation.</summary>
/// <remarks>
///     <see cref="ObservedCheatEngineVersion" /> is the coarse number returned by Cheat Engine's
///     <c>getCEVersion</c> global. It is deliberately separate from
///     <see cref="QualifiedCheatEngineBaseline" />, because that global cannot establish a complete four-part file
///     version.
/// </remarks>
public readonly record struct CheatEngineRuntimeSnapshot
{
	/// <summary>Creates a runtime snapshot from independently observed facts.</summary>
	public CheatEngineRuntimeSnapshot(
		long epoch,
		double? observedCheatEngineVersion,
		CheatEngineVersion qualifiedCheatEngineBaseline,
		Version clientAssemblyVersion,
		Version sdkAssemblyVersion,
		CheatEngineArchitecture systemArchitecture,
		CheatEngineArchitecture targetArchitecture,
		PointerSize targetPointerSize,
		TargetAbi targetAbi,
		RuntimeCapabilities sdkCapabilities,
		ClientCapabilities clientCapabilities)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(epoch);
		if (observedCheatEngineVersion is { } observed &&
		    (!double.IsFinite(observed) || observed < 0))
		{
			throw new ArgumentOutOfRangeException(nameof(observedCheatEngineVersion), observed,
				"The observed Cheat Engine version must be a finite non-negative number when supplied.");
		}

		Epoch = epoch;
		ObservedCheatEngineVersion = observedCheatEngineVersion;
		QualifiedCheatEngineBaseline = qualifiedCheatEngineBaseline;
		ClientAssemblyVersion = clientAssemblyVersion ?? throw new ArgumentNullException(nameof(clientAssemblyVersion));
		SdkAssemblyVersion = sdkAssemblyVersion ?? throw new ArgumentNullException(nameof(sdkAssemblyVersion));
		SystemArchitecture = systemArchitecture;
		TargetArchitecture = targetArchitecture;
		TargetPointerSize = targetPointerSize;
		TargetAbi = targetAbi;
		SdkCapabilities = sdkCapabilities ?? throw new ArgumentNullException(nameof(sdkCapabilities));
		ClientCapabilities = clientCapabilities ?? throw new ArgumentNullException(nameof(clientCapabilities));
	}

	/// <summary>Gets the current plugin activation epoch.</summary>
	public long Epoch
	{
		get;
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

	/// <summary>Gets the explicit availability observation for each SDK runtime capability that was probed.</summary>
	public RuntimeCapabilities SdkCapabilities
	{
		get;
	}

	/// <summary>Gets the explicit availability observation for each Client high-level capability.</summary>
	public ClientCapabilities ClientCapabilities
	{
		get;
	}

	/// <summary>Gets whether the observed coarse CE version belongs to the qualified major/minor line.</summary>
	public bool IsOnQualifiedCheatEngineLine => ObservedCheatEngineVersion is { } observed &&
	                                            observed >= QualifiedCheatEngineBaseline.Major +
	                                            QualifiedCheatEngineBaseline.Minor / 10d &&
	                                            observed < QualifiedCheatEngineBaseline.Major +
	                                            (QualifiedCheatEngineBaseline.Minor + 1) / 10d;
}
