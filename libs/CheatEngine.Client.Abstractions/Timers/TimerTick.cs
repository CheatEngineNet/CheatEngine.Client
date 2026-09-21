namespace CheatEngine.Client.Timers;

/// <summary>Contains a copied timer tick.</summary>
public readonly record struct TimerTick
{
	/// <summary>Creates a copied timer tick.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="sequence" /> is negative.</exception>
	public TimerTick(long sequence, DateTimeOffset occurredAt)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(sequence);
		Sequence = sequence;
		OccurredAt = occurredAt;
	}

	/// <summary>Gets the non-negative sequence number within this timer registration.</summary>
	public long Sequence
	{
		get;
	}

	/// <summary>Gets the copied tick timestamp.</summary>
	public DateTimeOffset OccurredAt
	{
		get;
	}
}
