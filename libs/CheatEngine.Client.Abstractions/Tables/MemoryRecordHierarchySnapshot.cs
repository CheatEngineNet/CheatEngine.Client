using System.Collections.Immutable;

namespace CheatEngine.Client.Tables;

/// <summary>A copied, recursively bounded memory-record tree.</summary>
public readonly record struct MemoryRecordHierarchySnapshot(
	MemoryRecordSnapshot Record,
	ImmutableArray<MemoryRecordHierarchySnapshot> Children);
