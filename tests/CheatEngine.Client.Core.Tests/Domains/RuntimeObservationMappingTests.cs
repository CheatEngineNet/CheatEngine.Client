using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Tests.Domains;

/// <summary>
///     Every status of the CheatEngine.SDK 2.0.0 runtime and process operations has a deliberate Client counterpart, and
///     a value the SDK could add later fails closed (Q48).
/// </summary>
public sealed class RuntimeObservationMappingTests
{
	private static readonly Dictionary<ProcessOperationStatusKind, CheatEngineFailureKind> FailureKinds = new()
	{
		[ProcessOperationStatusKind.Unknown] = CheatEngineFailureKind.IndeterminateHostResult,
		[ProcessOperationStatusKind.Success] = CheatEngineFailureKind.Unknown,
		[ProcessOperationStatusKind.TargetNotAttached] = CheatEngineFailureKind.TargetNotAttached,
		[ProcessOperationStatusKind.SelectionNotConfirmed] = CheatEngineFailureKind.OperationRejected,
		[ProcessOperationStatusKind.GlobalUnavailable] = CheatEngineFailureKind.CapabilityUnavailable,
		[ProcessOperationStatusKind.ProtectedLuaFailure] = CheatEngineFailureKind.LuaError,
		[ProcessOperationStatusKind.InvalidResult] = CheatEngineFailureKind.InvalidHostResult,
		[ProcessOperationStatusKind.TargetChanged] = CheatEngineFailureKind.TargetChanged,
		[ProcessOperationStatusKind.FileAsProcessTarget] = CheatEngineFailureKind.TargetIdentityUnavailable
	};

	private static readonly Dictionary<ProcessOperationStatusKind, ClientCapabilityEvidenceState> HostEvidence = new()
	{
		[ProcessOperationStatusKind.Unknown] = ClientCapabilityEvidenceState.Unknown,
		[ProcessOperationStatusKind.Success] = ClientCapabilityEvidenceState.Satisfied,
		[ProcessOperationStatusKind.TargetNotAttached] = ClientCapabilityEvidenceState.Satisfied,
		[ProcessOperationStatusKind.SelectionNotConfirmed] = ClientCapabilityEvidenceState.Unknown,
		[ProcessOperationStatusKind.GlobalUnavailable] = ClientCapabilityEvidenceState.Missing,
		[ProcessOperationStatusKind.ProtectedLuaFailure] = ClientCapabilityEvidenceState.Faulted,
		[ProcessOperationStatusKind.InvalidResult] = ClientCapabilityEvidenceState.Malformed,
		[ProcessOperationStatusKind.TargetChanged] = ClientCapabilityEvidenceState.Unknown,
		[ProcessOperationStatusKind.FileAsProcessTarget] = ClientCapabilityEvidenceState.Unknown
	};

	private static readonly Dictionary<LuaOperationStatusKind, ClientCapabilityEvidenceState> FactEvidence = new()
	{
		[LuaOperationStatusKind.Unknown] = ClientCapabilityEvidenceState.Unknown,
		[LuaOperationStatusKind.Success] = ClientCapabilityEvidenceState.Satisfied,
		[LuaOperationStatusKind.GlobalUnavailable] = ClientCapabilityEvidenceState.Missing,
		[LuaOperationStatusKind.LuaFailure] = ClientCapabilityEvidenceState.Faulted,
		[LuaOperationStatusKind.NilResult] = ClientCapabilityEvidenceState.Unknown,
		[LuaOperationStatusKind.InvalidResult] = ClientCapabilityEvidenceState.Malformed,
		[LuaOperationStatusKind.StackUnavailable] = ClientCapabilityEvidenceState.Faulted,
		[LuaOperationStatusKind.MissingResult] = ClientCapabilityEvidenceState.Malformed,
		[LuaOperationStatusKind.ResultCapacityExceeded] = ClientCapabilityEvidenceState.Malformed
	};

