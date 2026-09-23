using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Protected SDK implementation of record creation, destruction, selection, activation and parent reassignment.</summary>
internal sealed class SdkTableRecordMutationPort : ITableRecordMutationPort
{
	/// <inheritdoc />
	/// <remarks>
	///     When initialization, snapshotting or the parent assignment fails, the record created by this call is destroyed
	///     exactly once through its own handle. The <see langword="bool" /> result of that destroy is kept: a
	///     <see langword="false" /> result or a fault is reported as <see cref="TableRecordRollback.Unconfirmed" /> and
	///     never retried (audit A08-14).
	/// </remarks>
	public TableRecordCreation TryCreate(MemoryRecordDefinition definition, out MemoryRecordSnapshot record)
	{
		record = default;
		if (!AddressListAccess.TryGetCurrent(out AddressList list) || !list.TryCreateMemoryRecord(out MemoryRecord value))
		{
			return new TableRecordCreation(TableRecordMutationStatus.HostRejected, TableRecordRollback.NotRequired);
		}

		TableRecordMutationStatus status;
		Exception? fault = null;
		try
		{
			status = TryInitializeRecord(value, definition)
				? TryCompleteRecordCreation(value, definition, out record)
				: TableRecordMutationStatus.HostRejected;
			if (status == TableRecordMutationStatus.Success)
			{
				return TableRecordCreation.Created;
			}
		}
		catch (Exception exception) when (SdkBoundary.IsSdkFault(exception))
		{
			status = TableRecordMutationStatus.HostRejected;
			fault = exception;
		}

		record = default;
		(TableRecordRollback rollback, Exception? rollbackFault) = RollBackCreatedRecord(value);
		return new TableRecordCreation(status, rollback, fault, rollbackFault);
	}

	public TableRecordMutationStatus TryDelete(MemoryRecordId id)
	{
		if (!AddressListAccess.TryGetCurrent(out AddressList list))
		{
			return TableRecordMutationStatus.HostRejected;
		}

		if (!list.TryGetMemoryRecordById(id, out MemoryRecord record))
		{
			return TableRecordMutationStatus.RecordNotFound;
		}

		return record.Handle.TryCallMethod("destroy"u8)
			? TableRecordMutationStatus.Success
			: TableRecordMutationStatus.HostRejected;
	}

	public TableActivationObservation TrySetActive(MemoryRecordId id, bool requested)
	{
		if (!AddressListAccess.TryGetCurrent(out AddressList list))
		{
			return TableActivationObservation.Of(TableActivationStatus.AddressListUnavailable);
		}

		return list.TryGetMemoryRecordById(id, out MemoryRecord record)
			? TableRecordActivation.Apply(new RecordActivationAccess(record), requested)
			: TableActivationObservation.Of(TableActivationStatus.RecordNotFound);
	}

	public TableRecordMutationStatus TrySelect(MemoryRecordId id, out MemoryRecordSnapshot record)
	{
		record = default;
		if (!AddressListAccess.TryGetCurrent(out AddressList list))
		{
			return TableRecordMutationStatus.HostRejected;
		}

		if (!list.TryGetMemoryRecordById(id, out MemoryRecord value))
		{
			return TableRecordMutationStatus.RecordNotFound;
		}

		return list.TrySetSelectedRecord(value) && TableClient.TrySnapshot(value, out record)
			? TableRecordMutationStatus.Success
			: TableRecordMutationStatus.HostRejected;
	}

	public TableRecordMutationStatus TrySetParent(MemoryRecordId childId, MemoryRecordId? parentId,
		out MemoryRecordSnapshot record)
	{
		record = default;
		if (parentId is { } requestedParentId && requestedParentId == childId)
		{
			return TableRecordMutationStatus.InvalidRelationship;
		}

		TableRecordMutationStatus status = TryResolveMutationContext(childId, parentId, out AddressList list,
			out MemoryRecord child, out MemoryRecord parent);
		if (status != TableRecordMutationStatus.Success)
		{
			return status;
		}

		if (parentId is { } candidateParentId)
		{
			status = ValidateParentRelationship(list, childId, candidateParentId, parent);
			if (status != TableRecordMutationStatus.Success)
			{
				return status;
			}
		}

		return TryAssignParent(child, parent, out record);
	}

	private static bool TryInitializeRecord(MemoryRecord value, MemoryRecordDefinition definition)
	{
		return value.TrySetDescription(definition.Description) &&
			   value.TrySetAddressExpression(definition.AddressExpression) &&
			   value.TrySetVariableType(definition.VariableType) &&
			   value.TrySetValue(definition.Value);
	}

	private TableRecordMutationStatus TryCompleteRecordCreation(MemoryRecord value, MemoryRecordDefinition definition,
		out MemoryRecordSnapshot record)
	{
		record = default;
		if (definition.ParentId is not { } parentId)
		{
			return TableClient.TrySnapshot(value, out record)
				? TableRecordMutationStatus.Success
				: TableRecordMutationStatus.HostRejected;
		}

		return value.TryGetId(out MemoryRecordId createdId)
			? TrySetParent(createdId, parentId, out record)
			: TableRecordMutationStatus.HostRejected;
	}

