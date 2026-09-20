using System.Runtime.ExceptionServices;

using CheatEngine.Client.Core.Infrastructure;

namespace CheatEngine.Client.Core.Tests.Infrastructure;

public sealed class CoreResourceRegistryTests
{
	[Fact]
	public void DisposeReleasesDistinctResourcesInReverseRegistrationOrderAndIsIdempotent()
	{
		List<string> events = new();
		RecordingDisposable first = new("first", events);
		RecordingDisposable second = new("second", events);
		RecordingDisposable third = new("third", events);
		CoreResourceRegistry registry = new();

		Assert.Same(first, registry.Track(first));
		registry.Track(second);
		registry.Track(third);
		registry.Track(second);

		registry.Dispose();
		registry.Dispose();

		Assert.Equal(["third", "second", "first"], events);
		Assert.Equal(1, first.DisposeCount);
		Assert.Equal(1, second.DisposeCount);
		Assert.Equal(1, third.DisposeCount);
	}

	[Fact]
	public void DisposeContinuesInReverseOrderAfterAnEarlierResourceThrows()
	{
		List<string> events = new();
		RecordingDisposable first = new("first", events);
		RecordingDisposable failing = new("failing", events, new InvalidOperationException("expected"));
		RecordingDisposable last = new("last", events);
		CoreResourceRegistry registry = new();
		registry.Track(first);
		registry.Track(failing);
		registry.Track(last);

		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(registry.Dispose);

		Assert.Equal("expected", exception.Message);
		Assert.Equal(["last", "failing", "first"], events);
		Assert.Equal(1, first.DisposeCount);
		Assert.Equal(1, failing.DisposeCount);
		Assert.Equal(1, last.DisposeCount);
	}

	[Fact]
	public void UntrackRemovesOnlyThatResourceAndReportsWhetherItWasTracked()
	{
		List<string> events = new();
		RecordingDisposable retained = new("retained", events);
		RecordingDisposable removed = new("removed", events);
		RecordingDisposable untracked = new("untracked", events);
		CoreResourceRegistry registry = new();
		registry.Track(retained);
		registry.Track(removed);

		Assert.True(registry.Untrack(removed));
		Assert.False(registry.Untrack(untracked));

		registry.Dispose();

		Assert.Equal(["retained"], events);
		Assert.Equal(0, removed.DisposeCount);
	}

	[Fact]
	public void TrackAfterDisposeRejectsNewResources()
	{
		CoreResourceRegistry registry = new();
		registry.Dispose();

		Assert.Throws<ObjectDisposedException>(() => registry.Track(new RecordingDisposable("late", [])));
	}

	[Fact]
	public void DisposeTargetSelectionReleasesOnlyMatchingResourcesInReverseRegistrationOrder()
	{
		List<string> events = new();
		RecordingDisposable firstSelectionResource = new("first-selection", events);
		RecordingDisposable retainedResource = new("retained", events);
		RecordingDisposable lastSelectionResource = new("last-selection", events);
		CoreResourceRegistry registry = new();
		registry.Track(firstSelectionResource, 4);
		registry.Track(retainedResource, 5);
		registry.Track(lastSelectionResource, 4);

		registry.DisposeTargetSelection(4);

		Assert.Equal(["last-selection", "first-selection"], events);
		Assert.Equal(1, firstSelectionResource.DisposeCount);
		Assert.Equal(0, retainedResource.DisposeCount);
		Assert.Equal(1, lastSelectionResource.DisposeCount);

		registry.Dispose();

		Assert.Equal(["last-selection", "first-selection", "retained"], events);
		Assert.Equal(1, retainedResource.DisposeCount);
	}

	[Fact]
	public void DisposeTargetSelectionContinuesAfterFailureAndPreservesTheFirstException()
	{
		List<string> events = new();
		RecordingDisposable first = new("first", events);
		RecordingDisposable failing = new("failing", events, new InvalidOperationException("expected"));
		RecordingDisposable last = new("last", events);
		CoreResourceRegistry registry = new();
		registry.Track(first, 9);
		registry.Track(failing, 9);
		registry.Track(last, 9);

		InvalidOperationException exception =
			Assert.Throws<InvalidOperationException>(() => registry.DisposeTargetSelection(9));

		Assert.Equal("expected", exception.Message);
		Assert.Equal(["last", "failing", "first"], events);
		Assert.Equal(1, first.DisposeCount);
		Assert.Equal(1, failing.DisposeCount);
		Assert.Equal(1, last.DisposeCount);
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
