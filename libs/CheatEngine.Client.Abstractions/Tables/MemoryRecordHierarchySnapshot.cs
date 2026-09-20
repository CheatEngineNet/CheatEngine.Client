using System.Collections.Immutable;

namespace CheatEngine.Client.Tables;

/// <summary>A copied, recursively bounded memory-record tree.</summary>
public readonly record struct MemoryRecordHierarchySnapshot
{
	/// <summary>Creates a copied memory-record tree and normalizes unavailable child storage to empty.</summary>
	public MemoryRecordHierarchySnapshot(
		MemoryRecordSnapshot Record,
		ImmutableArray<MemoryRecordHierarchySnapshot> Children)
	{
		this.Record = Record;
		this.Children = Children;
	}

	/// <summary>Gets the copied root record.</summary>
	public MemoryRecordSnapshot Record
	{
		get;
		init;
	}

	/// <summary>Gets the copied child records; a default array is exposed as empty.</summary>
	public ImmutableArray<MemoryRecordHierarchySnapshot> Children
	{
		get => field.IsDefault ? ImmutableArray<MemoryRecordHierarchySnapshot>.Empty : field;
		init => field = value.IsDefault ? ImmutableArray<MemoryRecordHierarchySnapshot>.Empty : value;
	}

	/// <summary>Deconstructs the copied root record and normalized child records.</summary>
	public void Deconstruct(
		out MemoryRecordSnapshot Record,
		out ImmutableArray<MemoryRecordHierarchySnapshot> Children)
	{
		Record = this.Record;
		Children = this.Children;
	}
}
