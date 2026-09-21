using CheatEngine.Client.Timers;

namespace CheatEngine.Client.Abstractions.Tests.Timers;

public sealed class TimerContractsTests
{
	[Fact]
	public void RequestPreservesAStrictlyPositiveInterval()
	{
		TimerRequest request = new(TimeSpan.FromSeconds(1));

		Assert.Equal(TimeSpan.FromSeconds(1), request.Interval);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void RequestRejectsANonPositiveInterval(int milliseconds)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new TimerRequest(TimeSpan.FromMilliseconds(milliseconds)));
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(-2)]
	public void TickRejectsANegativeSequence(long sequence)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new TimerTick(sequence, DateTimeOffset.UtcNow));
	}

	[Fact]
	public void TickPreservesTheInitialSequenceAndTimestamp()
	{
		DateTimeOffset occurredAt = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
		TimerTick tick = new(0, occurredAt);

		Assert.Equal(0, tick.Sequence);
		Assert.Equal(occurredAt, tick.OccurredAt);
	}
}
