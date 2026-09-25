using CheatEngine.Client.Runtime;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Abstractions.Tests.Runtime;

public sealed class CheatEngineRuntimeSnapshotTests
{
	/// <summary>Keeps the version, platform, capability and Lua groups exactly as they were supplied.</summary>
	[Fact]
	public void SnapshotKeepsItsFourGroups()
	{
		CheatEngineRuntimeVersionInfo version = CreateVersionInfo(new CheatEngineVersion(7, 7, 0, 9999));
		CheatEngineRuntimePlatformInfo platform = CreatePlatformInfo(TargetBackend.LocalProcess,
			CheatEngineArchitecture.X86, PointerSize.Bit32, 4);
		ClientCapabilities capabilities = ClientCapabilities.Empty;
		CheatEngineRuntimeLuaInfo lua = new(true);

		CheatEngineRuntimeSnapshot snapshot = new(42, version, platform, capabilities, lua);

		Assert.Equal(42, snapshot.Epoch);
		Assert.Equal(version, snapshot.Version);
		Assert.Equal(platform, snapshot.Platform);
		Assert.Same(capabilities, snapshot.Capabilities);
		Assert.True(snapshot.Lua.ExternalStateResetDetected);
	}

	/// <summary>Keeps the observed four-part CE file version distinct from the qualified release baseline.</summary>
	[Fact]
	public void VersionInfoKeepsTheObservedFileVersionSeparateFromTheQualifiedBaseline()
	{
		CheatEngineRuntimeVersionInfo version = CreateVersionInfo(new CheatEngineVersion(7, 7, 0, 9999));

		Assert.Equal(new CheatEngineVersion(7, 7, 0, 9999), version.CheatEngineVersion);
		Assert.Equal(new CheatEngineVersion(7, 7, 0, 10621), version.QualifiedCheatEngineBaseline);
		Assert.True(version.IsOnQualifiedCheatEngineLine);
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
			new CheatEngineVersion(baselineMajor, baselineMinor, 0, 10621), new Version(1, 0, 0), new Version(2, 0, 0),
			null, false);

