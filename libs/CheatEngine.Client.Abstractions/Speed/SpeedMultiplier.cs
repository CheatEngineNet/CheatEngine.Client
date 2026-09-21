namespace CheatEngine.Client.Speed;

/// <summary>Represents a finite, strictly positive Client speed multiplier.</summary>
public readonly record struct SpeedMultiplier
{
	/// <summary>Creates a speed multiplier.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="value" /> is non-finite or not positive.</exception>
	public SpeedMultiplier(double value)
	{
		if (!double.IsFinite(value) || value <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(value),
				"A speed multiplier must be finite and strictly positive.");
		}

		Value = value;
	}

	/// <summary>Gets the finite, strictly positive multiplier value.</summary>
	public double Value
	{
		get;
	}
}
