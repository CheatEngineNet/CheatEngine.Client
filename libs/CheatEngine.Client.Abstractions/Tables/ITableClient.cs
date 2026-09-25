using System.Collections.Immutable;

using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.AddressList;

namespace CheatEngine.Client.Tables;

/// <summary>Reads and changes the current Cheat Engine address list through copied record snapshots.</summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add
///         members to it, so implement it only in a test double.
///     </para>
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
///         A <see langword="default" /> argument is a programming error, thrown before the activation check and before
///         any Cheat Engine call for the first field its check meets: an
///         <see cref="ArgumentOutOfRangeException" /> for a <see cref="MemoryRecordCollectionRequest" /> or
///         <see cref="MemoryRecordHierarchyRequest" />, which allows no record, and an <see cref="ArgumentException" />
///         for a <see cref="MemoryRecordSearch" />, <see cref="MemoryRecordDefinition" />,
///         <see cref="MemoryRecordUpdate" />, <see cref="TableLoadRequest" /> or <see cref="TableSaveRequest" />, which
///         searches, creates, changes or names nothing. A value type that is not a defined value throws an
///         <see cref="ArgumentOutOfRangeException" />.
///     </para>
///     <para>
///         Trusted table files follow the Client path policy. Cheat Engine's file form of <c>loadTable</c> offers no
///         option to suppress a table's Lua scripts, so a table with scripts may prompt or execute Lua. A refused path is
///         never retried through another overload and never turned into a stream.
///     </para>
/// </remarks>
public interface ITableClient
{
	/// <summary>Tries to count the top-level records of the current address list without copying them.</summary>
	/// <remarks>Use <see cref="TryGetSnapshot" /> to copy the records under an explicit materialization limit.</remarks>
	public bool TryGetRecordCount(out int recordCount, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Counts the top-level records of the current address list or throws when it is unavailable.</summary>
	public int GetRecordCount(CancellationToken cancellationToken = default);

	/// <summary>Copies every top-level record only when the caller supplies a materialization limit.</summary>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which allows no record.
	/// </exception>
	public bool TryGetSnapshot(MemoryRecordCollectionRequest request, out AddressTableSnapshot table,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Copies a bounded top-level record snapshot or throws when the table exceeds the limit.</summary>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which allows no record.
	/// </exception>
	public AddressTableSnapshot GetSnapshot(MemoryRecordCollectionRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Searches a bounded copied top-level record snapshot with conjunctive managed predicates.</summary>
	/// <exception cref="ArgumentException">
	///     <paramref name="search" /> is the <see langword="default" /> search, which has no predicate, or
	///     <paramref name="request" /> is the <see langword="default" /> request (an
	///     <see cref="ArgumentOutOfRangeException" />, as for a value type that is not a defined value).
	/// </exception>
	public bool TryFind(MemoryRecordSearch search, MemoryRecordCollectionRequest request,
		out ImmutableArray<MemoryRecordSnapshot> records, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Searches a bounded top-level record snapshot or throws when it cannot be materialized.</summary>
	/// <exception cref="ArgumentException">
	///     <paramref name="search" /> is the <see langword="default" /> search, which has no predicate, or
	///     <paramref name="request" /> is the <see langword="default" /> request (an
	///     <see cref="ArgumentOutOfRangeException" />, as for a value type that is not a defined value).
	/// </exception>
	public ImmutableArray<MemoryRecordSnapshot> Find(MemoryRecordSearch search, MemoryRecordCollectionRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Gets one record by its current zero-based address-list index.</summary>
	/// <remarks>An index is positional and changes when records are added, removed or moved; prefer an identifier.</remarks>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="index" /> is negative.</exception>
	public bool TryGetRecordAt(int index, out MemoryRecordSnapshot record, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Gets one record by its stable Cheat Engine identifier.</summary>
	public bool TryGetRecord(MemoryRecordId id, out MemoryRecordSnapshot record, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Gets one record by its current zero-based address-list index or throws when it is unavailable.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="index" /> is negative.</exception>
	public MemoryRecordSnapshot GetRecordAt(int index, CancellationToken cancellationToken = default);

	/// <summary>Gets one record by identifier or throws when it is unavailable.</summary>
	public MemoryRecordSnapshot GetRecord(MemoryRecordId id, CancellationToken cancellationToken = default);

	/// <summary>Copies the selected address-list record when Cheat Engine has one.</summary>
	/// <remarks>The read counterpart of <see cref="TrySelectRecord" />, which changes the selection.</remarks>
	public bool TryGetSelectedRecord(out MemoryRecordSnapshot record, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Gets the selected record or throws when Cheat Engine has no selection.</summary>
	public MemoryRecordSnapshot GetSelectedRecord(CancellationToken cancellationToken = default);

	/// <summary>Selects one record by its identifier and returns its copied snapshot.</summary>
	/// <remarks>
	///     This changes Cheat Engine's GUI selection, which the user and other plugins see: it is a host-visible mutation,
	///     not a cache operation. It is refused during a trusted table load, like every mutation (see
	///     <see cref="TryDelete" />).
	/// </remarks>
	public bool TrySelectRecord(MemoryRecordId id, out MemoryRecordSnapshot record, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Selects one record or throws when the record is unavailable.</summary>
	public MemoryRecordSnapshot SelectRecord(MemoryRecordId id, CancellationToken cancellationToken = default);

	/// <summary>Creates a memory record, assigns its defined fields, and returns a copied snapshot.</summary>
	/// <remarks>Refused during a trusted table load, like every mutation (see <see cref="TryDelete" />).</remarks>
	/// <exception cref="ArgumentException">
	///     <paramref name="definition" /> is the <see langword="default" /> definition, which has no field, or its
	///     value type is not a defined value (an <see cref="ArgumentOutOfRangeException" />).
	/// </exception>
	public bool TryCreate(MemoryRecordDefinition definition, out MemoryRecordSnapshot record,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Creates a record or throws when the host rejects it.</summary>
	/// <exception cref="ArgumentException">
	///     <paramref name="definition" /> is the <see langword="default" /> definition, which has no field, or its
	///     value type is not a defined value (an <see cref="ArgumentOutOfRangeException" />).
	/// </exception>
	public MemoryRecordSnapshot Create(MemoryRecordDefinition definition,
		CancellationToken cancellationToken = default);

	/// <summary>Applies a partial update to one record and returns a copied snapshot of the changed record.</summary>
	/// <param name="id">The identifier of the record to change, first like every record-targeting operation.</param>
	/// <param name="update">The fields to change.</param>
	/// <param name="record">The copied snapshot of the changed record on success.</param>
	/// <param name="failure">The classified failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">Observed before dispatch.</param>
	/// <returns><see langword="true" /> when every requested field was applied and the record was copied.</returns>
	/// <remarks>Refused during a trusted table load, like every mutation (see <see cref="TryDelete" />).</remarks>
	/// <exception cref="ArgumentException">
	///     <paramref name="update" /> is the <see langword="default" /> update, which changes nothing, or its value
	///     type is not a defined value (an <see cref="ArgumentOutOfRangeException" />).
	/// </exception>
	public bool TryUpdate(MemoryRecordId id, MemoryRecordUpdate update, out MemoryRecordSnapshot record,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Updates one record or throws when the host rejects the change.</summary>
	/// <param name="id">The identifier of the record to change.</param>
	/// <param name="update">The fields to change.</param>
	/// <param name="cancellationToken">Observed before dispatch.</param>
	/// <returns>The copied snapshot of the changed record.</returns>
	/// <exception cref="ArgumentException">
	///     <paramref name="update" /> is the <see langword="default" /> update, which changes nothing, or its value
	///     type is not a defined value (an <see cref="ArgumentOutOfRangeException" />).
	/// </exception>
	public MemoryRecordSnapshot Update(MemoryRecordId id, MemoryRecordUpdate update,
		CancellationToken cancellationToken = default);

	/// <summary>Deletes one memory record from the current Cheat Engine address list.</summary>
	/// <remarks>
	///     CheatEngine.SDK resolves the identifier in the current list and deletes the record once: a second delete of the
	///     same identifier is <see cref="CheatEngineFailureKind.NotFound" />. A delete that raised after it started is
	///     <see cref="CheatEngineFailureKind.LuaError" /> with <see cref="CheatEngineHostEffect.Started" /> and is never
	///     retried. Every mutation (<see cref="TryCreate" />, <see cref="TryUpdate" />, <see cref="TrySelectRecord" />,
	///     delete, <see cref="TrySetParent" /> and <see cref="TrySetActive" />) is refused without changing the Address
	///     List with <see cref="CheatEngineFailureKind.InvalidState" /> while a trusted table load runs on Cheat Engine's
	///     main thread (a script of that table calling the Client). Delete, <see cref="TrySetParent" /> and
	///     <see cref="TrySetActive" /> are also refused with <see cref="CheatEngineFailureKind.RuntimeChanged" /> when
	///     Cheat Engine's Lua runtime changed before the change was attempted; both refusals report
	///     <see cref="CheatEngineHostEffect.NotStarted" />.
	/// </remarks>
	public bool TryDelete(MemoryRecordId id, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Deletes one memory record or throws when Cheat Engine rejects the operation.</summary>
	public void Delete(MemoryRecordId id, CancellationToken cancellationToken = default);

	/// <summary>Activates or deactivates one memory record and returns its copied post-change snapshot.</summary>
	/// <remarks>
	///     <para>
	///         CheatEngine.SDK reads the record's state before and after the change. A record already in the requested
	///         state succeeds without calling Cheat Engine's setter. Otherwise the setter runs exactly once and is never
	///         retried (an activation-failure handler that asks for a retry already repeats the change inside Cheat
	///         Engine). The record is copied after the change, in the same call on Cheat Engine's main thread.
	///     </para>
	///     <para>
	///         When Cheat Engine leaves the record in the other state (an activation callback, a script or the record type
	///         refused the change), the result is <see langword="false" /> with
	///         <see cref="CheatEngineFailureKind.OperationRejected" />, <see cref="CheatEngineHostEffect.Started" /> and
	///         <paramref name="record" /> set to the copied post-change snapshot; partial script effects may persist. When
	///         the record activates asynchronously and is still processing, the result is <see langword="true" /> and the
	///         snapshot's <see cref="MemoryRecordStateSnapshot.IsAsyncProcessing" /> is <see langword="true" />: its
	///         <see cref="MemoryRecordStateSnapshot.IsActive" /> is not yet the final state, which a later snapshot
	///         observes. When the setter raised or the post-change state cannot be read, the result is
	///         <see cref="CheatEngineFailureKind.IndeterminateHostResult" /> with <see cref="CheatEngineHostEffect.Started" />
	///         and a default <paramref name="record" />. A record that is not found, and the refusals described under
	///         <see cref="TryDelete" />, report <see cref="CheatEngineHostEffect.NotStarted" />.
	///     </para>
	/// </remarks>
	public bool TrySetActive(MemoryRecordId id, bool isActive, out MemoryRecordSnapshot record,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Activates or deactivates one memory record or throws when Cheat Engine does not apply the change.</summary>
	/// <remarks>Follows <see cref="TrySetActive" />; the thrown failure carries the same kind and host effect.</remarks>
	public MemoryRecordSnapshot SetActive(MemoryRecordId id, bool isActive,
		CancellationToken cancellationToken = default);

	/// <summary>Moves one record under a parent, or passes <see langword="null" /> to restore it to the root.</summary>
	/// <remarks>
	///     CheatEngine.SDK walks the parent chain of the requested parent before it assigns it, up to an explicit bound of
	///     4096 records. A record requested as its own parent, or moved under one of its own descendants (a cycle), is
	///     <see cref="CheatEngineFailureKind.OperationRejected" />, a longer chain is
	///     <see cref="CheatEngineFailureKind.ResultLimitExceeded" />, and an absent record or parent is
	///     <see cref="CheatEngineFailureKind.NotFound" />; all of them report <see cref="CheatEngineHostEffect.NotStarted" />.
	///     The refusals described under <see cref="TryDelete" /> apply too.
	/// </remarks>
	public bool TrySetParent(MemoryRecordId childId, MemoryRecordId? parentId, out MemoryRecordSnapshot record,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Moves one record under a parent, or restores it to the root, and returns its copied post-change state.</summary>
	public MemoryRecordSnapshot SetParent(MemoryRecordId childId, MemoryRecordId? parentId,
		CancellationToken cancellationToken = default);

	/// <summary>Copies a bounded hierarchy rooted at one memory record.</summary>
	/// <remarks>
	///     Each record's children are read by position, for every position below its
	///     <see cref="MemoryRecordStateSnapshot.ChildCount" />. A child that Cheat Engine does not return at a position
	///     below that count is <see cref="CheatEngineFailureKind.InvalidHostResult" />, and the message names the record
	///     identifier and the position.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which allows no record and no level.
	/// </exception>
	public bool TryGetHierarchy(MemoryRecordId rootId, MemoryRecordHierarchyRequest request,
		out MemoryRecordHierarchySnapshot hierarchy, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Copies a bounded hierarchy or throws when its depth, cardinality, or host shape is invalid.</summary>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which allows no record and no level.
	/// </exception>
	public MemoryRecordHierarchySnapshot GetHierarchy(MemoryRecordId rootId, MemoryRecordHierarchyRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Tries to load a trusted table through Cheat Engine's native table loader.</summary>
	/// <remarks>
	///     <para>
	///         A load that reached Cheat Engine, successful or not, ends the validity of every record identifier handed
	///         out earlier by this client (see the interface remarks). A table with scripts may prompt or execute Lua.
	///     </para>
	///     <para>
	///         A path outside the allowed roots is refused before any Cheat Engine call
	///         (<see cref="CheatEngineHostEffect.NotStarted" />). CheatEngine.SDK then calls <c>loadTable</c> once with the
	///         path unchanged: a load that raised is <see cref="CheatEngineFailureKind.LuaError" /> with
	///         <see cref="CheatEngineHostEffect.Started" />, since part of the table and of its scripts may have been
	///         applied, and an unavailable loader is <see cref="CheatEngineFailureKind.CapabilityUnavailable" /> with
	///         <see cref="CheatEngineHostEffect.NotStarted" />. Failure messages never contain the path.
	///     </para>
	/// </remarks>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which names no file.
	/// </exception>
	public bool TryLoadTrustedTable(TableLoadRequest request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Loads a trusted table or throws when policy or host execution rejects it.</summary>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which names no file.
	/// </exception>
	public void LoadTrustedTable(TableLoadRequest request, CancellationToken cancellationToken = default);

	/// <summary>Tries to save the current table to an explicitly allowed path.</summary>
	/// <remarks>
	///     A path outside the allowed roots is refused before any Cheat Engine call. A save that raised is
	///     <see cref="CheatEngineFailureKind.LuaError" /> with <see cref="CheatEngineHostEffect.Started" />: the file may be
	///     partially written. Failure messages never contain the path.
	/// </remarks>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which names no file.
	/// </exception>
	public bool TrySaveTable(TableSaveRequest request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Saves the current table or throws when policy or host execution rejects it.</summary>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which names no file.
	/// </exception>
	public void SaveTable(TableSaveRequest request, CancellationToken cancellationToken = default);
}
