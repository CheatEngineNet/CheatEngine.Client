using CheatEngine.Client.Processes;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Abstractions.Tests.Processes;

public sealed class ProcessSnapshotTests
{
	[Fact]
	public void ProcessEnumerationRequestRejectsZeroMaximumAndAnEmptyNameFilter()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessEnumerationRequest(0));
		Assert.Throws<ArgumentException>(() => new ProcessEnumerationRequest(1, string.Empty));
	}

	[Fact]
	public void ProcessEnumerationResultNormalizesTheDefaultArrayAndRejectsEmptyTruncation()
	{
		ProcessEnumerationResult result = new(default, false);

		Assert.Empty(result.Processes);
		Assert.False(result.IsTruncated);
		Assert.Throws<ArgumentException>(() => new ProcessEnumerationResult([], true));
	}

	[Fact]
	public void SnapshotPreservesCopiedIdentityArchitectureAndSelectionEpoch()
	{
		ProcessSnapshot snapshot = new(
			new TargetProcessId(42),
			"fixture",
			"C:\\fixtures\\fixture.exe",
			CheatEngineArchitecture.X64,
			7);

		Assert.Equal(new TargetProcessId(42), snapshot.Id);
		Assert.Equal("fixture", snapshot.Name);
		Assert.Equal("C:\\fixtures\\fixture.exe", snapshot.ExecutablePath);
		Assert.Equal(CheatEngineArchitecture.X64, snapshot.TargetArchitecture);
		Assert.Equal(PointerSize.Bit64, snapshot.TargetPointerSize);
		Assert.Equal(7, snapshot.SelectionEpoch);
	}

	[Fact]
	public void LegacyShapeLeavesTargetFactsUnknownAtTheInitialSelectionEpoch()
	{
		ProcessSnapshot snapshot = new(new TargetProcessId(42), "fixture", null);

		Assert.Equal(CheatEngineArchitecture.Unknown, snapshot.TargetArchitecture);
		Assert.Equal(PointerSize.Unknown, snapshot.TargetPointerSize);
		Assert.Equal(0, snapshot.SelectionEpoch);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void SnapshotStoresTheObservedWidthInsteadOfDerivingIt()
	{
		ProcessSnapshot unknownIsa = new(
			new TargetProcessId(42),
			"fixture",
			null,
			CheatEngineArchitecture.Unknown,
			PointerSize.Bit64,
			3);
		ProcessSnapshot unknownWidth = new(
			new TargetProcessId(42),
			"fixture",
			null,
			CheatEngineArchitecture.X86,
			PointerSize.Unknown,
			3);

		Assert.Equal(CheatEngineArchitecture.Unknown, unknownIsa.TargetArchitecture);
		Assert.Equal(PointerSize.Bit64, unknownIsa.TargetPointerSize);
		Assert.Equal(CheatEngineArchitecture.X86, unknownWidth.TargetArchitecture);
		Assert.Equal(PointerSize.Unknown, unknownWidth.TargetPointerSize);
		Assert.NotEqual(unknownIsa, new ProcessSnapshot(new TargetProcessId(42), "fixture", null,
			CheatEngineArchitecture.Unknown, PointerSize.Bit32, 3));
	}

	[Theory]
	[InlineData(CheatEngineArchitecture.X64, 4)]
	[InlineData(CheatEngineArchitecture.X86, 8)]
	[InlineData(CheatEngineArchitecture.Arm64, 4)]
	[InlineData(CheatEngineArchitecture.Arm32, 8)]
	public void SnapshotRejectsAKnownIsaWithAContradictoryWidth(CheatEngineArchitecture architecture,
		int pointerBytes)
	{
		ArgumentException exception = Assert.Throws<ArgumentException>(() => new ProcessSnapshot(
			new TargetProcessId(42),
			"fixture",
			null,
			architecture,
			new PointerSize(pointerBytes),
			0));

		Assert.Equal("targetPointerSize", exception.ParamName);
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(long.MinValue)]
	public void SnapshotRejectsNegativeSelectionEpoch(long selectionEpoch)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessSnapshot(
			new TargetProcessId(42),
			"fixture",
			null,
			CheatEngineArchitecture.X64,
			selectionEpoch));
	}
}
