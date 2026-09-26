using System.Reflection;

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
		Assert.Equal(MemoryBatchLimits.MaximumOperationCount, limits.MaximumBatchOperationCount);
	}

	[Theory]
	[InlineData(0, 1, 1, 1, 1)]
	[InlineData(1, 0, 1, 1, 1)]
	[InlineData(1, 1, 0, 1, 1)]
	[InlineData(1, 1, 1, 0, 1)]
	[InlineData(1, 1, 1, 1, 0)]
	[InlineData(1, 1, 1, 1, MemoryBatchLimits.MaximumOperationCount + 1)]
	public void ExplicitLimitsRejectEveryInvalidBudget(int readBytes, int writeBytes, int stringBytes,
		int batchPayloadBytes, int batchOperationCount)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryResourceLimits(readBytes, writeBytes, stringBytes,
			batchPayloadBytes, batchOperationCount));
	}

	[Fact]
	public void ReadOutcomeCopiesItsCompletedPrefixAndExposesItsFailureDetails()
	{
		int[] prefix = [11, 22];
		CheatEngineFailure cause = new(CheatEngineFailureKind.MemoryReadFailed, "Memory.ReadPrimitiveBatch", "Denied.");

		MemoryPrimitiveBatchReadOutcome<int> outcome = new(3, prefix, 2, cause);
		prefix[0] = 99;

		Assert.Equal(3, outcome.RequestedCount);
		Assert.Equal(2, outcome.CompletedCount);
		Assert.Equal(2, outcome.FailedIndex);
		Assert.Equal(cause, outcome.Failure);
		Assert.Equal([11, 22], outcome.Values);
		Assert.False(outcome.IsSuccess);
	}

	[Fact]
	public void ReadOutcomeNamesTheArgumentThatContradictsTheRequest()
	{
		CheatEngineFailure cause = new(CheatEngineFailureKind.MemoryReadFailed, "Memory.ReadPrimitiveBatch", "Denied.");

		ArgumentOutOfRangeException noRequest = Assert.Throws<ArgumentOutOfRangeException>(() =>
			new MemoryPrimitiveBatchReadOutcome<int>(0, [], null, cause));
		ArgumentOutOfRangeException tooManyValues = Assert.Throws<ArgumentOutOfRangeException>(() =>
			new MemoryPrimitiveBatchReadOutcome<int>(1, [11, 22], null, null));
		ArgumentOutOfRangeException misplacedIndex = Assert.Throws<ArgumentOutOfRangeException>(() =>
			new MemoryPrimitiveBatchReadOutcome<int>(3, [11], 2, cause));
		ArgumentException missingFailure = Assert.Throws<ArgumentException>(() =>
			new MemoryPrimitiveBatchReadOutcome<int>(2, [11], null, null));

		Assert.Equal("requestedCount", noRequest.ParamName);
		Assert.Equal("values", tooManyValues.ParamName);
		Assert.Equal("failedIndex", misplacedIndex.ParamName);
		Assert.Equal("failure", missingFailure.ParamName);
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
		Assert.False(partial.IsSuccess);
	}

	[Fact]
	public void AnUnassignedWriteEffectStateIsUnknownAndNeverAnEstablishedEffect()
	{
		Assert.Equal(MemoryBatchWriteEffectState.Unknown, default(MemoryBatchWriteEffectState));
		Assert.Equal(0, (int) MemoryBatchWriteEffectState.Unknown);
		Assert.Throws<ArgumentException>(() => new MemoryPrimitiveBatchWriteOutcome(1, 1, null, null, default));
	}

	/// <summary>A5: every primitive member of <see cref="IMemoryClient" /> constrains its type to <c>unmanaged</c>.</summary>
	[Fact]
	public void EveryPrimitiveMemberConstrainsItsTypeToUnmanaged()
	{
		MethodInfo[] primitives =
		[
			.. typeof(IMemoryClient).GetMethods()
				.Where(static method => method.Name.Contains("Primitive", StringComparison.Ordinal))
		];

		Assert.Equal(10, primitives.Length);
		Assert.All(primitives, static method =>
		{
			Type parameter = Assert.Single(method.GetGenericArguments());
			Assert.True(parameter.GenericParameterAttributes.HasFlag(
				GenericParameterAttributes.NotNullableValueTypeConstraint), method.Name);
			Assert.Contains(parameter.GetCustomAttributesData(), static attribute =>
				attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsUnmanagedAttribute");
		});
	}

	[Fact]
	public void ByteReadOutcomeSeparatesAConfirmedPrefixFromACompleteRead()
	{
		CheatEngineFailure failure = new(CheatEngineFailureKind.MemoryReadFailed, "Memory.ReadBytes", "Partial.");

		MemoryBytesReadOutcome partial = new(4, [0x01, 0x02], failure);
		MemoryBytesReadOutcome complete = new(2, [0x0A, 0x0B], null);
		MemoryBytesReadOutcome refused = new(4, default, failure);

		Assert.Equal((4, 2, false), (partial.RequestedLength, partial.ConfirmedLength, partial.IsSuccess));
		Assert.Equal([0x01, 0x02], partial.Bytes);
		Assert.Equal(failure, partial.Failure);
		Assert.Equal((2, 2, true), (complete.RequestedLength, complete.ConfirmedLength, complete.IsSuccess));
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
