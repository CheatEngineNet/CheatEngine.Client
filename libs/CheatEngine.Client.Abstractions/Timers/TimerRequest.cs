namespace CheatEngine.Client.Timers;

/// <summary>Describes a recurring Client timer with an explicit positive interval.</summary>
public readonly record struct TimerRequest
{
	/// <summary>Creates a recurring timer request.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="interval" /> is not strictly positive.</exception>
	public TimerRequest(TimeSpan interval)
	{
		if (interval <= TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(interval), "A timer interval must be strictly positive.");
		}

		Interval = interval;
	}

	/// <summary>Gets the strictly positive timer interval.</summary>
	public TimeSpan Interval
	{
		get;
	}
}
