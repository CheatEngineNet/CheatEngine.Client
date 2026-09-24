using System.Collections.Immutable;

using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.AddressList;

namespace CheatEngine.Client.Tables;

/// <summary>Reads and changes the current Cheat Engine address list through copied record snapshots.</summary>
/// <remarks>
///     <para>
///         Memory records are live Cheat Engine objects; a snapshot copies them and does not freeze them. A
///         <see cref="MemoryRecordId" /> is valid only for the table load and the activation in which this client handed it
///         out: after a trusted table load that reached Cheat Engine (merge or replace, even a failed one), every
///         identifier-taking operation refuses an identifier captured earlier with
///         <see cref="CheatEngineFailureKind.InvalidState" /> and <see cref="CheatEngineHostEffect.NotStarted" />, until a
///         later snapshot observes it again. A reload by the user, a script or another plugin is not detected; the record is
///         then revalidated at mutation time and an absent record is reported as
///         <see cref="CheatEngineFailureKind.NotFound" />, distinct from a host error.
///     </para>
///     <para>
///         Concurrent callers: the table load, every snapshot copy and every identifier check are ordered on Cheat Engine's
///         main thread, not by the order in which the calling threads resume. A snapshot copied before a concurrent trusted
///         load hands out identifiers that are already refused, and an identifier-taking operation queued behind an
///         in-flight load is checked again on the main thread and refused without calling Cheat Engine.
///     </para>
///     <para>
///         Trusted table files follow the Client path policy. Cheat Engine's file form of <c>loadTable</c> offers no
///         option to suppress a table's Lua scripts, so a table with scripts may prompt or execute Lua. A refused path is
///         never retried through another overload and never turned into a stream.
///     </para>
/// </remarks>
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
	/// <remarks>An index is positional and changes when records are added, removed or moved; prefer an identifier.</remarks>
	public bool TryGetRecordAt(int index, out MemoryRecordSnapshot record, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Gets one record by its stable Cheat Engine identifier.</summary>
	public bool TryGetRecord(MemoryRecordId id, out MemoryRecordSnapshot record, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Gets one record by its current zero-based address-list index or throws when it is unavailable.</summary>
	public MemoryRecordSnapshot GetRecordAt(int index, CancellationToken cancellationToken = default);

	/// <summary>Gets one record by identifier or throws when it is unavailable.</summary>
	public MemoryRecordSnapshot GetRecord(MemoryRecordId id, CancellationToken cancellationToken = default);

	/// <summary>Copies the selected address-list record when Cheat Engine has one.</summary>
	public bool TryGetSelected(out MemoryRecordSnapshot record, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Gets the selected record or throws when Cheat Engine has no selection.</summary>
	public MemoryRecordSnapshot GetSelected(CancellationToken cancellationToken = default);

	/// <summary>Selects one record by its identifier and returns its copied snapshot.</summary>
	/// <remarks>
	///     This changes Cheat Engine's GUI selection, which the user and other plugins see: it is a host-visible mutation,
	///     not a cache operation.
	/// </remarks>
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
	/// <remarks>
	///     <para>
	///         The Client reads the record's state before and after the change. A record already in the requested state
	///         succeeds without calling Cheat Engine's setter. Otherwise the setter runs exactly once and is never retried
	///         (an activation-failure handler that asks for a retry already repeats the change inside Cheat Engine).
	///     </para>
	///     <para>
	///         When Cheat Engine leaves the record in the other state (an activation callback, a script or the record type
	///         refused the change), the result is <see langword="false" /> with
	///         <see cref="CheatEngineFailureKind.OperationRejected" />, <see cref="CheatEngineHostEffect.Started" /> and
	///         <paramref name="record" /> set to the copied post-change snapshot; partial script effects may persist. When
	///         the record is still processing asynchronously, the result is
	///         <see cref="CheatEngineFailureKind.IndeterminateHostResult" /> with <see cref="CheatEngineHostEffect.Started" />
	///         and the snapshot. When the post-change state cannot be read, the result is
	///         <see cref="CheatEngineFailureKind.InvalidHostResult" /> with <see cref="CheatEngineHostEffect.Unknown" /> and
	///         a default <paramref name="record" />.
	///     </para>
	/// </remarks>
	public bool TrySetActive(MemoryRecordId id, bool isActive, out MemoryRecordSnapshot record,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Activates or deactivates one memory record or throws when Cheat Engine does not apply the change.</summary>
	/// <remarks>Follows <see cref="TrySetActive" />; the thrown failure carries the same kind and host effect.</remarks>
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
	/// <remarks>
	///     A load that reached Cheat Engine, successful or not, ends the validity of every record identifier handed out
	///     earlier by this client (see the interface remarks). A table with scripts may prompt or execute Lua.
	/// </remarks>
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
