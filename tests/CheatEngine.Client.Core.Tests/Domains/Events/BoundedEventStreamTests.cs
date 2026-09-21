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
		Assert.Equal(2, stream.LostCount);
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
	public async Task ConcurrentReadersAreRejectedUntilTheActiveReaderIsDisposed()
	{
		using BoundedEventStream<int> stream = new(new EventStreamOptions(1));
		IAsyncEnumerator<int> first = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);

		for (int reader = 0; reader < 64; reader++)
		{
			InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
			{
				_ = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
			});
			Assert.Contains("one active", exception.Message, StringComparison.Ordinal);
		}

		await first.DisposeAsync();
		await using IAsyncEnumerator<int> second = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
		Assert.True(stream.TryPublish(10));
		Assert.True(await second.MoveNextAsync());
		Assert.Equal(10, second.Current);
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
	public async Task DisposingAWaitingEnumeratorCompletesItsReadAndFreesTheReaderSlot()
	{
		using BoundedEventStream<int> stream = new(new EventStreamOptions(1));
		IAsyncEnumerator<int> disposedEnumerator = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
		Task<bool> pendingMoveNext = disposedEnumerator.MoveNextAsync().AsTask();

		await disposedEnumerator.DisposeAsync();

		Assert.False(await pendingMoveNext);
		await using IAsyncEnumerator<int> activeEnumerator =
			stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
		Assert.True(stream.TryPublish(7));
		Assert.True(await activeEnumerator.MoveNextAsync());
		Assert.Equal(7, activeEnumerator.Current);
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
		Assert.Equal(1, stream.LostCount);

		stream.Dispose();
	}

	[Fact]
	public async Task DisposeCompletesAWaitingReaderWithoutRetainingIt()
	{
		BoundedEventStream<int> stream = new(new EventStreamOptions(1));
		await using IAsyncEnumerator<int> enumerator = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
		Task<bool> pendingMoveNext = enumerator.MoveNextAsync().AsTask();

		stream.Dispose();

		Assert.False(await pendingMoveNext);
		Assert.False(stream.TryPublish(1));
	}

	[Fact]
	public async Task CloseAdmissionRejectsNewObservationsAndLetsAcceptedObservationsDrain()
	{
		using BoundedEventStream<int> stream = new(new EventStreamOptions(1));
		Assert.True(stream.TryPublish(1));
		stream.CloseAdmission();
		stream.Complete();

		Assert.False(stream.TryPublish(2));
		await using IAsyncEnumerator<int> enumerator = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
		Assert.True(await enumerator.MoveNextAsync());
		Assert.Equal(1, enumerator.Current);
		Assert.False(await enumerator.MoveNextAsync());
		Assert.Equal(0, stream.LostCount);
	}

	[Fact]
	public async Task FaultedCompletionDiscardsBufferedEventsReportsLossAndFailsReaders()
	{
		using BoundedEventStream<int> stream = new(new EventStreamOptions(2));
		InvalidOperationException expected = new("callback failure");
		Assert.True(stream.TryPublish(1));
		stream.Complete(expected);

		await using IAsyncEnumerator<int> enumerator = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
		InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
		{
			_ = await enumerator.MoveNextAsync();
		});

		Assert.Same(expected, actual);
		Assert.Equal(1, stream.LostCount);
		Assert.False(stream.TryPublish(2));
	}

	[Fact]
	public async Task PublicationDoesNotWaitForASlowConsumerContinuation()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		using BoundedEventStream<int> stream = new(new EventStreamOptions(1));
		await using IAsyncEnumerator<int> enumerator = stream.GetAsyncEnumerator(cancellationToken);
		using ManualResetEventSlim consumerMayContinue = new(false);
		TaskCompletionSource consumerEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);

		Task consumer = Task.Run(async () =>
		{
			Assert.True(await enumerator.MoveNextAsync());
			consumerEntered.SetResult();
			consumerMayContinue.Wait(cancellationToken);
		}, cancellationToken);

		Task<bool> publish = Task.Run(() => stream.TryPublish(1));
		await consumerEntered.Task.WaitAsync(cancellationToken);
		Assert.True(publish.IsCompletedSuccessfully);

		consumerMayContinue.Set();
		await consumer;
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
