using System.Collections.Immutable;

namespace CheatEngine.Client.Tables;

/// <summary>A bounded copied snapshot of every top-level record in Cheat Engine's current address list.</summary>
/// <remarks>
///     <see cref="ITableClient.GetSnapshot" /> copies it under an explicit materialization limit. Use
///     <see cref="ITableClient.GetRecordCount" /> to read the number of top-level records without copying them.
/// </remarks>
public readonly record struct AddressTableSnapshot
{
	private readonly ImmutableArray<MemoryRecordSnapshot> _records;

	/// <summary>Creates a bounded copied snapshot of every top-level record in the address list.</summary>
	/// <param name="records">The copied top-level records; a default array is treated as empty.</param>
	public AddressTableSnapshot(ImmutableArray<MemoryRecordSnapshot> records)
	{
		_records = records.IsDefault ? ImmutableArray<MemoryRecordSnapshot>.Empty : records;
	}

	/// <summary>Gets the number of copied top-level records.</summary>
	/// <remarks>0 for the <see langword="default" /> value.</remarks>
	public int RecordCount => Records.Length;

	/// <summary>Gets the copied top-level records.</summary>
	/// <remarks>Empty for the <see langword="default" /> value, never a default array.</remarks>
	public ImmutableArray<MemoryRecordSnapshot> Records =>
		_records.IsDefault ? ImmutableArray<MemoryRecordSnapshot>.Empty : _records;
}
