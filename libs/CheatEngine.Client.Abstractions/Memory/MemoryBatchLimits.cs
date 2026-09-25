namespace CheatEngine.Client.Memory;

/// <summary>Defines the bounded sizes accepted by target-memory batch contracts.</summary>
public static class MemoryBatchLimits
{
	/// <summary>Gets the largest number of homogeneous scalar operations accepted in one batch.</summary>
	/// <remarks>
	///     <para>
	///         This is the hard <em>request count per batch</em>: the batch request constructors reject a longer list, and
	///         <see cref="MemoryResourceLimits.MaximumBatchOperationCount" /> can tighten it for one activation but never
	///         raise it.
	///     </para>
	///     <para>
	///         The limit bounds request copying, result materialization and the amount of work admitted to one Cheat
	///         Engine main-thread dispatch. It does not make the individual Lua globals atomic: the operations run in
	///         order, a write batch is never rolled back, and its <em>partial-effect state</em> is reported by
	///         <see cref="MemoryPrimitiveBatchWriteOutcome.EffectState" /> with the completed prefix length.
	///     </para>
	/// </remarks>
	public const int MaximumOperationCount = 1024;
}
