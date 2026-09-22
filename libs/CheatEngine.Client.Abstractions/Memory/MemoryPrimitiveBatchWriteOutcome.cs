using CheatEngine.Client.Results;

namespace CheatEngine.Client.Memory;

/// <summary>Describes the sequential outcome and effect state of one primitive batch write.</summary>
public sealed class MemoryPrimitiveBatchWriteOutcome
{
	/// <summary>Creates a write outcome with a known or explicitly unknown target effect state.</summary>
	public MemoryPrimitiveBatchWriteOutcome(int attemptedCount, int completedCount, int? failedIndex,
		CheatEngineFailure? cause, MemoryBatchWriteEffectState effectState)
	{
		Validate(attemptedCount, completedCount, failedIndex, cause, effectState);
		AttemptedCount = attemptedCount;
		CompletedCount = completedCount;
		FailedIndex = failedIndex;
		Cause = cause;
		EffectState = effectState;
	}

	/// <summary>Gets the number of operations requested by the batch.</summary>
	public int AttemptedCount
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
	public CheatEngineFailure? Cause
	{
		get;
	}

	/// <summary>Gets the known, partial, complete, or unknown target-memory effect state.</summary>
	public MemoryBatchWriteEffectState EffectState
	{
		get;
	}

	/// <summary>Gets whether every requested write completed successfully.</summary>
	public bool Succeeded => Cause is null && EffectState == MemoryBatchWriteEffectState.Complete;

	private static void Validate(int attemptedCount, int completedCount, int? failedIndex,
		CheatEngineFailure? cause, MemoryBatchWriteEffectState effectState)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attemptedCount);
		if (completedCount < 0 || completedCount > attemptedCount)
		{
			throw new ArgumentOutOfRangeException(nameof(completedCount));
		}

		if (!Enum.IsDefined(effectState))
		{
			throw new ArgumentOutOfRangeException(nameof(effectState));
		}

		if (failedIndex is { } index && (index < 0 || index >= attemptedCount || index != completedCount))
		{
			throw new ArgumentOutOfRangeException(nameof(failedIndex));
		}

		if (cause is null && (completedCount != attemptedCount || effectState != MemoryBatchWriteEffectState.Complete))
		{
			throw new ArgumentException("An incomplete write outcome requires a failure cause.", nameof(cause));
		}

		if (cause is not null &&
		    (effectState == MemoryBatchWriteEffectState.Complete || completedCount == attemptedCount))
		{
			throw new ArgumentException("A completed write outcome cannot contain a failure cause.", nameof(cause));
		}

		if (effectState == MemoryBatchWriteEffectState.NotStarted && completedCount != 0)
		{
			throw new ArgumentException("A not-started write outcome cannot contain completed writes.",
				nameof(effectState));
		}

		if (effectState == MemoryBatchWriteEffectState.Partial &&
		    (completedCount == 0 || completedCount == attemptedCount))
		{
			throw new ArgumentException("A partial write outcome requires a strict completed prefix.",
				nameof(effectState));
		}
	}
}
