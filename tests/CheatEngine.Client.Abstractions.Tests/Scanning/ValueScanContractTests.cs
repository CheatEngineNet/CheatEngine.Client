using System.Collections.Immutable;

using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Abstractions.Tests.Scanning;

public sealed class ValueScanContractTests
{
	[Fact]
	public void SessionStateUsesTheClientLifecycleWithoutSdkHandles()
	{
		Assert.Equal(0, (int) ValueScanSessionState.Created);
		Assert.Equal(1, (int) ValueScanSessionState.Scanning);
		Assert.Equal(2, (int) ValueScanSessionState.ResultsReady);
		Assert.Equal(3, (int) ValueScanSessionState.Invalidated);
		Assert.Equal(4, (int) ValueScanSessionState.Disposed);
	}

	[Fact]
	public void ReadRequestRejectsNegativeStartAndNonPositiveMaterializationLimit()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new ValueScanReadRequest(-1, 1));
		Assert.Throws<ArgumentOutOfRangeException>(() => new ValueScanReadRequest(0, 0));
	}

	[Fact]
	public void ResultPageNormalizesDefaultImmutableArrayWithoutChangingTheObservedCount()
	{
		ValueScanPage page = new(42, default);

		Assert.Equal((ulong) 42, page.TotalCount);
		Assert.True(page.Matches.IsEmpty);
		Assert.Equal(ImmutableArray<ValueScanMatch>.Empty, page.Matches);
	}

	[Fact]
	public void MatchRejectsNegativeIndexAndNullValue()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new ValueScanMatch(-1, Address.Zero, "7"));
		Assert.Throws<ArgumentNullException>(() => new ValueScanMatch(0, Address.Zero, null!));
	}
}
