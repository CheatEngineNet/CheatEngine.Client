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
	public void ProcessStartRequestRequiresAbsoluteExecutableAndWorkingDirectoryPaths()
	{
		Assert.Throws<ArgumentException>(() => new ProcessStartRequest("target.exe"));
		Assert.Throws<ArgumentException>(() => new ProcessStartRequest("C:\\target.exe", null, "working"));

		ProcessStartRequest request = new("C:\\target.exe", "--fixture", "C:\\working");
		Assert.Equal("C:\\target.exe", request.ExecutablePath);
		Assert.Equal("--fixture", request.Arguments);
		Assert.Equal("C:\\working", request.WorkingDirectory);
	}

	[Fact]
	public void ProcessPauseSnapshotPreservesCopiedTargetStateAndEpoch()
	{
		ProcessPauseSnapshot snapshot = new(new TargetProcessId(42), ProcessPauseState.Paused, 7);

		Assert.Equal(new TargetProcessId(42), snapshot.ProcessId);
		Assert.Equal(ProcessPauseState.Paused, snapshot.State);
		Assert.Equal(7, snapshot.SelectionEpoch);
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
