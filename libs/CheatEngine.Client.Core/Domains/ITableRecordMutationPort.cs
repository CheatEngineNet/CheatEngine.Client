using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Separates the static SDK address-list access from mutation classification so the Client's public failure contract
///     remains deterministic and directly testable.
/// </summary>
internal interface ITableRecordMutationPort
{
	public TableRecordMutationStatus TryDelete(MemoryRecordId id);

	public TableRecordMutationStatus TrySetParent(MemoryRecordId childId, MemoryRecordId? parentId,
		out MemoryRecordSnapshot record);
}
