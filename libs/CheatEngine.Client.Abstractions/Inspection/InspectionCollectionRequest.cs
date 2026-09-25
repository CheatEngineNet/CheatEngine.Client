namespace CheatEngine.Client.Inspection;

/// <summary>Bounds the number of copied inspection records an operation may materialize.</summary>
public readonly record struct InspectionCollectionRequest
{
	/// <summary>Creates a bounded inspection request.</summary>
	/// <param name="maximumItems">The positive maximum number of records to copy.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumItems" /> is zero or negative.</exception>
	public InspectionCollectionRequest(int maximumItems)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumItems);
		MaximumItems = maximumItems;
	}

	/// <summary>Gets the maximum number of copied records the caller permits.</summary>
	public int MaximumItems
	{
		get;
	}
}
