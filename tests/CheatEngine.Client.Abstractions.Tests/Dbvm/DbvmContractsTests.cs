using CheatEngine.Client.Dbvm;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Abstractions.Tests.Dbvm;

public sealed class DbvmContractsTests
{
	[Fact]
	public void StatusPreservesAnExplicitUninitializedObservation()
	{
		DbvmStatusSnapshot status = new(DbvmState.NotInitialized, "7.7");

		Assert.Equal(DbvmState.NotInitialized, status.State);
		Assert.Equal("7.7", status.Version);
	}

	[Fact]
	public void StatusRejectsAnUndefinedState()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new DbvmStatusSnapshot((DbvmState) 99));
	}

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	public void StatusRejectsAnEmptyVersionWhenOneIsProvided(string version)
	{
		Assert.Throws<ArgumentException>(() => new DbvmStatusSnapshot(DbvmState.Initialized, version));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void WatchRequestAndEventRejectANonPositiveLength(int length)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new DbvmWatchRequest(0x600000, length));
		Assert.Throws<ArgumentOutOfRangeException>(() => new DbvmWatchEvent(0x600000, length, DateTimeOffset.UtcNow));
	}

	[Fact]
	public void InitializationRequestPreservesTheExplicitStealthChoice()
	{
		DbvmInitializationRequest request = new(true);

		Assert.True(request.UseStealthMode);
	}

	[Fact]
	public void WatchRequestAndEventPreserveTheirBoundedCopiedObservation()
	{
		DateTimeOffset occurredAt = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
		DbvmWatchRequest request = new(0x600000, 8);
		DbvmWatchEvent watchEvent = new(0x600000, 8, occurredAt);

		Assert.Equal((Address) 0x600000, request.Address);
		Assert.Equal(8, request.Length);
		Assert.Equal((Address) 0x600000, watchEvent.Address);
		Assert.Equal(8, watchEvent.Length);
		Assert.Equal(occurredAt, watchEvent.OccurredAt);
	}
}