		Assert.Equal(expected, version.IsOnQualifiedCheatEngineLine);
	}

	/// <summary>An unobserved version is never on the qualified line.</summary>
	[Fact]
	public void VersionInfoAllowsAnUnobservedCheatEngineVersion()
	{
		CheatEngineRuntimeVersionInfo version = CreateVersionInfo(null);

		Assert.Null(version.CheatEngineVersion);
		Assert.False(version.IsOnQualifiedCheatEngineLine);
	}

	/// <summary>Reports the loaded CheatEngine.SDK package and whether it is exactly the reviewed one.</summary>
	[Theory]
	[InlineData("2.0.0+325c47b573f8bd39a247f1d0101f110fa36c1696", true)]
	[InlineData("2.0.1", false)]
	[InlineData(null, false)]
	public void VersionInfoReportsTheLoadedSdkPackage(string? packageVersion, bool reviewed)
	{
		CheatEngineRuntimeVersionInfo version = new(CheatEngineVersion.Ce77010621, CheatEngineVersion.Ce77010621,
			new Version(1, 0, 0), new Version(2, 0, 0), packageVersion, reviewed);

		Assert.Equal(packageVersion, version.SdkPackageVersion);
		Assert.Equal(reviewed, version.IsReviewedSdkPackage);
	}

	/// <summary>A reviewed package needs its version, and a package version is never blank.</summary>
	[Fact]
	public void VersionInfoRejectsABlankPackageVersionAndAReviewedPackageWithoutOne()
	{
		ArgumentException blank = Assert.Throws<ArgumentException>(() => new CheatEngineRuntimeVersionInfo(
			null, CheatEngineVersion.Ce77010621, new Version(1, 0, 0), new Version(2, 0, 0), " ", false));
		ArgumentException reviewed = Assert.Throws<ArgumentException>(() => new CheatEngineRuntimeVersionInfo(
			null, CheatEngineVersion.Ce77010621, new Version(1, 0, 0), new Version(2, 0, 0), null, true));

		Assert.Equal("sdkPackageVersion", blank.ParamName);
		Assert.Equal("isReviewedSdkPackage", reviewed.ParamName);
	}

	/// <summary>Rejects absent assembly versions that would leave a version observation uninitialized.</summary>
	[Fact]
	public void VersionInfoRejectsNullAssemblyVersions()
	{
		ArgumentNullException clientVersion = Assert.Throws<ArgumentNullException>(() =>
			new CheatEngineRuntimeVersionInfo(null, CheatEngineVersion.Ce77010621, Null<Version>(),
				new Version(1, 0, 0), null, false));
		ArgumentNullException sdkVersion = Assert.Throws<ArgumentNullException>(() =>
			new CheatEngineRuntimeVersionInfo(null, CheatEngineVersion.Ce77010621, new Version(0, 1, 0),
				Null<Version>(), null, false));

		Assert.Equal("clientAssemblyVersion", clientVersion.ParamName);
		Assert.Equal("sdkAssemblyVersion", sdkVersion.ParamName);
	}

	/// <summary>Rejects a snapshot epoch that cannot identify a real activation.</summary>
	[Theory]
	[InlineData(-1)]
	[InlineData(long.MinValue)]
	public void SnapshotRejectsNegativeActivationEpoch(long epoch)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new CheatEngineRuntimeSnapshot(epoch,
			CreateVersionInfo(CheatEngineVersion.Ce77010621), CreatePlatformInfo(), ClientCapabilities.Empty, default));
	}

	/// <summary>Rejects an absent capability collection instead of accepting an incomplete runtime snapshot.</summary>
	[Fact]
	public void SnapshotRejectsANullCapabilityCollection()
	{
		ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => new CheatEngineRuntimeSnapshot(42,
			CreateVersionInfo(CheatEngineVersion.Ce77010621), CreatePlatformInfo(), Null<ClientCapabilities>(),
			default));

		Assert.Equal("capabilities", exception.ParamName);
	}

	/// <summary>Rejects the default version-info value before it can produce a partially initialized snapshot.</summary>
	[Fact]
	public void SnapshotRejectsDefaultVersionInfo()
	{
		ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => new CheatEngineRuntimeSnapshot(
			42, default, CreatePlatformInfo(), ClientCapabilities.Empty, default));

		Assert.Equal("version", exception.ParamName);
	}

	/// <summary>Keeps every host and target fact as supplied; none is inferred from another (audit F08).</summary>
	[Fact]
	public void PlatformInfoKeepsEveryObservedFact()
	{
		CheatEngineRuntimePlatformInfo platform = new(CheatEngineOperatingSystem.Windows, true,
			CheatEngineArchitecture.X64, TargetBackend.CEServer, CheatEngineArchitecture.Arm64, PointerSize.Bit64,
			TargetAbi.Unix, true, 8);

		Assert.Equal(CheatEngineOperatingSystem.Windows, platform.HostOperatingSystem);
		Assert.True(platform.IsCheatEngine64Bit);
		Assert.Equal(CheatEngineArchitecture.X64, platform.SystemArchitecture);
		Assert.Equal(TargetBackend.CEServer, platform.TargetBackend);
		Assert.Equal(CheatEngineArchitecture.Arm64, platform.TargetArchitecture);
		Assert.Equal(PointerSize.Bit64, platform.TargetBitness);
		Assert.Equal(TargetAbi.Unix, platform.TargetAbi);
		Assert.True(platform.TargetIsAndroid);
		Assert.Equal(8, platform.ConfiguredPointerSizeBytes);
		Assert.False(platform.ConfiguredPointerSizeDiffersFromBitness);
	}

	/// <summary>The default platform keeps every fact unknown.</summary>
	[Fact]
	public void DefaultPlatformInfoKeepsEveryFactUnknown()
	{
		CheatEngineRuntimePlatformInfo platform = default;

		Assert.Equal(CheatEngineOperatingSystem.Unknown, platform.HostOperatingSystem);
		Assert.Null(platform.IsCheatEngine64Bit);
		Assert.Equal(TargetBackend.Unknown, platform.TargetBackend);
		Assert.Equal(PointerSize.Unknown, platform.TargetBitness);
		Assert.Null(platform.TargetIsAndroid);
		Assert.Null(platform.ConfiguredPointerSizeBytes);
		Assert.Null(platform.ConfiguredPointerSizeDiffersFromBitness);
	}

	/// <summary>Rejects a known target bitness that disagrees with a known target architecture.</summary>
	[Theory]
	[InlineData(CheatEngineArchitecture.X64, 4)]
	[InlineData(CheatEngineArchitecture.X86, 8)]
	[InlineData(CheatEngineArchitecture.Arm64, 4)]
	[InlineData(CheatEngineArchitecture.Arm32, 8)]
	public void PlatformInfoRejectsABitnessThatDoesNotMatchTheTargetArchitecture(
		CheatEngineArchitecture targetArchitecture,
		int pointerBytes)
	{
		ArgumentException exception = Assert.Throws<ArgumentException>(() => CreatePlatformInfo(
			TargetBackend.LocalProcess, targetArchitecture, new PointerSize(pointerBytes), null));

		Assert.Equal("targetBitness", exception.ParamName);
	}

	/// <summary>Keeps a known bitness when the ISA could not be derived (Q32: never infer the ISA from the width).</summary>
	[Fact]
	[Trait("Qualification", "Q32")]
	public void PlatformInfoAcceptsAnUnknownIsaWithAKnownBitness()
	{
		CheatEngineRuntimePlatformInfo platform = CreatePlatformInfo(TargetBackend.LocalProcess,
			CheatEngineArchitecture.Unknown, PointerSize.Bit64, null);

		Assert.Equal(CheatEngineArchitecture.Unknown, platform.TargetArchitecture);
		Assert.Equal(PointerSize.Bit64, platform.TargetBitness);
	}

	/// <summary>Keeps a known ISA when the bitness was not observed.</summary>
	[Theory]
	[InlineData(CheatEngineArchitecture.X86)]
	[InlineData(CheatEngineArchitecture.X64)]
	[InlineData(CheatEngineArchitecture.Arm32)]
	[InlineData(CheatEngineArchitecture.Arm64)]
	public void PlatformInfoAcceptsAKnownIsaWithAnUnknownBitness(CheatEngineArchitecture architecture)
	{
		CheatEngineRuntimePlatformInfo platform = CreatePlatformInfo(TargetBackend.LocalProcess, architecture,
			PointerSize.Unknown, 4);

		Assert.Equal(architecture, platform.TargetArchitecture);
		Assert.Equal(PointerSize.Unknown, platform.TargetBitness);
		Assert.Null(platform.ConfiguredPointerSizeDiffersFromBitness);
	}

	/// <summary>Keeps Cheat Engine's configured pointer size separate from the bitness (Q31, spike C3 D3).</summary>
	[Theory]
	[Trait("Qualification", "Q31")]
	[InlineData(4, true)]
	[InlineData(2, true)]
	[InlineData(8, false)]
	public void PlatformInfoKeepsARawConfiguredPointerSizeApartFromTheBitness(int configured, bool differs)
	{
		CheatEngineRuntimePlatformInfo platform = CreatePlatformInfo(TargetBackend.LocalProcess,
			CheatEngineArchitecture.X64, PointerSize.Bit64, configured);

		Assert.Equal(PointerSize.Bit64, platform.TargetBitness);
		Assert.Equal(configured, platform.ConfiguredPointerSizeBytes);
		Assert.Equal(configured switch
		{
			4 => PointerSize.Bit32,
			8 => PointerSize.Bit64,
			_ => PointerSize.Unknown
		}, platform.ConfiguredPointerSize);
		Assert.Equal(differs, platform.ConfiguredPointerSizeDiffersFromBitness);
	}

	/// <summary>Reports the external Lua state reset fact as supplied.</summary>
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void LuaInfoReportsTheExternalStateResetFact(bool detected)
	{
		Assert.Equal(detected, new CheatEngineRuntimeLuaInfo(detected).ExternalStateResetDetected);
		Assert.False(default(CheatEngineRuntimeLuaInfo).ExternalStateResetDetected);
	}

	private static CheatEngineRuntimeVersionInfo CreateVersionInfo(CheatEngineVersion? observedVersion)
	{
		return new CheatEngineRuntimeVersionInfo(observedVersion, CheatEngineVersion.Ce77010621, new Version(0, 1, 0),
			new Version(1, 0, 0), null, false);
	}

	private static CheatEngineRuntimePlatformInfo CreatePlatformInfo()
	{
		return CreatePlatformInfo(TargetBackend.Unknown, CheatEngineArchitecture.Unknown, PointerSize.Unknown, null);
	}

	private static CheatEngineRuntimePlatformInfo CreatePlatformInfo(TargetBackend backend,
		CheatEngineArchitecture architecture, PointerSize bitness, int? configured)
	{
		return new CheatEngineRuntimePlatformInfo(CheatEngineOperatingSystem.Windows, true, CheatEngineArchitecture.X64,
			backend, architecture, bitness, TargetAbi.Windows, false, configured);
	}

	private static T Null<T>() where T : class
	{
		return default!;
	}
}
