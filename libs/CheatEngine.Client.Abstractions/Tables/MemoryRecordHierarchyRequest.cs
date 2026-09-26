namespace CheatEngine.Client.Tables;

/// <summary>Bounds a copied memory-record hierarchy traversal.</summary>
public readonly record struct MemoryRecordHierarchyRequest
{
	/// <summary>Creates a bounded hierarchy materialization request.</summary>
	/// <param name="maximumItems">The positive maximum number of snapshots, the root included.</param>
	/// <param name="maximumDepth">The positive maximum depth; the root has depth one.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="maximumItems" /> or <paramref name="maximumDepth" /> is zero or negative.
	/// </exception>
	public MemoryRecordHierarchyRequest(int maximumItems, int maximumDepth)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumItems);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDepth);

		MaximumItems = maximumItems;
		MaximumDepth = maximumDepth;
	}

	/// <summary>Gets the maximum number of snapshots, including the requested root.</summary>
	public int MaximumItems
	{
		get;
	}

	/// <summary>Gets the maximum depth, where the requested root has depth one.</summary>
	public int MaximumDepth
	{
		get;
	}
}
