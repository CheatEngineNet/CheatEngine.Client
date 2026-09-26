using CheatEngine.Client.Results;

namespace CheatEngine.Client.Abstractions.Tests.Results;

public sealed class LeaseReleaseOutcomeTests
{
	/// <summary>Every kind keeps its published value; the vocabulary is the contiguous range 0 to 12.</summary>
	[Fact]
	public void KindsHaveStableExplicitValues()
	{
		Assert.Equal(0, (int) LeaseReleaseKind.Unknown);
		Assert.Equal(1, (int) LeaseReleaseKind.Released);
		Assert.Equal(2, (int) LeaseReleaseKind.AlreadyReleased);
		Assert.Equal(3, (int) LeaseReleaseKind.PartiallyReleased);
		Assert.Equal(4, (int) LeaseReleaseKind.Replaced);
		Assert.Equal(5, (int) LeaseReleaseKind.Superseded);
		Assert.Equal(6, (int) LeaseReleaseKind.ExternallyRemoved);
		Assert.Equal(7, (int) LeaseReleaseKind.RefusedTargetNotAttached);
		Assert.Equal(8, (int) LeaseReleaseKind.RefusedTargetChanged);
		Assert.Equal(9, (int) LeaseReleaseKind.RefusedTargetIdentityUnavailable);
		Assert.Equal(10, (int) LeaseReleaseKind.RefusedRuntimeChanged);
		Assert.Equal(11, (int) LeaseReleaseKind.CleanupUnconfirmed);
		Assert.Equal(12, (int) LeaseReleaseKind.CleanupUnavailable);
		Assert.Equal(Enumerable.Range(0, 13), Enum.GetValues<LeaseReleaseKind>().Select(static kind => (int) kind));
	}

	/// <summary>
	///     Each kind sets exactly one of the three flags; only Unknown and CleanupUnavailable are retryable, as in
	///     CheatEngine.SDK (A9).
	/// </summary>
	[Theory]
	[InlineData(LeaseReleaseKind.Unknown, false, true, false)]
	[InlineData(LeaseReleaseKind.Released, true, false, false)]
	[InlineData(LeaseReleaseKind.AlreadyReleased, true, false, false)]
	[InlineData(LeaseReleaseKind.PartiallyReleased, false, false, true)]
	[InlineData(LeaseReleaseKind.Replaced, true, false, false)]
	[InlineData(LeaseReleaseKind.Superseded, true, false, false)]
	[InlineData(LeaseReleaseKind.ExternallyRemoved, true, false, false)]
	[InlineData(LeaseReleaseKind.RefusedTargetNotAttached, false, false, true)]
	[InlineData(LeaseReleaseKind.RefusedTargetChanged, false, false, true)]
	[InlineData(LeaseReleaseKind.RefusedTargetIdentityUnavailable, false, false, true)]
	[InlineData(LeaseReleaseKind.RefusedRuntimeChanged, false, false, true)]
	[InlineData(LeaseReleaseKind.CleanupUnconfirmed, false, false, true)]
	[InlineData(LeaseReleaseKind.CleanupUnavailable, false, true, false)]
	public void EachKindSetsExactlyOneFlag(LeaseReleaseKind kind, bool complete, bool retryable, bool manualRecovery)
	{
		LeaseReleaseOutcome outcome = new(kind, CheatEngineHostEffect.NotStarted);

		Assert.Equal(complete, outcome.IsComplete);
		Assert.Equal(retryable, outcome.IsRetryable);
		Assert.Equal(manualRecovery, outcome.RequiresManualRecovery);
		Assert.Equal(1, (outcome.IsComplete ? 1 : 0) + (outcome.IsRetryable ? 1 : 0) +
						(outcome.RequiresManualRecovery ? 1 : 0));
	}

	/// <summary>The theory above covers every defined kind, so a new kind fails here until its flags are decided.</summary>
	[Fact]
	public void TheFlagTableCoversEveryKind()
	{
		int retryable = Enum.GetValues<LeaseReleaseKind>()
			.Count(static kind => new LeaseReleaseOutcome(kind, CheatEngineHostEffect.Unknown).IsRetryable);

		Assert.Equal(13, Enum.GetValues<LeaseReleaseKind>().Length);
		Assert.Equal(2, retryable);
	}

	/// <summary>The default outcome never reads as a release: it is Unknown, with an unknown effect, and retryable.</summary>
	[Fact]
	public void DefaultOutcomeIsAnUnknownRetryableOutcome()
	{
		LeaseReleaseOutcome outcome = default;

		Assert.Equal(LeaseReleaseKind.Unknown, outcome.Kind);
		Assert.Equal(CheatEngineHostEffect.Unknown, outcome.HostEffect);
		Assert.False(outcome.IsComplete);
		Assert.True(outcome.IsRetryable);
		Assert.False(outcome.RequiresManualRecovery);
		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.Unknown, CheatEngineHostEffect.Unknown), outcome);
	}

	/// <summary>Kind and host effect round-trip and take part in equality.</summary>
	[Fact]
	public void KindAndHostEffectRoundTripAndDefineEquality()
	{
		LeaseReleaseOutcome unconfirmed = new(LeaseReleaseKind.CleanupUnconfirmed, CheatEngineHostEffect.Started);

		Assert.Equal(LeaseReleaseKind.CleanupUnconfirmed, unconfirmed.Kind);
		Assert.Equal(CheatEngineHostEffect.Started, unconfirmed.HostEffect);
		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnconfirmed, CheatEngineHostEffect.Started),
			unconfirmed);
		Assert.NotEqual(new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnconfirmed, CheatEngineHostEffect.Unknown),
			unconfirmed);
	}

	/// <summary>An undefined kind or host effect is rejected instead of being stored unclassified.</summary>
	[Theory]
	[InlineData(-1, 0, "kind")]
	[InlineData(13, 0, "kind")]
	[InlineData(0, -1, "hostEffect")]
	[InlineData(0, 6, "hostEffect")]
	public void ConstructorRejectsUndefinedValues(int kind, int hostEffect, string parameter)
	{
		ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
			new LeaseReleaseOutcome((LeaseReleaseKind) kind, (CheatEngineHostEffect) hostEffect));

		Assert.Equal(parameter, exception.ParamName);
	}

	/// <summary>Formatting the outcome, as structured loggers do, emits only the kind and the effect.</summary>
	[Fact]
	[Trait("Qualification", "Q46")]
	public void ToStringReturnsOnlyTheKindAndTheEffect()
	{
		LeaseReleaseOutcome outcome = new(LeaseReleaseKind.RefusedTargetChanged, CheatEngineHostEffect.NotStarted);

		Assert.Equal("RefusedTargetChanged (host effect: NotStarted)", outcome.ToString());
	}
}
