using System.Collections.Immutable;

using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.AddressList;

namespace CheatEngine.Client.Tables;

/// <summary>Reads and changes the current Cheat Engine address list through copied record snapshots.</summary>
public interface ITableClient
{
	/// <summary>Tries to get a snapshot of the current address list.</summary>
	public bool TryGetCurrent(out AddressTableSnapshot table, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Gets the current table or throws when it is unavailable.</summary>
	public AddressTableSnapshot GetCurrent(CancellationToken cancellationToken = default);

	/// <summary>Copies every top-level record only when the caller supplies a materialization limit.</summary>
	public bool TryGetSnapshot(MemoryRecordCollectionRequest request, out AddressTableSnapshot table,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Copies a bounded top-level record snapshot or throws when the table exceeds the limit.</summary>
	public AddressTableSnapshot GetSnapshot(MemoryRecordCollectionRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Searches a bounded copied top-level record snapshot with conjunctive managed predicates.</summary>
	public bool TryFind(MemoryRecordSearch search, MemoryRecordCollectionRequest request,
		out ImmutableArray<MemoryRecordSnapshot> records, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Searches a bounded top-level record snapshot or throws when it cannot be materialized.</summary>
	public ImmutableArray<MemoryRecordSnapshot> Find(MemoryRecordSearch search, MemoryRecordCollectionRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Gets one record by its current zero-based address-list index.</summary>
	public bool TryGetRecord(int index, out MemoryRecordSnapshot record, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Gets one record by its stable Cheat Engine identifier.</summary>
	public bool TryGetRecord(MemoryRecordId id, out MemoryRecordSnapshot record, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Gets one record by index or throws when it is unavailable.</summary>
	public MemoryRecordSnapshot GetRecord(int index, CancellationToken cancellationToken = default);

	/// <summary>Gets one record by identifier or throws when it is unavailable.</summary>
	public MemoryRecordSnapshot GetRecord(MemoryRecordId id, CancellationToken cancellationToken = default);

	/// <summary>Copies the selected address-list record when Cheat Engine has one.</summary>
	public bool TryGetSelected(out MemoryRecordSnapshot record, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Gets the selected record or throws when Cheat Engine has no selection.</summary>
	public MemoryRecordSnapshot GetSelected(CancellationToken cancellationToken = default);

	/// <summary>Selects one record by its stable identifier and returns its copied snapshot.</summary>
	public bool TrySelect(MemoryRecordId id, out MemoryRecordSnapshot record, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Selects one record or throws when the record is unavailable.</summary>
	public MemoryRecordSnapshot SelectRecord(MemoryRecordId id, CancellationToken cancellationToken = default);

	/// <summary>Creates a memory record, assigns its defined fields, and returns a copied snapshot.</summary>
	public bool TryCreate(MemoryRecordDefinition definition, out MemoryRecordSnapshot record,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Creates a record or throws when the host rejects it.</summary>
	public MemoryRecordSnapshot Create(MemoryRecordDefinition definition,
		CancellationToken cancellationToken = default);

	/// <summary>Applies a partial update and returns a copied snapshot of the changed record.</summary>
	public bool TryUpdate(MemoryRecordUpdate update, out MemoryRecordSnapshot record,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Updates a record or throws when the host rejects the change.</summary>
	public MemoryRecordSnapshot Update(MemoryRecordUpdate update, CancellationToken cancellationToken = default);

	/// <summary>Deletes one memory record from the current Cheat Engine address list.</summary>
	public bool TryDelete(MemoryRecordId id, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Deletes one memory record or throws when Cheat Engine rejects the operation.</summary>
	public void Delete(MemoryRecordId id, CancellationToken cancellationToken = default);

	/// <summary>Activates or deactivates one memory record and returns its copied post-change snapshot.</summary>
	public bool TrySetActive(MemoryRecordId id, bool isActive, out MemoryRecordSnapshot record,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Activates or deactivates one memory record or throws when Cheat Engine rejects the change.</summary>
	public MemoryRecordSnapshot SetActive(MemoryRecordId id, bool isActive,
		CancellationToken cancellationToken = default);

	/// <summary>Moves one record under a parent, or passes <see langword="null" /> to restore it to the root.</summary>
	public bool TrySetParent(MemoryRecordId childId, MemoryRecordId? parentId, out MemoryRecordSnapshot record,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Moves one record under a parent, or restores it to the root, and returns its copied post-change state.</summary>
	public MemoryRecordSnapshot SetParent(MemoryRecordId childId, MemoryRecordId? parentId,
		CancellationToken cancellationToken = default);

	/// <summary>Copies a bounded hierarchy rooted at one memory record.</summary>
	public bool TryGetHierarchy(MemoryRecordId rootId, MemoryRecordHierarchyRequest request,
		out MemoryRecordHierarchySnapshot hierarchy, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Copies a bounded hierarchy or throws when its depth, cardinality, or host shape is invalid.</summary>
	public MemoryRecordHierarchySnapshot GetHierarchy(MemoryRecordId rootId, MemoryRecordHierarchyRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Tries to load a trusted table through Cheat Engine's native table loader.</summary>
	public bool TryLoadTrustedTable(TableLoadRequest request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Loads a trusted table or throws when policy or host execution rejects it.</summary>
	public void LoadTrustedTable(TableLoadRequest request, CancellationToken cancellationToken = default);

	/// <summary>Tries to save the current table to an explicitly allowed path.</summary>
	public bool TrySaveTable(TableSaveRequest request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Saves the current table or throws when policy or host execution rejects it.</summary>
	public void SaveTable(TableSaveRequest request, CancellationToken cancellationToken = default);
}
