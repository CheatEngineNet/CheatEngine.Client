using CheatEngine.Client.Processes;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Tests.Processes;

public sealed class ProcessSnapshotTests
{
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
