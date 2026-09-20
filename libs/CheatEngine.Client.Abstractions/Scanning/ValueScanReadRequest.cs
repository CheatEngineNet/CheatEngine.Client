namespace CheatEngine.Client.Scanning;

/// <summary>Describes a bounded, zero-based value-scan result read.</summary>
public readonly record struct ValueScanReadRequest
{
	/// <summary>Creates a bounded result-read request.</summary>
	public ValueScanReadRequest(int startIndex, int maximumCount)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCount);
		StartIndex = startIndex;
		MaximumCount = maximumCount;
	}

	/// <summary>Gets the first zero-based result index to read.</summary>
	public int StartIndex
	{
		get;
	}

	/// <summary>Gets the maximum number of copied matches to materialize.</summary>
	public int MaximumCount
	{
		get;
	}
}
