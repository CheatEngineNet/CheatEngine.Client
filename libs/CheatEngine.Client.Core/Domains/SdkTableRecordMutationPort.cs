using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Protected SDK implementation of record destruction and parent reassignment.</summary>
internal sealed class SdkTableRecordMutationPort : ITableRecordMutationPort
{
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
}
