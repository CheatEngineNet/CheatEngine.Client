using CheatEngine.Client.Core.Domains.Events;
using CheatEngine.Client.Events;

namespace CheatEngine.Client.Core.Tests.Domains.Events;

public sealed class EventStreamLeaseTests
{
	[Fact]
	public async Task DisposeStopsAdmissionBeforeNeutralizingThenCompletesAndReleasesInOrder()
	{
		List<string> calls = [];
		BoundedEventStream<int> stream = new(new EventStreamOptions(1));
		EventStreamLease<int> lease = new(
			stream,
			() =>
			{
				Assert.False(stream.IsAdmissionOpen);
				calls.Add("neutralize");
			},
			() => calls.Add("release"),
			_ => calls.Add("untrack"));

		lease.Dispose();

		Assert.True(lease.IsReleased);
		Assert.Equal(["neutralize", "release", "untrack"], calls);
		Assert.False(lease.TryPublish(1));
		await using IAsyncEnumerator<int> enumerator =
			lease.Events.GetAsyncEnumerator(TestContext.Current.CancellationToken);
		Assert.False(await enumerator.MoveNextAsync());
	}

	[Fact]
	public async Task DisposeCompletesAWaitingReaderAndClosesAdmissionBeforeReleasingTheHostRegistration()
	{
		BoundedEventStream<int> stream = new(new EventStreamOptions(1));
		await using IAsyncEnumerator<int> enumerator =
			stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
		Task<bool> pendingMoveNext = enumerator.MoveNextAsync().AsTask();
		EventStreamLease<int> lease = new(stream, static () =>
		{
		}, () => Assert.False(stream.IsAdmissionOpen));

		lease.Dispose();

		Assert.False(await pendingMoveNext);
		Assert.True(lease.IsReleased);
		Assert.False(lease.TryPublish(1));
	}

	[Fact]
	public void DisposeIsIdempotentAndDoesNotReleaseTheHostTwice()
	{
		int neutralized = 0;
		int released = 0;
		BoundedEventStream<int> stream = new(new EventStreamOptions(1));
		EventStreamLease<int> lease = new(stream, () => neutralized++, () => released++);

		lease.Dispose();
		lease.Dispose();

		Assert.Equal(1, neutralized);
		Assert.Equal(1, released);
	}

	[Fact]
	public async Task DisposeCompletesTheStreamAndReleasesTheHostEvenWhenNeutralizationFails()
	{
		BoundedEventStream<int> stream = new(new EventStreamOptions(1));
		int released = 0;
		EventStreamLease<int> lease = new(stream,
			static () => throw new InvalidOperationException("neutralize"),
			() => released++);

		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(lease.Dispose);

		Assert.Equal("neutralize", exception.Message);
		Assert.Equal(1, released);
		Assert.True(lease.IsReleased);
		Assert.False(lease.TryPublish(1));
		await using IAsyncEnumerator<int> enumerator =
			lease.Events.GetAsyncEnumerator(TestContext.Current.CancellationToken);
		Assert.False(await enumerator.MoveNextAsync());
	}

	[Fact]
	public async Task LeaseExposesTheStreamLossCounterWithoutWaitingForConsumers()
	{
		BoundedEventStream<int> stream = new(new EventStreamOptions(1));
		using EventStreamLease<int> lease = new(stream, static () =>
		{
		}, static () =>
		{
		});

		Assert.True(lease.TryPublish(1));
		Assert.True(lease.TryPublish(2));
		Assert.Equal(1, lease.DroppedEventCount);

		await using IAsyncEnumerator<int> enumerator =
			lease.Events.GetAsyncEnumerator(TestContext.Current.CancellationToken);
		Assert.True(await enumerator.MoveNextAsync());
		Assert.Equal(2, enumerator.Current);
	}
}
