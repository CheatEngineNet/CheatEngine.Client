using CheatEngine.Client.Results;

namespace CheatEngine.Client.Memory;

/// <summary>Describes the sequential outcome and effect state of one primitive batch write.</summary>
public sealed class MemoryPrimitiveBatchWriteOutcome
{
	/// <summary>Creates a write outcome with a known or explicitly unknown target effect state.</summary>
	/// <param name="requestedCount">The positive number of writes the batch requested.</param>
	/// <param name="completedCount">The number of writes known to have completed in order.</param>
	/// <param name="failedIndex">
	///     The write that failed, which is <paramref name="completedCount" />, or <see langword="null" /> when it is
	///     not known.
	/// </param>
	/// <param name="failure">The failure when the batch did not complete; <see langword="null" /> otherwise.</param>
	/// <param name="effectState">What is known about the writes' effect on the target.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="requestedCount" /> is zero or negative, <paramref name="completedCount" /> is negative or
	///     larger than it, <paramref name="effectState" /> is not a defined value, or <paramref name="failedIndex" />
	///     is not <paramref name="completedCount" />.
	/// </exception>
	/// <exception cref="ArgumentException">
	///     The values contradict each other: <paramref name="failure" /> is <see langword="null" /> for an incomplete
	///     batch or set for a complete one, a <see cref="MemoryBatchWriteEffectState.NotStarted" /> state has completed
	///     writes, or a <see cref="MemoryBatchWriteEffectState.Partial" /> state has none or all of them.
	/// </exception>
	public MemoryPrimitiveBatchWriteOutcome(int requestedCount, int completedCount, int? failedIndex,
		CheatEngineFailure? failure, MemoryBatchWriteEffectState effectState)
	{
		Validate(requestedCount, completedCount, failedIndex, failure, effectState);
		RequestedCount = requestedCount;
		CompletedCount = completedCount;
		FailedIndex = failedIndex;
		Failure = failure;
		EffectState = effectState;
	}

	/// <summary>Gets the number of operations requested by the batch.</summary>
	public int RequestedCount
	{
		get;
	}

	/// <summary>Gets the number of writes known to have completed in order.</summary>
	public int CompletedCount
	{
		get;
	}

	/// <summary>Gets the individual write index that failed, or <see langword="null" /> when it is not known.</summary>
	public int? FailedIndex
	{
		get;
	}

	/// <summary>Gets the expected admission, dispatch, or target-memory failure when the batch did not complete.</summary>
	public CheatEngineFailure? Failure
	{
		get;
	}

	/// <summary>Gets the known, partial, complete, or unknown target-memory effect state.</summary>
	public MemoryBatchWriteEffectState EffectState
	{
		get;
	}

	/// <summary>Gets whether every requested write completed successfully.</summary>
	public bool IsSuccess => Failure is null && EffectState == MemoryBatchWriteEffectState.Completed;

	private static void Validate(int requestedCount, int completedCount, int? failedIndex,
		CheatEngineFailure? failure, MemoryBatchWriteEffectState effectState)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedCount);
		if (completedCount < 0 || completedCount > requestedCount)
		{
			throw new ArgumentOutOfRangeException(nameof(completedCount));
		}

		if (!Enum.IsDefined(effectState))
		{
			throw new ArgumentOutOfRangeException(nameof(effectState));
		}

		if (failedIndex is { } index && (index < 0 || index >= requestedCount || index != completedCount))
		{
			throw new ArgumentOutOfRangeException(nameof(failedIndex));
		}

		if (failure is null && (completedCount != requestedCount || effectState != MemoryBatchWriteEffectState.Completed))
		{
			throw new ArgumentException("An incomplete write outcome requires a failure.", nameof(failure));
		}

		if (failure is not null &&
			(effectState == MemoryBatchWriteEffectState.Completed || completedCount == requestedCount))
		{
			throw new ArgumentException("A completed write outcome cannot contain a failure.", nameof(failure));
		}

		if (effectState == MemoryBatchWriteEffectState.NotStarted && completedCount != 0)
		{
			throw new ArgumentException("A not-started write outcome cannot contain completed writes.",
				nameof(effectState));
		}

		if (effectState == MemoryBatchWriteEffectState.Partial &&
			(completedCount == 0 || completedCount == requestedCount))
		{
			throw new ArgumentException("A partial write outcome requires a strict completed prefix.",
				nameof(effectState));
		}
	}
}
