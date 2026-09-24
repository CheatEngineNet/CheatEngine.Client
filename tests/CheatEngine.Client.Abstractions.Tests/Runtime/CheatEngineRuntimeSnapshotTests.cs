using CheatEngine.Client.Runtime;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Abstractions.Tests.Runtime;

public sealed class CheatEngineRuntimeSnapshotTests
{
	/// <summary>Keeps the observed four-part CE file version distinct from the qualified release baseline.</summary>
	[Fact]
	public void SnapshotKeepsTheObservedFileVersionSeparateFromTheQualifiedBaseline()
	{
		CheatEngineRuntimeSnapshot snapshot = Create(new CheatEngineVersion(7, 7, 0, 9999));

		Assert.Equal(new CheatEngineVersion(7, 7, 0, 9999), snapshot.CheatEngineVersion);
		Assert.Equal(new CheatEngineVersion(7, 7, 0, 10621), snapshot.QualifiedCheatEngineBaseline);
		Assert.True(snapshot.IsOnQualifiedCheatEngineLine);
	}

	/// <summary>
	///     Compares the major and minor components as integers: 7.10 is its own line, never 7.1 as a coarse decimal
	///     number would read it.
	/// </summary>
	[Theory]
	[InlineData(7, 7, 7, 7, true)]
	[InlineData(7, 7, 7, 10, false)]
	[InlineData(7, 7, 7, 1, false)]
	[InlineData(7, 7, 8, 7, false)]
	[InlineData(7, 10, 7, 10, true)]
	[InlineData(7, 10, 7, 1, false)]
	[InlineData(7, 10, 7, 11, false)]
	public void IsOnQualifiedCheatEngineLineComparesMajorAndMinorAsIntegers(int baselineMajor, int baselineMinor,
		int major, int minor, bool expected)
	{
		CheatEngineRuntimeVersionInfo version = new(new CheatEngineVersion(major, minor, 0, 1),
			new CheatEngineVersion(baselineMajor, baselineMinor, 0, 10621), new Version(1, 0, 0), new Version(2, 0, 0));

		Assert.Equal(expected, version.IsOnQualifiedCheatEngineLine);
	}

	/// <summary>Forwards grouped runtime observations through the established leaf-property compatibility surface.</summary>
	[Fact]
	public void SnapshotForwardsVersionAndPlatformComponentsToItsExistingLeafProperties()
	{
		Version clientAssemblyVersion = new(0, 1, 2, 3);
		Version sdkAssemblyVersion = new(1, 2, 3, 4);
		CheatEngineRuntimeVersionInfo version = new(CheatEngineVersion.Ce77010621, CheatEngineVersion.Ce77010621,
			clientAssemblyVersion, sdkAssemblyVersion);
		CheatEngineRuntimePlatformInfo platform = new(
			CheatEngineArchitecture.X64,
			CheatEngineArchitecture.X86,
			PointerSize.Bit32,
			TargetAbi.Windows);
		CheatEngineRuntimeSnapshot snapshot = new(
			42,
			version,
			platform,
			RuntimeCapabilities.Empty,
			ClientCapabilities.Empty);

		Assert.Equal(version, snapshot.Version);
		Assert.Equal(platform, snapshot.Platform);
		Assert.Equal(version.CheatEngineVersion, snapshot.CheatEngineVersion);
		Assert.Equal(version.QualifiedCheatEngineBaseline, snapshot.QualifiedCheatEngineBaseline);
		Assert.Same(clientAssemblyVersion, snapshot.ClientAssemblyVersion);
		Assert.Same(sdkAssemblyVersion, snapshot.SdkAssemblyVersion);
		Assert.Equal(platform.SystemArchitecture, snapshot.SystemArchitecture);
		Assert.Equal(platform.TargetArchitecture, snapshot.TargetArchitecture);
		Assert.Equal(platform.TargetPointerSize, snapshot.TargetPointerSize);
		Assert.Equal(platform.TargetAbi, snapshot.TargetAbi);
	}

	/// <summary>Rejects absent assembly versions that would leave a version observation uninitialized.</summary>
	[Fact]
	public void VersionInfoRejectsNullAssemblyVersions()
	{
		ArgumentNullException clientVersion = Assert.Throws<ArgumentNullException>(() =>
			new CheatEngineRuntimeVersionInfo(
				CheatEngineVersion.Ce77010621,
				CheatEngineVersion.Ce77010621,
				Null<Version>(),
				new Version(1, 0, 0)));
		ArgumentNullException sdkVersion = Assert.Throws<ArgumentNullException>(() => new CheatEngineRuntimeVersionInfo(
			CheatEngineVersion.Ce77010621,
			CheatEngineVersion.Ce77010621,
			new Version(0, 1, 0),
			Null<Version>()));

		Assert.Equal("clientAssemblyVersion", clientVersion.ParamName);
		Assert.Equal("sdkAssemblyVersion", sdkVersion.ParamName);
	}

