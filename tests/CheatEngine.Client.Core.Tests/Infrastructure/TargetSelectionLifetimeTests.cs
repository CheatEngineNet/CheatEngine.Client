using System.Runtime.ExceptionServices;

using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Tests.Infrastructure;

public sealed class TargetSelectionLifetimeTests
{
	[Fact]
	public void AdvanceInvalidatesOnlyThePreviousSelectionAndDisposesItsResourcesInLifoOrder()
	{
		List<string> events = new();
		TargetSelectionLifetime lifetime = new(static _ =>
		{
		});
		long firstEpoch = lifetime.Epoch;
		RecordingDisposable first = new("first", events);
		RecordingDisposable second = new("second", events);
		lifetime.Track(first, firstEpoch);
		lifetime.Track(second, firstEpoch);

		long nextEpoch = lifetime.Advance("Process.Refresh");

		Assert.Equal(firstEpoch + 1, nextEpoch);
		Assert.Equal(["second", "first"], events);
		Assert.Equal(1, first.DisposeCount);
		Assert.Equal(1, second.DisposeCount);

		CheatEngineClientLifecycleException exception = Assert.Throws<CheatEngineClientLifecycleException>(() =>
			lifetime.ThrowIfExpired(firstEpoch, "Memory.Read"));

		Assert.Equal(CheatEngineFailureKind.InvalidState, exception.Failure.Kind);
		Assert.Equal("Memory.Read", exception.Failure.Operation);

		lifetime.ThrowIfExpired(nextEpoch, "Memory.Read");
	}

	[Fact]
	public void AdvanceContinuesCleanupAfterAResourceFails()
	{
		List<string> events = new();
		TargetSelectionLifetime lifetime = new(static _ =>
		{
		});
		long epoch = lifetime.Epoch;
		RecordingDisposable first = new("first", events);
		RecordingDisposable failing = new("failing", events, new InvalidOperationException("expected"));
		RecordingDisposable last = new("last", events);
		lifetime.Track(first, epoch);
		lifetime.Track(failing, epoch);
		lifetime.Track(last, epoch);

		InvalidOperationException exception =
			Assert.Throws<InvalidOperationException>(() => lifetime.Advance("Process.Refresh"));

		Assert.Equal("expected", exception.Message);
		Assert.Equal(["last", "failing", "first"], events);
		Assert.Equal(epoch + 1, lifetime.Epoch);
	}

	[Fact]
	public void DisposeIsIdempotentAndRejectsNewTargetBoundRegistrations()
	{
		List<string> events = new();
		TargetSelectionLifetime lifetime = new(static _ =>
		{
		});
		RecordingDisposable resource = new("resource", events);
		lifetime.Track(resource, lifetime.Epoch);

		lifetime.Dispose();
		lifetime.Dispose();

		Assert.Equal(["resource"], events);
		Assert.Equal(1, resource.DisposeCount);
		Assert.Throws<ObjectDisposedException>(() => lifetime.Track(new RecordingDisposable("late", events), 0));
	}

	[Fact]
	public void UntrackPreventsTheNextSelectionAdvanceFromDisposingTheReleasedResource()
	{
		List<string> events = new();
		TargetSelectionLifetime lifetime = new(static _ =>
		{
		});
		RecordingDisposable resource = new("resource", events);
		lifetime.Track(resource, lifetime.Epoch);

		Assert.True(lifetime.Untrack(resource));
		Assert.False(lifetime.Untrack(resource));

		lifetime.Advance("Process.Refresh");

		Assert.Empty(events);
		Assert.Equal(0, resource.DisposeCount);
	}

	[Fact]
	public void ActivationGuardRejectsAnAdvanceWithoutChangingTheTargetSelectionEpoch()
	{
		CheatEngineActivationExpiredException expected = new("Process.Refresh", "The plugin epoch changed.");
		TargetSelectionLifetime lifetime = new(static operation =>
			throw new CheatEngineActivationExpiredException(operation, "The plugin epoch changed."));

		CheatEngineActivationExpiredException exception =
			Assert.Throws<CheatEngineActivationExpiredException>(() => lifetime.Advance("Process.Refresh"));

		Assert.Equal(expected.Failure.Kind, exception.Failure.Kind);
		Assert.Equal(expected.Failure.Operation, exception.Failure.Operation);
		Assert.Equal(0, lifetime.Epoch);
	}

	private sealed class RecordingDisposable(string name, List<string> events, Exception? exception = null)
		: IDisposable
	{
		internal int DisposeCount
		{
			get;
			private set;
		}

		public void Dispose()
		{
			DisposeCount++;
			events.Add(name);
			if (exception is not null)
			{
				ExceptionDispatchInfo.Capture(exception).Throw();
			}
		}
	}
}
