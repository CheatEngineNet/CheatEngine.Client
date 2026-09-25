using System.Collections.Immutable;

using CheatEngine.Client.Results;

namespace CheatEngine.Client.Memory;

/// <summary>Describes the sequential outcome of one primitive batch read.</summary>
/// <typeparam name="T">
///     The homogeneous primitive value type, one of the types <see cref="IMemoryClient" /> supports.
/// </typeparam>
public sealed class MemoryPrimitiveBatchReadOutcome<T>
	where T : unmanaged
{
	/// <summary>Creates a read outcome and copies the values read in order.</summary>
	/// <param name="requestedCount">The number of reads the batch requested.</param>
	/// <param name="values">The values read in order before the batch stopped: the confirmed prefix.</param>
	/// <param name="failedIndex">The read that failed, or <see langword="null" /> when admission or dispatch failed.</param>
	/// <param name="failure">The failure when the batch did not complete; <see langword="null" /> otherwise.</param>
	public MemoryPrimitiveBatchReadOutcome(int requestedCount, ReadOnlySpan<T> values, int? failedIndex,
		CheatEngineFailure? failure)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedCount);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(values.Length, requestedCount, nameof(values));
		ValidateFailure(requestedCount, values.Length, failedIndex, failure);
		RequestedCount = requestedCount;
		FailedIndex = failedIndex;
		Failure = failure;
		Values = ImmutableArray.Create(values);
	}

	/// <summary>Gets the number of operations requested by the batch.</summary>
	public int RequestedCount
	{
		get;
	}

	/// <summary>Gets the number of reads known to have completed in order: the length of <see cref="Values" />.</summary>
	public int CompletedCount => Values.Length;

	/// <summary>Gets the individual read index that failed, or <see langword="null" /> when admission or dispatch failed.</summary>
	public int? FailedIndex
	{
		get;
	}

	/// <summary>Gets the expected admission, dispatch, or target-memory failure when the batch did not complete.</summary>
	public CheatEngineFailure? Failure
	{
		get;
	}

	/// <summary>
	///     Gets an immutable copy of the values read in order before <see cref="FailedIndex" />: the confirmed prefix, and
	///     every value on success, as the <c>values</c> output of <see cref="IMemoryClient.TryReadPrimitiveBatch{T}" />.
	/// </summary>
	public ImmutableArray<T> Values
	{
		get;
	}

	/// <summary>Gets whether every requested read completed successfully.</summary>
	public bool IsSuccess => Failure is null && CompletedCount == RequestedCount;

	private static void ValidateFailure(int requestedCount, int completedCount, int? failedIndex,
		CheatEngineFailure? failure)
	{
		if (failedIndex is { } index && (index < 0 || index >= requestedCount || index != completedCount))
		{
			throw new ArgumentOutOfRangeException(nameof(failedIndex));
		}

		if (failure is null && (failedIndex is not null || completedCount != requestedCount))
		{
			throw new ArgumentException("An incomplete read outcome requires a failure.", nameof(failure));
		}

		if (failure is not null && completedCount == requestedCount)
		{
			throw new ArgumentException("A completed read outcome cannot contain a failure.", nameof(failure));
		}
	}
}
