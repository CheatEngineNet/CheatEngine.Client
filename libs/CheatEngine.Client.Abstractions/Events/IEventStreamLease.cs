namespace CheatEngine.Client.Events;

/// <summary>Owns a bounded, unicast stream of copied callback observations for one Client activation.</summary>
/// <typeparam name="TEvent">The copied event snapshot type.</typeparam>
/// <remarks>
///     The stream has one active reader at a time; a concurrent reader is rejected and observations are never
///     broadcast. Implementations must not wait for a stream consumer on a Cheat Engine or Lua callback thread,
///     although admission may use a bounded synchronization primitive. Cancelling or disposing an enumerator ends
///     that observation read only; it neither cancels nor decides the native callback. Disposing a lease closes
///     admission, detaches its callback, completes <see cref="Events" />, and releases host state. Activation teardown
///     follows the same release contract, so an expired lease cannot admit later observations.
/// </remarks>
public interface IEventStreamLease<TEvent> : IDisposable
{
	/// <summary>
	///     Gets the bounded, single-active-reader stream of copied observations. Buffered observations are delivered in
	///     FIFO order to that reader; overflow is reported through <see cref="DroppedEventCount" /> and does not provide
	///     a native callback decision.
	/// </summary>
	public IAsyncEnumerable<TEvent> Events
	{
		get;
	}

	/// <summary>
	///     Gets the number of observations not delivered because the bounded stream overflowed or terminal processing
	///     discarded buffered observations.
	/// </summary>
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
