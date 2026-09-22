using System.Collections.Immutable;

using CheatEngine.Client.Results;

namespace CheatEngine.Client.Memory;

/// <summary>Describes the sequential outcome of one primitive batch read.</summary>
/// <typeparam name="T">The homogeneous primitive value type.</typeparam>
public sealed class MemoryPrimitiveBatchReadOutcome<T>
{
	/// <summary>Creates a read outcome and copies the completed value prefix.</summary>
	public MemoryPrimitiveBatchReadOutcome(int attemptedCount, int completedCount, int? failedIndex,
		CheatEngineFailure? cause, ReadOnlySpan<T> readPrefix)
	{
		ValidateCounts(attemptedCount, completedCount, failedIndex, cause);
		if (readPrefix.Length != completedCount)
		{
			throw new ArgumentException("The read prefix length must equal the completed operation count.",
				nameof(readPrefix));
		}

		AttemptedCount = attemptedCount;
		CompletedCount = completedCount;
		FailedIndex = failedIndex;
		Cause = cause;
		ReadPrefix = ImmutableArray.Create(readPrefix.ToArray());
	}

	/// <summary>Gets the number of operations requested by the batch.</summary>
	public int AttemptedCount
	{
		get;
	}

	/// <summary>Gets the number of reads known to have completed in order.</summary>
	public int CompletedCount
	{
		get;
	}

	/// <summary>Gets the individual read index that failed, or <see langword="null" /> when admission or dispatch failed.</summary>
	public int? FailedIndex
	{
		get;
	}

	/// <summary>Gets the expected admission, dispatch, or target-memory failure when the batch did not complete.</summary>
	public CheatEngineFailure? Cause
	{
		get;
	}

	/// <summary>Gets an immutable copy of the successfully read values before <see cref="FailedIndex" />.</summary>
	public ImmutableArray<T> ReadPrefix
	{
		get;
	}

	/// <summary>Gets whether every requested read completed successfully.</summary>
	public bool Succeeded => Cause is null && CompletedCount == AttemptedCount;

	private static void ValidateCounts(int attemptedCount, int completedCount, int? failedIndex,
		CheatEngineFailure? cause)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attemptedCount);
		if (completedCount < 0 || completedCount > attemptedCount)
		{
			throw new ArgumentOutOfRangeException(nameof(completedCount));
		}

		if (failedIndex is { } index && (index < 0 || index >= attemptedCount || index != completedCount))
		{
			throw new ArgumentOutOfRangeException(nameof(failedIndex));
		}

		if (cause is null && (failedIndex is not null || completedCount != attemptedCount))
		{
			throw new ArgumentException("An incomplete read outcome requires a failure cause.", nameof(cause));
		}

		if (cause is not null && completedCount == attemptedCount)
		{
			throw new ArgumentException("A completed read outcome cannot contain a failure cause.", nameof(cause));
		}
	}
}
