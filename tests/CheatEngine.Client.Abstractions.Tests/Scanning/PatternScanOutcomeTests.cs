using System.Collections.Immutable;

using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Abstractions.Tests.Scanning;

public sealed class PatternScanOutcomeTests
{
	private static readonly TimeSpan Elapsed = TimeSpan.FromMilliseconds(3);

	[Theory]
	[InlineData(-1, 0, 0, 0)]
	[InlineData(0, -1, 0, 0)]
	[InlineData(0, 0, -1, 0)]
	[InlineData(0, 0, 0, -1)]
	public void MetricsConstructorRejectsNegativeCounts(int host, int examined, int filteredOut, int materialized)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new PatternScanMetrics(host, examined, filteredOut,
			materialized, PatternScanScope.GlobalHostScanWithManagedFilter, Elapsed, Elapsed));
	}

	[Theory]
	[InlineData(2, 3, 0, 0)]
	[InlineData(5, 3, 2, 2)]
	[InlineData(5, 3, 0, 4)]
	[InlineData(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue)]
	public void MetricsConstructorRejectsInconsistentCounts(int host, int examined, int filteredOut, int materialized)
	{
		Assert.Throws<ArgumentException>(() => new PatternScanMetrics(host, examined, filteredOut, materialized,
			PatternScanScope.GlobalHostScanWithManagedFilter, Elapsed, Elapsed));
	}

	[Fact]
	public void MetricsConstructorRejectsNegativeDurationsAndUndefinedScopes()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new PatternScanMetrics(1, 1, 0, 1,
			PatternScanScope.GlobalHostScanWithManagedFilter, TimeSpan.FromTicks(-1), Elapsed));
		Assert.Throws<ArgumentOutOfRangeException>(() => new PatternScanMetrics(1, 1, 0, 1,
			PatternScanScope.GlobalHostScanWithManagedFilter, Elapsed, TimeSpan.FromTicks(-1)));
		Assert.Throws<ArgumentOutOfRangeException>(() => new PatternScanMetrics(1, 1, 0, 1,
			(PatternScanScope) 42, Elapsed, Elapsed));
	}

	[Fact]
	public void MetricsConstructorKeepsTheValidatedValues()
	{
		PatternScanMetrics metrics = new(10, 6, 3, 2, PatternScanScope.GlobalHostScanWithManagedFilter,
			TimeSpan.FromMilliseconds(70), TimeSpan.FromMilliseconds(8));

		Assert.Equal(10, metrics.HostMatchCount);
		Assert.Equal(6, metrics.ExaminedCount);
		Assert.Equal(3, metrics.FilteredOutCount);
		Assert.Equal(2, metrics.MaterializedCount);
		Assert.Equal(PatternScanScope.GlobalHostScanWithManagedFilter, metrics.Scope);
		Assert.Equal(TimeSpan.FromMilliseconds(70), metrics.HostScanElapsed);
		Assert.Equal(TimeSpan.FromMilliseconds(8), metrics.MaterializationElapsed);
		Assert.Equal(PatternScanScope.Unknown, default(PatternScanMetrics).Scope);
	}

	[Fact]
	public void OutcomeRequiresExactlyOneOfResultOrCause()
	{
		AobScanResult result = new(ImmutableArray.Create<Address>(0x401000), false);
		CheatEngineFailure failure = new(CheatEngineFailureKind.IndeterminateHostResult, "Patterns.Scan",
			"no list", null, CheatEngineHostEffect.Completed);
		PatternScanMetrics metrics = Metrics(materialized: 1);

		Assert.Throws<ArgumentException>(() => new PatternScanOutcome(null, null, null));
		Assert.Throws<ArgumentException>(() => new PatternScanOutcome(result, failure, metrics));
		Assert.Throws<ArgumentException>(() => new PatternScanOutcome(null, default(CheatEngineFailure), null));

		PatternScanOutcome success = new(result, null, metrics);
		PatternScanOutcome noList = new(null, failure, null);

		Assert.True(success.Succeeded);
		Assert.Equal(result, success.Result);
		Assert.Equal(metrics, success.Metrics);
		Assert.False(noList.Succeeded);
		Assert.Equal(failure, noList.Cause);
		Assert.Null(noList.Metrics);
	}

	[Fact]
	public void SuccessfulOutcomeRequiresMetricsThatMatchTheCopiedResult()
	{
		AobScanResult result = new(ImmutableArray.Create<Address>(0x401000, 0x401010), false);

		Assert.Throws<ArgumentException>(() => new PatternScanOutcome(result, null, null));
		Assert.Throws<ArgumentException>(() => new PatternScanOutcome(result, null, Metrics(materialized: 1)));
		Assert.True(new PatternScanOutcome(result, null, Metrics(materialized: 2)).Succeeded);
	}

	private static PatternScanMetrics Metrics(int materialized)
	{
		return new PatternScanMetrics(5, 4, 1, materialized, PatternScanScope.GlobalHostScanWithManagedFilter,
			Elapsed, Elapsed);
	}
}
