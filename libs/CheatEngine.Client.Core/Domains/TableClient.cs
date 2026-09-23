using System.Collections.Immutable;

using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Results;
using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Marshalling;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Address List domain: copied snapshots, host-visible mutations and trusted table files.</summary>
/// <remarks>
///     Record identifiers are bound to the table load in which this activation observed them
///     (<see cref="TableRecordGeneration" />): every identifier-taking operation refuses an identifier captured before the
///     last trusted table load. The check runs before dispatch and again inside the dispatched callback on Cheat Engine's
///     main thread, where trusted loads advance the generation, so an operation queued behind an in-flight load is
///     judged against the table that load produced. Every copied snapshot is observed with the generation it was copied
///     in, read inside the same dispatched callback.
/// </remarks>
internal sealed class TableClient(
	ICheatEngineDispatcher dispatcher,
	CoreClientPolicy policy,
	ITableRecordMutationPort? recordMutations = null,
	CoreLifetime? lifetime = null,
	ITableRecordLookupPort? recordLookups = null,
	ITableFilePort? tableFiles = null) : ITableClient
{
	/// <summary>The message of a refused identifier captured before the last trusted table load.</summary>
	internal const string StaleRecordIdentifierMessage =
		"The memory record identifier was captured before the last trusted table load of this activation; read the " +
		"record again.";

	private const string _getHierarchyOperation = "Tables.GetHierarchy";

	private readonly ICheatEngineDispatcher _dispatcher =
		dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

	private readonly TableRecordGeneration _generation = new();

	private readonly CoreLifetime? _lifetime = lifetime;

	private readonly CoreClientPolicy _policy = policy ?? throw new ArgumentNullException(nameof(policy));
	private readonly ITableRecordLookupPort _recordLookups = recordLookups ?? new SdkTableRecordLookupPort();
	private readonly ITableRecordMutationPort _recordMutations = recordMutations ?? new SdkTableRecordMutationPort();
	private readonly ITableFilePort _tableFiles = tableFiles ?? SdkTableFilePort.Instance;

	/// <summary>Gets the number of trusted table loads of this activation that reached Cheat Engine.</summary>
	internal long TableGeneration => _generation.Generation;

	public bool TryGetCurrent(out AddressTableSnapshot table, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		AddressTableSnapshot captured = default;
		bool succeeded = false;
		if (!SdkBoundary.TryInvoke(_dispatcher, "Tables.GetCurrent", () =>
				succeeded = AddressListAccess.TryGetCurrent(out AddressList list) && list.TryGetCount(out int count) &&
							CaptureTable(count, out captured), CheatEngineHostEffect.Unknown, _lifetime, out failure, cancellationToken))
		{
			table = default;
			return false;
		}

		table = captured;
		if (succeeded)
		{
			return true;
		}

		failure = HostFailure("Tables.GetCurrent");
		return false;
	}

	public AddressTableSnapshot GetCurrent(CancellationToken cancellationToken = default)
	{
		if (TryGetCurrent(out AddressTableSnapshot result, out CheatEngineFailure failure, cancellationToken))
		{
			return result;
		}

		failure.Throw();
		return default;
	}

	public bool TryGetSnapshot(MemoryRecordCollectionRequest request, out AddressTableSnapshot table,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		AddressTableSnapshot captured = default;
		RecordLookupStatus status = RecordLookupStatus.InvalidRecord;
		if (!TryDispatch("Tables.GetSnapshot", null, null,
				() => status = _recordLookups.TryGetTable(request.MaximumItems, out captured),
				out long observedGeneration, out failure, cancellationToken))
		{
			table = default;
			return false;
		}

		if (status == RecordLookupStatus.Success)
		{
			_generation.Observe(captured, observedGeneration);
			table = captured;
			return true;
		}

		table = default;
		failure = status == RecordLookupStatus.LimitExceeded
			? ResultLimitFailure("Tables.GetSnapshot", request.MaximumItems)
			: HostFailure("Tables.GetSnapshot");
		return false;
	}

	public AddressTableSnapshot GetSnapshot(MemoryRecordCollectionRequest request,
		CancellationToken cancellationToken = default)
	{
		if (TryGetSnapshot(request, out AddressTableSnapshot result, out CheatEngineFailure failure, cancellationToken))
		{
			return result;
		}

		failure.Throw();
		return default;
	}

	public bool TryFind(MemoryRecordSearch search, MemoryRecordCollectionRequest request,
		out ImmutableArray<MemoryRecordSnapshot> records, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		if (!HasPredicate(search))
		{
			records = [];
			failure = new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, "Tables.Find",
				"A memory-record search must specify at least one predicate.", null, CheatEngineHostEffect.NotStarted);
			return false;
		}

		if (!TryGetSnapshot(request, out AddressTableSnapshot snapshot, out failure, cancellationToken))
		{
			records = [];
			return false;
		}

		if (cancellationToken.IsCancellationRequested)
		{
			// The read-only snapshot has already been copied: the host call completed and left nothing behind.
			records = [];
			failure = new CheatEngineFailure(CheatEngineFailureKind.Cancelled, "Tables.Find",
				"The search was cancelled after the Address List snapshot was copied; no result was published.", null,
				CheatEngineHostEffect.Completed);
			return false;
		}

		records = snapshot.Records.Where(record => Matches(search, record)).ToImmutableArray();
		failure = default;
		return true;
	}

	public ImmutableArray<MemoryRecordSnapshot> Find(MemoryRecordSearch search,
		MemoryRecordCollectionRequest request, CancellationToken cancellationToken = default)
	{
		if (TryFind(search, request, out ImmutableArray<MemoryRecordSnapshot> result, out CheatEngineFailure failure,
				cancellationToken))
		{
			return result;
		}

		failure.Throw();
		return [];
	}

	public bool TryGetRecord(int index, out MemoryRecordSnapshot record, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		return TryRecord("Tables.GetRecord", null, (out result) =>
			_recordLookups.TryGetRecord(index, out result), out record, out failure, cancellationToken);
	}

	public bool TryGetRecord(MemoryRecordId id, out MemoryRecordSnapshot record,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		if (IsStale("Tables.GetRecord", id, out failure))
		{
			record = default;
			return false;
		}

		return TryRecord("Tables.GetRecord", id, (out result) =>
			_recordLookups.TryGetRecord(id, out result), out record, out failure, cancellationToken);
	}

	public MemoryRecordSnapshot GetRecord(int index, CancellationToken cancellationToken = default)
	{
		if (TryGetRecord(index, out MemoryRecordSnapshot result, out CheatEngineFailure failure, cancellationToken))
		{
			return result;
		}

		failure.Throw();
		return default;
	}

	public MemoryRecordSnapshot GetRecord(MemoryRecordId id, CancellationToken cancellationToken = default)
	{
		if (TryGetRecord(id, out MemoryRecordSnapshot result, out CheatEngineFailure failure, cancellationToken))
		{
			return result;
		}

		failure.Throw();
		return default;
	}

	public bool TryGetSelected(out MemoryRecordSnapshot record, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return TryRecord("Tables.GetSelected", null, _recordLookups.TryGetSelected, out record, out failure,
			cancellationToken);
	}

	public MemoryRecordSnapshot GetSelected(CancellationToken cancellationToken = default)
	{
		if (TryGetSelected(out MemoryRecordSnapshot result, out CheatEngineFailure failure, cancellationToken))
		{
			return result;
		}

		failure.Throw();
		return default;
	}

	public bool TrySelect(MemoryRecordId id, out MemoryRecordSnapshot record, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		if (IsStale("Tables.Select", id, out failure))
		{
			record = default;
			return false;
		}

		// Changing Cheat Engine's GUI selection is a host-visible mutation, so it goes through the mutation port.
		MemoryRecordSnapshot captured = default;
		TableRecordMutationStatus status = TableRecordMutationStatus.HostRejected;
		if (!TryDispatch("Tables.Select", id, null, () => status = _recordMutations.TrySelect(id, out captured),
				out long observedGeneration, out failure, cancellationToken))
		{
			record = default;
			return false;
		}

		if (status == TableRecordMutationStatus.Success)
		{
			_generation.Observe(captured, observedGeneration);
			record = captured;
			return true;
		}

		record = default;
		failure = MutationFailure("Tables.Select", status);
		return false;
	}

	public MemoryRecordSnapshot SelectRecord(MemoryRecordId id, CancellationToken cancellationToken = default)
	{
		if (TrySelect(id, out MemoryRecordSnapshot result, out CheatEngineFailure failure, cancellationToken))
		{
			return result;
		}

		failure.Throw();
		return default;
	}

	public bool TryCreate(MemoryRecordDefinition definition, out MemoryRecordSnapshot record,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		if (definition.ParentId is { } parentId && IsStale("Tables.Create", parentId, out failure))
		{
			record = default;
			return false;
		}

		MemoryRecordSnapshot captured = default;
		TableRecordCreation creation = default;
		if (!TryDispatch("Tables.Create", definition.ParentId, null,
				() => creation = _recordMutations.TryCreate(definition, out captured),
				out long observedGeneration, out failure, cancellationToken))
		{
			record = default;
			return false;
		}

		if (creation.Status == TableRecordMutationStatus.Success)
		{
			_generation.Observe(captured, observedGeneration);
			record = captured;
			return true;
		}

		record = default;
		failure = CreateCreationFailure(creation);
		return false;
	}

	public MemoryRecordSnapshot Create(MemoryRecordDefinition definition,
		CancellationToken cancellationToken = default)
	{
		if (TryCreate(definition, out MemoryRecordSnapshot result, out CheatEngineFailure failure, cancellationToken))
		{
			return result;
		}

		failure.Throw();
		return default;
	}

	public bool TryUpdate(MemoryRecordUpdate update, out MemoryRecordSnapshot record,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		if (IsStale("Tables.Update", update.Id, out failure))
		{
			record = default;
			return false;
		}

		MemoryRecordSnapshot captured = default;
		bool succeeded = false;
		if (!TryDispatch("Tables.Update", update.Id, null, () => succeeded = TryUpdateRecord(update, out captured),
				out long observedGeneration, out failure, cancellationToken))
		{
			record = default;
			return false;
		}

		record = captured;
		if (succeeded)
		{
			_generation.Observe(captured, observedGeneration);
			return true;
		}

		failure = HostFailure("Tables.Update");
		return false;
	}

	public MemoryRecordSnapshot Update(MemoryRecordUpdate update, CancellationToken cancellationToken = default)
	{
		if (TryUpdate(update, out MemoryRecordSnapshot result, out CheatEngineFailure failure, cancellationToken))
		{
			return result;
		}

		failure.Throw();
		return default;
	}

	public bool TryDelete(MemoryRecordId id, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		if (IsStale("Tables.Delete", id, out failure))
		{
			return false;
		}

		// A successful delete does not make the identifier stale: a second delete reports not found (A14-38).
		TableRecordMutationStatus status = TableRecordMutationStatus.HostRejected;
		if (!TryDispatch("Tables.Delete", id, null, () => status = _recordMutations.TryDelete(id), out _,
				out failure, cancellationToken))
		{
			return false;
		}

		if (status == TableRecordMutationStatus.Success)
		{
			failure = default;
			return true;
		}

		failure = MutationFailure("Tables.Delete", status);
		return false;
	}

	public void Delete(MemoryRecordId id, CancellationToken cancellationToken = default)
	{
		if (!TryDelete(id, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw();
		}
	}

	public bool TrySetActive(MemoryRecordId id, bool isActive, out MemoryRecordSnapshot record,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		const string Operation = "Tables.SetActive";
		if (IsStale(Operation, id, out failure))
		{
			record = default;
			return false;
		}

		TableActivationObservation observation = default;
		if (!TryDispatch(Operation, id, null, () => observation = _recordMutations.TrySetActive(id, isActive),
				out long observedGeneration, out failure, cancellationToken))
		{
			record = default;
			return false;
		}

		if (observation.Snapshot is { } snapshot)
		{
			_generation.Observe(snapshot, observedGeneration);
		}

		bool applied = TryMapActivation(Operation, isActive, observation, out record, out failure);
		if (GetNotAppliedStatus(observation.Status) is { } notApplied)
		{
			_lifetime?.Diagnostics.RecordActivationNotApplied(Operation, isActive, notApplied);
		}

		return applied;
	}

	public MemoryRecordSnapshot SetActive(MemoryRecordId id, bool isActive,
		CancellationToken cancellationToken = default)
	{
		if (TrySetActive(id, isActive, out MemoryRecordSnapshot result, out CheatEngineFailure failure,
				cancellationToken))
		{
			return result;
		}

		failure.Throw();
		return default;
	}

	public bool TrySetParent(MemoryRecordId childId, MemoryRecordId? parentId, out MemoryRecordSnapshot record,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		if (IsStale("Tables.SetParent", childId, out failure) ||
			(parentId is { } parentToCheck && IsStale("Tables.SetParent", parentToCheck, out failure)))
		{
			record = default;
			return false;
		}

		if (parentId is { } requestedParentId && requestedParentId == childId)
		{
			record = default;
			failure = CoreFailureFactory.WithHostEffect(
				MutationFailure("Tables.SetParent", TableRecordMutationStatus.InvalidRelationship),
				CheatEngineHostEffect.NotStarted);
			return false;
		}

		MemoryRecordSnapshot captured = default;
		TableRecordMutationStatus status = TableRecordMutationStatus.HostRejected;
		if (!TryDispatch("Tables.SetParent", childId, parentId,
				() => status = _recordMutations.TrySetParent(childId, parentId, out captured),
				out long observedGeneration, out failure, cancellationToken))
		{
			record = default;
			return false;
		}

		record = captured;
		if (status == TableRecordMutationStatus.Success)
		{
			_generation.Observe(captured, observedGeneration);
			failure = default;
			return true;
		}

		failure = MutationFailure("Tables.SetParent", status);
		return false;
	}

	public MemoryRecordSnapshot SetParent(MemoryRecordId childId, MemoryRecordId? parentId,
		CancellationToken cancellationToken = default)
	{
		if (TrySetParent(childId, parentId, out MemoryRecordSnapshot result, out CheatEngineFailure failure,
				cancellationToken))
		{
			return result;
		}

		failure.Throw();
		return default;
	}

	public bool TryGetHierarchy(MemoryRecordId rootId, MemoryRecordHierarchyRequest request,
		out MemoryRecordHierarchySnapshot hierarchy, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		if (IsStale(_getHierarchyOperation, rootId, out failure))
		{
			hierarchy = default;
			return false;
		}

		MemoryRecordHierarchySnapshot captured = default;
		bool found = false;
		bool succeeded = false;
		HierarchyBuildProblem problem = HierarchyBuildProblem.None;
		if (!TryDispatch(_getHierarchyOperation, rootId, null, () =>
			{
				if (!AddressListAccess.TryGetCurrent(out AddressList list) ||
					!list.TryGetMemoryRecordById(rootId, out MemoryRecord root))
				{
					return;
				}

				found = true;
				HashSet<MemoryRecordId> visited = [];
				int materialized = 0;
				succeeded = TryBuildHierarchy(root, request, 1, visited, ref materialized, out captured, out problem);
			}, out long observedGeneration, out failure, cancellationToken))
		{
			hierarchy = default;
			return false;
		}

		hierarchy = captured;
		if (succeeded)
		{
			_generation.Observe(captured, observedGeneration);
			return true;
		}

		failure = GetHierarchyFailure(problem, found, request);
		return false;
	}

	public MemoryRecordHierarchySnapshot GetHierarchy(MemoryRecordId rootId,
		MemoryRecordHierarchyRequest request, CancellationToken cancellationToken = default)
	{
		if (TryGetHierarchy(rootId, request, out MemoryRecordHierarchySnapshot result, out CheatEngineFailure failure,
				cancellationToken))
		{
			return result;
		}

		failure.Throw();
		return default;
	}

	public bool TryLoadTrustedTable(TableLoadRequest request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		_lifetime?.ThrowIfInactive("Tables.LoadTrustedTable");
		if (!TryAuthorize(request.File.FullPath, "Tables.LoadTrustedTable", out failure))
		{
			return false;
		}

		// loadTable can execute table Lua: a fault leaves the Address List state unknown. A load that reached Cheat
		// Engine advances the table generation whatever its result, merge or replace: Cheat Engine does not promise that
		// an earlier record identifier survives it (A14-05, open issue O4). The generation advances inside the dispatched
		// callback, on Cheat Engine's main thread, so it is ordered with every dispatched snapshot copy and identifier
		// check. The diagnostics event is emitted after dispatch, never inside the callback. The refused path above never
		// reaches here and is never retried through another overload or a stream.
		bool reachedCheatEngine = false;
		long advancedGeneration = 0;
		try
		{
			return SdkBoundary.TryInvoke(_dispatcher, "Tables.LoadTrustedTable",
				() =>
				{
					reachedCheatEngine = true;
					try
					{
						_tableFiles.LoadTable(request.File.FullPath, request.Merge);
					}
					finally
					{
						advancedGeneration = _generation.Advance();
					}
				},
				CheatEngineHostEffect.Unknown, _lifetime, out failure, cancellationToken);
		}
		finally
		{
			if (reachedCheatEngine)
			{
				_lifetime?.Diagnostics.TableGenerationAdvanced(_lifetime.Epoch, advancedGeneration);
			}
		}
	}

	public void LoadTrustedTable(TableLoadRequest request, CancellationToken cancellationToken = default)
	{
		if (!TryLoadTrustedTable(request, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw();
		}
	}

	public bool TrySaveTable(TableSaveRequest request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		_lifetime?.ThrowIfInactive("Tables.SaveTable");
		if (!TryAuthorize(request.File.FullPath, "Tables.SaveTable", out failure))
		{
			return false;
		}

		return SdkBoundary.TryInvoke(_dispatcher, "Tables.SaveTable",
			() => _tableFiles.SaveTable(request.File.FullPath), CheatEngineHostEffect.Unknown, _lifetime,
			out failure, cancellationToken);
	}

	public void SaveTable(TableSaveRequest request, CancellationToken cancellationToken = default)
	{
		if (!TrySaveTable(request, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw();
		}
	}

	private bool TryRecord(string operation, MemoryRecordId? id, RecordLookup lookup,
		out MemoryRecordSnapshot record, out CheatEngineFailure failure, CancellationToken cancellationToken)
	{
		MemoryRecordSnapshot captured = default;
		RecordLookupStatus status = RecordLookupStatus.InvalidRecord;
		if (!TryDispatch(operation, id, null, () => status = lookup(out captured), out long observedGeneration,
				out failure, cancellationToken))
		{
			record = default;
			return false;
		}

		record = captured;
		if (status == RecordLookupStatus.Success)
		{
			_generation.Observe(captured, observedGeneration);
			return true;
		}

		failure = LookupFailure(operation, status);
		return false;
	}

	/// <summary>
	///     Dispatches Client-internal Address List work after re-checking, on Cheat Engine's main thread, that no
	///     identifier it takes became stale, and reads there the table generation the work observes.
	/// </summary>
	/// <remarks>
	///     A trusted load dispatched after the pre-dispatch check and before this callback advances the generation on the
	///     main thread first; the re-check then refuses the identifier without calling Cheat Engine. The generation is read
	///     before the work, so a snapshot copied while a nested load ran inside the work is treated as an earlier state.
	/// </remarks>
	private bool TryDispatch(string operation, MemoryRecordId? firstId, MemoryRecordId? secondId, Action work,
		out long observedGeneration, out CheatEngineFailure failure, CancellationToken cancellationToken)
	{
		bool stale = false;
		long generation = 0;
		bool dispatched = SdkBoundary.TryInvoke(_dispatcher, operation, () =>
			{
				if ((firstId is { } first && _generation.IsStale(first)) ||
					(secondId is { } second && _generation.IsStale(second)))
				{
					stale = true;
					return;
				}

				generation = _generation.Generation;
				work();
			},
			CheatEngineHostEffect.Unknown, _lifetime, out failure, cancellationToken);
		observedGeneration = generation;
		if (!dispatched)
		{
			return false;
		}

		if (stale)
		{
			failure = RefuseStale(operation);
			return false;
		}

		return true;
	}

	/// <summary>Refuses, before any dispatch, an identifier captured before the last trusted table load.</summary>
	private bool IsStale(string operation, MemoryRecordId id, out CheatEngineFailure failure)
	{
		if (!_generation.IsStale(id))
		{
			failure = default;
			return false;
		}

		failure = RefuseStale(operation);
		return true;
	}

	/// <summary>Creates the stale-identifier refusal and emits its diagnostics event (never inside a callback).</summary>
	private CheatEngineFailure RefuseStale(string operation)
	{
		_lifetime?.Diagnostics.StaleRecordIdentifierRefused(operation, _generation.Generation);
		return new CheatEngineFailure(CheatEngineFailureKind.InvalidState, operation, StaleRecordIdentifierMessage,
			null, CheatEngineHostEffect.NotStarted);
	}

	/// <summary>
	///     Names an activation outcome that did not apply the requested state for the diagnostics event, or returns
	///     <see langword="null" /> for an applied, unchanged or refused-before-start outcome.
	/// </summary>
	private static string? GetNotAppliedStatus(TableActivationStatus status)
	{
		return status switch
		{
			TableActivationStatus.RefusedByHost => nameof(TableActivationStatus.RefusedByHost),
			TableActivationStatus.Pending => nameof(TableActivationStatus.Pending),
			TableActivationStatus.Indeterminate or TableActivationStatus.Unknown =>
				nameof(TableActivationStatus.Indeterminate),
			_ => null
		};
	}

	/// <summary>Maps an activation observation to the public result (audit A14-12, A14-33, A14-42, Q35).</summary>
	private static bool TryMapActivation(string operation, bool requested, TableActivationObservation observation,
		out MemoryRecordSnapshot record, out CheatEngineFailure failure)
	{
		record = observation.Snapshot ?? default;
		switch (observation.Status)
		{
			case TableActivationStatus.Unchanged or TableActivationStatus.Applied when observation.Snapshot is not null:
				failure = default;
				return true;
			case TableActivationStatus.Unchanged:
				failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, operation,
					"The memory record already had the requested state, but its snapshot could not be copied.", null,
					CheatEngineHostEffect.NotStarted);
				return false;
			case TableActivationStatus.Applied:
				failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, operation,
					"Cheat Engine applied the requested state, but the record snapshot could not be copied.", null,
					CheatEngineHostEffect.Completed);
				return false;
			case TableActivationStatus.RecordNotFound:
				failure = new CheatEngineFailure(CheatEngineFailureKind.NotFound, operation,
					"The requested Cheat Engine memory record was not found.", null, CheatEngineHostEffect.NotStarted);
				return false;
			case TableActivationStatus.AddressListUnavailable:
				failure = new CheatEngineFailure(CheatEngineFailureKind.CapabilityUnavailable, operation,
					"Cheat Engine's Address List capability is unavailable.", null, CheatEngineHostEffect.NotStarted);
				return false;
			case TableActivationStatus.NotAttempted:
				failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, operation,
					"Cheat Engine did not report the record's current activation state, so no change was attempted.",
					null, CheatEngineHostEffect.NotStarted);
				return false;
			case TableActivationStatus.RefusedByHost:
				failure = new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, operation,
					$"Cheat Engine left the memory record {(requested ? "inactive" : "active")}; an activation callback, " +
					"script or record type refused the change; partial script effects may persist.", null,
					CheatEngineHostEffect.Started);
				return false;
			case TableActivationStatus.Pending:
				failure = new CheatEngineFailure(CheatEngineFailureKind.IndeterminateHostResult, operation,
					"The record activates asynchronously; its final state is not observable in this call.", null,
					CheatEngineHostEffect.Started);
				return false;
			default:
				record = default;
				failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, operation,
					"Cheat Engine did not report the record's state after the change; its effect is unknown.", null,
					CheatEngineHostEffect.Unknown);
				return false;
		}
	}

	/// <summary>Classifies a failed creation and states whether a partially initialized record may remain.</summary>
	private CheatEngineFailure CreateCreationFailure(TableRecordCreation creation)
	{
		CheatEngineFailure failure = creation.Fault is { } fault
			? SdkBoundary.Translate("Tables.Create", fault, CheatEngineHostEffect.Unknown, _lifetime)
			: MutationFailure("Tables.Create", creation.Status);
		return creation.Rollback switch
		{
			TableRecordRollback.Confirmed =>
				CoreFailureFactory.WithHostEffect(failure, CheatEngineHostEffect.Completed),
			TableRecordRollback.Unconfirmed => new CheatEngineFailure(failure.Kind, failure.Operation,
				failure.Message + " The rollback of the partially initialized memory record was not confirmed, so " +
				"it may remain in the Address List.",
				CombineFaults(failure.Exception, creation.RollbackFault), CheatEngineHostEffect.CleanupUnconfirmed),
			_ => failure
		};
	}

	private static Exception? CombineFaults(Exception? primary, Exception? rollback)
	{
		return (primary, rollback) switch
		{
			({ } first, { } second) => new AggregateException(first, second),
			({ } first, null) => first,
			(null, { } second) => second,
			_ => null
		};
	}

	private static bool TryUpdateRecord(MemoryRecordUpdate update, out MemoryRecordSnapshot record)
	{
		record = default;
		if (!AddressListAccess.TryGetCurrent(out AddressList list) ||
			!list.TryGetMemoryRecordById(update.Id, out MemoryRecord value))
		{
			return false;
		}

		return TryApplyUpdate(value, update) && TrySnapshot(value, out record);
	}

	private static bool TryApplyUpdate(MemoryRecord value, MemoryRecordUpdate update)
	{
		return (update.Description is null || value.TrySetDescription(update.Description)) &&
			   (update.AddressExpression is null || value.TrySetAddressExpression(update.AddressExpression)) &&
			   (!update.VariableType.HasValue || value.TrySetVariableType(update.VariableType.Value)) &&
			   (update.Value is null || value.TrySetValue(update.Value));
	}

	private bool TryAuthorize(string path, string operation, out CheatEngineFailure failure)
	{
		if (_policy.AllowedTableRoots.Count == 0)
		{
			failure = new CheatEngineFailure(CheatEngineFailureKind.CapabilityUnavailable, operation,
				"Table import and export are disabled because no allowed root is configured.", null,
				CheatEngineHostEffect.NotStarted);
			return false;
		}

		if (_policy.TryAuthorizeTableFile(path, operation == "Tables.LoadTrustedTable", out string reason))
		{
			failure = default;
			return true;
		}

		failure = new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, operation, reason, null,
			CheatEngineHostEffect.NotStarted);
		return false;
	}

	internal static bool TrySnapshot(MemoryRecord value, out MemoryRecordSnapshot snapshot)
	{
		if (!value.TryGetId(out MemoryRecordId id) || !value.TryGetIndex(out int index) ||
			!value.TryGetDescription(out string? description) ||
			!value.TryGetAddressExpression(out string? expression) ||
			!value.TryGetValue(out string? text) || !value.TryGetVariableType(out VariableType variableType) ||
			!value.Handle.TryGetProperty<BooleanMarshaller, bool>("Active"u8, out bool isActive) ||
			!value.Handle.TryGetProperty<Int32Marshaller, int>("Count"u8, out int childCount) || childCount < 0)
		{
			snapshot = default;
			return false;
		}

		Address? currentAddress = value.TryGetCurrentAddress(out Address address) ? address : null;
		snapshot = new MemoryRecordSnapshot(
			id,
			index,
			new MemoryRecordContentSnapshot(description, expression, text, variableType),
			new MemoryRecordStateSnapshot(currentAddress, isActive, childCount));
		return true;
	}


	private static bool TryBuildHierarchy(MemoryRecord value, MemoryRecordHierarchyRequest request, int depth,
		HashSet<MemoryRecordId> visited, ref int materialized, out MemoryRecordHierarchySnapshot hierarchy,
		out HierarchyBuildProblem problem)
	{
		hierarchy = default;
		if (materialized >= request.MaximumItems)
		{
			problem = HierarchyBuildProblem.ItemLimit;
			return false;
		}

		if (!TrySnapshot(value, out MemoryRecordSnapshot snapshot) || !visited.Add(snapshot.Id))
		{
			problem = HierarchyBuildProblem.InvalidShape;
			return false;
		}

		materialized++;
		if (snapshot.ChildCount == 0)
		{
			hierarchy = new MemoryRecordHierarchySnapshot(snapshot, []);
			problem = HierarchyBuildProblem.None;
			return true;
		}

		if (depth >= request.MaximumDepth)
		{
			problem = HierarchyBuildProblem.DepthLimit;
			return false;
		}

		if (snapshot.ChildCount > request.MaximumItems - materialized)
		{
			problem = HierarchyBuildProblem.ItemLimit;
			return false;
		}

		ImmutableArray<MemoryRecordHierarchySnapshot>.Builder children =
			ImmutableArray.CreateBuilder<MemoryRecordHierarchySnapshot>(snapshot.ChildCount);
		for (int index = 0; index < snapshot.ChildCount; index++)
		{
			problem = HierarchyBuildProblem.InvalidShape;
			if (!value.TryGetChild(index, out MemoryRecord child) ||
				!TryBuildHierarchy(child, request, depth + 1, visited, ref materialized,
					out MemoryRecordHierarchySnapshot childSnapshot,
					out problem))
			{
				return false;
			}

			children.Add(childSnapshot);
		}

		hierarchy = new MemoryRecordHierarchySnapshot(snapshot, children.MoveToImmutable());
		problem = HierarchyBuildProblem.None;
		return true;
	}

	private static bool Matches(MemoryRecordSearch search, MemoryRecordSnapshot record)
	{
		return (search.DescriptionContains is null || record.Description.Contains(search.DescriptionContains,
				   StringComparison.OrdinalIgnoreCase)) &&
			   (search.AddressExpression is null || string.Equals(record.AddressExpression, search.AddressExpression,
				   StringComparison.OrdinalIgnoreCase)) &&
			   (!search.VariableType.HasValue || record.VariableType == search.VariableType.Value) &&
			   (!search.IsActive.HasValue || record.IsActive == search.IsActive.Value);
	}

	private static bool HasPredicate(MemoryRecordSearch search)
	{
		return search.DescriptionContains is not null || search.AddressExpression is not null ||
			   search.VariableType.HasValue || search.IsActive.HasValue;
	}

	private static bool CaptureTable(int count, out AddressTableSnapshot table)
	{
		table = new AddressTableSnapshot(count);
		return true;
	}

	private static CheatEngineFailure HostFailure(string operation)
	{
		return new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, operation,
			"Cheat Engine did not return the expected Address List contract.");
	}

	private static CheatEngineFailure LookupFailure(string operation, RecordLookupStatus status)
	{
		return status switch
		{
			RecordLookupStatus.NotFound => new CheatEngineFailure(CheatEngineFailureKind.NotFound, operation,
				"The requested Cheat Engine memory record was not found."),
			RecordLookupStatus.AddressListUnavailable => new CheatEngineFailure(
				CheatEngineFailureKind.CapabilityUnavailable, operation,
				"Cheat Engine's Address List capability is unavailable."),
			RecordLookupStatus.InvalidRecord => HostFailure(operation),
			_ => HostFailure(operation)
		};
	}

	private static CheatEngineFailure ResultLimitFailure(string operation, int maximumItems)
	{
		return new CheatEngineFailure(CheatEngineFailureKind.ResultLimitExceeded, operation,
			$"The operation requires more records than the explicit limit of {maximumItems}.");
	}

	private static CheatEngineFailure GetHierarchyFailure(HierarchyBuildProblem problem, bool found,
		MemoryRecordHierarchyRequest request)
	{
		return problem switch
		{
			HierarchyBuildProblem.ItemLimit => ResultLimitFailure(_getHierarchyOperation, request.MaximumItems),
			HierarchyBuildProblem.DepthLimit => new CheatEngineFailure(CheatEngineFailureKind.ResultLimitExceeded,
				_getHierarchyOperation,
				$"The operation requires a hierarchy depth greater than the explicit limit of {request.MaximumDepth}."),
			HierarchyBuildProblem.InvalidShape => HostFailure(_getHierarchyOperation),
			_ when !found => new CheatEngineFailure(CheatEngineFailureKind.NotFound, _getHierarchyOperation,
				"The requested Cheat Engine memory record was not found."),
			_ => HostFailure(_getHierarchyOperation)
		};
	}

	private static CheatEngineFailure MutationFailure(string operation, TableRecordMutationStatus status)
	{
		return status switch
		{
			TableRecordMutationStatus.RecordNotFound => new CheatEngineFailure(CheatEngineFailureKind.NotFound,
				operation, "The requested Cheat Engine memory record was not found."),
			TableRecordMutationStatus.ParentNotFound => new CheatEngineFailure(CheatEngineFailureKind.NotFound,
				operation, "The requested parent Cheat Engine memory record was not found."),
			TableRecordMutationStatus.InvalidRelationship => new CheatEngineFailure(
				CheatEngineFailureKind.OperationRejected, operation,
				"The requested parent relationship is invalid: it is self-referential, cyclic, or exceeds the " +
				"supported hierarchy depth."),
			// Same classification as TrySetActive and the lookups (ADR-08): an unavailable Address List is a capability
			// condition, not an unexpected host result, and no record was reached.
			TableRecordMutationStatus.AddressListUnavailable => new CheatEngineFailure(
				CheatEngineFailureKind.CapabilityUnavailable, operation,
				"Cheat Engine's Address List capability is unavailable.", null, CheatEngineHostEffect.NotStarted),
			_ => HostFailure(operation)
		};
	}

	private enum HierarchyBuildProblem
	{
		None,
		ItemLimit,
		DepthLimit,
		InvalidShape
	}

	private delegate RecordLookupStatus RecordLookup(out MemoryRecordSnapshot record);
}
