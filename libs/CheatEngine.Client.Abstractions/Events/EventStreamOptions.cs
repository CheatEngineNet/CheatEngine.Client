namespace CheatEngine.Client.Events;

/// <summary>Configures bounded, non-blocking materialization of host callback events.</summary>
public readonly record struct EventStreamOptions
{
	/// <summary>Creates bounded stream options.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity" /> is not positive.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="overflowPolicy" /> is not a defined value.</exception>
	public EventStreamOptions(
		int capacity,
		EventStreamOverflowPolicy overflowPolicy = EventStreamOverflowPolicy.DropOldest)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
		if (!Enum.IsDefined(overflowPolicy))
		{
			throw new ArgumentOutOfRangeException(nameof(overflowPolicy));
		}

		Capacity = capacity;
		OverflowPolicy = overflowPolicy;
	}

	/// <summary>Gets the mandatory maximum number of events buffered for a consumer.</summary>
	public int Capacity
	{
		get;
	}

	/// <summary>Gets the policy applied when <see cref="Capacity" /> is reached.</summary>
	public EventStreamOverflowPolicy OverflowPolicy
	{
		get;
	}
}
