using CheatEngine.Client.Events;

namespace CheatEngine.Client.Abstractions.Tests.Events;

public sealed class EventStreamOptionsTests
{
	[Fact]
	public void ConstructorUsesDropOldestByDefaultForAPositiveCapacity()
	{
		EventStreamOptions options = new(32);

		Assert.Equal(32, options.Capacity);
		Assert.Equal(EventStreamOverflowPolicy.DropOldest, options.OverflowPolicy);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void ConstructorRejectsANonPositiveCapacity(int capacity)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new EventStreamOptions(capacity));
	}

	[Theory]
	[InlineData(EventStreamOverflowPolicy.DropNewest)]
	[InlineData(EventStreamOverflowPolicy.FailSubscription)]
	public void ConstructorPreservesEachExplicitOverflowPolicy(EventStreamOverflowPolicy overflowPolicy)
	{
		EventStreamOptions options = new(1, overflowPolicy);

		Assert.Equal(overflowPolicy, options.OverflowPolicy);
	}

	[Fact]
	public void ConstructorRejectsAnUndefinedOverflowPolicy()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new EventStreamOptions(1, (EventStreamOverflowPolicy) 99));
	}
}
