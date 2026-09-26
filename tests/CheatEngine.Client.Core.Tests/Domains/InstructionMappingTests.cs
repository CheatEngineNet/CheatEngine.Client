using CheatEngine.Client.Core.Domains.Assembly;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Assembly;

namespace CheatEngine.Client.Core.Tests.Domains;

/// <summary>
///     Every CheatEngine.SDK 2.0.0 instruction status has its Client result in each phase of a call, and a status the
///     SDK could add later fails closed (plan L18).
/// </summary>
public sealed class InstructionMappingTests
{
	private const string Operation = "Assembly.Disassemble";

	/// <summary>The Client failure kind of each status; <see langword="null" /> for a success.</summary>
	private static readonly Dictionary<InstructionOperationStatus, CheatEngineFailureKind?> Kinds = new()
	{
		[InstructionOperationStatus.Unknown] = CheatEngineFailureKind.IndeterminateHostResult,
		[InstructionOperationStatus.Success] = null,
		[InstructionOperationStatus.InvalidProfile] = CheatEngineFailureKind.InvalidHostResult,
		[InstructionOperationStatus.AddressExceedsProfileWidth] = CheatEngineFailureKind.OperationRejected,
		[InstructionOperationStatus.TargetNotSelected] = CheatEngineFailureKind.TargetNotAttached,
		[InstructionOperationStatus.TargetChanged] = CheatEngineFailureKind.TargetChanged,
		[InstructionOperationStatus.DestinationTooSmall] = CheatEngineFailureKind.ResultLimitExceeded,
		[InstructionOperationStatus.OutputTooLong] = CheatEngineFailureKind.ResultLimitExceeded,
		[InstructionOperationStatus.InstructionRejected] = CheatEngineFailureKind.OperationRejected,
		[InstructionOperationStatus.GlobalUnavailable] = CheatEngineFailureKind.CapabilityUnavailable,
		[InstructionOperationStatus.LuaFailure] = CheatEngineFailureKind.LuaError,
		[InstructionOperationStatus.InvalidResult] = CheatEngineFailureKind.InvalidHostResult,
		[InstructionOperationStatus.UnsupportedTargetBackend] = CheatEngineFailureKind.Unsupported
	};