	private static readonly Dictionary<RuntimeCapabilityAvailabilityState, ClientCapabilityEvidenceState>
		AvailabilityEvidence = new()
		{
			[RuntimeCapabilityAvailabilityState.Unknown] = ClientCapabilityEvidenceState.Unknown,
			[RuntimeCapabilityAvailabilityState.Available] = ClientCapabilityEvidenceState.Satisfied,
			[RuntimeCapabilityAvailabilityState.Unavailable] = ClientCapabilityEvidenceState.Missing
		};

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryProcessOperationStatusMapsToItsFailureKindAndHostEvidence()
	{
		MappingTotality.AssertTotal<ProcessOperationStatusKind>(
			static kind => FailureKinds.TryGetValue(kind, out CheatEngineFailureKind expected) &&
						   RuntimeObservationMapping.ToFailureKind(kind) == expected,
			static kind => RuntimeObservationMapping.ToFailureKind(kind) ==
						   CheatEngineFailureKind.IndeterminateHostResult);
		MappingTotality.AssertTotal<ProcessOperationStatusKind>(
			static kind => HostEvidence.TryGetValue(kind, out ClientCapabilityEvidenceState expected) &&
						   RuntimeObservationMapping.ToHostEvidenceState(kind) == expected,
			static kind => RuntimeObservationMapping.ToHostEvidenceState(kind) == ClientCapabilityEvidenceState.Unknown);
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryLuaOperationStatusMapsToTheEvidenceOfTheFactItRead()
	{
		MappingTotality.AssertTotal<LuaOperationStatusKind>(
			static kind => FactEvidence.TryGetValue(kind, out ClientCapabilityEvidenceState expected) &&
						   RuntimeObservationMapping.ToFactEvidenceState(kind) == expected,
			static kind => RuntimeObservationMapping.ToFactEvidenceState(kind) == ClientCapabilityEvidenceState.Unknown);
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EverySdkCapabilityAvailabilityMapsToItsHostEvidence()
	{
		MappingTotality.AssertTotal<RuntimeCapabilityAvailabilityState>(
			static state => AvailabilityEvidence.TryGetValue(state, out ClientCapabilityEvidenceState expected) &&
							RuntimeObservationMapping.ToHostEvidenceState(state) == expected,
			static state =>
				RuntimeObservationMapping.ToHostEvidenceState(state) == ClientCapabilityEvidenceState.Unknown);
	}

	[Theory]
	[InlineData(ProcessOperationStatusKind.Unknown)]
	[InlineData(ProcessOperationStatusKind.TargetNotAttached)]
	[InlineData(ProcessOperationStatusKind.SelectionNotConfirmed)]
	[InlineData(ProcessOperationStatusKind.GlobalUnavailable)]
	[InlineData(ProcessOperationStatusKind.ProtectedLuaFailure)]
	[InlineData(ProcessOperationStatusKind.InvalidResult)]
	[InlineData(ProcessOperationStatusKind.TargetChanged)]
	[InlineData(ProcessOperationStatusKind.FileAsProcessTarget)]
	public void AFailureCarriesItsKindTheOperationAndTheHostEffectWithAStableMessage(ProcessOperationStatusKind kind)
	{
		CheatEngineFailure failure = RuntimeObservationMapping.ToFailure("Processes.GetCurrent",
			TargetObservations.Status(kind), CheatEngineHostEffect.Completed);

		Assert.Equal(FailureKinds[kind], failure.Kind);
		Assert.Equal("Processes.GetCurrent", failure.Operation);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Null(failure.Exception);
		Assert.False(string.IsNullOrWhiteSpace(failure.Message));
	}

	[Fact]
	public void ASuccessfulStatusIsNotAFailure()
	{
		Assert.Throws<ArgumentException>(() => RuntimeObservationMapping.ToFailure("Processes.GetCurrent",
			ProcessOperationStatus.Success, CheatEngineHostEffect.Completed));
	}
}
