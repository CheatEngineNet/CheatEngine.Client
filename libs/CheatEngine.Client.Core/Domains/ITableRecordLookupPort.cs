using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Separates static SDK Address List access from lookup classification so the Client's public failure contract
///     remains deterministic and directly testable. Every member is read-only.
/// </summary>
internal interface ITableRecordLookupPort
{
	public RecordLookupStatus TryGetRecord(int index, out MemoryRecordSnapshot record);

	public RecordLookupStatus TryGetRecord(MemoryRecordId id, out MemoryRecordSnapshot record);

	public RecordLookupStatus TryGetSelected(out MemoryRecordSnapshot record);

	/// <summary>
	///     Copies every top-level record when the table holds at most <paramref name="maximumItems" /> records; a larger
	///     table is <see cref="RecordLookupStatus.LimitExceeded" /> and nothing is copied.
	/// </summary>
	public RecordLookupStatus TryGetTable(int maximumItems, out AddressTableSnapshot table);
}
