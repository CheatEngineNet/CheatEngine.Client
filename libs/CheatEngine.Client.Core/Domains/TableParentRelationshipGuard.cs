using CheatEngine.SDK.Engine.AddressList;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Bounded, handle-free validation of a parent chain before Cheat Engine is asked to mutate it.</summary>
internal static class TableParentRelationshipGuard
{
	internal static TableRecordMutationStatus Validate(MemoryRecordId childId, MemoryRecordId candidateParentId,
		int maximumHops, Func<MemoryRecordId, ParentChainStep> getNext)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumHops);
		ArgumentNullException.ThrowIfNull(getNext);

		HashSet<MemoryRecordId> visited = new();
		MemoryRecordId current = candidateParentId;
		for (int hop = 0; hop < maximumHops; hop++)
		{
			if (current == childId || !visited.Add(current))
			{
				return TableRecordMutationStatus.InvalidRelationship;
			}

			ParentChainStep step = getNext(current);
			switch (step.Kind)
			{
				case ParentChainStepKind.Root:
					return TableRecordMutationStatus.Success;
				case ParentChainStepKind.Parent:
					current = step.ParentId;
					break;
				case ParentChainStepKind.HostRejected:
					return TableRecordMutationStatus.HostRejected;
				default:
					return TableRecordMutationStatus.HostRejected;
			}
		}

		return TableRecordMutationStatus.InvalidRelationship;
	}
}
