using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Separates the static SDK address-list access from mutation classification so the Client's public failure contract
///     remains deterministic and directly testable. Every member is a host-visible mutation and reports its outcome in
///     CheatEngine.SDK's mutation vocabulary, which <see cref="TableMapping" /> classifies.
/// </summary>
internal interface ITableRecordMutationPort
{
	/// <summary>Creates, initializes and optionally re-parents one record; a failed creation is rolled back once.</summary>
	public TableRecordCreation TryCreate(MemoryRecordDefinition definition, out MemoryRecordSnapshot record);

	/// <summary>Deletes one record through <c>AddressListMutations.Delete</c>.</summary>
	public TableRecordMutationOutcome TryDelete(MemoryRecordId id);

	/// <summary>
	///     Assigns a parent through <c>AddressListMutations.SetParent</c>, which validates the parent chain within an
	///     explicit traversal limit, then copies the moved record.
	/// </summary>
	public TableRecordMutationOutcome TrySetParent(MemoryRecordId childId, MemoryRecordId? parentId,
		out MemoryRecordSnapshot record);

	/// <summary>
	///     Changes the <c>Active</c> state of one record through <c>AddressListMutations.SetActive</c>, which calls the
	///     setter at most once and never retries, then copies the record.
	/// </summary>
	public TableActivationObservation TrySetActive(MemoryRecordId id, bool requested);

	/// <summary>
	///     Selects one record in Cheat Engine's Address List. This changes the GUI selection that the user and other
	///     plugins see: a host-visible mutation, not a cache operation (audit A14-08).
	/// </summary>
	public TableRecordMutationOutcome TrySelect(MemoryRecordId id, out MemoryRecordSnapshot record);
}
