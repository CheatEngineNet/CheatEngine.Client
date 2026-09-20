using System.Globalization;

using CheatEngine.Client.Runtime;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Tests.Runtime;

public sealed class CheatEngineRuntimeSnapshotTests
{
	/// <summary>Keeps an observed coarse CE version distinct from its qualified four-part release baseline.</summary>
	[Fact]
	public void SnapshotKeepsObservedCeVersionSeparateFromTheQualifiedFourPartBaseline()
	{
		CheatEngineRuntimeSnapshot snapshot = Create();

		Assert.Equal(7.7d, snapshot.ObservedCheatEngineVersion);
		Assert.Equal(new CheatEngineVersion(7, 7, 0, 10621), snapshot.QualifiedCheatEngineBaseline);
		Assert.NotEqual(
			snapshot.ObservedCheatEngineVersion!.Value.ToString(CultureInfo.InvariantCulture),
			snapshot.QualifiedCheatEngineBaseline.ToString());
		Assert.True(snapshot.IsOnQualifiedCheatEngineLine);
	}

	/// <summary>Forwards grouped runtime observations through the established leaf-property compatibility surface.</summary>
	[Fact]
	public void SnapshotForwardsVersionAndPlatformComponentsToItsExistingLeafProperties()
	{
		Version clientAssemblyVersion = new(0, 1, 2, 3);
		Version sdkAssemblyVersion = new(1, 2, 3, 4);
		CheatEngineRuntimeVersionInfo version = new(7.7d, CheatEngineVersion.Ce77010621, clientAssemblyVersion, sdkAssemblyVersion);
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
		Assert.Equal(version.ObservedCheatEngineVersion, snapshot.ObservedCheatEngineVersion);
		Assert.Equal(version.QualifiedCheatEngineBaseline, snapshot.QualifiedCheatEngineBaseline);
		Assert.Same(clientAssemblyVersion, snapshot.ClientAssemblyVersion);
		Assert.Same(sdkAssemblyVersion, snapshot.SdkAssemblyVersion);
		Assert.Equal(platform.SystemArchitecture, snapshot.SystemArchitecture);
		Assert.Equal(platform.TargetArchitecture, snapshot.TargetArchitecture);
		Assert.Equal(platform.TargetPointerSize, snapshot.TargetPointerSize);
		Assert.Equal(platform.TargetAbi, snapshot.TargetAbi);
	}

	/// <summary>Rejects non-finite or negative observed CE version values.</summary>
	[Theory]
	[InlineData(double.NaN)]
	[InlineData(double.PositiveInfinity)]
	[InlineData(-0.1d)]
	public void VersionInfoRejectsInvalidObservedCeVersion(double observedVersion)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => CreateVersionInfo(observedVersion));
	}

	/// <summary>Rejects absent assembly versions that would leave a version observation uninitialized.</summary>
	[Fact]
	public void VersionInfoRejectsNullAssemblyVersions()
	{
		ArgumentNullException clientVersion = Assert.Throws<ArgumentNullException>(() => new CheatEngineRuntimeVersionInfo(
			7.7d,
			CheatEngineVersion.Ce77010621,
			Null<Version>(),
			new Version(1, 0, 0)));
		ArgumentNullException sdkVersion = Assert.Throws<ArgumentNullException>(() => new CheatEngineRuntimeVersionInfo(
			7.7d,
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

		Assert.Null(snapshot.ObservedCheatEngineVersion);
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
		ArgumentNullException sdkCapabilities = Assert.Throws<ArgumentNullException>(() => new CheatEngineRuntimeSnapshot(
			42,
			CreateVersionInfo(),
			CreatePlatformInfo(),
			Null<RuntimeCapabilities>(),
			ClientCapabilities.Empty));
		ArgumentNullException clientCapabilities = Assert.Throws<ArgumentNullException>(() => new CheatEngineRuntimeSnapshot(
			42,
			CreateVersionInfo(),
			CreatePlatformInfo(),
			RuntimeCapabilities.Empty,
			Null<ClientCapabilities>()));

		Assert.Equal("sdkCapabilities", sdkCapabilities.ParamName);
		Assert.Equal("clientCapabilities", clientCapabilities.ParamName);
	}

	/// <summary>Rejects target pointer widths that disagree with the observed target architecture.</summary>
	[Theory]
	[InlineData(CheatEngineArchitecture.X64, 4)]
	[InlineData(CheatEngineArchitecture.X86, 8)]
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

	private static CheatEngineRuntimeSnapshot Create(double? observedVersion = 7.7d, long epoch = 42)
	{
		return new CheatEngineRuntimeSnapshot(
			epoch,
			CreateVersionInfo(observedVersion),
			CreatePlatformInfo(),
			RuntimeCapabilities.Empty,
			ClientCapabilities.Empty);
	}

	private static CheatEngineRuntimeVersionInfo CreateVersionInfo(double? observedVersion = 7.7d)
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

	private static T Null<T>() where T : class => default!;
}
