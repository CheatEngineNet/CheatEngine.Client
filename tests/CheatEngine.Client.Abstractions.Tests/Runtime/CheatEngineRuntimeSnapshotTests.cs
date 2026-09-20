using System.Globalization;

using CheatEngine.Client.Runtime;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Tests.Runtime;

public sealed class CheatEngineRuntimeSnapshotTests
{
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

	[Theory]
	[InlineData(double.NaN)]
	[InlineData(double.PositiveInfinity)]
	[InlineData(-0.1d)]
	public void SnapshotRejectsInvalidObservedCeVersion(double observedVersion)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Create(observedVersion));
	}

	[Fact]
	public void SnapshotAllowsAnUnavailableObservedVersionAndKeepsUnknownTargetFacts()
	{
		CheatEngineRuntimeSnapshot snapshot = Create(null);

		Assert.Null(snapshot.ObservedCheatEngineVersion);
		Assert.Equal(CheatEngineArchitecture.Unknown, snapshot.TargetArchitecture);
		Assert.Equal(PointerSize.Unknown, snapshot.TargetPointerSize);
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(long.MinValue)]
	public void SnapshotRejectsNegativeActivationEpoch(long epoch)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Create(epoch: epoch));
	}

	private static CheatEngineRuntimeSnapshot Create(double? observedVersion = 7.7d, long epoch = 42)
	{
		return new CheatEngineRuntimeSnapshot(
			epoch,
			observedVersion,
			CheatEngineVersion.Ce77010621,
			new Version(0, 1, 0),
			new Version(1, 0, 0),
			CheatEngineArchitecture.X64,
			CheatEngineArchitecture.Unknown,
			PointerSize.Unknown,
			TargetAbi.Windows,
			RuntimeCapabilities.Empty,
			ClientCapabilities.Empty);
	}
}
