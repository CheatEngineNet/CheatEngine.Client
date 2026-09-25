#pragma warning disable CECLIENT5004 // The mapping produces the experimental Auto Assembler check result.

using CheatEngine.Client.Assembly;
using CheatEngine.Client.Core.Domains.Assembly;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Assembly;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Tests.Domains;

/// <summary>
///     Every CheatEngine.SDK 2.0.0 Auto Assembler outcome category has its Client result, and a category the SDK could
///     add later fails closed (plan L17).
/// </summary>
public sealed class AutoAssemblerMappingTests
{
	private const string Operation = "AutoAssembler.ApplyPatch";

	/// <summary>The Client failure of each activation category; <see langword="null" /> for a returned lease.</summary>
	private static readonly Dictionary<AutoAssemblerApplyOutcomeKind, (CheatEngineFailureKind, CheatEngineHostEffect)?>
		ApplyResults = new()
		{
			[AutoAssemblerApplyOutcomeKind.Unknown] =
			(CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.Unknown),
			[AutoAssemblerApplyOutcomeKind.Applied] = null,
			[AutoAssemblerApplyOutcomeKind.AppliedTargetChanged] = null,
			[AutoAssemblerApplyOutcomeKind.Rejected] =
				(CheatEngineFailureKind.OperationRejected, CheatEngineHostEffect.Unknown),
			[AutoAssemblerApplyOutcomeKind.GlobalUnavailable] =
				(CheatEngineFailureKind.CapabilityUnavailable, CheatEngineHostEffect.NotStarted),
			[AutoAssemblerApplyOutcomeKind.ProtectedLuaFailure] =
				(CheatEngineFailureKind.LuaError, CheatEngineHostEffect.Unknown),
			[AutoAssemblerApplyOutcomeKind.InvalidResult] =
				(CheatEngineFailureKind.InvalidHostResult, CheatEngineHostEffect.Unknown),
			[AutoAssemblerApplyOutcomeKind.TargetIdentityUnavailable] =
				(CheatEngineFailureKind.TargetIdentityUnavailable, CheatEngineHostEffect.NotStarted),
			[AutoAssemblerApplyOutcomeKind.HandoffFailed] =
				(CheatEngineFailureKind.BindingError, CheatEngineHostEffect.CleanupUnconfirmed)
		};

	/// <summary>The Client result of each check category: a verdict, or the failure kind of a check without one.</summary>
	private static readonly Dictionary<AutoAssemblerCheckOutcomeKind, (bool? Accepted, CheatEngineFailureKind Kind)>
		CheckResults = new()
		{
			[AutoAssemblerCheckOutcomeKind.Unknown] = (null, CheatEngineFailureKind.IndeterminateHostResult),
			[AutoAssemblerCheckOutcomeKind.Accepted] = (true, CheatEngineFailureKind.Unknown),
			[AutoAssemblerCheckOutcomeKind.Rejected] = (false, CheatEngineFailureKind.Unknown),
			[AutoAssemblerCheckOutcomeKind.GlobalUnavailable] = (null, CheatEngineFailureKind.CapabilityUnavailable),
			[AutoAssemblerCheckOutcomeKind.ProtectedLuaFailure] = (null, CheatEngineFailureKind.LuaError),
			[AutoAssemblerCheckOutcomeKind.InvalidResult] = (null, CheatEngineFailureKind.InvalidHostResult)
		};

	/// <summary>The Client outcome of each release status of a patch owner.</summary>
	private static readonly Dictionary<TargetReleaseStatus, LeaseReleaseOutcome> ReleaseOutcomes = new()
	{
		[TargetReleaseStatus.Unspecified] = new(LeaseReleaseKind.Unknown, CheatEngineHostEffect.NotStarted),
		[TargetReleaseStatus.Released] = new(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed),
		[TargetReleaseStatus.RefusedNoTarget] = new(LeaseReleaseKind.RefusedNoTarget, CheatEngineHostEffect.NotStarted),
		[TargetReleaseStatus.RefusedIdentityUnavailable] =
			new(LeaseReleaseKind.RefusedTargetIdentityUnavailable, CheatEngineHostEffect.NotStarted),
		[TargetReleaseStatus.RefusedTargetChanged] =
			new(LeaseReleaseKind.RefusedTargetChanged, CheatEngineHostEffect.NotStarted),
		[TargetReleaseStatus.RefusedProcessReused] =
			new(LeaseReleaseKind.RefusedTargetChanged, CheatEngineHostEffect.NotStarted),
		[TargetReleaseStatus.UnconfirmedAfterInvocation] =
			new(LeaseReleaseKind.CleanupUnconfirmed, CheatEngineHostEffect.Started),
		// The owner consumed its disable information although the disable could not begin: nothing is left to retry.
		[TargetReleaseStatus.NotInvoked] = new(LeaseReleaseKind.RefusedRuntimeChanged, CheatEngineHostEffect.NotStarted),
		[TargetReleaseStatus.RefusedRuntimeChanged] =
			new(LeaseReleaseKind.RefusedRuntimeChanged, CheatEngineHostEffect.NotStarted)
	};

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryReleaseStatusOfAPatchOwnerMapsToItsOutcome()
	{
		MappingTotality.AssertTotal<TargetReleaseStatus>(
			static status => ReleaseOutcomes.TryGetValue(status, out LeaseReleaseOutcome expected) &&
							 AutoAssemblerMapping.ToReleaseOutcome(status) == expected,
			static status => AutoAssemblerMapping.ToReleaseOutcome(status) ==
							 new LeaseReleaseOutcome(LeaseReleaseKind.Unknown, CheatEngineHostEffect.Unknown));
	}

