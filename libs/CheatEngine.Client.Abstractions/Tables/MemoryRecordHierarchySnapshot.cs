using System.Collections.Immutable;

namespace CheatEngine.Client.Tables;

/// <summary>A copied, recursively bounded memory-record tree.</summary>
public readonly struct MemoryRecordHierarchySnapshot
{
	private readonly ImmutableArray<MemoryRecordHierarchySnapshot> _children;

	/// <summary>Creates a copied memory-record tree and normalizes unavailable child storage to empty.</summary>
	/// <param name="record">The copied root record.</param>
	/// <param name="children">The copied child trees; a default array is stored as empty.</param>
	public MemoryRecordHierarchySnapshot(MemoryRecordSnapshot record,
		ImmutableArray<MemoryRecordHierarchySnapshot> children)
	{
		Record = record;
		_children = children.IsDefault ? ImmutableArray<MemoryRecordHierarchySnapshot>.Empty : children;
	}

	/// <summary>Gets the copied root record.</summary>
	public MemoryRecordSnapshot Record
	{
		get;
	}

	/// <summary>Gets the copied child records.</summary>
	/// <remarks>Empty for the <see langword="default" /> value, never a default array.</remarks>
	public ImmutableArray<MemoryRecordHierarchySnapshot> Children =>
		_children.IsDefault ? ImmutableArray<MemoryRecordHierarchySnapshot>.Empty : _children;
}
