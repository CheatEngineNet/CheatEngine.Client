using CheatEngine.Client.Core.Domains.Events;
using CheatEngine.Client.Events;

namespace CheatEngine.Client.Core.Tests.Domains.Events;

public sealed class BoundedEventStreamTests
{
	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void ConstructorRejectsNonPositiveCapacity(int capacity)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
		{
			_ = new BoundedEventStream<int>(new EventStreamOptions(capacity));
		});
	}

	[Fact]
	public void ConstructorDefensivelyRejectsDefaultOptionsWhoseCapacityIsZero()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
		{
			_ = new BoundedEventStream<int>(default);
		});
	}

	[Fact]
	public async Task DropOldestEvictsTheOldestCopiedEventAndRecordsTheLoss()
	{
		using BoundedEventStream<int> stream = new(new EventStreamOptions(2));

		Assert.True(stream.TryPublish(1));
		Assert.True(stream.TryPublish(2));
		Assert.True(stream.TryPublish(3));
		stream.Complete();

		await using IAsyncEnumerator<int> enumerator = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
		Assert.True(await enumerator.MoveNextAsync());
		Assert.Equal(2, enumerator.Current);
		Assert.True(await enumerator.MoveNextAsync());
		Assert.Equal(3, enumerator.Current);
		Assert.False(await enumerator.MoveNextAsync());
		Assert.Equal(1, stream.LostCount);
	}

	[Fact]
	public async Task DropNewestPreservesTheBufferedEventsAndRecordsTheLoss()
	{
		using BoundedEventStream<int> stream = new(new EventStreamOptions(2, EventStreamOverflowPolicy.DropNewest));

		Assert.True(stream.TryPublish(1));
		Assert.True(stream.TryPublish(2));
		Assert.True(stream.TryPublish(3));
		stream.Complete();

		await using IAsyncEnumerator<int> enumerator = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
		Assert.True(await enumerator.MoveNextAsync());
		Assert.Equal(1, enumerator.Current);
		Assert.True(await enumerator.MoveNextAsync());
		Assert.Equal(2, enumerator.Current);
		Assert.False(await enumerator.MoveNextAsync());
		Assert.Equal(1, stream.LostCount);
	}

	[Fact]
	public async Task FailSubscriptionClosesTheStreamWithAnErrorAndRejectsFutureAdmission()
	{
		using BoundedEventStream<int> stream =
			new(new EventStreamOptions(1, EventStreamOverflowPolicy.FailSubscription));

		Assert.True(stream.TryPublish(1));
		Assert.False(stream.TryPublish(2));

		await using IAsyncEnumerator<int> enumerator = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
		InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
		{
			_ = await enumerator.MoveNextAsync();
		});

		Assert.Contains("overflowed", exception.Message, StringComparison.Ordinal);
		Assert.True(stream.IsCompleted);
		Assert.False(stream.IsAdmissionOpen);
		Assert.Equal(1, stream.LostCount);
		Assert.False(stream.TryPublish(3));
	}

	[Fact]
	public async Task PendingReaderReceivesPublishedValueWithoutBlockingTheCallbackAdmissionPath()
	{
		using BoundedEventStream<string> stream = new(new EventStreamOptions(1));
		await using IAsyncEnumerator<string> enumerator =
			stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);

		Task<bool> moveNext = enumerator.MoveNextAsync().AsTask();
		Assert.False(moveNext.IsCompleted);

		Assert.True(stream.TryPublish("copied"));
		Assert.True(await moveNext);
		Assert.Equal("copied", enumerator.Current);
	}

	[Fact]
	public async Task MultipleWaitingReadersReceiveCopiedEventsInAdmissionOrder()
	{
		using BoundedEventStream<int> stream = new(new EventStreamOptions(1));
		await using IAsyncEnumerator<int> first = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
		await using IAsyncEnumerator<int> second = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);

		Task<bool> firstMoveNext = first.MoveNextAsync().AsTask();
		Task<bool> secondMoveNext = second.MoveNextAsync().AsTask();

		Assert.True(stream.TryPublish(10));
		Assert.True(stream.TryPublish(20));

		Assert.True(await firstMoveNext);
		Assert.Equal(10, first.Current);
		Assert.True(await secondMoveNext);
		Assert.Equal(20, second.Current);
	}

	[Fact]
	public async Task CompletionClosesAdmissionAndCompletesAWaitingReader()
	{
		using BoundedEventStream<int> stream = new(new EventStreamOptions(1));
		await using IAsyncEnumerator<int> enumerator = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);

		Task<bool> moveNext = enumerator.MoveNextAsync().AsTask();
		Assert.False(moveNext.IsCompleted);

		stream.Complete();

		Assert.False(await moveNext);
		Assert.True(stream.IsCompleted);
		Assert.False(stream.IsAdmissionOpen);
		Assert.False(stream.TryPublish(1));
	}

	[Fact]
	public async Task CancellationRemovesTheWaitingReaderWithoutDiscardingTheNextPublishedEvent()
	{
		using BoundedEventStream<int> stream = new(new EventStreamOptions(1));
		using CancellationTokenSource cancellation = new();
		await using IAsyncEnumerator<int> cancelledEnumerator = stream.GetAsyncEnumerator(cancellation.Token);

		Task<bool> cancelledMoveNext = cancelledEnumerator.MoveNextAsync().AsTask();
		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
		{
			_ = await cancelledMoveNext;
		});

		Assert.True(stream.TryPublish(7));
		await using IAsyncEnumerator<int> activeEnumerator =
			stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
		Assert.True(await activeEnumerator.MoveNextAsync());
		Assert.Equal(7, activeEnumerator.Current);
		Assert.Equal(0, stream.LostCount);
	}

	[Fact]
	public async Task DisposeClosesAdmissionDiscardsBufferedValuesAndCompletesReaders()
	{
		BoundedEventStream<int> stream = new(new EventStreamOptions(1));
		Assert.True(stream.TryPublish(1));
		stream.Dispose();

		await using IAsyncEnumerator<int> enumerator = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
		Assert.False(await enumerator.MoveNextAsync());
		Assert.True(stream.IsCompleted);
		Assert.False(stream.IsAdmissionOpen);
		Assert.False(stream.TryPublish(2));

		stream.Dispose();
	}

	[Fact]
	public async Task OneEnumeratorRejectsOverlappingMoveNextCalls()
	{
		using BoundedEventStream<int> stream = new(new EventStreamOptions(1));
		await using IAsyncEnumerator<int> enumerator = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);

		Task<bool> firstMoveNext = enumerator.MoveNextAsync().AsTask();
		InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
		{
			_ = await enumerator.MoveNextAsync();
		});
		Assert.Contains("Concurrent", exception.Message, StringComparison.Ordinal);

		stream.Complete();
		Assert.False(await firstMoveNext);
	}
}
