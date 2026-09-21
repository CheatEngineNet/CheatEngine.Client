namespace CheatEngine.Client.Events;

/// <summary>Owns a bounded stream of copied callback events for one Client activation.</summary>
/// <typeparam name="TEvent">The copied event snapshot type.</typeparam>
/// <remarks>
///     Implementations must never wait for a stream consumer on a Cheat Engine or Lua callback thread. Disposing a
///     lease closes admission, detaches its callback, completes <see cref="Events" />, and releases host state.
/// </remarks>
public interface IEventStreamLease<TEvent> : IDisposable
{
	/// <summary>Gets the bounded stream of copied events.</summary>
	public IAsyncEnumerable<TEvent> Events
	{
		get;
	}

	/// <summary>Gets the number of events that were not admitted to the stream.</summary>
	public long DroppedEventCount
	{
		get;
	}

	/// <summary>Gets whether this activation-scoped subscription has released its host registration.</summary>
	public bool IsReleased
	{
		get;
	}
}
