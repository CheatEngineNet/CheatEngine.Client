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
	/// <summary>Creates a runtime snapshot from grouped version, platform, and capability observations.</summary>
	public CheatEngineRuntimeSnapshot(
		long epoch,
		CheatEngineRuntimeVersionInfo versionInfo,
		CheatEngineRuntimePlatformInfo platformInfo,
		RuntimeCapabilities sdkCapabilities,
		ClientCapabilities clientCapabilities)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(epoch);

		Epoch = epoch;
		Version = versionInfo;
		Platform = platformInfo;
		SdkCapabilities = sdkCapabilities ?? throw new ArgumentNullException(nameof(sdkCapabilities));
		ClientCapabilities = clientCapabilities ?? throw new ArgumentNullException(nameof(clientCapabilities));
	}

	/// <summary>Gets the current plugin activation epoch.</summary>
	public long Epoch
	{
		get;
	}

	/// <summary>Gets the grouped version observations captured for this activation.</summary>
	public CheatEngineRuntimeVersionInfo Version
	{
		get;
	}

	/// <summary>Gets the grouped platform observations captured for this activation.</summary>
	public CheatEngineRuntimePlatformInfo Platform
	{
		get;
	}

	/// <summary>Gets the coarse number returned by CE's <c>getCEVersion</c> global, when it was callable.</summary>
	public double? ObservedCheatEngineVersion
	{
		get => Version.ObservedCheatEngineVersion;
	}

	/// <summary>Gets the complete CE build against which this Client release was qualified.</summary>
	public CheatEngineVersion QualifiedCheatEngineBaseline
	{
		get => Version.QualifiedCheatEngineBaseline;
	}

	/// <summary>Gets the assembly version of this Client abstraction assembly.</summary>
	public Version ClientAssemblyVersion
	{
		get => Version.ClientAssemblyVersion;
	}

	/// <summary>Gets the assembly version of the SDK runtime-contract assembly.</summary>
	public Version SdkAssemblyVersion
	{
		get => Version.SdkAssemblyVersion;
	}

	/// <summary>Gets the CE host architecture observed from CE's system-architecture global.</summary>
	public CheatEngineArchitecture SystemArchitecture
	{
		get => Platform.SystemArchitecture;
	}

	/// <summary>Gets the target architecture observed by a target-specific probe, or unknown.</summary>
	public CheatEngineArchitecture TargetArchitecture
	{
		get => Platform.TargetArchitecture;
	}

	/// <summary>Gets the pointer width implied by the observed target architecture, or unknown.</summary>
	public PointerSize TargetPointerSize
	{
		get => Platform.TargetPointerSize;
	}

	/// <summary>Gets the target ABI observed from CE's ABI global, or unknown.</summary>
	public TargetAbi TargetAbi
	{
		get => Platform.TargetAbi;
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
