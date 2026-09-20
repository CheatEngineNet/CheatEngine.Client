namespace CheatEngine.Client.Tables;

/// <summary>Bounds a copied memory-record hierarchy traversal.</summary>
public readonly record struct MemoryRecordHierarchyRequest
{
	/// <summary>Creates a bounded hierarchy materialization request.</summary>
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