	/// <summary>Destroys the record created by this call exactly once and reports whether Cheat Engine confirmed it.</summary>
	private static (TableRecordRollback Rollback, Exception? Fault) RollBackCreatedRecord(MemoryRecord value)
	{
		try
		{
			return (value.Handle.TryCallMethod("destroy"u8)
				? TableRecordRollback.Confirmed
				: TableRecordRollback.Unconfirmed, null);
		}
		catch (Exception exception) when (SdkBoundary.IsSdkFault(exception))
		{
			return (TableRecordRollback.Unconfirmed, exception);
		}
	}

	private static TableRecordMutationStatus TryResolveMutationContext(MemoryRecordId childId, MemoryRecordId? parentId,
		out AddressList list, out MemoryRecord child, out MemoryRecord parent)
	{
		list = default;
		child = default;
		parent = MemoryRecord.Null;
		if (!AddressListAccess.TryGetCurrent(out list))
		{
			return TableRecordMutationStatus.HostRejected;
		}

		if (!list.TryGetMemoryRecordById(childId, out child))
		{
			return TableRecordMutationStatus.RecordNotFound;
		}

		if (parentId is { } parentIdValue && !list.TryGetMemoryRecordById(parentIdValue, out parent))
		{
			return TableRecordMutationStatus.ParentNotFound;
		}

		return TableRecordMutationStatus.Success;
	}

	private static TableRecordMutationStatus ValidateParentRelationship(AddressList list, MemoryRecordId childId,
		MemoryRecordId candidateParentId, MemoryRecord parent)
	{
		if (!list.TryGetCount(out int topLevelCount))
		{
			return TableRecordMutationStatus.HostRejected;
		}

		MemoryRecord current = parent;
		return TableParentRelationshipGuard.Validate(childId, candidateParentId, GetMaximumParentHops(topLevelCount),
			_ => GetNextParentChainStep(ref current));
	}

	private static ParentChainStep GetNextParentChainStep(ref MemoryRecord current)
	{
		ParentReadStatus parentReadStatus = TryReadParent(current, out MemoryRecord next);
		if (parentReadStatus == ParentReadStatus.Root)
		{
			return ParentChainStep.Root;
		}

		if (parentReadStatus != ParentReadStatus.Parent)
		{
			return ParentChainStep.HostRejected;
		}

		current = next;
		if (!current.TryGetId(out MemoryRecordId nextId))
		{
			return ParentChainStep.HostRejected;
		}

		return ParentChainStep.Parent(nextId);
	}

	private static ParentReadStatus TryReadParent(MemoryRecord current, out MemoryRecord parent)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		if (!current.Handle.TryGetProperty(state, "Parent"u8).IsOk)
		{
			parent = default;
			return ParentReadStatus.HostRejected;
		}

		if (state.IsNil(-1))
		{
			parent = default;
			return ParentReadStatus.Root;
		}

		return MemoryRecord.TryRead(state, -1, out parent)
			? ParentReadStatus.Parent
			: ParentReadStatus.HostRejected;
	}

	private static TableRecordMutationStatus TryAssignParent(MemoryRecord child, MemoryRecord parent,
		out MemoryRecordSnapshot record)
	{
		record = default;
		if (!child.Handle.TrySetProperty<MemoryRecord, MemoryRecord>("Parent"u8, parent) ||
			!TableClient.TrySnapshot(child, out record))
		{
			return TableRecordMutationStatus.HostRejected;
		}

		return TableRecordMutationStatus.Success;
	}

	private static int GetMaximumParentHops(int topLevelCount)
	{
		const int traversalSlack = 1024;
		return topLevelCount > int.MaxValue - traversalSlack
			? int.MaxValue
			: Math.Max(1, topLevelCount) + traversalSlack;
	}

	private enum ParentReadStatus
	{
		Root,
		Parent,
		HostRejected
	}

	/// <summary>
	///     The SDK handle calls of the activation algorithm (moved here from <see cref="TableClient" /> behind the mutation
	///     port for C1 testability; the <c>Active</c> and <c>AsyncProcessing</c> names are Cheat Engine's MemoryRecord
	///     properties).
	/// </summary>
	private readonly struct RecordActivationAccess(MemoryRecord record) : IRecordActivationAccess
	{
		public bool TryReadActive(out bool active)
		{
			return record.Handle.TryGetProperty<BooleanMarshaller, bool>("Active"u8, out active);
		}

		public bool TryWriteActive(bool active)
		{
			return record.Handle.TrySetProperty<BooleanMarshaller, bool>("Active"u8, active);
		}

		public bool TryReadAsyncProcessing(out bool processing)
		{
			return record.Handle.TryGetProperty<BooleanMarshaller, bool>("AsyncProcessing"u8, out processing);
		}

		public bool TrySnapshot(out MemoryRecordSnapshot snapshot)
		{
			return TableClient.TrySnapshot(record, out snapshot);
		}
	}
}
