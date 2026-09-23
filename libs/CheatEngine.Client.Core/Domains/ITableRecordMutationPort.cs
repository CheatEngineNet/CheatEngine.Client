using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Separates the static SDK address-list access from mutation classification so the Client's public failure contract
///     remains deterministic and directly testable. Every member is a host-visible mutation.
/// </summary>
internal interface ITableRecordMutationPort
{
	/// <summary>Creates, initializes and optionally re-parents one record; a failed creation is rolled back once.</summary>
	public TableRecordCreation TryCreate(MemoryRecordDefinition definition, out MemoryRecordSnapshot record);

	public TableRecordMutationStatus TryDelete(MemoryRecordId id);

	public TableRecordMutationStatus TrySetParent(MemoryRecordId childId, MemoryRecordId? parentId,
		out MemoryRecordSnapshot record);

	/// <summary>
	///     Changes the <c>Active</c> state of one record with a before/after observation (<see cref="TableRecordActivation" />);
	///     the setter is never called when the record is already in the requested state and never retried.
	/// </summary>
	public TableActivationObservation TrySetActive(MemoryRecordId id, bool requested);

	/// <summary>
	///     Selects one record in Cheat Engine's Address List. This changes the GUI selection that the user and other
	///     plugins see: a host-visible mutation, not a cache operation (audit A14-08).
	/// </summary>
	public TableRecordMutationStatus TrySelect(MemoryRecordId id, out MemoryRecordSnapshot record);
}
