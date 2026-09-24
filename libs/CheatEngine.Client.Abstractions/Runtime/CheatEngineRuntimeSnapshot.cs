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
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="epoch" /> is negative.</exception>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="versionInfo" /> is uninitialized, or a capability collection is <see langword="null" />.
	/// </exception>
	public CheatEngineRuntimeSnapshot(
		long epoch,
		CheatEngineRuntimeVersionInfo versionInfo,
		CheatEngineRuntimePlatformInfo platformInfo,
		RuntimeCapabilities sdkCapabilities,
		ClientCapabilities clientCapabilities)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(epoch);
		ArgumentNullException.ThrowIfNull(versionInfo.ClientAssemblyVersion);
		ArgumentNullException.ThrowIfNull(versionInfo.SdkAssemblyVersion);

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
	public double? ObservedCheatEngineVersion => Version.ObservedCheatEngineVersion;

	/// <summary>Gets the complete CE build against which this Client release was qualified.</summary>
	public CheatEngineVersion QualifiedCheatEngineBaseline => Version.QualifiedCheatEngineBaseline;

	/// <summary>Gets the assembly version of this Client abstraction assembly.</summary>
	public Version ClientAssemblyVersion => Version.ClientAssemblyVersion;

	/// <summary>Gets the assembly version of the SDK runtime-contract assembly.</summary>
	public Version SdkAssemblyVersion => Version.SdkAssemblyVersion;

	/// <summary>Gets the CE host architecture observed from CE's system-architecture global.</summary>
	public CheatEngineArchitecture SystemArchitecture => Platform.SystemArchitecture;

	/// <summary>Gets the target ISA derived from Cheat Engine's ISA-family and 64-bit facts, or unknown.</summary>
	public CheatEngineArchitecture TargetArchitecture => Platform.TargetArchitecture;

	/// <summary>
	///     Gets the process width of the selected target (the width Cheat Engine's <c>readPointer</c> uses), or unknown.
	///     It is observed from <c>targetIs64Bit</c>, not derived from <see cref="TargetArchitecture" />, and it is not
	///     Cheat Engine's configured pointer size.
	/// </summary>
	public PointerSize TargetPointerSize => Platform.TargetPointerSize;

	/// <summary>
	///     Gets Cheat Engine's configured pointer size for the current attachment as a width when it is 4 or 8 bytes,
	///     otherwise unknown. See <see cref="CheatEngineRuntimePlatformInfo.ConfiguredPointerSizeBytes" /> for the raw
	///     value; it is independent of <see cref="TargetPointerSize" />.
	/// </summary>
	public PointerSize ConfiguredPointerSize => Platform.ConfiguredPointerSize;

	/// <summary>Gets the target ABI observed from CE's ABI global, or unknown.</summary>
	public TargetAbi TargetAbi => Platform.TargetAbi;

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
												(QualifiedCheatEngineBaseline.Minor / 10d) &&
												observed < QualifiedCheatEngineBaseline.Major +
												((QualifiedCheatEngineBaseline.Minor + 1) / 10d);
}