	/// <summary>Retains unavailable version and target facts without inferring values that CE did not provide.</summary>
	[Fact]
	public void SnapshotAllowsAnUnavailableObservedVersionAndKeepsUnknownTargetFacts()
	{
		CheatEngineRuntimeSnapshot snapshot = Create(null);

		Assert.Null(snapshot.CheatEngineVersion);
		Assert.False(snapshot.IsOnQualifiedCheatEngineLine);
		Assert.Equal(CheatEngineArchitecture.Unknown, snapshot.TargetArchitecture);
		Assert.Equal(PointerSize.Unknown, snapshot.TargetPointerSize);
	}

	/// <summary>Rejects a snapshot epoch that cannot identify a real activation.</summary>
	[Theory]
	[InlineData(-1)]
	[InlineData(long.MinValue)]
	public void SnapshotRejectsNegativeActivationEpoch(long epoch)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Create(epoch: epoch));
	}

	/// <summary>Rejects absent capability collections instead of accepting an incomplete runtime snapshot.</summary>
	[Fact]
	public void SnapshotRejectsNullCapabilityCollections()
	{
		ArgumentNullException sdkCapabilities = Assert.Throws<ArgumentNullException>(() =>
			new CheatEngineRuntimeSnapshot(
				42,
				CreateVersionInfo(),
				CreatePlatformInfo(),
				Null<RuntimeCapabilities>(),
				ClientCapabilities.Empty));
		ArgumentNullException clientCapabilities = Assert.Throws<ArgumentNullException>(() =>
			new CheatEngineRuntimeSnapshot(
				42,
				CreateVersionInfo(),
				CreatePlatformInfo(),
				RuntimeCapabilities.Empty,
				Null<ClientCapabilities>()));

		Assert.Equal("sdkCapabilities", sdkCapabilities.ParamName);
		Assert.Equal("clientCapabilities", clientCapabilities.ParamName);
	}

	/// <summary>Rejects a known target pointer width that disagrees with a known target architecture.</summary>
	[Theory]
	[InlineData(CheatEngineArchitecture.X64, 4)]
	[InlineData(CheatEngineArchitecture.X86, 8)]
	[InlineData(CheatEngineArchitecture.Arm64, 4)]
	[InlineData(CheatEngineArchitecture.Arm32, 8)]
	public void PlatformInfoRejectsPointerSizeThatDoesNotMatchTargetArchitecture(
		CheatEngineArchitecture targetArchitecture,
		int pointerBytes)
	{
		ArgumentException exception = Assert.Throws<ArgumentException>(() => new CheatEngineRuntimePlatformInfo(
			CheatEngineArchitecture.X64,
			targetArchitecture,
			new PointerSize(pointerBytes),
			TargetAbi.Windows));

		Assert.Equal("targetPointerSize", exception.ParamName);
	}

	/// <summary>Accepts unknown architecture and pointer-size facts without inventing a target platform.</summary>
	[Fact]
	public void PlatformInfoAcceptsConsistentlyUnknownTargetArchitectureAndPointerSize()
	{
		CheatEngineRuntimePlatformInfo platform = new(
			CheatEngineArchitecture.Unknown,
			CheatEngineArchitecture.Unknown,
			PointerSize.Unknown,
			TargetAbi.Unknown);

		Assert.Equal(CheatEngineArchitecture.Unknown, platform.TargetArchitecture);
		Assert.Equal(PointerSize.Unknown, platform.TargetPointerSize);
	}

	/// <summary>Keeps a known process width when the ISA could not be derived (Q32: never infer the ISA from the width).</summary>
	[Fact]
	[Trait("Qualification", "Q32")]
	public void PlatformInfoAcceptsAnUnknownIsaWithAKnownProcessWidth()
	{
		CheatEngineRuntimePlatformInfo platform = new(
			CheatEngineArchitecture.X64,
			CheatEngineArchitecture.Unknown,
			PointerSize.Bit64,
			TargetAbi.Windows);

		Assert.Equal(CheatEngineArchitecture.Unknown, platform.TargetArchitecture);
		Assert.Equal(PointerSize.Bit64, platform.TargetPointerSize);
	}

	/// <summary>Keeps a known ISA when the process width was not observed.</summary>
	[Theory]
	[InlineData(CheatEngineArchitecture.X86)]
	[InlineData(CheatEngineArchitecture.X64)]
	[InlineData(CheatEngineArchitecture.Arm32)]
	[InlineData(CheatEngineArchitecture.Arm64)]
	public void PlatformInfoAcceptsAKnownIsaWithAnUnknownProcessWidth(CheatEngineArchitecture architecture)
	{
		CheatEngineRuntimePlatformInfo platform = new(
			CheatEngineArchitecture.X64,
			architecture,
			PointerSize.Unknown,
			TargetAbi.Windows);

		Assert.Equal(architecture, platform.TargetArchitecture);
		Assert.Equal(PointerSize.Unknown, platform.TargetPointerSize);
		Assert.Null(platform.ConfiguredPointerSizeDiffersFromTargetPointerSize);
	}

	/// <summary>Keeps Cheat Engine's configured pointer size separate from the process width (Q31, spike C3 D3).</summary>
	[Fact]
	[Trait("Qualification", "Q31")]
	public void PlatformInfoKeepsAConfiguredPointerSizeThatDiffersFromTheProcessWidth()
	{
		CheatEngineRuntimePlatformInfo platform = new(
			CheatEngineArchitecture.X64,
			CheatEngineArchitecture.X64,
			PointerSize.Bit64,
			TargetAbi.Windows,
			4);

		Assert.Equal(PointerSize.Bit64, platform.TargetPointerSize);
		Assert.Equal(4, platform.ConfiguredPointerSizeBytes);
		Assert.Equal(PointerSize.Bit32, platform.ConfiguredPointerSize);
		Assert.True(platform.ConfiguredPointerSizeDiffersFromTargetPointerSize);
	}

	/// <summary>Keeps any raw configured pointer size, because Cheat Engine accepts any integer (spike C3 D3(b)).</summary>
	[Fact]
	[Trait("Qualification", "Q31")]
	public void PlatformInfoKeepsARawConfiguredPointerSizeOutsideFourAndEight()
	{
		CheatEngineRuntimePlatformInfo platform = new(
			CheatEngineArchitecture.X64,
			CheatEngineArchitecture.X64,
			PointerSize.Bit64,
			TargetAbi.Windows,
			2);

		Assert.Equal(2, platform.ConfiguredPointerSizeBytes);
		Assert.Equal(PointerSize.Unknown, platform.ConfiguredPointerSize);
		Assert.True(platform.ConfiguredPointerSizeDiffersFromTargetPointerSize);
	}

	/// <summary>The four-argument constructor records that the configured pointer size was not observed.</summary>
	[Fact]
	public void LegacyPlatformInfoConstructorLeavesTheConfiguredPointerSizeAbsent()
	{
		CheatEngineRuntimePlatformInfo platform = new(
			CheatEngineArchitecture.X64,
			CheatEngineArchitecture.X64,
			PointerSize.Bit64,
			TargetAbi.Windows);

		Assert.Null(platform.ConfiguredPointerSizeBytes);
		Assert.Equal(PointerSize.Unknown, platform.ConfiguredPointerSize);
		Assert.Null(platform.ConfiguredPointerSizeDiffersFromTargetPointerSize);
		Assert.Equal(new CheatEngineRuntimePlatformInfo(
			CheatEngineArchitecture.X64,
			CheatEngineArchitecture.X64,
			PointerSize.Bit64,
			TargetAbi.Windows,
			null), platform);
	}

	/// <summary>The snapshot forwards the configured pointer size of its platform observations.</summary>
	[Fact]
	public void SnapshotForwardsTheConfiguredPointerSize()
	{
		CheatEngineRuntimeSnapshot snapshot = new(
			42,
			CreateVersionInfo(),
			new CheatEngineRuntimePlatformInfo(
				CheatEngineArchitecture.X64,
				CheatEngineArchitecture.X64,
				PointerSize.Bit64,
				TargetAbi.Windows,
				4),
			RuntimeCapabilities.Empty,
			ClientCapabilities.Empty);

		Assert.Equal(PointerSize.Bit32, snapshot.ConfiguredPointerSize);
		Assert.Equal(PointerSize.Bit64, snapshot.TargetPointerSize);
		Assert.Equal(snapshot.Platform.ConfiguredPointerSize, snapshot.ConfiguredPointerSize);
	}

	/// <summary>Rejects the default version-info value before it can produce a partially initialized snapshot.</summary>
	[Fact]
	public void SnapshotRejectsDefaultVersionInfo()
	{
		ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => new CheatEngineRuntimeSnapshot(
			42,
			default,
			CreatePlatformInfo(),
			RuntimeCapabilities.Empty,
			ClientCapabilities.Empty));

		Assert.Equal("versionInfo.ClientAssemblyVersion", exception.ParamName);
	}

	private static CheatEngineRuntimeSnapshot Create(long epoch = 42)
	{
		return Create(CheatEngineVersion.Ce77010621, epoch);
	}

	private static CheatEngineRuntimeSnapshot Create(CheatEngineVersion? observedVersion, long epoch = 42)
	{
		return new CheatEngineRuntimeSnapshot(
			epoch,
			CreateVersionInfo(observedVersion),
			CreatePlatformInfo(),
			RuntimeCapabilities.Empty,
			ClientCapabilities.Empty);
	}

	private static CheatEngineRuntimeVersionInfo CreateVersionInfo()
	{
		return CreateVersionInfo(CheatEngineVersion.Ce77010621);
	}

	private static CheatEngineRuntimeVersionInfo CreateVersionInfo(CheatEngineVersion? observedVersion)
	{
		return new CheatEngineRuntimeVersionInfo(
			observedVersion,
			CheatEngineVersion.Ce77010621,
			new Version(0, 1, 0),
			new Version(1, 0, 0));
	}

	private static CheatEngineRuntimePlatformInfo CreatePlatformInfo()
	{
		return new CheatEngineRuntimePlatformInfo(
			CheatEngineArchitecture.X64,
			CheatEngineArchitecture.Unknown,
			PointerSize.Unknown,
			TargetAbi.Windows);
	}

	private static T Null<T>() where T : class
	{
		return default!;
	}
}