	/// <summary>The host effect of each failed status reported by the operation itself.</summary>
	private static readonly Dictionary<InstructionOperationStatus, CheatEngineHostEffect> OperationEffects = new()
	{
		[InstructionOperationStatus.Unknown] = CheatEngineHostEffect.Unknown,
		[InstructionOperationStatus.InvalidProfile] = CheatEngineHostEffect.Unknown,
		[InstructionOperationStatus.AddressExceedsProfileWidth] = CheatEngineHostEffect.NotStarted,
		[InstructionOperationStatus.TargetNotSelected] = CheatEngineHostEffect.Unknown,
		[InstructionOperationStatus.TargetChanged] = CheatEngineHostEffect.Unknown,
		[InstructionOperationStatus.DestinationTooSmall] = CheatEngineHostEffect.Completed,
		[InstructionOperationStatus.OutputTooLong] = CheatEngineHostEffect.Completed,
		[InstructionOperationStatus.InstructionRejected] = CheatEngineHostEffect.NotApplied,
		[InstructionOperationStatus.GlobalUnavailable] = CheatEngineHostEffect.NotStarted,
		[InstructionOperationStatus.LuaFailure] = CheatEngineHostEffect.Unknown,
		[InstructionOperationStatus.InvalidResult] = CheatEngineHostEffect.Unknown,
		[InstructionOperationStatus.UnsupportedTargetBackend] = CheatEngineHostEffect.Unknown
	};

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryStatusMapsToSuccessOrItsFailureKind()
	{
		MappingTotality.AssertTotal<InstructionOperationStatus>(
			static status => Kinds.TryGetValue(status, out CheatEngineFailureKind? expected) &&
							 Map(status, InstructionCallPhase.Operation)?.Kind == expected &&
							 Map(status, InstructionCallPhase.ProfileObservation)?.Kind == expected &&
							 Map(status, InstructionCallPhase.AfterEarlierCall)?.Kind == expected,
			static status => Map(status, InstructionCallPhase.Operation) is
			{
				Kind: CheatEngineFailureKind.IndeterminateHostResult, HostEffect: CheatEngineHostEffect.Unknown
			});
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryFailedStatusOfTheOperationHasItsHostEffect()
	{
		MappingTotality.AssertTotal<InstructionOperationStatus>(
			static status => status == InstructionOperationStatus.Success
				? Map(status, InstructionCallPhase.Operation) is null
				: OperationEffects.TryGetValue(status, out CheatEngineHostEffect expected) &&
				  Map(status, InstructionCallPhase.Operation)?.HostEffect == expected,
			static status => InstructionMapping.ToHostEffect(status, InstructionCallPhase.Operation) ==
							 CheatEngineHostEffect.Unknown);
	}

	[Fact]
	public void AStatusOfTheProfileObservationNeverStartedTheInstructionOperation()
	{
		foreach (InstructionOperationStatus status in Enum.GetValues<InstructionOperationStatus>()
					 .Where(static status => status != InstructionOperationStatus.Success))
		{
			CheatEngineFailure failure = Map(status, InstructionCallPhase.ProfileObservation)!.Value;

			Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
			Assert.Equal(Operation, failure.Operation);
			Assert.Null(failure.Exception);
			Assert.False(string.IsNullOrWhiteSpace(failure.Message));
		}
	}

	[Fact]
	public void AStatusAfterAnEarlierCallIsNeverNotStarted()
	{
		foreach (InstructionOperationStatus status in Enum.GetValues<InstructionOperationStatus>()
					 .Where(static status => status != InstructionOperationStatus.Success))
		{
			CheatEngineHostEffect first = Map(status, InstructionCallPhase.Operation)!.Value.HostEffect;
			CheatEngineHostEffect later = Map(status, InstructionCallPhase.AfterEarlierCall)!.Value.HostEffect;

			Assert.Equal(first == CheatEngineHostEffect.NotStarted ? CheatEngineHostEffect.Completed : first, later);
		}

		Assert.Equal(CheatEngineHostEffect.Completed,
			Map(InstructionOperationStatus.GlobalUnavailable, InstructionCallPhase.AfterEarlierCall)!.Value.HostEffect);
	}

	[Fact]
	public void AnEarlierCallTurnsOnlyANotStartedFailureIntoACompletedOne()
	{
		CheatEngineFailure notStarted = new(CheatEngineFailureKind.CapabilityUnavailable, Operation, "Unavailable.",
			null, CheatEngineHostEffect.NotStarted);
		CheatEngineFailure unknown = new(CheatEngineFailureKind.MemoryReadFailed, Operation, "Read failed.");

		CheatEngineFailure restated = InstructionMapping.AfterEarlierCall(notStarted);

		Assert.Equal((CheatEngineFailureKind.CapabilityUnavailable, CheatEngineHostEffect.Completed),
			(restated.Kind, restated.HostEffect));
		Assert.Equal(notStarted.Operation, restated.Operation);
		Assert.Equal(notStarted.Message, restated.Message);
		Assert.Equal(unknown, InstructionMapping.AfterEarlierCall(unknown));
	}

	[Fact]
	public void TheClientRefusalsNameTheirEffectWithoutAnAddress()
	{
		CheatEngineFailure beforeCall = InstructionMapping.AddressOutsideProfile(Operation);
		CheatEngineFailure afterCall = InstructionMapping.ReturnedAddressOutsideProfile(Operation);
		CheatEngineFailure limit = InstructionMapping.ResultExceedsLimit(Operation, 40, 32);
		CheatEngineFailure invalid = InstructionMapping.InvalidResult(Operation, "Shape violated.");

		Assert.Equal((CheatEngineFailureKind.OperationRejected, CheatEngineHostEffect.NotStarted),
			(beforeCall.Kind, beforeCall.HostEffect));
		Assert.Equal((CheatEngineFailureKind.OperationRejected, CheatEngineHostEffect.Completed),
			(afterCall.Kind, afterCall.HostEffect));
		Assert.Equal((CheatEngineFailureKind.ResultLimitExceeded, CheatEngineHostEffect.Completed),
			(limit.Kind, limit.HostEffect));
		Assert.Contains("40", limit.Message, StringComparison.Ordinal);
		Assert.Contains("32", limit.Message, StringComparison.Ordinal);
		Assert.Equal((CheatEngineFailureKind.InvalidHostResult, CheatEngineHostEffect.Completed),
			(invalid.Kind, invalid.HostEffect));
		Assert.DoesNotContain("0x", beforeCall.Message, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("0x", afterCall.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ABlankOperationIsAClientDefect()
	{
		Assert.Throws<ArgumentException>(() =>
			InstructionMapping.ToFailure(" ", InstructionOperationStatus.LuaFailure, InstructionCallPhase.Operation));
	}

	private static CheatEngineFailure? Map(InstructionOperationStatus status, InstructionCallPhase phase)
	{
		return InstructionMapping.ToFailure(Operation, status, phase);
	}
}
