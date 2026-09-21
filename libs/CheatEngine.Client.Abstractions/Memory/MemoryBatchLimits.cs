namespace CheatEngine.Client.Memory;

/// <summary>Defines the bounded sizes accepted by target-memory batch contracts.</summary>
public static class MemoryBatchLimits
{
	/// <summary>Gets the largest number of homogeneous scalar operations accepted in one batch.</summary>
	/// <remarks>
	///     The limit bounds request copying, result materialization and the amount of work admitted to one Cheat Engine
	///     main-thread dispatch. It does not make the individual Lua globals atomic.
	/// </remarks>
	public const int MaximumOperations = 1024;
}
