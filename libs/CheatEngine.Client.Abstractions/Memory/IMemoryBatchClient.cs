namespace CheatEngine.Client.Memory;

/// <summary>Executes primitive batches while preserving their per-operation outcome details.</summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add
///         members to it, so implement it only in a test double.
///     </para>
///     <para>
///         This companion contract is intentionally separate from <see cref="IMemoryClient" /> so existing client
///         implementations remain source-compatible. Batch writes are sequential and never imply a transaction or a
///         rollback.
///     </para>
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
