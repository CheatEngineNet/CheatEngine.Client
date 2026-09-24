using CheatEngine.Client.Core.Domains.Events;
using CheatEngine.Client.Events;

namespace CheatEngine.Client.Core.Tests.Domains.Events;

public sealed class EventStreamLeaseTests
{
	[Fact]
	public async Task DisposeStopsAdmissionBeforeNeutralizingThenCompletesAndReleasesInOrderAsync()
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
	public async Task DisposeCompletesAWaitingReaderAndClosesAdmissionBeforeReleasingTheHostRegistrationAsync()
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

	[Fact(Timeout = 10_000)]
	public async Task DisposeDoesNotHoldItsGateWhileExternalTeardownWaitsForReentrantCallbacksAsync()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		TaskCompletionSource neutralizationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource neutralizationCallbackStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource releaseStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource releaseCallbackStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
		List<string> calls = [];
		BoundedEventStream<int> stream = new(new EventStreamOptions(1));
		EventStreamLease<int> lease = null!;

		Task callback = Task.Run(async () =>
		{
			await neutralizationStarted.Task.WaitAsync(cancellationToken);
			lease.Dispose();
			neutralizationCallbackStopped.SetResult();

			await releaseStarted.Task.WaitAsync(cancellationToken);
			lease.Dispose();
			releaseCallbackStopped.SetResult();
		}, cancellationToken);

		lease = new EventStreamLease<int>(
			stream,
			() =>
			{
				calls.Add("neutralize");
				neutralizationStarted.SetResult();
				neutralizationCallbackStopped.Task.Wait(cancellationToken);
			},
			() =>
			{
				calls.Add("release");
				releaseStarted.SetResult();
				releaseCallbackStopped.Task.Wait(cancellationToken);
			},
			_ => calls.Add("untrack"));

		await Task.Run(lease.Dispose, cancellationToken).WaitAsync(cancellationToken);
		await callback.WaitAsync(cancellationToken);

		Assert.True(lease.IsReleased);
		Assert.True(stream.IsCompleted);
		Assert.Equal(["neutralize", "release", "untrack"], calls);
	}

	[Fact(Timeout = 10_000)]
	public async Task ConcurrentDisposalsRunOnlyOneCleanupSequenceAsync()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		using ManualResetEventSlim allowNeutralizationToFinish = new(false);
		TaskCompletionSource neutralizationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
		int neutralized = 0;
		int released = 0;
		int untracked = 0;
		BoundedEventStream<int> stream = new(new EventStreamOptions(1));
		EventStreamLease<int> lease = new(
			stream,
			() =>
			{
				Interlocked.Increment(ref neutralized);
				neutralizationStarted.SetResult();
				allowNeutralizationToFinish.Wait();
			},
			() => Interlocked.Increment(ref released),
			_ => Interlocked.Increment(ref untracked));

		Task firstDispose = Task.Run(lease.Dispose, cancellationToken);
		await neutralizationStarted.Task.WaitAsync(cancellationToken);
		Task secondDispose = Task.Run(lease.Dispose, cancellationToken);

		try
		{
			await secondDispose.WaitAsync(cancellationToken);
			Assert.False(firstDispose.IsCompleted);
		}
		finally
		{
			allowNeutralizationToFinish.Set();
		}

		await firstDispose.WaitAsync(cancellationToken);

		Assert.True(lease.IsReleased);
		Assert.Equal(1, neutralized);
		Assert.Equal(1, released);
		Assert.Equal(1, untracked);
	}

	[Fact]
	public async Task DisposeCompletesTheStreamAndReleasesTheHostEvenWhenNeutralizationFailsAsync()
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
	public async Task LeaseExposesTheStreamLossCounterWithoutWaitingForConsumersAsync()
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
