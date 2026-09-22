namespace CheatEngine.Client.Memory;

/// <summary>Executes primitive batches while preserving their per-operation outcome details.</summary>
/// <remarks>
///     This companion contract is intentionally separate from <see cref="IMemoryClient" /> so existing client
///     implementations remain source-compatible. Batch writes are sequential and never imply a transaction or a
///     rollback.
/// </remarks>
public interface IMemoryBatchClient
{
	/// <summary>Executes a primitive read batch and returns its completed immutable value prefix and failure details.</summary>
	public MemoryPrimitiveBatchReadOutcome<T> ReadPrimitiveBatchDetailed<T>(MemoryPrimitiveBatchReadRequest<T> request,
		CancellationToken cancellationToken = default);

	/// <summary>Executes a primitive write batch and returns its completed count and observable effect state.</summary>
	public MemoryPrimitiveBatchWriteOutcome WritePrimitiveBatchDetailed<T>(MemoryPrimitiveBatchWriteRequest<T> request,
		CancellationToken cancellationToken = default);
}
