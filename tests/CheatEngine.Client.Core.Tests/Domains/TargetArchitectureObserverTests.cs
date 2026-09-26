using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Core.Tests.Domains;

/// <summary>
///     The one target observation policy: the SDK's bracketed observation answers, and a broken fact narrows the
///     observation to the PID, the bitness and the configured size instead of discarding them (audit F08, Q31, Q32).
/// </summary>
public sealed class TargetArchitectureObserverTests
{
	[Fact]
	[Trait("Qualification", "Q32")]
	public void ObserverReturnsTheSdkObservationWithItsOwnIsaDerivation()
	{
		TargetObservationDouble port = new()
		{
			Target = TargetObservations.Create(processId: 77, is64Bit: false, backend: TargetBackend.CEServer)
		};

		ObservedTarget observed = TargetArchitectureObserver.Observe(port);

		Assert.True(observed.HasTarget);
		Assert.Null(observed.NarrowedFrom);
		Assert.Equal(77, observed.ProcessId!.Value.Value);
		Assert.Equal(TargetBackend.CEServer, observed.Backend);
		Assert.Equal(CheatEngineArchitecture.X86, observed.Architecture);
		Assert.Equal(PointerSize.Bit32, observed.Bitness);
		Assert.Equal(TargetAbi.Windows, observed.Abi);
		Assert.Equal([nameof(ITargetObservationPort.ObserveTargetArchitecture)], port.TargetCalls);
	}

	[Theory]
	[Trait("Qualification", "Q32")]
	[InlineData(ProcessOperationStatusKind.TargetNotAttached, TargetBackend.Unknown)]
	[InlineData(ProcessOperationStatusKind.FileAsProcessTarget, TargetBackend.FileAsProcess)]
	[InlineData(ProcessOperationStatusKind.TargetChanged, TargetBackend.Unknown)]
	[InlineData(ProcessOperationStatusKind.GlobalUnavailable, TargetBackend.Unknown)]
	public void ObserverAttributesNoFactWithoutASelectedProcessOrAfterATargetChange(ProcessOperationStatusKind kind,
		TargetBackend expectedBackend)
	{
		// Spike C3 D2: with no target Cheat Engine reports x64-like facts, so the SDK reads none; nothing is re-read.
		TargetObservationDouble port = new()
		{
			TargetStatus = TargetObservations.Status(kind)
		};

		ObservedTarget observed = TargetArchitectureObserver.Observe(port);

		Assert.False(observed.HasTarget);
		Assert.Equal(kind == ProcessOperationStatusKind.TargetNotAttached, observed.NoTargetSelected);
		Assert.Equal(expectedBackend, observed.Backend);
		Assert.Null(observed.ProcessId);
		Assert.Equal(PointerSize.Unknown, observed.Bitness);
		Assert.Equal(CheatEngineArchitecture.Unknown, observed.Architecture);
		Assert.Null(observed.ConfiguredPointerSizeBytes);
		Assert.False(observed.ConfiguredPointerSizeDiffersFromBitness);
		Assert.Equal([nameof(ITargetObservationPort.ObserveTargetArchitecture)], port.TargetCalls);
	}

	[Theory]
	[Trait("Qualification", "Q32")]
	[InlineData(ProcessOperationStatusKind.ProtectedLuaFailure)]
	[InlineData(ProcessOperationStatusKind.InvalidResult)]
	public void ObserverNarrowsABrokenFactToThePidTheBitnessAndTheConfiguredSize(ProcessOperationStatusKind kind)
	{
		TargetObservationDouble port = new()
		{
			TargetStatus = TargetObservations.Status(kind),
			Target = TargetObservations.Create(processId: 42, configuredPointerSizeBytes: 4)
		};

		ObservedTarget observed = TargetArchitectureObserver.Observe(port);

		Assert.True(observed.HasTarget);
		Assert.Equal(kind, observed.NarrowedFrom!.Value.Kind);
		Assert.Equal(42, observed.ProcessId!.Value.Value);
		Assert.Equal(PointerSize.Bit64, observed.Bitness);
		Assert.Equal(4, observed.ConfiguredPointerSizeBytes);
		Assert.True(observed.ConfiguredPointerSizeDiffersFromBitness);
		// The narrowed reads establish neither the backend nor the ISA: nothing is inferred from the bitness.
		Assert.Equal(TargetBackend.Unknown, observed.Backend);
		Assert.Equal(CheatEngineArchitecture.Unknown, observed.Architecture);
		Assert.Equal(TargetAbi.Unknown, observed.Abi);
		Assert.Equal(
		[
			nameof(ITargetObservationPort.ObserveTargetArchitecture), nameof(ITargetObservationPort.ObserveCurrent),
			nameof(ITargetObservationPort.TryGetConfiguredPointerSize), nameof(ITargetObservationPort.ObserveCurrent)
		], port.TargetCalls);
	}

