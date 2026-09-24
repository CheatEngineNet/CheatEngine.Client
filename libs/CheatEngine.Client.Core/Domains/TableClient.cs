using System.Collections.Immutable;

using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Results;
using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
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

	private const string GetHierarchyOperation = "Tables.GetHierarchy";

	private readonly ICheatEngineDispatcher _dispatcher =
		dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

	private readonly TableRecordGeneration _generation = new();

	private readonly CoreLifetime? _lifetime = lifetime;

	private readonly CoreClientPolicy _policy = policy ?? throw new ArgumentNullException(nameof(policy));
	private readonly ITableRecordLookupPort _recordLookups = recordLookups ?? new SdkTableRecordLookupPort();
	private readonly ITableRecordMutationPort _recordMutations = recordMutations ?? new SdkTableRecordMutationPort();
	private readonly ITableFilePort _tableFiles = tableFiles ?? SdkTableFilePort.Instance;

	// Depth of the trusted table loads in progress. Read and written only inside dispatched callbacks, on Cheat Engine's
	// main thread, where every load runs: a callback that observes a non-zero depth runs inside a load, like a script of
	// the table being loaded calling the Client.
	private int _trustedLoadDepth;

	/// <summary>Gets the number of trusted table loads of this activation that reached Cheat Engine.</summary>
	internal long TableGeneration => _generation.Generation;

	/// <summary>
	///     Gets the refusal of a creation, update or selection issued while a trusted table load runs: CheatEngine.SDK
	///     refuses its own Address List commands then (the identifiers they resolve are being replaced), and has no
	///     command for these three, so the Client refuses them the same way before any Cheat Engine call.
	/// </summary>
	private static TableRecordMutationOutcome LoadInProgress =>
		TableRecordMutationOutcome.NotAttempted(MemoryRecordMutationProblem.TableLoadInProgress);

	/// <summary>Gets whether a trusted table load is running; read only inside a dispatched callback.</summary>
	private bool IsTrustedLoadInProgress => _trustedLoadDepth != 0;

	public bool TryGetRecordCount(out int recordCount, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		const string Operation = "Tables.GetRecordCount";
		int count = 0;
		bool available = false;
		bool counted = false;
		if (!SdkBoundary.TryInvoke(_dispatcher, Operation, () =>
				{
					available = AddressListAccess.TryGetCurrent(out AddressList list);
					counted = available && list.TryGetCount(out count) && count >= 0;
				}, CheatEngineHostEffect.Unknown, _lifetime, out failure, cancellationToken))
		{
			recordCount = 0;
			return false;
		}

		recordCount = counted ? count : 0;
		if (counted)
		{
			return true;
		}

		failure = LookupFailure(Operation,
			available ? RecordLookupStatus.InvalidRecord : RecordLookupStatus.AddressListUnavailable);
		return false;
	}

	public int GetRecordCount(CancellationToken cancellationToken = default)
	{
		if (TryGetRecordCount(out int result, out CheatEngineFailure failure, cancellationToken))
		{
			return result;
		}

		failure.Throw(cancellationToken);
		return 0;
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

		failure.Throw(cancellationToken);
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
			failure = CancellationMapping.AfterNativeCall("Tables.Find",
				"The search was cancelled after the Address List snapshot was copied; no result was published.");
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

		failure.Throw(cancellationToken);
		return [];
	}

	public bool TryGetRecordAt(int index, out MemoryRecordSnapshot record, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		return TryRecord("Tables.GetRecordAt", null, (out result) =>
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

	public MemoryRecordSnapshot GetRecordAt(int index, CancellationToken cancellationToken = default)
	{
		if (TryGetRecordAt(index, out MemoryRecordSnapshot result, out CheatEngineFailure failure, cancellationToken))
		{
			return result;
		}

		failure.Throw(cancellationToken);
		return default;
	}

	public MemoryRecordSnapshot GetRecord(MemoryRecordId id, CancellationToken cancellationToken = default)
	{
		if (TryGetRecord(id, out MemoryRecordSnapshot result, out CheatEngineFailure failure, cancellationToken))
		{
			return result;
		}

		failure.Throw(cancellationToken);
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

		failure.Throw(cancellationToken);
		return default;
	}

	public bool TrySelectRecord(MemoryRecordId id, out MemoryRecordSnapshot record, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		if (IsStale("Tables.SelectRecord", id, out failure))
		{
			record = default;
			return false;
		}

		// Changing Cheat Engine's GUI selection is a host-visible mutation, so it goes through the mutation port.
		MemoryRecordSnapshot captured = default;
		TableRecordMutationOutcome outcome = default;
		if (!TryDispatch("Tables.SelectRecord", id, null,
				() => outcome = IsTrustedLoadInProgress ? LoadInProgress : _recordMutations.TrySelect(id, out captured),
				out long observedGeneration, out failure, cancellationToken))
		{
			record = default;
			return false;
		}

		if (outcome.IsSuccess)
		{
			_generation.Observe(captured, observedGeneration);
			record = captured;
			return true;
		}

		record = default;
		failure = TableMapping.MutationFailure("Tables.SelectRecord", outcome);
		return false;
	}

	public MemoryRecordSnapshot SelectRecord(MemoryRecordId id, CancellationToken cancellationToken = default)
	{
		if (TrySelectRecord(id, out MemoryRecordSnapshot result, out CheatEngineFailure failure, cancellationToken))
		{
			return result;
		}

		failure.Throw(cancellationToken);
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
				() => creation = IsTrustedLoadInProgress
					? new TableRecordCreation(LoadInProgress, TableRecordRollback.NotRequired)
					: _recordMutations.TryCreate(definition, out captured),
				out long observedGeneration, out failure, cancellationToken))
		{
			record = default;
			return false;
		}

		if (creation.Outcome.IsSuccess)
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

		failure.Throw(cancellationToken);
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
		bool refused = false;
		if (!TryDispatch("Tables.Update", update.Id, null, () =>
				{
					refused = IsTrustedLoadInProgress;
					succeeded = !refused && TryUpdateRecord(update, out captured);
				},
				out long observedGeneration, out failure, cancellationToken))
		{
			record = default;
			return false;
		}

		if (refused)
		{
			record = default;
			failure = TableMapping.MutationFailure("Tables.Update", LoadInProgress);
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

		failure.Throw(cancellationToken);
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
		TableRecordMutationOutcome outcome = default;
		if (!TryDispatch("Tables.Delete", id, null, () => outcome = _recordMutations.TryDelete(id), out _,
				out failure, cancellationToken))
		{
			return false;
		}

		if (outcome.IsSuccess)
		{
			failure = default;
			return true;
		}

		failure = TableMapping.MutationFailure("Tables.Delete", outcome);
		return false;
	}

	public void Delete(MemoryRecordId id, CancellationToken cancellationToken = default)
	{
		if (!TryDelete(id, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw(cancellationToken);
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

		bool applied = TableMapping.TryClassifyActivation(Operation, isActive, observation, out failure);
		record = TableMapping.CopiesRecord(observation.Kind) ? observation.Snapshot ?? default : default;
		if (GetNotAppliedStatus(observation.Kind) is { } notApplied)
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

		failure.Throw(cancellationToken);
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
			// CheatEngine.SDK refuses it the same way before any Lua call; refusing it here spares the dispatch.
			record = default;
			failure = TableMapping.MutationFailure("Tables.SetParent",
				TableRecordMutationOutcome.NotAttempted(MemoryRecordMutationProblem.SelfParent));
			return false;
		}

		MemoryRecordSnapshot captured = default;
		TableRecordMutationOutcome outcome = default;
		if (!TryDispatch("Tables.SetParent", childId, parentId,
				() => outcome = _recordMutations.TrySetParent(childId, parentId, out captured),
				out long observedGeneration, out failure, cancellationToken))
		{
			record = default;
			return false;
		}

		if (outcome.IsSuccess)
		{
			_generation.Observe(captured, observedGeneration);
			record = captured;
			failure = default;
			return true;
		}

		record = default;
		failure = TableMapping.MutationFailure("Tables.SetParent", outcome);
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

		failure.Throw(cancellationToken);
		return default;
	}

	public bool TryGetHierarchy(MemoryRecordId rootId, MemoryRecordHierarchyRequest request,
		out MemoryRecordHierarchySnapshot hierarchy, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		if (IsStale(GetHierarchyOperation, rootId, out failure))
		{
			hierarchy = default;
			return false;
		}

		MemoryRecordHierarchySnapshot captured = default;
		bool found = false;
		bool succeeded = false;
		HierarchyProblem problem = default;
		if (!TryDispatch(GetHierarchyOperation, rootId, null, () =>
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

		failure.Throw(cancellationToken);
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

		// CheatTableFiles.TryLoad calls loadTable, which can execute table Lua: a failure or a fault leaves the Address
		// List state unknown. A dispatched load advances the table generation whatever its result, merge or replace:
		// Cheat Engine does not promise that an earlier record identifier survives it (A14-05, open issue O4). The
		// generation advances inside the dispatched callback, on Cheat Engine's main thread, so it is ordered with every
		// dispatched snapshot copy and identifier check. The diagnostics event is emitted after dispatch, never inside the
		// callback. The refused path above never reaches here and is never retried through another overload or a stream.
		bool reachedCheatEngine = false;
		long advancedGeneration = 0;
		LuaOperationStatus status = default;
		try
		{
			if (!SdkBoundary.TryInvoke(_dispatcher, "Tables.LoadTrustedTable",
					() =>
					{
						reachedCheatEngine = true;
						_trustedLoadDepth++;
						try
						{
							status = _tableFiles.TryLoad(request.File.FullPath, request.Merge);
						}
						finally
						{
							_trustedLoadDepth--;
							advancedGeneration = _generation.Advance();
						}
					},
					CheatEngineHostEffect.Unknown, _lifetime, out failure, cancellationToken))
			{
				return false;
			}
		}
		finally
		{
			if (reachedCheatEngine)
			{
				_lifetime?.Diagnostics.TableGenerationAdvanced(_lifetime.Epoch, advancedGeneration);
			}
		}

		return TableMapping.TryClassifyTableFile("Tables.LoadTrustedTable", true, status.Kind, out failure);
	}

	public void LoadTrustedTable(TableLoadRequest request, CancellationToken cancellationToken = default)
	{
		if (!TryLoadTrustedTable(request, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw(cancellationToken);
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

		LuaOperationStatus status = default;
		return SdkBoundary.TryInvoke(_dispatcher, "Tables.SaveTable",
				   () => status = _tableFiles.TrySave(request.File.FullPath), CheatEngineHostEffect.Unknown, _lifetime,
				   out failure, cancellationToken) &&
			   TableMapping.TryClassifyTableFile("Tables.SaveTable", false, status.Kind, out failure);
	}

	public void SaveTable(TableSaveRequest request, CancellationToken cancellationToken = default)
	{
		if (!TrySaveTable(request, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw(cancellationToken);
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
	private static string? GetNotAppliedStatus(MemoryRecordActivationOutcomeKind kind)
	{
		return kind switch
		{
			MemoryRecordActivationOutcomeKind.Applied or MemoryRecordActivationOutcomeKind.Unchanged
				or MemoryRecordActivationOutcomeKind.NotAttempted => null,
			MemoryRecordActivationOutcomeKind.RefusedByHost => nameof(MemoryRecordActivationOutcomeKind.RefusedByHost),
			MemoryRecordActivationOutcomeKind.Pending => nameof(MemoryRecordActivationOutcomeKind.Pending),
			_ => nameof(MemoryRecordActivationOutcomeKind.Indeterminate)
		};
	}

	/// <summary>Classifies a failed creation and states whether a partially initialized record may remain.</summary>
	private CheatEngineFailure CreateCreationFailure(TableRecordCreation creation)
	{
		CheatEngineFailure failure = creation.Fault is { } fault
			? SdkBoundary.Translate("Tables.Create", fault, CheatEngineHostEffect.Unknown, _lifetime)
			: TableMapping.MutationFailure("Tables.Create", creation.Outcome);
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

	/// <summary>Copies one record through CheatEngine.SDK's typed <see cref="MemoryRecord" /> getters.</summary>
	/// <remarks>
	///     Every field is required except the current address, which Cheat Engine cannot always resolve, and the script,
	///     which only an Auto Assembler record has: <see cref="MemoryRecord.TryGetScript" /> reports no script text for
	///     any other record.
	/// </remarks>
	internal static bool TrySnapshot(MemoryRecord value, out MemoryRecordSnapshot snapshot)
	{
		if (!value.TryGetId(out MemoryRecordId id) || !value.TryGetIndex(out int index) ||
			!value.TryGetDescription(out string? description) ||
			!value.TryGetAddressExpression(out string? expression) ||
			!value.TryGetValue(out string? text) || !value.TryGetVariableType(out VariableType variableType) ||
			!value.TryGetOffsetCount(out int offsetCount) || offsetCount < 0 ||
			!value.TryGetActive(out bool isActive) || !value.TryGetAsync(out bool isAsync) ||
			!value.TryGetAsyncProcessing(out bool isAsyncProcessing) || !TryGetChildCount(value, out int childCount))
		{
			snapshot = default;
			return false;
		}

		string? script = value.TryGetScript(out string? scriptText) ? scriptText : null;
		Address? currentAddress = value.TryGetCurrentAddress(out Address address) ? address : null;
		snapshot = new MemoryRecordSnapshot(
			id,
			index,
			new MemoryRecordContentSnapshot(description, expression, text, variableType, script, offsetCount),
			new MemoryRecordStateSnapshot(currentAddress, isActive, childCount, isAsync, isAsyncProcessing));
		return true;
	}

	/// <summary>Reads Cheat Engine's <c>Count</c> property of a record: the number of its immediate children.</summary>
	/// <remarks>
	///     CheatEngine.SDK 2.0.0 has no child-count getter, and <see cref="MemoryRecord.TryGetChild" /> reports an index
	///     past the last child and a failed read alike, so <see cref="MemoryRecordStateSnapshot.ChildCount" /> keeps this
	///     read for its precision (ADR-08). It is the one registered raw property read of the Tables domain in the ADR-01
	///     ratchet, until the SDK offers the getter.
	/// </remarks>
	private static bool TryGetChildCount(MemoryRecord value, out int childCount)
	{
		return value.Handle.TryGetProperty<Int32Marshaller, int>("Count"u8, out childCount) && childCount >= 0;
	}


	private static bool TryBuildHierarchy(MemoryRecord value, MemoryRecordHierarchyRequest request, int depth,
		HashSet<MemoryRecordId> visited, ref int materialized, out MemoryRecordHierarchySnapshot hierarchy,
		out HierarchyProblem problem)
	{
		hierarchy = default;
		if (materialized >= request.MaximumItems)
		{
			problem = new HierarchyProblem(HierarchyBuildProblem.ItemLimit);
			return false;
		}

		if (!TrySnapshot(value, out MemoryRecordSnapshot snapshot) || !visited.Add(snapshot.Id))
		{
			problem = new HierarchyProblem(HierarchyBuildProblem.InvalidShape);
			return false;
		}

		materialized++;
		int childCount = snapshot.State.ChildCount;
		if (childCount == 0)
		{
			hierarchy = new MemoryRecordHierarchySnapshot(snapshot, []);
			problem = default;
			return true;
		}

		if (depth >= request.MaximumDepth)
		{
			problem = new HierarchyProblem(HierarchyBuildProblem.DepthLimit);
			return false;
		}

		if (childCount > request.MaximumItems - materialized)
		{
			problem = new HierarchyProblem(HierarchyBuildProblem.ItemLimit);
			return false;
		}

		ImmutableArray<MemoryRecordHierarchySnapshot>.Builder children =
			ImmutableArray.CreateBuilder<MemoryRecordHierarchySnapshot>(childCount);
		for (int index = 0; index < childCount; index++)
		{
			// Every position below the reported count must hold a child: Cheat Engine refusing one is reported at it.
			if (!value.TryGetChild(index, out MemoryRecord child))
			{
				problem = new HierarchyProblem(HierarchyBuildProblem.ChildUnavailable, snapshot.Id, index, childCount);
				return false;
			}

			if (!TryBuildHierarchy(child, request, depth + 1, visited, ref materialized,
					out MemoryRecordHierarchySnapshot childSnapshot, out problem))
			{
				return false;
			}

			children.Add(childSnapshot);
		}

		hierarchy = new MemoryRecordHierarchySnapshot(snapshot, children.MoveToImmutable());
		problem = default;
		return true;
	}

	private static bool Matches(MemoryRecordSearch search, MemoryRecordSnapshot record)
	{
		return (search.DescriptionContains is null || record.Content.Description.Contains(search.DescriptionContains,
				   StringComparison.OrdinalIgnoreCase)) &&
			   (search.AddressExpression is null || string.Equals(record.Content.AddressExpression,
				   search.AddressExpression, StringComparison.OrdinalIgnoreCase)) &&
			   (!search.VariableType.HasValue || record.Content.VariableType == search.VariableType.Value) &&
			   (!search.IsActive.HasValue || record.State.IsActive == search.IsActive.Value);
	}

	private static bool HasPredicate(MemoryRecordSearch search)
	{
		return search.DescriptionContains is not null || search.AddressExpression is not null ||
			   search.VariableType.HasValue || search.IsActive.HasValue;
	}

	private static CheatEngineFailure HostFailure(string operation)
	{
		return new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, operation,
			TableMapping.InvalidContractMessage);
	}

	private static CheatEngineFailure LookupFailure(string operation, RecordLookupStatus status)
	{
		return status switch
		{
			RecordLookupStatus.NotFound => new CheatEngineFailure(CheatEngineFailureKind.NotFound, operation,
				TableMapping.RecordNotFoundMessage),
			RecordLookupStatus.AddressListUnavailable => new CheatEngineFailure(
				CheatEngineFailureKind.CapabilityUnavailable, operation, TableMapping.AddressListUnavailableMessage),
			RecordLookupStatus.InvalidRecord => HostFailure(operation),
			_ => HostFailure(operation)
		};
	}

	private static CheatEngineFailure ResultLimitFailure(string operation, int maximumItems)
	{
		return new CheatEngineFailure(CheatEngineFailureKind.ResultLimitExceeded, operation,
			$"The operation requires more records than the explicit limit of {maximumItems}.");
	}

	/// <summary>
	///     Creates the failure of a hierarchy copy that stopped because Cheat Engine did not return a child at a position
	///     below the record's reported child count. Internal for the hierarchy tests.
	/// </summary>
	internal static CheatEngineFailure ChildUnavailableFailure(MemoryRecordId recordId, int childIndex, int childCount)
	{
		return new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, GetHierarchyOperation,
			$"Cheat Engine did not return child {childIndex} of memory record {recordId.Value}, which reports " +
			$"{childCount} children.");
	}

	private static CheatEngineFailure GetHierarchyFailure(HierarchyProblem problem, bool found,
		MemoryRecordHierarchyRequest request)
	{
		return problem.Kind switch
		{
			HierarchyBuildProblem.ItemLimit => ResultLimitFailure(GetHierarchyOperation, request.MaximumItems),
			HierarchyBuildProblem.DepthLimit => new CheatEngineFailure(CheatEngineFailureKind.ResultLimitExceeded,
				GetHierarchyOperation,
				$"The operation requires a hierarchy depth greater than the explicit limit of {request.MaximumDepth}."),
			HierarchyBuildProblem.ChildUnavailable =>
				ChildUnavailableFailure(problem.RecordId, problem.ChildIndex, problem.ChildCount),
			HierarchyBuildProblem.InvalidShape => HostFailure(GetHierarchyOperation),
			_ when !found => new CheatEngineFailure(CheatEngineFailureKind.NotFound, GetHierarchyOperation,
				TableMapping.RecordNotFoundMessage),
			_ => HostFailure(GetHierarchyOperation)
		};
	}

	private enum HierarchyBuildProblem
	{
		None,
		ItemLimit,
		DepthLimit,
		InvalidShape,

		/// <summary>Cheat Engine did not return a child at a position below the record's reported child count.</summary>
		ChildUnavailable
	}

	/// <summary>Why a hierarchy copy stopped, and for a missing child, where.</summary>
	private readonly record struct HierarchyProblem(
		HierarchyBuildProblem Kind,
		MemoryRecordId RecordId = default,
		int ChildIndex = 0,
		int ChildCount = 0);

	private delegate RecordLookupStatus RecordLookup(out MemoryRecordSnapshot record);
}
