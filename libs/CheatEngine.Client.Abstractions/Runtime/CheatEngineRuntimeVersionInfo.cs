using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Runtime;

/// <summary>Immutable version observations captured during one active Cheat Engine activation.</summary>
/// <remarks>
///     <see cref="CheatEngineVersion" /> is the complete four-part file version Cheat Engine reports through
///     <c>getCheatEngineFileVersion</c>, read by CheatEngine.SDK. It is never derived from the coarse floating-point
///     number of <c>getCEVersion</c>, and versions are compared component by component as integers, so 7.10 is not 7.1.
///     <see cref="SdkPackageVersion" /> identifies the CheatEngine.SDK package actually loaded, and
///     <see cref="IsReviewedSdkPackage" /> says whether it is exactly the package this Client build was reviewed with.
/// </remarks>
public readonly record struct CheatEngineRuntimeVersionInfo
{
	/// <summary>The version every version property reports for the <see langword="default" /> value: 0.0.</summary>
	private static readonly Version NoVersion = new(0, 0);

	private readonly Version? _clientAssemblyVersion;
	private readonly Version? _sdkAssemblyVersion;

	/// <summary>Creates runtime version observations from independently observed facts.</summary>
	/// <param name="cheatEngineVersion">
	///     The complete Cheat Engine file version, or <see langword="null" /> when it was not observed.
	/// </param>
	/// <param name="qualifiedCheatEngineBaseline">The complete Cheat Engine build this Client release is qualified on.</param>
	/// <param name="clientAssemblyVersion">The assembly version of the Client abstraction assembly.</param>
	/// <param name="sdkAssemblyVersion">The assembly version of the SDK runtime-contract assembly.</param>
	/// <param name="sdkPackageVersion">
	///     The informational version of the loaded CheatEngine.SDK.Engine assembly (its package version and source
	///     commit), or <see langword="null" /> when it declares none.
	/// </param>
	/// <param name="isReviewedSdkPackage">
	///     Whether the loaded CheatEngine.SDK is exactly the package this Client build consumed and was reviewed with.
	/// </param>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="clientAssemblyVersion" /> or <paramref name="sdkAssemblyVersion" /> is
	///     <see langword="null" />.
	/// </exception>
	/// <exception cref="ArgumentException">
	///     <paramref name="sdkPackageVersion" /> is empty or white space, or <paramref name="isReviewedSdkPackage" /> is
	///     <see langword="true" /> without a package version.
	/// </exception>
	public CheatEngineRuntimeVersionInfo(
		CheatEngineVersion? cheatEngineVersion,
		CheatEngineVersion qualifiedCheatEngineBaseline,
		Version clientAssemblyVersion,
		Version sdkAssemblyVersion,
		string? sdkPackageVersion,
		bool isReviewedSdkPackage)
	{
		if (sdkPackageVersion is not null && string.IsNullOrWhiteSpace(sdkPackageVersion))
		{
			throw new ArgumentException("An SDK package version must be null or non-blank.", nameof(sdkPackageVersion));
		}

		if (isReviewedSdkPackage && sdkPackageVersion is null)
		{
			throw new ArgumentException("A reviewed SDK package requires its package version.",
				nameof(isReviewedSdkPackage));
		}

		CheatEngineVersion = cheatEngineVersion;
		QualifiedCheatEngineBaseline = qualifiedCheatEngineBaseline;
		_clientAssemblyVersion =
			clientAssemblyVersion ?? throw new ArgumentNullException(nameof(clientAssemblyVersion));
		_sdkAssemblyVersion = sdkAssemblyVersion ?? throw new ArgumentNullException(nameof(sdkAssemblyVersion));
		SdkPackageVersion = sdkPackageVersion;
		IsReviewedSdkPackage = isReviewedSdkPackage;
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
	/// <remarks>0.0 for the <see langword="default" /> value.</remarks>
	public Version ClientAssemblyVersion => _clientAssemblyVersion ?? NoVersion;

	/// <summary>Gets the assembly version of the SDK runtime-contract assembly.</summary>
	/// <remarks>0.0 for the <see langword="default" /> value.</remarks>
	public Version SdkAssemblyVersion => _sdkAssemblyVersion ?? NoVersion;

	/// <summary>
	///     Gets the informational version of the loaded CheatEngine.SDK.Engine assembly, for example
	///     <c>2.0.0+&lt;commit&gt;</c>, or <see langword="null" /> when it declares none.
	/// </summary>
	public string? SdkPackageVersion
	{
		get;
	}

	/// <summary>
	///     Gets whether the loaded CheatEngine.SDK is exactly the package this Client build consumed and was reviewed
	///     with; another release of the supported major can still satisfy the package gate.
	/// </summary>
	public bool IsReviewedSdkPackage
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

	/// <summary>Gets whether this value is the uninitialized <see langword="default" />.</summary>
	internal bool IsDefault => _clientAssemblyVersion is null;
}
