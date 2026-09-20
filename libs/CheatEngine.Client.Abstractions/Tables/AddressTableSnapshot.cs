using System.Collections.Immutable;

namespace CheatEngine.Client.Tables;

/// <summary>A copied snapshot of Cheat Engine's current address-list cardinality.</summary>
public readonly record struct AddressTableSnapshot
{
	/// <summary>Creates an address-table snapshot.</summary>
	public AddressTableSnapshot(int recordCount)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(recordCount);
		RecordCount = recordCount;
		Records = ImmutableArray<MemoryRecordSnapshot>.Empty;
	}

	/// <summary>Creates a bounded copied snapshot of every top-level record in the address list.</summary>
	public AddressTableSnapshot(ImmutableArray<MemoryRecordSnapshot> records)
	{
		Records = records.IsDefault ? ImmutableArray<MemoryRecordSnapshot>.Empty : records;
		RecordCount = Records.Length;
	}

	/// <summary>Gets the number of top-level records observed in the current address list.</summary>
	public int RecordCount
	{
		get;
	}

	/// <summary>Gets the copied top-level records when this snapshot was explicitly materialized.</summary>
	/// <remarks>
	///     A cardinality-only value returned by <see cref="ITableClient.GetCurrent" /> has an empty collection. Use
	///     <see cref="ITableClient.GetSnapshot" /> to request records with an explicit materialization limit.
	/// </remarks>
	public ImmutableArray<MemoryRecordSnapshot> Records
	{
		get;
	}
}
