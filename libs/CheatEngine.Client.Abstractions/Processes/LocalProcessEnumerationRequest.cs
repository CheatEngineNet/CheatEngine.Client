namespace CheatEngine.Client.Processes;

/// <summary>Defines a bounded, copied local-process enumeration.</summary>
public readonly record struct LocalProcessEnumerationRequest
{
	/// <summary>Creates a bounded process enumeration request.</summary>
	/// <param name="maximumResults">The positive maximum number of processes to copy.</param>
	/// <param name="nameContains">
	///     A case-insensitive substring the process name must contain, or <see langword="null" /> for every process.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="maximumResults" /> is zero or negative.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="nameContains" /> is empty.</exception>
	public LocalProcessEnumerationRequest(int maximumResults, string? nameContains = null)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumResults);
		if (nameContains is { Length: 0 })
		{
			throw new ArgumentException("A process-name filter must be null or non-empty.", nameof(nameContains));
		}

		MaximumResults = maximumResults;
		NameContains = nameContains;
	}

	/// <summary>
	///     Gets the maximum number of process records copied: a longer catalog is truncated to it and reported with
	///     <see cref="LocalProcessEnumerationResult.IsTruncated" />.
	/// </summary>
	public int MaximumResults
	{
		get;
	}

	/// <summary>Gets the optional case-insensitive substring applied to process names.</summary>
	public string? NameContains
	{
		get;
	}
}
