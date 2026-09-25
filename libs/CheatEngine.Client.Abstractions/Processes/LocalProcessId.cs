namespace CheatEngine.Client.Processes;

/// <summary>Identifies a process observed by the local operating-system catalog.</summary>
/// <remarks>This is deliberately not a Cheat Engine target identity. A numeric process identifier can be reused.</remarks>
public readonly record struct LocalProcessId
{
	/// <summary>Creates a positive local operating-system process identifier.</summary>
	/// <param name="value">The positive process identifier.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="value" /> is zero or negative.</exception>
	public LocalProcessId(int value)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
		Value = value;
	}

	/// <summary>Gets the locally observed numeric process identifier.</summary>
	public int Value
	{
		get;
	}
}