	[Theory]
	[Trait("Qualification", "Q31")]
	[InlineData(2, ProcessOperationStatusKind.InvalidResult, 2)]
	[InlineData(0, ProcessOperationStatusKind.InvalidResult, null)]
	[InlineData(null, ProcessOperationStatusKind.GlobalUnavailable, null)]
	[InlineData(8, ProcessOperationStatusKind.TargetChanged, null)]
	public void NarrowingKeepsARawConfiguredSizeOnlyWhenAnIntegerWasRead(int? raw, ProcessOperationStatusKind status,
		int? expected)
	{
		// Cheat Engine accepts any configured size (spike C3 D3(b)); the SDK keeps the raw integer of an invalid width.
		TargetObservationDouble port = new()
		{
			TargetStatus = TargetObservations.LuaFailure,
			Target = TargetObservations.Create(configuredPointerSizeBytes: raw),
			ConfiguredStatus = TargetObservations.Status(status)
		};

		ObservedTarget observed = TargetArchitectureObserver.Observe(port);

		Assert.True(observed.HasTarget);
		Assert.Equal(expected, observed.ConfiguredPointerSizeBytes);
		Assert.Equal(PointerSize.Bit64, observed.Bitness);
	}

	[Theory]
	[Trait("Qualification", "Q32")]
	[InlineData(ProcessOperationStatusKind.Success, 43)]
	[InlineData(ProcessOperationStatusKind.TargetNotAttached, 0)]
	[InlineData(ProcessOperationStatusKind.FileAsProcessTarget, 0)]
	public void ANarrowedObservationWhoseClosingSelectionDiffersIsATargetChange(ProcessOperationStatusKind closing,
		int closingProcessId)
	{
		TargetObservationDouble port = new()
		{
			TargetStatus = TargetObservations.LuaFailure,
			CurrentReads =
			[
				(ProcessOperationStatus.Success, 42), (TargetObservations.Status(closing), closingProcessId)
			]
		};

		ObservedTarget observed = TargetArchitectureObserver.Observe(port);

		Assert.False(observed.HasTarget);
		Assert.Equal(ProcessOperationStatusKind.TargetChanged, observed.Status.Kind);
		Assert.Equal(ProcessOperationStatusKind.ProtectedLuaFailure, observed.NarrowedFrom!.Value.Kind);
		Assert.Equal(PointerSize.Unknown, observed.Bitness);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void ANarrowedObservationKeepsTheFailureOfItsSelectionReads()
	{
		// A failed read is a failed read, not evidence of a different target.
		TargetObservationDouble opening = new()
		{
			TargetStatus = TargetObservations.LuaFailure,
			CurrentReads = [(ProcessOperationStatus.TargetNotAttached, 0)]
		};
		TargetObservationDouble closing = new()
		{
			TargetStatus = ProcessOperationStatus.InvalidResult,
			CurrentReads = [(ProcessOperationStatus.Success, 42), (TargetObservations.LuaFailure, 0)]
		};

		ObservedTarget openingObserved = TargetArchitectureObserver.Observe(opening);
		ObservedTarget closingObserved = TargetArchitectureObserver.Observe(closing);

		Assert.True(openingObserved.NoTargetSelected);
		Assert.Equal(2, opening.TargetCalls.Count);
		Assert.Equal(ProcessOperationStatusKind.ProtectedLuaFailure, closingObserved.Status.Kind);
		Assert.Equal(ProcessOperationStatusKind.InvalidResult, closingObserved.NarrowedFrom!.Value.Kind);
		Assert.False(closingObserved.HasTarget);
	}

	[Theory]
	[Trait("Qualification", "Q31")]
	[InlineData(true, 4, true)]
	[InlineData(true, 8, false)]
	[InlineData(false, 8, true)]
	[InlineData(true, null, false)]
	public void TheMismatchIsReportedOnlyBetweenKnownFacts(bool is64Bit, int? configured, bool differs)
	{
		TargetObservationDouble port = new()
		{
			Target = TargetObservations.Create(is64Bit: is64Bit, configuredPointerSizeBytes: configured)
		};

		ObservedTarget observed = TargetArchitectureObserver.Observe(port);

		Assert.Equal(differs, observed.ConfiguredPointerSizeDiffersFromBitness);
		Assert.Equal(configured, observed.ConfiguredPointerSizeBytes);
		Assert.Equal(configured switch
		{
			4 => PointerSize.Bit32,
			8 => PointerSize.Bit64,
			_ => PointerSize.Unknown
		}, observed.ConfiguredPointerSize);
	}

	[Fact]
	public void ObserverRejectsANullPort()
	{
		Assert.Throws<ArgumentNullException>(() => TargetArchitectureObserver.Observe(null!));
	}
}
