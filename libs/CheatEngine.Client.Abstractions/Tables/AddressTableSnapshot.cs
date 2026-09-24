using System.Collections.Immutable;

namespace CheatEngine.Client.Tables;

/// <summary>A bounded copied snapshot of every top-level record in Cheat Engine's current address list.</summary>
/// <remarks>
///     <see cref="ITableClient.GetSnapshot" /> copies it under an explicit materialization limit. Use
///     <see cref="ITableClient.GetRecordCount" /> to read the number of top-level records without copying them.
/// </remarks>
public readonly record struct AddressTableSnapshot
{
	/// <summary>Creates a bounded copied snapshot of every top-level record in the address list.</summary>
	/// <param name="records">The copied top-level records; a default array is treated as empty.</param>
	public AddressTableSnapshot(ImmutableArray<MemoryRecordSnapshot> records)
	{
		Records = records.IsDefault ? ImmutableArray<MemoryRecordSnapshot>.Empty : records;
		RecordCount = Records.Length;
	}

	/// <summary>Gets the number of copied top-level records.</summary>
	public int RecordCount
	{
		get;
	}

	/// <summary>Gets the copied top-level records.</summary>
	public ImmutableArray<MemoryRecordSnapshot> Records
	{
		get;
	}
}
