using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Separates static SDK Address List access from lookup classification so the Client's public failure contract
///     remains deterministic and directly testable.
/// </summary>
internal interface ITableRecordLookupPort
{
	public RecordLookupStatus TryGetRecord(int index, out MemoryRecordSnapshot record);

	public RecordLookupStatus TryGetRecord(MemoryRecordId id, out MemoryRecordSnapshot record);

	public RecordLookupStatus TryGetSelected(out MemoryRecordSnapshot record);
}
