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

internal sealed class TableClient(
	ICheatEngineDispatcher dispatcher,
	CoreClientPolicy policy,
	ITableRecordMutationPort? recordMutations = null) : ITableClient
{
	private const string GetHierarchyOperation = "Tables.GetHierarchy";

	private readonly ICheatEngineDispatcher _dispatcher =
		dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

	private readonly CoreClientPolicy _policy = policy ?? throw new ArgumentNullException(nameof(policy));
	private readonly ITableRecordMutationPort _recordMutations = recordMutations ?? new SdkTableRecordMutationPort();

	public bool TryGetCurrent(out AddressTableSnapshot table, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		AddressTableSnapshot captured = default;
		bool succeeded = false;
		if (!_dispatcher.TryInvoke(() =>
			    succeeded = AddressListAccess.TryGetCurrent(out AddressList list) && list.TryGetCount(out int count) &&
			                CaptureTable(count, out captured), out failure, cancellationToken))
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
		bool succeeded = false;
		bool exceededLimit = false;
		if (!_dispatcher.TryInvoke(
			() => succeeded = TryCaptureSnapshot(request, out captured, out exceededLimit),
			out failure, cancellationToken))
		{
			table = default;
			return false;
		}

		table = captured;
		if (succeeded)
		{
			return true;
		}

		failure = exceededLimit
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
				"A memory-record search must specify at least one predicate.");
			return false;
		}

		if (!TryGetSnapshot(request, out AddressTableSnapshot snapshot, out failure, cancellationToken))
		{
			records = [];
			return false;
		}

		if (cancellationToken.IsCancellationRequested)
		{
			records = [];
			failure = CoreFailureFactory.Cancelled("Tables.Find");
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
		return TryRecord("Tables.GetRecord",
			list => list.TryGetMemoryRecord(index, out MemoryRecord value) ? value : null,
			out record, out failure, cancellationToken);
	}

	public bool TryGetRecord(MemoryRecordId id, out MemoryRecordSnapshot record,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		return TryRecord("Tables.GetRecord",
			list => list.TryGetMemoryRecordById(id, out MemoryRecord value) ? value : null,
			out record, out failure, cancellationToken);
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
		return TryRecord("Tables.GetSelected", list => list.TryGetSelectedRecord(out MemoryRecord value) ? value : null,
			out record, out failure, cancellationToken);
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
		return TryMutateRecord("Tables.Select", id,
			static (list, value) => list.TrySetSelectedRecord(value), out record, out failure, cancellationToken);
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
		MemoryRecordSnapshot captured = default;
		bool succeeded = false;
		TableRecordMutationStatus parentMutationStatus = TableRecordMutationStatus.Success;
		if (!_dispatcher.TryInvoke(
			() => succeeded = TryCreateRecord(definition, out captured, out parentMutationStatus),
			out failure, cancellationToken))
		{
			record = default;
			return false;
		}

		record = captured;
		if (succeeded)
		{
			return true;
		}

		failure = parentMutationStatus == TableRecordMutationStatus.Success
			? HostFailure("Tables.Create")
			: MutationFailure("Tables.Create", parentMutationStatus);
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
		MemoryRecordSnapshot captured = default;
		bool succeeded = false;
		if (!_dispatcher.TryInvoke(() => succeeded = TryUpdateRecord(update, out captured), out failure,
			cancellationToken))
		{
			record = default;
			return false;
		}

		record = captured;
		if (succeeded)
		{
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
		TableRecordMutationStatus status = TableRecordMutationStatus.HostRejected;
		if (!_dispatcher.TryInvoke(() => status = _recordMutations.TryDelete(id), out failure, cancellationToken))
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
		return TryMutateRecord("Tables.SetActive", id,
			static (_, value, active) => value.Handle.TrySetProperty<BooleanMarshaller, bool>("Active"u8, active),
			isActive, out record, out failure, cancellationToken);
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
		if (parentId is { } requestedParentId && requestedParentId == childId)
		{
			record = default;
			failure = MutationFailure("Tables.SetParent", TableRecordMutationStatus.InvalidRelationship);
			return false;
		}

		MemoryRecordSnapshot captured = default;
		TableRecordMutationStatus status = TableRecordMutationStatus.HostRejected;
		if (!_dispatcher.TryInvoke(() => status = _recordMutations.TrySetParent(childId, parentId, out captured),
			    out failure, cancellationToken))
		{
			record = default;
			return false;
		}

		record = captured;
		if (status == TableRecordMutationStatus.Success)
		{
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
		MemoryRecordHierarchySnapshot captured = default;
		bool found = false;
		bool succeeded = false;
		HierarchyBuildProblem problem = HierarchyBuildProblem.None;
		if (!_dispatcher.TryInvoke(() =>
		    {
			    if (!AddressListAccess.TryGetCurrent(out AddressList list) ||
			        !list.TryGetMemoryRecordById(rootId, out MemoryRecord root))
			    {
				    return;
			    }

			    found = true;
			    HashSet<MemoryRecordId> visited = new();
			    int materialized = 0;
			    succeeded = TryBuildHierarchy(root, request, 1, visited, ref materialized, out captured, out problem);
		    }, out failure, cancellationToken))
		{
			hierarchy = default;
			return false;
		}

		hierarchy = captured;
		if (succeeded)
		{
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
		if (!TryAuthorize(request.File.FullPath, "Tables.LoadTrustedTable", out failure))
		{
			return false;
		}

		return _dispatcher.TryInvoke(() => ClientLuaGlobals.LoadTable(request.File.FullPath, request.Merge),
			out failure, cancellationToken);
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
		if (!TryAuthorize(request.File.FullPath, "Tables.SaveTable", out failure))
		{
			return false;
		}

		return _dispatcher.TryInvoke(() => ClientLuaGlobals.SaveTable(request.File.FullPath),
			out failure, cancellationToken);
	}

	public void SaveTable(TableSaveRequest request, CancellationToken cancellationToken = default)
	{
		if (!TrySaveTable(request, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw();
		}
	}

	private bool TryRecord(string operation, Func<AddressList, MemoryRecord?> selector,
		out MemoryRecordSnapshot record, out CheatEngineFailure failure, CancellationToken cancellationToken)
	{
		MemoryRecordSnapshot captured = default;
		bool succeeded = false;
		if (!_dispatcher.TryInvoke(() =>
		    {
			    if (!AddressListAccess.TryGetCurrent(out AddressList list))
			    {
				    return;
			    }

			    MemoryRecord? value = selector(list);
			    succeeded = value.HasValue && TrySnapshot(value.Value, out captured);
		    }, out failure, cancellationToken))
		{
			record = default;
			return false;
		}

		record = captured;
		if (succeeded)
		{
			return true;
		}

		failure = new CheatEngineFailure(CheatEngineFailureKind.NotFound, operation,
			"The requested Cheat Engine memory record was not found or was malformed.");
		return false;
	}

	private static bool TryCaptureSnapshot(MemoryRecordCollectionRequest request, out AddressTableSnapshot table,
		out bool exceededLimit)
	{
		table = default;
		exceededLimit = false;
		if (!AddressListAccess.TryGetCurrent(out AddressList list) || !list.TryGetCount(out int count))
		{
			return false;
		}

		if (count > request.MaximumItems)
		{
			exceededLimit = true;
			return false;
		}

		return TryCaptureRecords(list, count, out table);
	}

	private static bool TryCaptureRecords(AddressList list, int count, out AddressTableSnapshot table)
	{
		ImmutableArray<MemoryRecordSnapshot>.Builder records =
			ImmutableArray.CreateBuilder<MemoryRecordSnapshot>(count);
		for (int index = 0; index < count; index++)
		{
			if (!list.TryGetMemoryRecord(index, out MemoryRecord value) ||
			    !TrySnapshot(value, out MemoryRecordSnapshot snapshot))
			{
				table = default;
				return false;
			}

			records.Add(snapshot);
		}

		table = new AddressTableSnapshot(records.MoveToImmutable());
		return true;
	}

	private bool TryCreateRecord(MemoryRecordDefinition definition, out MemoryRecordSnapshot record,
		out TableRecordMutationStatus parentMutationStatus)
	{
		record = default;
		parentMutationStatus = TableRecordMutationStatus.Success;
		if (!AddressListAccess.TryGetCurrent(out AddressList list) || !list.TryCreateMemoryRecord(out MemoryRecord value))
		{
			return false;
		}

		bool succeeded = TryInitializeRecord(value, definition) &&
			TryCompleteRecordCreation(value, definition, out record, out parentMutationStatus);
		if (!succeeded)
		{
			_ = value.Handle.TryCallMethod("destroy"u8);
		}

		return succeeded;
	}

	private static bool TryInitializeRecord(MemoryRecord value, MemoryRecordDefinition definition)
	{
		return value.TrySetDescription(definition.Description) &&
		       value.TrySetAddressExpression(definition.AddressExpression) &&
		       value.TrySetVariableType(definition.VariableType) &&
		       value.TrySetValue(definition.Value);
	}

	private bool TryCompleteRecordCreation(MemoryRecord value, MemoryRecordDefinition definition,
		out MemoryRecordSnapshot record, out TableRecordMutationStatus parentMutationStatus)
	{
		record = default;
		parentMutationStatus = TableRecordMutationStatus.Success;
		if (definition.ParentId is not { } parentId)
		{
			return TrySnapshot(value, out record);
		}

		if (!value.TryGetId(out MemoryRecordId createdId))
		{
			parentMutationStatus = TableRecordMutationStatus.HostRejected;
			return false;
		}

		parentMutationStatus = _recordMutations.TrySetParent(createdId, parentId, out record);
		return parentMutationStatus == TableRecordMutationStatus.Success;
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
				"Table import and export are disabled because no allowed root is configured.");
			return false;
		}

		if (_policy.TryAuthorizeTableFile(path, operation == "Tables.LoadTrustedTable", out string reason))
		{
			failure = default;
			return true;
		}

		failure = new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, operation,
			reason);
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
		snapshot = new MemoryRecordSnapshot(id, index, description, expression, text, variableType, currentAddress,
			isActive, childCount);
		return true;
	}

	private bool TryMutateRecord(string operation, MemoryRecordId id,
		Func<AddressList, MemoryRecord, bool> mutation, out MemoryRecordSnapshot record,
		out CheatEngineFailure failure, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(mutation);
		MemoryRecordSnapshot captured = default;
		bool found = false;
		bool succeeded = false;
		if (!_dispatcher.TryInvoke(() =>
		    {
			    if (!AddressListAccess.TryGetCurrent(out AddressList list) ||
			        !list.TryGetMemoryRecordById(id, out MemoryRecord value))
			    {
				    return;
			    }

			    found = true;
			    succeeded = mutation(list, value) && TrySnapshot(value, out captured);
		    }, out failure, cancellationToken))
		{
			record = default;
			return false;
		}

		record = captured;
		if (succeeded)
		{
			return true;
		}

		failure = found
			? HostFailure(operation)
			: new CheatEngineFailure(CheatEngineFailureKind.NotFound, operation,
				"The requested Cheat Engine memory record was not found.");
		return false;
	}

	private bool TryMutateRecord<TArgument>(string operation, MemoryRecordId id,
		Func<AddressList, MemoryRecord, TArgument, bool> mutation, TArgument argument,
		out MemoryRecordSnapshot record, out CheatEngineFailure failure, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(mutation);
		return TryMutateRecord(operation, id, (list, value) => mutation(list, value, argument), out record,
			out failure, cancellationToken);
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
			HierarchyBuildProblem.ItemLimit => ResultLimitFailure(GetHierarchyOperation, request.MaximumItems),
			HierarchyBuildProblem.DepthLimit => new CheatEngineFailure(CheatEngineFailureKind.ResultLimitExceeded,
				GetHierarchyOperation,
				$"The operation requires a hierarchy depth greater than the explicit limit of {request.MaximumDepth}."),
			HierarchyBuildProblem.InvalidShape => HostFailure(GetHierarchyOperation),
			_ when !found => new CheatEngineFailure(CheatEngineFailureKind.NotFound, GetHierarchyOperation,
				"The requested Cheat Engine memory record was not found."),
			_ => HostFailure(GetHierarchyOperation)
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
				"A memory record cannot be its own parent."),
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
}
