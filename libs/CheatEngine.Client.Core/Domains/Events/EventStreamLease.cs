using System.Runtime.ExceptionServices;

using CheatEngine.Client.Events;

namespace CheatEngine.Client.Core.Domains.Events;

/// <summary>
///     Owns the release sequence for one host callback and its copied, bounded Client event stream. The sequence is
///     deliberately ordered so that the host can no longer enqueue an event before consumers observe completion.
/// </summary>
/// <typeparam name="TEvent">The copied event type admitted by the callback.</typeparam>
internal sealed class EventStreamLease<TEvent>(
	BoundedEventStream<TEvent> stream,
	Action neutralizeCallback,
	Action releaseHostRegistration,
	Action<EventStreamLease<TEvent>>? untrack = null) : IEventStreamLease<TEvent>
{
	private readonly Lock _gate = new();

	private readonly Action _neutralizeCallback =
		neutralizeCallback ?? throw new ArgumentNullException(nameof(neutralizeCallback));

	private readonly Action _releaseHostRegistration = releaseHostRegistration ??
	                                                   throw new ArgumentNullException(nameof(releaseHostRegistration));

	private readonly BoundedEventStream<TEvent> _stream = stream ?? throw new ArgumentNullException(nameof(stream));
	private readonly Action<EventStreamLease<TEvent>>? _untrack = untrack;
	private int _disposing;
	private int _released;

	/// <inheritdoc />
	public IAsyncEnumerable<TEvent> Events => _stream;

	/// <inheritdoc />
	public long DroppedEventCount => _stream.LostCount;

	/// <inheritdoc />
	public bool IsReleased => Volatile.Read(ref _released) != 0;

	/// <summary>
	///     Releases the subscription in lifecycle order: stop admission, neutralize the host callback, complete the
	///     stream, release the host registration, and finally untrack the lease.
	/// </summary>
	public void Dispose()
	{
		lock (_gate)
		{
			if (Volatile.Read(ref _released) != 0 || Volatile.Read(ref _disposing) != 0)
			{
				return;
			}

			Volatile.Write(ref _disposing, 1);
		}

		Exception? firstFailure = null;
		firstFailure = RunCleanupStep(_stream.CloseAdmission, firstFailure);
		firstFailure = RunCleanupStep(_neutralizeCallback, firstFailure);
		firstFailure = RunCleanupStep(_stream.Complete, firstFailure);
		firstFailure = RunCleanupStep(_releaseHostRegistration, firstFailure);
		firstFailure = RunCleanupStep(Untrack, firstFailure);
		Volatile.Write(ref _released, 1);
		Volatile.Write(ref _disposing, 0);

		if (firstFailure is not null)
		{
			ExceptionDispatchInfo.Capture(firstFailure).Throw();
		}
	}

	/// <summary>Attempts to admit one copied event from the host callback without waiting for a consumer.</summary>
	internal bool TryPublish(TEvent value)
	{
		return _stream.TryPublish(value);
	}

	private void Untrack()
	{
		_untrack?.Invoke(this);
	}

	private static Exception? RunCleanupStep(Action cleanup, Exception? firstFailure)
	{
		try
		{
			cleanup();
		}
		catch (Exception exception)
		{
			return firstFailure ?? exception;
		}

		return firstFailure;
	}
}
