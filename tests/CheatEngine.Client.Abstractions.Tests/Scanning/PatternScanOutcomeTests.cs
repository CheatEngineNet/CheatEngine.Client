using System.Collections.Immutable;

using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Abstractions.Tests.Scanning;

public sealed class PatternScanOutcomeTests
{
	private const PatternScanScope Scope = PatternScanScope.GlobalHostScanWithManagedFilter;

	private static readonly TimeSpan Elapsed = TimeSpan.FromMilliseconds(3);

	[Fact]
	public void MetricsConstructorRejectsANegativeMaterializedCountNegativeDurationsAndUndefinedScopes()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new PatternScanMetrics(Scope, 1, 1, 0, -1, 0, 0, 0, true,
			Elapsed, Elapsed));
		Assert.Throws<ArgumentOutOfRangeException>(() => new PatternScanMetrics(Scope, 1, 1, 0, 1, 0, 0, 0, true,
			TimeSpan.FromTicks(-1), Elapsed));
		Assert.Throws<ArgumentOutOfRangeException>(() => new PatternScanMetrics(Scope, 1, 1, 0, 1, 0, 0, 0, true,
			Elapsed, TimeSpan.FromTicks(-1)));
		Assert.Throws<ArgumentOutOfRangeException>(() => new PatternScanMetrics((PatternScanScope) 42, 1, 1, 0, 1, 0,
			0, 0, true, Elapsed, Elapsed));
	}

	/// <summary>Every documented invariant between the counts is enforced.</summary>
	[Theory]
	[InlineData(2UL, 3UL, 0UL, 0, 0UL, 0UL, 0UL, false)]
	[InlineData(5UL, 3UL, 0UL, 0, 0UL, 0UL, 1UL, false)]
	[InlineData(5UL, 3UL, 4UL, 0, 0UL, 0UL, 2UL, false)]
	[InlineData(5UL, 3UL, 2UL, 2, 0UL, 0UL, 2UL, false)]
	[InlineData(5UL, 5UL, 2UL, 1, 2UL, 1UL, 0UL, true)]
	[InlineData(5UL, 3UL, 1UL, 1, 0UL, 0UL, 2UL, true)]
	[InlineData(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, 1, 0UL, 0UL, 0UL, false)]
	public void MetricsConstructorRejectsInconsistentCounts(ulong host, ulong examined, ulong filteredOut,
		int materialized, ulong belowStart, ulong atOrAfterStop, ulong unread, bool exact)
	{
		Assert.Throws<ArgumentException>(() => new PatternScanMetrics(Scope, host, examined, filteredOut, materialized,
			belowStart, atOrAfterStop, unread, exact, Elapsed, Elapsed));
	}

	[Fact]
	public void MetricsConstructorKeepsTheValidatedValues()
	{
		PatternScanMetrics metrics = new(PatternScanScope.HostBoundedRange, 10, 6, 3, 2, 1, 1, 4, false,
			TimeSpan.FromMilliseconds(70), TimeSpan.FromMilliseconds(8));

		Assert.Equal(PatternScanScope.HostBoundedRange, metrics.Scope);
		Assert.Equal(10UL, metrics.HostResultCount);
		Assert.Equal(6UL, metrics.ExaminedCount);
		Assert.Equal(3UL, metrics.FilteredOutCount);
		Assert.Equal(2, metrics.MaterializedCount);
		Assert.Equal(1UL, metrics.BelowStartSkippedCount);
		Assert.Equal(1UL, metrics.AtOrAfterStopSkippedCount);
		Assert.Equal(4UL, metrics.UnreadHostRowCount);
		Assert.False(metrics.InBoundsCountIsExact);
		Assert.Equal(TimeSpan.FromMilliseconds(70), metrics.HostScanElapsed);
		Assert.Equal(TimeSpan.FromMilliseconds(8), metrics.MaterializationElapsed);
		Assert.Equal(PatternScanScope.Unknown, default(PatternScanMetrics).Scope);
	}

	[Fact]
	public void OutcomeRequiresExactlyOneOfResultOrFailure()
	{
		AobScanResult result = new(ImmutableArray.Create<Address>(0x401000), false);
		CheatEngineFailure failure = new(CheatEngineFailureKind.IndeterminateHostResult, "Patterns.Scan",
			"no list", null, CheatEngineHostEffect.Completed);
		PatternScanMetrics metrics = Metrics(materialized: 1);

		Assert.Throws<ArgumentException>(() => Outcome(null, null, null));
		Assert.Throws<ArgumentException>(() => Outcome(result, failure, metrics));
		Assert.Throws<ArgumentException>(() => Outcome(null, default(CheatEngineFailure), null));

		PatternScanOutcome success = new(result, null, metrics, PatternScanHostOutcomeKind.Matches,
			PatternScanRouteReason.TargetIdentityNotQualified, false);
		PatternScanOutcome noList = new(null, failure, null, PatternScanHostOutcomeKind.NoResult,
			PatternScanRouteReason.UnscopedRequest, false);

		Assert.True(success.IsSuccess);
		Assert.Equal(result, success.Result);
		Assert.Equal(metrics, success.Metrics);
		Assert.Equal(PatternScanHostOutcomeKind.Matches, success.HostOutcome);
		Assert.Equal(PatternScanRouteReason.TargetIdentityNotQualified, success.RouteReason);
		Assert.False(success.TargetIdentityVerified);
		Assert.False(noList.IsSuccess);
		Assert.Equal(failure, noList.Failure);
		Assert.Null(noList.Metrics);
		Assert.Equal(PatternScanHostOutcomeKind.NoResult, noList.HostOutcome);
	}

	[Fact]
	public void SuccessfulOutcomeRequiresMetricsThatMatchTheCopiedResult()
	{
		AobScanResult result = new(ImmutableArray.Create<Address>(0x401000, 0x401010), false);

		Assert.Throws<ArgumentException>(() => Outcome(result, null, null));
		Assert.Throws<ArgumentException>(() => Outcome(result, null, Metrics(materialized: 1)));
		Assert.True(Outcome(result, null, Metrics(materialized: 2)).IsSuccess);
	}

	[Theory]
	[InlineData(PatternScanHostOutcomeKind.NoResult, PatternScanRouteReason.UnscopedRequest)]
	[InlineData(PatternScanHostOutcomeKind.Unknown, PatternScanRouteReason.UnscopedRequest)]
	[InlineData(PatternScanHostOutcomeKind.Matches, PatternScanRouteReason.Unknown)]
	public void ASuccessReportsAMatchesOrNoMatchesHostOutcomeAndItsRoute(PatternScanHostOutcomeKind hostOutcome,
		PatternScanRouteReason routeReason)
	{
		AobScanResult result = new(ImmutableArray.Create<Address>(0x401000), false);

		Assert.Throws<ArgumentException>(() =>
			new PatternScanOutcome(result, null, Metrics(materialized: 1), hostOutcome, routeReason, false));
	}

	[Fact]
	public void AFailureNeverReportsAVerifiedTarget()
	{
		CheatEngineFailure failure = new(CheatEngineFailureKind.TargetChanged, "Patterns.Scan", "changed", null,
			CheatEngineHostEffect.Completed);

		Assert.Throws<ArgumentException>(() => new PatternScanOutcome(null, failure, null,
			PatternScanHostOutcomeKind.Matches, PatternScanRouteReason.UnscopedRequest, true));
	}

	[Fact]
	public void OutcomeRejectsUndefinedHostOutcomesAndRouteReasons()
	{
		CheatEngineFailure failure = new(CheatEngineFailureKind.InvalidHostResult, "Patterns.Scan", "bad", null,
			CheatEngineHostEffect.Completed);

		Assert.Throws<ArgumentOutOfRangeException>(() => new PatternScanOutcome(null, failure, null,
			(PatternScanHostOutcomeKind) 99, PatternScanRouteReason.Unknown, false));
		Assert.Throws<ArgumentOutOfRangeException>(() => new PatternScanOutcome(null, failure, null,
			PatternScanHostOutcomeKind.Unknown, (PatternScanRouteReason) 99, false));
	}

	private static PatternScanOutcome Outcome(AobScanResult? result, CheatEngineFailure? failure,
		PatternScanMetrics? metrics)
	{
		return new PatternScanOutcome(result, failure, metrics, PatternScanHostOutcomeKind.Matches,
			PatternScanRouteReason.UnscopedRequest, false);
	}

	private static PatternScanMetrics Metrics(int materialized)
	{
		return new PatternScanMetrics(Scope, 5, 4, 1, materialized, 0, 0, 1, false, Elapsed, Elapsed);
	}
}
