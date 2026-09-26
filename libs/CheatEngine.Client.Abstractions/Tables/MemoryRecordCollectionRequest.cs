namespace CheatEngine.Client.Tables;

/// <summary>Bounds copied top-level or child memory records materialized from the Cheat Engine address list.</summary>
public readonly record struct MemoryRecordCollectionRequest
{
	/// <summary>Creates a bounded record materialization request.</summary>
	/// <param name="maximumItems">The positive maximum number of record snapshots to copy.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumItems" /> is zero or negative.</exception>
	public MemoryRecordCollectionRequest(int maximumItems)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumItems);
		MaximumItems = maximumItems;
	}

	/// <summary>Gets the maximum number of record snapshots the caller permits.</summary>
	public int MaximumItems
	{
		get;
	}
}
