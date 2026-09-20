using CheatEngine.SDK.Engine.AddressList;

namespace CheatEngine.Client.Core.Domains;

/// <summary>One inspected link while validating that a record can be reparented without forming a cycle.</summary>
internal readonly record struct ParentChainStep(ParentChainStepKind Kind, MemoryRecordId ParentId)
{
	internal static ParentChainStep Root => new(ParentChainStepKind.Root, default);

	internal static ParentChainStep HostRejected => new(ParentChainStepKind.HostRejected, default);

	internal static ParentChainStep Parent(MemoryRecordId parentId)
	{
		return new ParentChainStep(ParentChainStepKind.Parent, parentId);
	}
}
