using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Domains.ValueScanning;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Targets;

namespace CheatEngine.Client.Core.Tests.Domains.ValueScanning;

/// <summary>
///     Every value-scan outcome of the consumed CheatEngine.SDK maps to its dedicated Client value, and a value the SDK
///     could add fails closed (plan L15, Q48).
/// </summary>
public sealed class ValueScanMappingTests
{
	private const string Operation = "ValueScans.Contract";

	private static Dictionary<MemoryScanCreationStatus, (CheatEngineFailureKind Kind, CheatEngineHostEffect Effect)>
		ExpectedCreationFailures => new()
		{
			[MemoryScanCreationStatus.TargetIdentityUnavailable] =
				(CheatEngineFailureKind.TargetIdentityUnavailable, CheatEngineHostEffect.NotStarted),
			[MemoryScanCreationStatus.GlobalUnavailable] =
				(CheatEngineFailureKind.CapabilityUnavailable, CheatEngineHostEffect.NotApplied),
			[MemoryScanCreationStatus.LuaFailure] = (CheatEngineFailureKind.LuaError, CheatEngineHostEffect.Unknown),
			[MemoryScanCreationStatus.NoScannerResult] =
				(CheatEngineFailureKind.OperationRejected, CheatEngineHostEffect.NotApplied),
			[MemoryScanCreationStatus.NoFoundListResult] =
				(CheatEngineFailureKind.OperationRejected, CheatEngineHostEffect.NotApplied),
			[MemoryScanCreationStatus.InvalidScannerResult] =
				(CheatEngineFailureKind.InvalidHostResult, CheatEngineHostEffect.NotApplied),
			[MemoryScanCreationStatus.InvalidFoundListResult] =
				(CheatEngineFailureKind.InvalidHostResult, CheatEngineHostEffect.NotApplied),
			[MemoryScanCreationStatus.AliasedFoundList] =
				(CheatEngineFailureKind.InvalidHostResult, CheatEngineHostEffect.NotApplied),
			[MemoryScanCreationStatus.RollbackUnconfirmed] =
				(CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.CleanupUnconfirmed),
			// A success without a session breaks the port contract, and Unknown is never produced by a completed factory:
			// like an unrecognized status, neither proves that no scanner remains.
			[MemoryScanCreationStatus.Success] =
				(CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.CleanupUnconfirmed),
			[MemoryScanCreationStatus.Unknown] =
				(CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.CleanupUnconfirmed)
		};

