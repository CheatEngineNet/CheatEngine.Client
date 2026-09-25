using CheatEngine.Client.Processes;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Abstractions.Tests.Processes;

public sealed class ProcessSnapshotTests
{
	private static readonly DateTimeOffset StartTime = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

	[Fact]
	public void ProcessEnumerationRequestRejectsZeroMaximumAndAnEmptyNameFilter()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new LocalProcessEnumerationRequest(0));
		Assert.Throws<ArgumentException>(() => new LocalProcessEnumerationRequest(1, string.Empty));
	}

	[Fact]
	public void ProcessEnumerationResultNormalizesTheDefaultArrayAndRejectsEmptyTruncation()
	{
		LocalProcessEnumerationResult result = new(default, false);

		Assert.Empty(result.Processes);
		Assert.False(result.IsTruncated);
		Assert.Throws<ArgumentException>(() => new LocalProcessEnumerationResult([], true));
	}

	[Fact]
	public void SnapshotPreservesEveryCopiedObservation()
	{
		ProcessSnapshot snapshot = new(
			new TargetProcessId(42),
			"fixture",
			"C:\\fixtures\\fixture.exe",
			TargetBackend.LocalProcess,
			CheatEngineArchitecture.X64,
			PointerSize.Bit64,
			8,
			StartTime,
			7);

		Assert.Equal(new TargetProcessId(42), snapshot.Id);
		Assert.Equal("fixture", snapshot.Name);
		Assert.Equal("C:\\fixtures\\fixture.exe", snapshot.ExecutablePath);
		Assert.Equal(TargetBackend.LocalProcess, snapshot.Backend);
		Assert.Equal(CheatEngineArchitecture.X64, snapshot.Architecture);
		Assert.Equal(PointerSize.Bit64, snapshot.Bitness);
		Assert.Equal(8, snapshot.ConfiguredPointerSizeBytes);
		Assert.Equal(PointerSize.Bit64, snapshot.ConfiguredPointerSize);
		Assert.False(snapshot.ConfiguredPointerSizeDiffersFromBitness);
		Assert.Equal(StartTime, snapshot.StartTimeUtc);
		Assert.Equal(7, snapshot.SelectionEpoch);
	}

	[Fact]
	public void TheDefaultSnapshotKeepsEveryFactUnknown()
	{
		ProcessSnapshot snapshot = default;

		Assert.Equal(TargetBackend.Unknown, snapshot.Backend);
		Assert.Equal(CheatEngineArchitecture.Unknown, snapshot.Architecture);
		Assert.Equal(PointerSize.Unknown, snapshot.Bitness);
		Assert.Null(snapshot.ConfiguredPointerSizeBytes);
		Assert.Null(snapshot.ConfiguredPointerSizeDiffersFromBitness);
		Assert.Null(snapshot.StartTimeUtc);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void SnapshotStoresTheObservedBitnessInsteadOfDerivingIt()
	{
		ProcessSnapshot unknownIsa = Create(CheatEngineArchitecture.Unknown, PointerSize.Bit64);
		ProcessSnapshot unknownBitness = Create(CheatEngineArchitecture.X86, PointerSize.Unknown);

		Assert.Equal(CheatEngineArchitecture.Unknown, unknownIsa.Architecture);
		Assert.Equal(PointerSize.Bit64, unknownIsa.Bitness);
		Assert.Equal(CheatEngineArchitecture.X86, unknownBitness.Architecture);
		Assert.Equal(PointerSize.Unknown, unknownBitness.Bitness);
		Assert.NotEqual(unknownIsa, Create(CheatEngineArchitecture.Unknown, PointerSize.Bit32));
	}

	[Theory]
	[Trait("Qualification", "Q31")]
	[InlineData(4, true)]
	[InlineData(8, false)]
	[InlineData(2, true)]
	[InlineData(null, null)]
	public void SnapshotKeepsTheRawConfiguredPointerSizeApartFromTheBitness(int? configured, bool? differs)
	{
		// Spike C3 D3: setPointerSize(4) on an x64 target leaves targetIs64Bit true; any integer is accepted.
		ProcessSnapshot snapshot = new(new TargetProcessId(42), null, null, TargetBackend.LocalProcess,
			CheatEngineArchitecture.X64, PointerSize.Bit64, configured, null, 0);

		Assert.Equal(configured, snapshot.ConfiguredPointerSizeBytes);
		Assert.Equal(PointerSize.Bit64, snapshot.Bitness);
		Assert.Equal(differs, snapshot.ConfiguredPointerSizeDiffersFromBitness);
		Assert.Equal(configured switch
		{
			4 => PointerSize.Bit32,
			8 => PointerSize.Bit64,
			_ => PointerSize.Unknown
		}, snapshot.ConfiguredPointerSize);
	}

	[Theory]
	[InlineData(CheatEngineArchitecture.X64, 4)]
	[InlineData(CheatEngineArchitecture.X86, 8)]
	[InlineData(CheatEngineArchitecture.Arm64, 4)]
	[InlineData(CheatEngineArchitecture.Arm32, 8)]
	public void SnapshotRejectsAKnownIsaWithAContradictoryBitness(CheatEngineArchitecture architecture,
		int pointerBytes)
	{
		ArgumentException exception = Assert.Throws<ArgumentException>(() =>
			Create(architecture, new PointerSize(pointerBytes)));

		Assert.Equal("bitness", exception.ParamName);
	}

	[Theory]
	[Trait("Qualification", "Q32")]
	[InlineData(TargetBackend.CEServer, "fixture", null, false)]
	[InlineData(TargetBackend.Unknown, null, "C:\\fixtures\\fixture.exe", false)]
	[InlineData(TargetBackend.FileAsProcess, null, null, true)]
	public void SnapshotRejectsLocalMetadataForABackendOtherThanALocalProcess(TargetBackend backend, string? name,
		string? path, bool withStartTime)
	{
		// A local name, path or creation time does not describe a CEServer target, an unknown backend or a file.
		ArgumentException exception = Assert.Throws<ArgumentException>(() => new ProcessSnapshot(
			new TargetProcessId(42), name, path, backend, CheatEngineArchitecture.X64, PointerSize.Bit64, 8,
			withStartTime ? StartTime : null, 0));

		Assert.Equal("backend", exception.ParamName);
	}

	[Fact]
	public void SnapshotAcceptsANonLocalBackendWithoutLocalMetadata()
	{
		ProcessSnapshot snapshot = new(new TargetProcessId(900), null, null, TargetBackend.CEServer,
			CheatEngineArchitecture.Arm64, PointerSize.Bit64, 8, null, 1);

		Assert.Equal(TargetBackend.CEServer, snapshot.Backend);
		Assert.Null(snapshot.StartTimeUtc);
	}

	[Fact]
	public void SnapshotRejectsAStartTimeThatIsNotUtcAndAnUndefinedBackend()
	{
		ArgumentException local = Assert.Throws<ArgumentException>(() => new ProcessSnapshot(new TargetProcessId(42),
			null, null, TargetBackend.LocalProcess, CheatEngineArchitecture.X64, PointerSize.Bit64, 8,
			new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.FromHours(2)), 0));
		ArgumentOutOfRangeException undefined = Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessSnapshot(
			new TargetProcessId(42), null, null, (TargetBackend) 99, CheatEngineArchitecture.X64, PointerSize.Bit64, 8,
			null, 0));

		Assert.Equal("startTimeUtc", local.ParamName);
		Assert.Equal("backend", undefined.ParamName);
	}

	[Theory]
	[InlineData("", null)]
	[InlineData(null, "")]
	public void SnapshotRejectsAnEmptyNameOrPath(string? name, string? path)
	{
		Assert.Throws<ArgumentException>(() => new ProcessSnapshot(new TargetProcessId(42), name, path,
			TargetBackend.LocalProcess, CheatEngineArchitecture.X64, PointerSize.Bit64, 8, null, 0));
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(long.MinValue)]
	public void SnapshotRejectsNegativeSelectionEpoch(long selectionEpoch)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessSnapshot(new TargetProcessId(42), "fixture", null,
			TargetBackend.LocalProcess, CheatEngineArchitecture.X64, PointerSize.Bit64, 8, null, selectionEpoch));
	}

	private static ProcessSnapshot Create(CheatEngineArchitecture architecture, PointerSize bitness)
	{
		return new ProcessSnapshot(new TargetProcessId(42), "fixture", null, TargetBackend.LocalProcess, architecture,
			bitness, null, null, 3);
	}
}
