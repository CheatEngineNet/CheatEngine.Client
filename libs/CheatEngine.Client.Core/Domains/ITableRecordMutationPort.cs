using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Separates the static SDK address-list access from mutation classification so the Client's public failure contract
///     remains deterministic and directly testable.
/// </summary>
internal interface ITableRecordMutationPort
{
	/// <summary>Creates, initializes and optionally re-parents one record; a failed creation is rolled back once.</summary>
	public TableRecordCreation TryCreate(MemoryRecordDefinition definition, out MemoryRecordSnapshot record);

	public TableRecordMutationStatus TryDelete(MemoryRecordId id);

	public TableRecordMutationStatus TrySetParent(MemoryRecordId childId, MemoryRecordId? parentId,
		out MemoryRecordSnapshot record);
}
