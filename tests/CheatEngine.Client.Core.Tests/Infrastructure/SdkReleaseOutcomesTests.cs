using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Targets;

namespace CheatEngine.Client.Core.Tests.Infrastructure;

public sealed class SdkReleaseOutcomesTests
{
	private static readonly LeaseReleaseOutcome UnrecognizedOutcome =
		new(LeaseReleaseKind.Unknown, CheatEngineHostEffect.Unknown);

	private static readonly Dictionary<TargetReleaseStatus, LeaseReleaseOutcome> TargetTable = new()
	{
		[TargetReleaseStatus.Unspecified] = Outcome(LeaseReleaseKind.Unknown, CheatEngineHostEffect.NotStarted),
		[TargetReleaseStatus.Released] = Outcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed),
		[TargetReleaseStatus.RefusedNoTarget] =
			Outcome(LeaseReleaseKind.RefusedNoTarget, CheatEngineHostEffect.NotStarted),
		[TargetReleaseStatus.RefusedIdentityUnavailable] =
			Outcome(LeaseReleaseKind.RefusedTargetIdentityUnavailable, CheatEngineHostEffect.NotStarted),
		[TargetReleaseStatus.RefusedTargetChanged] =
			Outcome(LeaseReleaseKind.RefusedTargetChanged, CheatEngineHostEffect.NotStarted),
		[TargetReleaseStatus.RefusedProcessReused] =
			Outcome(LeaseReleaseKind.RefusedTargetChanged, CheatEngineHostEffect.NotStarted),
		[TargetReleaseStatus.UnconfirmedAfterInvocation] =
			Outcome(LeaseReleaseKind.CleanupUnconfirmed, CheatEngineHostEffect.Started),
		[TargetReleaseStatus.NotInvoked] =
			Outcome(LeaseReleaseKind.CleanupUnavailable, CheatEngineHostEffect.NotStarted),
		[TargetReleaseStatus.RefusedRuntimeChanged] =
			Outcome(LeaseReleaseKind.RefusedRuntimeChanged, CheatEngineHostEffect.NotStarted)
	};

	private static readonly Dictionary<SymbolRegistrationReleaseKind, LeaseReleaseOutcome> SymbolTable = new()
	{
		[SymbolRegistrationReleaseKind.Unknown] = UnrecognizedOutcome,
		[SymbolRegistrationReleaseKind.Released] = Outcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed),
		[SymbolRegistrationReleaseKind.AlreadyReleased] =
			Outcome(LeaseReleaseKind.AlreadyReleased, CheatEngineHostEffect.NotStarted),
		[SymbolRegistrationReleaseKind.Superseded] =
			Outcome(LeaseReleaseKind.Superseded, CheatEngineHostEffect.NotStarted),
		[SymbolRegistrationReleaseKind.StaleRuntime] =
			Outcome(LeaseReleaseKind.RefusedRuntimeChanged, CheatEngineHostEffect.NotStarted),
		[SymbolRegistrationReleaseKind.CleanupUnavailable] =
			Outcome(LeaseReleaseKind.CleanupUnavailable, CheatEngineHostEffect.NotStarted),
		[SymbolRegistrationReleaseKind.CleanupIndeterminate] =
			Outcome(LeaseReleaseKind.CleanupUnconfirmed, CheatEngineHostEffect.Started),
		[SymbolRegistrationReleaseKind.Replaced] = Outcome(LeaseReleaseKind.Replaced, CheatEngineHostEffect.NotStarted),
		[SymbolRegistrationReleaseKind.ExternallyRemoved] =
			Outcome(LeaseReleaseKind.ExternallyRemoved, CheatEngineHostEffect.NotStarted)
	};

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryTargetReleaseStatusIsMappedAndAnUnknownStatusFailsClosed()
	{
		MappingTotality.AssertTotal<TargetReleaseStatus>(
			static status => TargetTable.TryGetValue(status, out LeaseReleaseOutcome expected) &&
							 SdkReleaseOutcomes.FromTarget(status) == expected,
			static status => SdkReleaseOutcomes.FromTarget(status) == UnrecognizedOutcome);
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EverySymbolRegistrationReleaseKindIsMappedAndAnUnknownKindFailsClosed()
	{
		MappingTotality.AssertTotal<SymbolRegistrationReleaseKind>(
			static kind => SymbolTable.TryGetValue(kind, out LeaseReleaseOutcome expected) &&
						   SdkReleaseOutcomes.FromSymbolRegistration(kind) == expected,
			static kind => SdkReleaseOutcomes.FromSymbolRegistration(kind) == UnrecognizedOutcome);
	}

	/// <summary>The three SDK statuses the plan names keep their documented Client counterparts.</summary>
	[Fact]
	public void ProcessReuseUnconfirmedAndNotInvokedKeepTheirDocumentedMeaning()
	{
		LeaseReleaseOutcome reused = SdkReleaseOutcomes.FromTarget(TargetReleaseStatus.RefusedProcessReused);
		LeaseReleaseOutcome unconfirmed = SdkReleaseOutcomes.FromTarget(TargetReleaseStatus.UnconfirmedAfterInvocation);
		LeaseReleaseOutcome notInvoked = SdkReleaseOutcomes.FromTarget(TargetReleaseStatus.NotInvoked);

		Assert.Equal(LeaseReleaseKind.RefusedTargetChanged, reused.Kind);
		Assert.True(reused.RequiresManualRecovery);
		Assert.Equal(Outcome(LeaseReleaseKind.CleanupUnconfirmed, CheatEngineHostEffect.Started), unconfirmed);
		Assert.True(unconfirmed.RequiresManualRecovery);
		Assert.Equal(Outcome(LeaseReleaseKind.CleanupUnavailable, CheatEngineHostEffect.NotStarted), notInvoked);
		Assert.True(notInvoked.IsRetryable);
	}

	/// <summary>No SDK status reads as a release unless the SDK confirmed one.</summary>
	[Fact]
	public void OnlyConfirmedSdkReleasesMapToReleased()
	{
		Assert.Equal([TargetReleaseStatus.Released], TargetTable
			.Where(static pair => pair.Value.Kind == LeaseReleaseKind.Released).Select(static pair => pair.Key));
		Assert.Equal([SymbolRegistrationReleaseKind.Released], SymbolTable
			.Where(static pair => pair.Value.Kind == LeaseReleaseKind.Released).Select(static pair => pair.Key));
	}

	/// <summary>The combinator is commutative and keeps the kind that leaves the most to do.</summary>
	[Theory]
	[InlineData(LeaseReleaseKind.Released, LeaseReleaseKind.CleanupUnconfirmed, LeaseReleaseKind.CleanupUnconfirmed)]
	[InlineData(LeaseReleaseKind.Released, LeaseReleaseKind.Replaced, LeaseReleaseKind.Replaced)]
	[InlineData(LeaseReleaseKind.AlreadyReleased, LeaseReleaseKind.Released, LeaseReleaseKind.AlreadyReleased)]
	[InlineData(LeaseReleaseKind.RefusedTargetChanged, LeaseReleaseKind.PartiallyReleased,
		LeaseReleaseKind.PartiallyReleased)]
	[InlineData(LeaseReleaseKind.RefusedRuntimeChanged, LeaseReleaseKind.RefusedNoTarget,
		LeaseReleaseKind.RefusedRuntimeChanged)]
	[InlineData(LeaseReleaseKind.CleanupUnconfirmed, LeaseReleaseKind.CleanupUnavailable,
		LeaseReleaseKind.CleanupUnavailable)]
	[InlineData(LeaseReleaseKind.CleanupUnavailable, LeaseReleaseKind.Unknown, LeaseReleaseKind.Unknown)]
	[InlineData(LeaseReleaseKind.ExternallyRemoved, LeaseReleaseKind.RefusedNoTarget,
		LeaseReleaseKind.RefusedNoTarget)]
	public void WorstKeepsTheKindThatLeavesTheMostToDo(LeaseReleaseKind first, LeaseReleaseKind second,
		LeaseReleaseKind expected)
	{
		LeaseReleaseOutcome a = Outcome(first, CheatEngineHostEffect.NotStarted);
		LeaseReleaseOutcome b = Outcome(second, CheatEngineHostEffect.NotStarted);

		Assert.Equal(expected, SdkReleaseOutcomes.Worst(a, b).Kind);
		Assert.Equal(expected, SdkReleaseOutcomes.Worst(b, a).Kind);
	}

	/// <summary>A retryable part keeps the whole lease retryable, so the part that can still be released is retried.</summary>
	[Fact]
	public void AnyRetryablePartKeepsTheCombinedOutcomeRetryable()
	{
		LeaseReleaseKind[] kinds = Enum.GetValues<LeaseReleaseKind>();
		foreach (LeaseReleaseKind first in kinds)
		{
			foreach (LeaseReleaseKind second in kinds)
			{
				LeaseReleaseOutcome a = Outcome(first, CheatEngineHostEffect.Completed);
				LeaseReleaseOutcome b = Outcome(second, CheatEngineHostEffect.Completed);
				LeaseReleaseOutcome combined = SdkReleaseOutcomes.Worst(a, b);

				Assert.Equal(combined, SdkReleaseOutcomes.Worst(b, a));
				Assert.Equal(a.IsRetryable || b.IsRetryable, combined.IsRetryable);
				Assert.Equal(a.IsComplete && b.IsComplete, combined.IsComplete);
			}
		}
	}

	/// <summary>Host effects combine separately: equal stays, Unknown wins, then CleanupUnconfirmed, else Started.</summary>
	[Theory]
	[InlineData(CheatEngineHostEffect.Completed, CheatEngineHostEffect.Completed, CheatEngineHostEffect.Completed)]
	[InlineData(CheatEngineHostEffect.NotStarted, CheatEngineHostEffect.NotStarted, CheatEngineHostEffect.NotStarted)]
	[InlineData(CheatEngineHostEffect.Completed, CheatEngineHostEffect.NotStarted, CheatEngineHostEffect.Started)]
	[InlineData(CheatEngineHostEffect.Started, CheatEngineHostEffect.Completed, CheatEngineHostEffect.Started)]
	[InlineData(CheatEngineHostEffect.Unknown, CheatEngineHostEffect.CleanupUnconfirmed, CheatEngineHostEffect.Unknown)]
	[InlineData(CheatEngineHostEffect.CleanupUnconfirmed, CheatEngineHostEffect.Completed,
		CheatEngineHostEffect.CleanupUnconfirmed)]
	public void WorstCombinesHostEffectsSeparately(CheatEngineHostEffect first, CheatEngineHostEffect second,
		CheatEngineHostEffect expected)
	{
		LeaseReleaseOutcome a = Outcome(LeaseReleaseKind.Released, first);
		LeaseReleaseOutcome b = Outcome(LeaseReleaseKind.CleanupUnconfirmed, second);

		Assert.Equal(expected, SdkReleaseOutcomes.Worst(a, b).HostEffect);
		Assert.Equal(expected, SdkReleaseOutcomes.Worst(b, a).HostEffect);
	}

	private static LeaseReleaseOutcome Outcome(LeaseReleaseKind kind, CheatEngineHostEffect hostEffect)
	{
		return new LeaseReleaseOutcome(kind, hostEffect);
	}
}
