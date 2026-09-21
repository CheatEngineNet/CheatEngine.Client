namespace CheatEngine.Client.Processes;

/// <summary>Defines a bounded, copied local-process enumeration.</summary>
public readonly record struct ProcessEnumerationRequest
{
	/// <summary>Creates a bounded process enumeration request.</summary>
	public ProcessEnumerationRequest(int maximumItems, string? nameContains = null)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumItems);
		if (nameContains is { Length: 0 })
		{
			throw new ArgumentException("A process-name filter must be null or non-empty.", nameof(nameContains));
		}

		MaximumItems = maximumItems;
		NameContains = nameContains;
	}

	/// <summary>Gets the maximum number of copied process records the caller permits.</summary>
	public int MaximumItems
	{
		get;
	}

	/// <summary>Gets the optional case-insensitive substring applied to process names.</summary>
	public string? NameContains
	{
		get;
	}
}