	private static Dictionary<MemoryScanMaterializationStatus, (CheatEngineFailureKind Kind, CheatEngineHostEffect Effect)>
		ExpectedPageFailures => new()
		{
			[MemoryScanMaterializationStatus.DestinationTooSmall] =
				(CheatEngineFailureKind.ResultLimitExceeded, CheatEngineHostEffect.Completed),
			[MemoryScanMaterializationStatus.Cancelled] =
				(CheatEngineFailureKind.Cancelled, CheatEngineHostEffect.Completed),
			[MemoryScanMaterializationStatus.RuntimeInvalidated] =
				(CheatEngineFailureKind.RuntimeChanged, CheatEngineHostEffect.NotStarted),
			[MemoryScanMaterializationStatus.TargetIdentityUnavailable] =
				(CheatEngineFailureKind.TargetIdentityUnavailable, CheatEngineHostEffect.NotStarted),
			[MemoryScanMaterializationStatus.TargetIdentityMismatch] =
				(CheatEngineFailureKind.TargetChanged, CheatEngineHostEffect.NotStarted),
			[MemoryScanMaterializationStatus.LuaFailure] = (CheatEngineFailureKind.LuaError, CheatEngineHostEffect.Unknown),
			[MemoryScanMaterializationStatus.InvalidResult] =
				(CheatEngineFailureKind.InvalidHostResult, CheatEngineHostEffect.Completed),
			[MemoryScanMaterializationStatus.PageStartOutOfRange] =
				(CheatEngineFailureKind.OperationRejected, CheatEngineHostEffect.Completed),
			[MemoryScanMaterializationStatus.Unknown] =
				(CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.Unknown)
		};

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryCreationStatusMapsToItsFailure()
	{
		CheatEngineFailure fallback = ValueScanMapping.FromCreationStatus(
			MappingTotality.Undefined<MemoryScanCreationStatus>(), Operation);

		MappingTotality.AssertTotal<MemoryScanCreationStatus>(
			static status => ExpectedCreationFailures.TryGetValue(status,
								 out (CheatEngineFailureKind Kind, CheatEngineHostEffect Effect) expected) &&
							 Describe(ValueScanMapping.FromCreationStatus(status, Operation)) == expected,
			status => ValueScanMapping.FromCreationStatus(status, Operation) == fallback);
		Assert.Equal((CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.CleanupUnconfirmed),
			Describe(fallback));
		Assert.Equal(Enum.GetValues<MemoryScanCreationStatus>().Order(), ExpectedCreationFailures.Keys.Order());
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EverySessionStateMapsToAClientState()
	{
		MappingTotality.AssertTotal<MemoryScanState>(
			static state => ValueScanMapping.ToState(state) != ValueScanSessionState.Unknown,
			static state => ValueScanMapping.ToState(state) == ValueScanSessionState.Unknown);
		Assert.Equal(ValueScanSessionState.Created, ValueScanMapping.ToState(MemoryScanState.New));
		Assert.Equal(ValueScanSessionState.Closed, ValueScanMapping.ToState(MemoryScanState.Disposed));
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryInvalidationReasonMapsToAClientKind()
	{
		Dictionary<MemoryScanInvalidationReason, ValueScanInvalidationKind> expected = new()
		{
			[MemoryScanInvalidationReason.None] = ValueScanInvalidationKind.None,
			[MemoryScanInvalidationReason.ProtectedLuaFailure] = ValueScanInvalidationKind.HostCallFailed,
			[MemoryScanInvalidationReason.RuntimeIdentityChanged] = ValueScanInvalidationKind.RuntimeChanged,
			[MemoryScanInvalidationReason.TargetChanged] = ValueScanInvalidationKind.TargetChanged,
			[MemoryScanInvalidationReason.TargetProcessReused] = ValueScanInvalidationKind.TargetChanged,
			// Only the experimental stop request, which the Client never makes, produces it.
			[MemoryScanInvalidationReason.ScanTerminated] = ValueScanInvalidationKind.Unknown
		};

		MappingTotality.AssertTotal<MemoryScanInvalidationReason>(
			reason => expected.TryGetValue(reason, out ValueScanInvalidationKind kind) &&
					  ValueScanMapping.ToInvalidation(reason) == kind,
			static reason => ValueScanMapping.ToInvalidation(reason) == ValueScanInvalidationKind.Unknown);
		Assert.Equal(Enum.GetValues<MemoryScanInvalidationReason>().Order(), expected.Keys.Order());
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryCancellationMilestoneMapsToACancellationEffect()
	{
		Dictionary<MemoryScanCancellationMilestone, CheatEngineHostEffect> expected = new()
		{
			[MemoryScanCancellationMilestone.None] = CheatEngineHostEffect.Unknown,
			[MemoryScanCancellationMilestone.CancelledBeforeNativeCall] = CheatEngineHostEffect.NotStarted,
			[MemoryScanCancellationMilestone.ObservedAfterNativeCall] = CheatEngineHostEffect.Completed
		};
		CheatEngineFailure fallback = ValueScanMapping.Cancelled(Operation,
			MappingTotality.Undefined<MemoryScanCancellationMilestone>());

		MappingTotality.AssertTotal<MemoryScanCancellationMilestone>(
			milestone => expected.TryGetValue(milestone, out CheatEngineHostEffect effect) &&
						 ValueScanMapping.Cancelled(Operation, milestone) is
						 {
							 Kind: CheatEngineFailureKind.Cancelled
						 } failure &&
						 failure.HostEffect == effect && failure != fallback,
			milestone => ValueScanMapping.Cancelled(Operation, milestone) == fallback);
		Assert.Equal(CheatEngineHostEffect.Unknown, fallback.HostEffect);
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryMaterializationStatusIsAPageOrItsFailure()
	{
		CheatEngineFailure fallback = PageFailure(MappingTotality.Undefined<MemoryScanMaterializationStatus>());

		MappingTotality.AssertTotal<MemoryScanMaterializationStatus>(
			static status => status is MemoryScanMaterializationStatus.Success or MemoryScanMaterializationStatus.NoResults
				? !ValueScanMapping.TryGetPageFailure(status, MemoryScanCancellationMilestone.None, Operation, out _)
				: ExpectedPageFailures.TryGetValue(status,
					  out (CheatEngineFailureKind Kind, CheatEngineHostEffect Effect) expected) &&
				  Describe(PageFailure(status)) == expected,
			status => PageFailure(status) == fallback);
		Assert.Equal((CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.Unknown), Describe(fallback));
		Assert.Equal(
			Enum.GetValues<MemoryScanMaterializationStatus>().Except(
				[MemoryScanMaterializationStatus.Success, MemoryScanMaterializationStatus.NoResults]).Order(),
			ExpectedPageFailures.Keys.Order());
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryMemoryScanFailureKindTellsHowFarAScanGot()
	{
		// The SDK checks the runtime and the target before any Cheat Engine call of an operation; every other failure
		// category comes from a Cheat Engine call that began.
		Dictionary<MemoryScanFailureKind, CheatEngineHostEffect> expected = new()
		{
			[MemoryScanFailureKind.MissingCapability] = CheatEngineHostEffect.Started,
			[MemoryScanFailureKind.LuaError] = CheatEngineHostEffect.Started,
			[MemoryScanFailureKind.UnexpectedResult] = CheatEngineHostEffect.Started,
			[MemoryScanFailureKind.RuntimeInvalidated] = CheatEngineHostEffect.NotStarted,
			[MemoryScanFailureKind.TargetIdentityUnavailable] = CheatEngineHostEffect.NotStarted,
			[MemoryScanFailureKind.TargetIdentityMismatch] = CheatEngineHostEffect.NotStarted
		};

		MappingTotality.AssertTotal<MemoryScanFailureKind>(
			kind => expected.TryGetValue(kind, out CheatEngineHostEffect effect) &&
					ValueScanMapping.MutationFaultEffect(kind) == effect,
			static kind => ValueScanMapping.MutationFaultEffect(kind) == CheatEngineHostEffect.Unknown);
		Assert.Equal(Enum.GetValues<MemoryScanFailureKind>().Order(), expected.Keys.Order());
		Assert.Equal(CheatEngineHostEffect.NotStarted, ValueScanMapping.MutationFaultEffect(ScanFaults.State()));
		Assert.Equal(CheatEngineHostEffect.Unknown,
			ValueScanMapping.MutationFaultEffect(new ObjectDisposedException("MemoryScanSession")));
		Assert.Equal(CheatEngineHostEffect.Completed,
			ValueScanMapping.ReadFaultEffect(ScanFaults.Scan(MemoryScanFailureKind.UnexpectedResult)));
		Assert.Equal(CheatEngineHostEffect.NotStarted,
			ValueScanMapping.ReadFaultEffect(ScanFaults.Scan(MemoryScanFailureKind.RuntimeInvalidated)));
		Assert.Equal(CheatEngineHostEffect.Unknown,
			ValueScanMapping.ReadFaultEffect(ScanFaults.Scan(MemoryScanFailureKind.LuaError)));
	}

	[Theory]
	[InlineData(TargetReleaseStatus.Released, TargetReleaseStatus.Released, MemoryScanTerminationStatus.NotRequired,
		LeaseReleaseKind.Released, CheatEngineHostEffect.Completed)]
	[InlineData(TargetReleaseStatus.Released, TargetReleaseStatus.Released, MemoryScanTerminationStatus.Confirmed,
		LeaseReleaseKind.Released, CheatEngineHostEffect.Completed)]
	[InlineData(TargetReleaseStatus.Released, TargetReleaseStatus.Released, MemoryScanTerminationStatus.WaitTimedOut,
		LeaseReleaseKind.CleanupUnconfirmed, CheatEngineHostEffect.Started)]
	[InlineData(TargetReleaseStatus.Released, TargetReleaseStatus.UnconfirmedAfterInvocation,
		MemoryScanTerminationStatus.NotRequired, LeaseReleaseKind.CleanupUnconfirmed, CheatEngineHostEffect.Started)]
	[InlineData(TargetReleaseStatus.RefusedProcessReused, TargetReleaseStatus.RefusedTargetChanged,
		MemoryScanTerminationStatus.NotInvoked, LeaseReleaseKind.RefusedTargetChanged, CheatEngineHostEffect.NotStarted)]
	[InlineData(TargetReleaseStatus.NotInvoked, TargetReleaseStatus.NotInvoked, MemoryScanTerminationStatus.NotInvoked,
		LeaseReleaseKind.CleanupUnavailable, CheatEngineHostEffect.NotStarted)]
	[InlineData(TargetReleaseStatus.Unspecified, TargetReleaseStatus.Unspecified, MemoryScanTerminationStatus.Unknown,
		LeaseReleaseKind.Unknown, CheatEngineHostEffect.NotStarted)]
	public void TheReleaseKeepsTheWorseOfTheFoundListScannerAndStopOutcomes(TargetReleaseStatus foundList,
		TargetReleaseStatus memScan, MemoryScanTerminationStatus termination, LeaseReleaseKind kind,
		CheatEngineHostEffect hostEffect)
	{
		LeaseReleaseOutcome outcome =
			ValueScanMapping.FromRelease(new ValueScanReleaseStatuses(foundList, memScan, termination));

		Assert.Equal(new LeaseReleaseOutcome(kind, hostEffect), outcome);
		Assert.Equal(outcome,
			ValueScanMapping.FromRelease(new ValueScanReleaseStatuses(memScan, foundList, termination)));
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryTerminationStatusTellsWhetherTheScanIsStopped()
	{
		Dictionary<MemoryScanTerminationStatus, bool> expected = new()
		{
			[MemoryScanTerminationStatus.Unknown] = false,
			[MemoryScanTerminationStatus.NotRequired] = true,
			[MemoryScanTerminationStatus.Confirmed] = true,
			[MemoryScanTerminationStatus.WaitTimedOut] = false,
			[MemoryScanTerminationStatus.TerminateFailed] = false,
			[MemoryScanTerminationStatus.WaitFailed] = false,
			[MemoryScanTerminationStatus.NotInvoked] = false
		};

		MappingTotality.AssertTotal<MemoryScanTerminationStatus>(
			termination => expected.TryGetValue(termination, out bool stopped) &&
						   ScanTermination.IsStopConfirmed(termination) == stopped,
			static termination => !ScanTermination.IsStopConfirmed(termination));
		Assert.Equal(Enum.GetValues<MemoryScanTerminationStatus>().Order(), expected.Keys.Order());
	}

	[Theory]
	[InlineData(LeaseReleaseKind.RefusedTargetChanged, CheatEngineFailureKind.TargetChanged)]
	[InlineData(LeaseReleaseKind.RefusedTargetIdentityUnavailable, CheatEngineFailureKind.TargetIdentityUnavailable)]
	[InlineData(LeaseReleaseKind.RefusedRuntimeChanged, CheatEngineFailureKind.RuntimeChanged)]
	[InlineData(LeaseReleaseKind.Released, CheatEngineFailureKind.InvalidState)]
	[InlineData(LeaseReleaseKind.CleanupUnavailable, CheatEngineFailureKind.InvalidState)]
	public void AReleasedSessionRefusesWithTheReasonOfItsRelease(LeaseReleaseKind releaseKind,
		CheatEngineFailureKind kind)
	{
		CheatEngineFailure failure = ValueScanMapping.Released(Operation,
			new LeaseReleaseOutcome(releaseKind, CheatEngineHostEffect.NotStarted));

		Assert.Equal(kind, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(CheatEngineFailureKind.InvalidState, ValueScanMapping.Released(Operation, null).Kind);
	}

	[Fact]
	public void TheHostErrorTextIsAppendedOnlyWhenCheatEngineReportedOne()
	{
		CheatEngineFailure failure = new(CheatEngineFailureKind.LuaError, Operation, "The scan failed.");

		Assert.Equal(failure, ValueScanMapping.WithHostErrorText(failure, null, false));
		Assert.Equal(failure, ValueScanMapping.WithHostErrorText(failure, " ", true));
		Assert.Equal("The scan failed. Cheat Engine reported: Nothing found",
			ValueScanMapping.WithHostErrorText(failure, " Nothing found ", false).Message);
	}

	[Fact]
	public void TheDocumentedPageLimitIsTheCoreLimit()
	{
		// ValueScanReadRequest's documentation states the limit; keep both in step.
		Assert.Equal(1024, ScanResourceLimits.MaximumValueScanPage);
	}

	private static CheatEngineFailure PageFailure(MemoryScanMaterializationStatus status)
	{
		_ = ValueScanMapping.TryGetPageFailure(status, MemoryScanCancellationMilestone.ObservedAfterNativeCall,
			Operation, out CheatEngineFailure failure);
		return failure;
	}

	private static (CheatEngineFailureKind Kind, CheatEngineHostEffect Effect) Describe(CheatEngineFailure failure)
	{
		return (failure.Kind, failure.HostEffect);
	}
}
