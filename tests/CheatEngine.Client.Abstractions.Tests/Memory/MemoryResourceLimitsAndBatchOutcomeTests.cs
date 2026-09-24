using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Abstractions.Tests.Memory;

public sealed class MemoryResourceLimitsAndBatchOutcomeTests
{
	[Fact]
	public void DefaultLimitsPreserveTheDocumentedSafeBudgetsAndBatchHardCap()
	{
		MemoryResourceLimits limits = new();

		Assert.Equal(MemoryResourceLimits.DefaultMaximumReadBytes, limits.MaximumReadBytes);
		Assert.Equal(MemoryResourceLimits.DefaultMaximumWriteBytes, limits.MaximumWriteBytes);
		Assert.Equal(MemoryResourceLimits.DefaultMaximumStringBytes, limits.MaximumStringBytes);
		Assert.Equal(MemoryResourceLimits.DefaultMaximumBatchPayloadBytes, limits.MaximumBatchPayloadBytes);
		Assert.Equal(MemoryBatchLimits.MaximumOperations, limits.MaximumBatchOperationCount);
	}

	[Theory]
	[InlineData(0, 1, 1, 1, 1)]
	[InlineData(1, 0, 1, 1, 1)]
	[InlineData(1, 1, 0, 1, 1)]
	[InlineData(1, 1, 1, 0, 1)]
	[InlineData(1, 1, 1, 1, 0)]
	[InlineData(1, 1, 1, 1, MemoryBatchLimits.MaximumOperations + 1)]
	public void ExplicitLimitsRejectEveryInvalidBudget(int readBytes, int writeBytes, int stringBytes,
		int batchPayloadBytes, int batchOperationCount)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryResourceLimits(readBytes, writeBytes, stringBytes,
			batchPayloadBytes, batchOperationCount));
	}

	[Fact]
	public void SnapshotIsIndependentAndValidatesConfigurationBoundProperties()
	{
		MemoryResourceLimits configured = new(33, 34, 35, 36, 37);
		MemoryResourceLimits snapshot = configured.CreateSnapshot();
		configured.MaximumReadBytes = 1;
		configured.MaximumWriteBytes = 2;
		configured.MaximumStringBytes = 3;
		configured.MaximumBatchPayloadBytes = 4;
		configured.MaximumBatchOperationCount = 5;

		Assert.Equal(33, snapshot.MaximumReadBytes);
		Assert.Equal(34, snapshot.MaximumWriteBytes);
		Assert.Equal(35, snapshot.MaximumStringBytes);
		Assert.Equal(36, snapshot.MaximumBatchPayloadBytes);
		Assert.Equal(37, snapshot.MaximumBatchOperationCount);
		Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryResourceLimits
		{
			MaximumBatchOperationCount = MemoryBatchLimits.MaximumOperations + 1
		}.CreateSnapshot());
	}

	[Fact]
	public void ReadOutcomeCopiesItsCompletedPrefixAndExposesItsFailureDetails()
	{
		int[] prefix = [11, 22];
		CheatEngineFailure cause = new(CheatEngineFailureKind.MemoryReadFailed, "Memory.ReadPrimitiveBatch", "Denied.");

		MemoryPrimitiveBatchReadOutcome<int> outcome = new(3, 2, 2, cause, prefix);
		prefix[0] = 99;

		Assert.Equal(3, outcome.AttemptedCount);
		Assert.Equal(2, outcome.CompletedCount);
		Assert.Equal(2, outcome.FailedIndex);
		Assert.Equal(cause, outcome.Cause);
		Assert.Equal([11, 22], outcome.ReadPrefix);
		Assert.False(outcome.Succeeded);
	}

	[Fact]
	public void WriteOutcomeDistinguishesPartialAndUnknownEffects()
	{
		CheatEngineFailure cause = new(CheatEngineFailureKind.MemoryWriteFailed, "Memory.WritePrimitiveBatch",
			"Denied.");
		MemoryPrimitiveBatchWriteOutcome partial = new(3, 2, 2, cause, MemoryBatchWriteEffectState.Partial);
		MemoryPrimitiveBatchWriteOutcome unknown = new(3, 0, null, cause, MemoryBatchWriteEffectState.Unknown);

		Assert.Equal(MemoryBatchWriteEffectState.Partial, partial.EffectState);
		Assert.Equal(2, partial.CompletedCount);
		Assert.Equal(MemoryBatchWriteEffectState.Unknown, unknown.EffectState);
		Assert.Null(unknown.FailedIndex);
		Assert.False(partial.Succeeded);
	}

	[Fact]
	public void ByteReadOutcomeSeparatesAConfirmedPrefixFromACompleteRead()
	{
		CheatEngineFailure failure = new(CheatEngineFailureKind.MemoryReadFailed, "Memory.ReadBytes", "Partial.");

		MemoryBytesReadOutcome partial = new(4, [0x01, 0x02], failure);
		MemoryBytesReadOutcome complete = new(2, [0x0A, 0x0B], null);
		MemoryBytesReadOutcome refused = new(4, default, failure);

		Assert.Equal((4, 2, false, false), (partial.RequestedLength, partial.ConfirmedLength, partial.IsComplete,
			partial.IsSuccess));
		Assert.Equal([0x01, 0x02], partial.Bytes);
		Assert.Equal(failure, partial.Failure);
		Assert.Equal((2, true, true), (complete.ConfirmedLength, complete.IsComplete, complete.IsSuccess));
		Assert.Null(complete.Failure);
		Assert.True(refused.Bytes.IsEmpty);
		Assert.Equal(0, refused.ConfirmedLength);
	}

	[Fact]
	public void ByteReadOutcomeRejectsAnInconsistentPrefixOrFailure()
	{
		CheatEngineFailure failure = new(CheatEngineFailureKind.MemoryReadFailed, "Memory.ReadBytes", "Partial.");

		Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryBytesReadOutcome(0, [], failure));
		Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryBytesReadOutcome(1, [0x01, 0x02], failure));
		Assert.Throws<ArgumentException>(() => new MemoryBytesReadOutcome(2, [0x01], null));
		Assert.Throws<ArgumentException>(() => new MemoryBytesReadOutcome(1, [0x01], failure));
	}
}