	[Fact]
	public void EveryStatusOfAConsumedPatchOwnerEndsTheLease()
	{
		// CheatEngine.SDK's own TargetReleaseOutcome.RequiresManualRecovery holds for every status of a consumed owner
		// except Released; none of them leaves anything to retry.
		foreach (TargetReleaseStatus status in Enum.GetValues<TargetReleaseStatus>()
					 .Where(static status => status != TargetReleaseStatus.Unspecified))
		{
			LeaseReleaseOutcome outcome = AutoAssemblerMapping.ToReleaseOutcome(status);

			Assert.False(outcome.IsRetryable, $"{status} is retryable.");
			Assert.Equal(status != TargetReleaseStatus.Released, outcome.RequiresManualRecovery);
		}
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryActivationCategoryMapsToALeaseOrItsFailure()
	{
		MappingTotality.AssertTotal<AutoAssemblerApplyOutcomeKind>(
			static kind => ApplyResults.TryGetValue(kind, out (CheatEngineFailureKind, CheatEngineHostEffect)? expected) &&
						   Describe(AutoAssemblerMapping.ToApplyFailure(Operation, Facts(kind))) == expected,
			static kind => Describe(AutoAssemblerMapping.ToApplyFailure(Operation, Facts(kind))) ==
						   (CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.Unknown));
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryCheckCategoryMapsToAVerdictOrItsFailure()
	{
		MappingTotality.AssertTotal<AutoAssemblerCheckOutcomeKind>(
			static kind => CheckResults.TryGetValue(kind, out (bool? Accepted, CheatEngineFailureKind Kind) expected) &&
						   Check(kind) == expected,
			static kind => Check(kind) == (default(bool?), CheatEngineFailureKind.IndeterminateHostResult));
	}

	[Fact]
	public void TheActivationEffectComesFromTheSdkEffectStateExceptForAFailedHandoff()
	{
		AutoAssemblerApplyFacts rejectedNotApplied = Facts(AutoAssemblerApplyOutcomeKind.Rejected) with
		{
			Effect = EngineEffectState.NotApplied
		};
		AutoAssemblerApplyFacts handoffNotStarted = Facts(AutoAssemblerApplyOutcomeKind.HandoffFailed) with
		{
			Effect = EngineEffectState.NotStarted
		};

		Assert.Equal(CheatEngineHostEffect.NotApplied,
			AutoAssemblerMapping.ToApplyFailure(Operation, rejectedNotApplied)!.Value.HostEffect);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed,
			AutoAssemblerMapping.ToApplyFailure(Operation, handoffNotStarted)!.Value.HostEffect);
		Assert.Contains("an unreported status",
			AutoAssemblerMapping.ToApplyFailure(Operation, handoffNotStarted)!.Value.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ARejectedCheckKeepsTruncationOnlyWithItsText()
	{
		Assert.True(AutoAssemblerMapping.TryMapCheck(Operation,
			new AutoAssemblerCheckFacts(AutoAssemblerCheckOutcomeKind.Rejected, LuaStatus.Ok, null, true),
			out AutoAssemblerCheckResult withoutText, out _));

		Assert.Equal(new AutoAssemblerCheckResult(false, null, false), withoutText);
	}

	private static (CheatEngineFailureKind, CheatEngineHostEffect)? Describe(CheatEngineFailure? failure)
	{
		return failure is { } value ? (value.Kind, value.HostEffect) : null;
	}

	private static (bool? Accepted, CheatEngineFailureKind Kind) Check(AutoAssemblerCheckOutcomeKind kind)
	{
		return AutoAssemblerMapping.TryMapCheck(Operation,
			new AutoAssemblerCheckFacts(kind, LuaStatus.Ok, null, false), out AutoAssemblerCheckResult result,
			out CheatEngineFailure failure)
			? (result.IsAccepted, CheatEngineFailureKind.Unknown)
			: (null, failure.Kind);
	}

	private static AutoAssemblerApplyFacts Facts(AutoAssemblerApplyOutcomeKind kind)
	{
		EngineEffectState effect = kind switch
		{
			AutoAssemblerApplyOutcomeKind.Applied => EngineEffectState.Applied,
			AutoAssemblerApplyOutcomeKind.GlobalUnavailable or AutoAssemblerApplyOutcomeKind.TargetIdentityUnavailable =>
				EngineEffectState.NotStarted,
			_ => EngineEffectState.Unknown
		};
		return new AutoAssemblerApplyFacts(kind, effect, LuaStatus.Ok, null, false, null, false, null);
	}
}
