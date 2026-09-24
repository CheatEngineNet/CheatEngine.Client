using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Runtime;

/// <summary>Immutable version observations captured during one active Cheat Engine activation.</summary>
/// <remarks>
///     <see cref="CheatEngineVersion" /> is the complete four-part file version Cheat Engine reports through
///     <c>getCheatEngineFileVersion</c>, read by CheatEngine.SDK. It is never derived from the coarse floating-point
///     number of <c>getCEVersion</c>, and versions are compared component by component as integers, so 7.10 is not 7.1.
/// </remarks>
public readonly record struct CheatEngineRuntimeVersionInfo
{
	/// <summary>Creates runtime version observations from independently observed facts.</summary>
	/// <param name="cheatEngineVersion">
	///     The complete Cheat Engine file version, or <see langword="null" /> when it was not observed.
	/// </param>
	/// <param name="qualifiedCheatEngineBaseline">The complete Cheat Engine build this Client release is qualified on.</param>
	/// <param name="clientAssemblyVersion">The assembly version of the Client abstraction assembly.</param>
	/// <param name="sdkAssemblyVersion">The assembly version of the SDK runtime-contract assembly.</param>
	/// <exception cref="ArgumentNullException">An assembly version is <see langword="null" />.</exception>
	public CheatEngineRuntimeVersionInfo(
		CheatEngineVersion? cheatEngineVersion,
		CheatEngineVersion qualifiedCheatEngineBaseline,
		Version clientAssemblyVersion,
		Version sdkAssemblyVersion)
	{
		CheatEngineVersion = cheatEngineVersion;
		QualifiedCheatEngineBaseline = qualifiedCheatEngineBaseline;
		ClientAssemblyVersion = clientAssemblyVersion ?? throw new ArgumentNullException(nameof(clientAssemblyVersion));
		SdkAssemblyVersion = sdkAssemblyVersion ?? throw new ArgumentNullException(nameof(sdkAssemblyVersion));
	}

	/// <summary>
	///     Gets the complete Cheat Engine file version (<c>getCheatEngineFileVersion</c>), or <see langword="null" /> when
	///     Cheat Engine did not report one.
	/// </summary>
	public CheatEngineVersion? CheatEngineVersion
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

	/// <summary>
	///     Gets whether the observed Cheat Engine version has the major and minor components of the qualified baseline,
	///     compared as integers; <see langword="false" /> when no version was observed.
	/// </summary>
	public bool IsOnQualifiedCheatEngineLine => CheatEngineVersion is { } observed &&
												observed.Major == QualifiedCheatEngineBaseline.Major &&
												observed.Minor == QualifiedCheatEngineBaseline.Minor;
}
