using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Memory;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Tests.Domains;

/// <summary>
///     Resource budgets and batch outcomes of the memory client. The batch-outcome tests carry the Q33 trait: every
///     effect category (not started, partial with its completed prefix, complete, unknown) stays observable (A24-10,
///     AX06-30).
/// </summary>
public sealed class MemoryClientResourceLimitsAndBatchOutcomeTests
{
	private static readonly Address TestAddress = new(0x700000);

	[Fact]
	public void EveryDirectBudgetRejectsWorkBeforeDispatcherAdmission()
	{
		CountingDispatcher dispatcher = new();
		MemoryClient byteClient = CreateClient(dispatcher, new MemoryResourceLimits(1, 1, 1, 64, 2));

		Assert.False(byteClient.TryReadBytes(new MemoryBytesReadRequest(TestAddress, 2), out ImmutableArray<byte> bytes,
			out CheatEngineFailure readFailure, TestContext.Current.CancellationToken));
		Assert.Empty(bytes);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, readFailure.Kind);
		Assert.False(byteClient.TryWriteBytes(new MemoryBytesWriteRequest(TestAddress, [1, 2]),
			out CheatEngineFailure writeFailure, TestContext.Current.CancellationToken));
		Assert.Equal(CheatEngineFailureKind.OperationRejected, writeFailure.Kind);
		Assert.False(byteClient.TryReadString(new MemoryStringReadRequest(TestAddress, 1, MemoryStringEncoding.Utf16), out string? text,
			out CheatEngineFailure stringReadFailure, TestContext.Current.CancellationToken));
		Assert.Null(text);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, stringReadFailure.Kind);
		Assert.False(byteClient.TryWriteString(new MemoryStringWriteRequest(TestAddress, "A", 1, MemoryStringEncoding.Utf16),
			out CheatEngineFailure stringWriteFailure, TestContext.Current.CancellationToken));
		Assert.Equal(CheatEngineFailureKind.OperationRejected, stringWriteFailure.Kind);
		Assert.Equal(0, dispatcher.InvocationCount);

		MemoryClient countClient = CreateClient(dispatcher, new MemoryResourceLimits(4, 4, 4, 64, 1));
		MemoryPrimitiveBatchReadOutcome<int> countOutcome = countClient.ReadPrimitiveBatchDetailed(
			new MemoryPrimitiveBatchReadRequest<int>([TestAddress, TestAddress + 4]), TestContext.Current.CancellationToken);
		Assert.Equal(MemoryBatchWriteEffectState.NotStarted, countClient.WritePrimitiveBatchDetailed(
			new MemoryPrimitiveBatchWriteRequest<int>([
				new MemoryAddressValue<int>(TestAddress, 1), new MemoryAddressValue<int>(TestAddress + 4, 2)
			]),
			TestContext.Current.CancellationToken).EffectState);
		Assert.Equal(0, countOutcome.CompletedCount);
		Assert.Null(countOutcome.FailedIndex);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, countOutcome.Failure?.Kind);
		Assert.Equal(0, dispatcher.InvocationCount);

		MemoryClient payloadClient = CreateClient(dispatcher, new MemoryResourceLimits(8, 8, 8, sizeof(int), 2));
		MemoryPrimitiveBatchReadOutcome<int> payloadOutcome = payloadClient.ReadPrimitiveBatchDetailed(
			new MemoryPrimitiveBatchReadRequest<int>([TestAddress, TestAddress + 4]), TestContext.Current.CancellationToken);
		MemoryPrimitiveBatchWriteOutcome payloadWriteOutcome = payloadClient.WritePrimitiveBatchDetailed(
			new MemoryPrimitiveBatchWriteRequest<int>([
				new MemoryAddressValue<int>(TestAddress, 1), new MemoryAddressValue<int>(TestAddress + 4, 2)
			]),
			TestContext.Current.CancellationToken);
		Assert.Equal(0, payloadOutcome.CompletedCount);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, payloadOutcome.Failure?.Kind);
		Assert.Equal(MemoryBatchWriteEffectState.NotStarted, payloadWriteOutcome.EffectState);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, payloadWriteOutcome.Failure?.Kind);
		Assert.Equal(0, dispatcher.InvocationCount);
	}

	[Fact]
	public void DirectByteReadAndWriteBudgetsUseTheirIndependentlyConfiguredLimits()
	{
		CheatEngineFailure dispatchFailure = new(CheatEngineFailureKind.InvalidState, "Test.Dispatcher", "Rejected.");
		RejectingDispatcher dispatcher = new(dispatchFailure);
		MemoryClient client = CreateClient(dispatcher, new MemoryResourceLimits(2, 1, 32, 32, 2));

		Assert.False(client.TryReadBytes(new MemoryBytesReadRequest(TestAddress, 2), out _,
			out CheatEngineFailure readFailure, TestContext.Current.CancellationToken));
		Assert.False(client.TryWriteBytes(new MemoryBytesWriteRequest(TestAddress, [1, 2]),
			out CheatEngineFailure writeFailure, TestContext.Current.CancellationToken));

		Assert.Equal(dispatchFailure, readFailure);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, writeFailure.Kind);
		Assert.Equal("Memory.WriteBytes", writeFailure.Operation);
		Assert.Equal(1, dispatcher.InvocationCount);
	}

	[Fact]
	[Trait("Qualification", "Q33")]
	public void BatchPayloadAdmissionUsesTheActualLongElementSizeBeforeDispatch()
	{
		CountingDispatcher dispatcher = new();
		MemoryClient client = CreateClient(dispatcher, new MemoryResourceLimits(32, 32, 32, 4, 2));

		MemoryPrimitiveBatchWriteOutcome outcome = client.WritePrimitiveBatchDetailed(
			new MemoryPrimitiveBatchWriteRequest<long>([new MemoryAddressValue<long>(TestAddress, 10L)]),
			TestContext.Current.CancellationToken);

		Assert.False(outcome.IsSuccess);
		Assert.Equal(1, outcome.RequestedCount);
		Assert.Equal(0, outcome.CompletedCount);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, outcome.Failure?.Kind);
		Assert.Equal(MemoryBatchWriteEffectState.NotStarted, outcome.EffectState);
		Assert.Equal(0, dispatcher.InvocationCount);
	}

	[Fact]
	public void ExactResourceBoundariesAreAdmittedToTheDispatcher()
	{
		CheatEngineFailure expected = new(CheatEngineFailureKind.InvalidState, "Test.Dispatcher", "Rejected.");
		MemoryClient client = CreateClient(new RejectingDispatcher(expected), new MemoryResourceLimits(2, 2, 2, 8, 2));
		MemoryPrimitiveBatchReadRequest<int> reads = new([TestAddress, TestAddress + 4]);
		MemoryPrimitiveBatchWriteRequest<int> writes = new([
			new MemoryAddressValue<int>(TestAddress, 1), new MemoryAddressValue<int>(TestAddress + 4, 2)
		]);

		Assert.False(client.TryReadBytes(new MemoryBytesReadRequest(TestAddress, 2), out _,
			out CheatEngineFailure byteReadFailure,
			TestContext.Current.CancellationToken));
		Assert.False(client.TryWriteBytes(new MemoryBytesWriteRequest(TestAddress, [1, 2]),
			out CheatEngineFailure byteWriteFailure,
			TestContext.Current.CancellationToken));
		Assert.False(client.TryReadString(new MemoryStringReadRequest(TestAddress, 1, MemoryStringEncoding.Utf16), out _,
			out CheatEngineFailure stringReadFailure, TestContext.Current.CancellationToken));
		Assert.False(client.TryWriteString(new MemoryStringWriteRequest(TestAddress, "A", 1, MemoryStringEncoding.Utf16),
			out CheatEngineFailure stringWriteFailure, TestContext.Current.CancellationToken));

		Assert.Equal(expected, byteReadFailure);
		Assert.Equal(expected, byteWriteFailure);
		Assert.Equal(expected, stringReadFailure);
		Assert.Equal(expected, stringWriteFailure);
		Assert.Equal(expected, client.ReadPrimitiveBatchDetailed(reads, TestContext.Current.CancellationToken).Failure);
		Assert.Equal(expected, client.WritePrimitiveBatchDetailed(writes, TestContext.Current.CancellationToken).Failure);
	}

	[Theory]
	[Trait("Qualification", "Q33")]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	public void DetailedReadReportsEveryHostFailureIndexAndItsImmutableCompletedPrefix(int failedIndex)
	{
		BatchPort port = new()
		{
			ReadFailureIndex = failedIndex
		};
		MemoryClient client = CreateClient(new CountingDispatcher(), new MemoryResourceLimits(32, 32, 32, 32, 3), port);
		MemoryPrimitiveBatchReadOutcome<int> outcome = client.ReadPrimitiveBatchDetailed(
			new MemoryPrimitiveBatchReadRequest<int>([TestAddress, TestAddress + 4, TestAddress + 8]),
			TestContext.Current.CancellationToken);

		Assert.False(outcome.IsSuccess);
		Assert.Equal(3, outcome.RequestedCount);
		Assert.Equal(failedIndex, outcome.CompletedCount);
		Assert.Equal(failedIndex, outcome.FailedIndex);
		Assert.Equal(Enumerable.Range(0, failedIndex).Select(static index => 100 + index), outcome.Values);
		Assert.Equal(CheatEngineFailureKind.MemoryReadFailed, outcome.Failure?.Kind);
		Assert.Equal(failedIndex + 1, port.ReadInvocationCount);
	}

	[Theory]
	[Trait("Qualification", "Q33")]
	[InlineData(0, MemoryBatchWriteEffectState.NotStarted)]
	[InlineData(1, MemoryBatchWriteEffectState.Partial)]
	[InlineData(2, MemoryBatchWriteEffectState.Partial)]
	public void DetailedWriteReportsEveryHostFailureIndexWithoutRollback(int failedIndex,
		MemoryBatchWriteEffectState expectedEffectState)
	{
		BatchPort port = new()
		{
			WriteFailureIndex = failedIndex
		};
		MemoryClient client = CreateClient(new CountingDispatcher(), new MemoryResourceLimits(32, 32, 32, 32, 3), port);
		MemoryPrimitiveBatchWriteOutcome outcome = client.WritePrimitiveBatchDetailed(
			new MemoryPrimitiveBatchWriteRequest<int>([
				new MemoryAddressValue<int>(TestAddress, 10), new MemoryAddressValue<int>(TestAddress + 4, 20),
				new MemoryAddressValue<int>(TestAddress + 8, 30)
			]),
			TestContext.Current.CancellationToken);

		Assert.False(outcome.IsSuccess);
		Assert.Equal(3, outcome.RequestedCount);
		Assert.Equal(failedIndex, outcome.CompletedCount);
		Assert.Equal(failedIndex, outcome.FailedIndex);
		Assert.Equal(expectedEffectState, outcome.EffectState);
		Assert.Equal(CheatEngineFailureKind.MemoryWriteFailed, outcome.Failure?.Kind);
		Assert.Equal(Enumerable.Range(0, failedIndex).Select(static index => 10 + (index * 10)), port.CommittedValues);
		Assert.Equal(failedIndex + 1, port.WriteInvocationCount);
	}

	[Fact]
	[Trait("Qualification", "Q33")]
	public void DetailedWriteReportsCompleteEffectAndLegacyWrappersPreserveTheOldFailureShape()
	{
		BatchPort successfulPort = new();
		CountingDispatcher successfulDispatcher = new();
		MemoryClient successfulClient = CreateClient(successfulDispatcher, new MemoryResourceLimits(32, 32, 32, 32, 3),
			successfulPort);
		MemoryPrimitiveBatchWriteOutcome success = successfulClient.WritePrimitiveBatchDetailed(
			new MemoryPrimitiveBatchWriteRequest<int>([
				new MemoryAddressValue<int>(TestAddress, 10), new MemoryAddressValue<int>(TestAddress + 4, 20)
			]),
			TestContext.Current.CancellationToken);

		Assert.True(success.IsSuccess);
		Assert.Equal(2, success.CompletedCount);
		Assert.Equal(MemoryBatchWriteEffectState.Completed, success.EffectState);
		Assert.Null(success.Failure);
		Assert.Equal([10, 20], successfulPort.CommittedValues);
		Assert.Equal(2, successfulPort.WriteInvocationCount);
		Assert.Equal(1, successfulDispatcher.InvocationCount);

		BatchPort failedPort = new()
		{
			ReadFailureIndex = 1,
			WriteFailureIndex = 1
		};
		MemoryClient failedClient = CreateClient(new CountingDispatcher(), new MemoryResourceLimits(32, 32, 32, 32, 3),
			failedPort);
		MemoryPrimitiveBatchReadRequest<int> reads = new([TestAddress, TestAddress + 4]);
		MemoryPrimitiveBatchWriteRequest<int> writes = new([
			new MemoryAddressValue<int>(TestAddress, 10), new MemoryAddressValue<int>(TestAddress + 4, 20)
		]);

		Assert.False(failedClient.TryReadPrimitiveBatch(reads, out ImmutableArray<int> values,
			out CheatEngineFailure readFailure, TestContext.Current.CancellationToken));
		Assert.Empty(values);
		Assert.Equal(CheatEngineFailureKind.MemoryReadFailed, readFailure.Kind);
		Assert.False(failedClient.TryWritePrimitiveBatch(writes, out CheatEngineFailure writeFailure,
			TestContext.Current.CancellationToken));
		Assert.Equal(CheatEngineFailureKind.MemoryWriteFailed, writeFailure.Kind);
	}

	[Fact]
	[Trait("Qualification", "Q33")]
	public void DetailedBatchOutcomesReportTheirFailureAndSuccessDirectly()
	{
		BatchPort port = new()
		{
			WriteFailureIndex = 1
		};
		MemoryClient memory = CreateClient(new CountingDispatcher(), new MemoryResourceLimits(32, 32, 32, 32, 3), port);

		MemoryPrimitiveBatchReadOutcome<int> read = memory.ReadPrimitiveBatchDetailed(
			new MemoryPrimitiveBatchReadRequest<int>([TestAddress, TestAddress + 4]), TestContext.Current.CancellationToken);
		MemoryPrimitiveBatchWriteOutcome write = memory.WritePrimitiveBatchDetailed(
			new MemoryPrimitiveBatchWriteRequest<int>([
				new MemoryAddressValue<int>(TestAddress, 10), new MemoryAddressValue<int>(TestAddress + 4, 20)
			]),
			TestContext.Current.CancellationToken);

		Assert.True(read.IsSuccess);
		Assert.Null(read.Failure);
		Assert.Equal([100, 101], read.Values);
		Assert.False(write.IsSuccess);
		Assert.Equal(MemoryBatchWriteEffectState.Partial, write.EffectState);
		Assert.Equal(CheatEngineFailureKind.MemoryWriteFailed, write.Failure!.Value.Kind);
	}

	[Fact]
	[Trait("Qualification", "Q33")]
	public void DispatcherFailureLeavesWriteEffectUnknownAndDoesNotExposeAFailedIndex()
	{
		CheatEngineFailure expected = new(CheatEngineFailureKind.InvalidState, "Test.Dispatcher", "Rejected.");
		MemoryClient client =
			CreateClient(new RejectingDispatcher(expected), new MemoryResourceLimits(32, 32, 32, 32, 3));

		MemoryPrimitiveBatchWriteOutcome outcome = client.WritePrimitiveBatchDetailed(
			new MemoryPrimitiveBatchWriteRequest<int>([new MemoryAddressValue<int>(TestAddress, 10)]),
			TestContext.Current.CancellationToken);

		Assert.False(outcome.IsSuccess);
		Assert.Equal(0, outcome.CompletedCount);
		Assert.Null(outcome.FailedIndex);
		Assert.Equal(expected, outcome.Failure);
		Assert.Equal(MemoryBatchWriteEffectState.Unknown, outcome.EffectState);
	}

	[Fact]
	public void ConstructionSnapshotsLimitsAndCodecProgrammingExceptionsStillPropagate()
	{
		MemoryResourceLimits configured = new(32, 32, 32, 32, 2);
		BatchPort port = new();
		MemoryClient client = CreateClient(new CountingDispatcher(), configured, port);
		configured.MaximumBatchOperationCount = 1;

		MemoryPrimitiveBatchReadOutcome<int> outcome = client.ReadPrimitiveBatchDetailed(
			new MemoryPrimitiveBatchReadRequest<int>([TestAddress, TestAddress + 4]), TestContext.Current.CancellationToken);
		Assert.True(outcome.IsSuccess);
		Assert.Equal(2, outcome.CompletedCount);
		Assert.Throws<InvalidOperationException>(() => client.TryRead(
			new MemoryReadRequest<int>(TestAddress, new ThrowingCodec()),
			out _, out _, TestContext.Current.CancellationToken));
	}

	[Fact]
	public void DefaultLimitsAdmitANormalBatchAndCodecContextsStopOversizedUnknownBuffers()
	{
		BatchPort defaultPort = new();
		MemoryClient defaultClient = CreateClient(new CountingDispatcher(), new MemoryResourceLimits(), defaultPort);
		MemoryPrimitiveBatchReadOutcome<int> defaultOutcome = defaultClient.ReadPrimitiveBatchDetailed(
			new MemoryPrimitiveBatchReadRequest<int>([TestAddress, TestAddress + 4]), TestContext.Current.CancellationToken);

		Assert.True(defaultOutcome.IsSuccess);
		Assert.Equal([100, 101], defaultOutcome.Values);

		BatchPort constrainedPort = new();
		MemoryClient constrainedClient = CreateClient(new CountingDispatcher(),
			new MemoryResourceLimits(1, 1, 32, 32, 2),
			constrainedPort);
		bool succeeded = constrainedClient.TryRead(new MemoryReadRequest<int>(TestAddress, new OversizedReadCodec()),
			out _, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.MemoryReadFailed, failure.Kind);
		Assert.Equal(0, constrainedPort.RawReadInvocationCount);
	}

	[Fact]
	public void CustomCodecWriteAboveTheConfiguredBudgetDoesNotReachTheRawPort()
	{
		BatchPort port = new();
		MemoryClient client = CreateClient(new CountingDispatcher(), new MemoryResourceLimits(2, 1, 32, 32, 2), port);

		bool succeeded = client.TryWrite(new MemoryWriteRequest<int>(TestAddress, 42, new OversizedWriteCodec()),
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.MemoryWriteFailed, failure.Kind);
		Assert.Contains("exceeds the activation write budget of 1 bytes", failure.Message, StringComparison.Ordinal);
		Assert.Equal(0, port.RawWriteInvocationCount);
	}

	private static MemoryClient CreateClient(ICheatEngineDispatcher dispatcher, MemoryResourceLimits limits,
		IMemoryCodecContextPort? port = null)
	{
		return new MemoryClient(dispatcher, InertCoreLifetime.Create(), port ?? new BatchPort(), limits);
	}

	private sealed class BatchPort : TargetObservationDouble, IMemoryCodecContextPort
	{
		internal List<int> CommittedValues
		{
			get;
		} = [];

		internal int ReadFailureIndex
		{
			get;
			init;
		} = -1;

		internal int ReadInvocationCount
		{
			get;
			private set;
		}

		internal int RawReadInvocationCount
		{
			get;
			private set;
		}

		internal int RawWriteInvocationCount
		{
			get;
			private set;
		}

		internal int WriteFailureIndex
		{
			get;
			init;
		} = -1;

		internal int WriteInvocationCount
		{
			get;
			private set;
		}

		public bool TryReadBytes(Address address, Span<byte> destination, out int written,
			out MemoryAccessFailure failure)
		{
			RawReadInvocationCount++;
			destination.Clear();
			written = destination.Length;
			failure = MemoryAccessFailure.None;
			return true;
		}

		public bool TryReadPrimitive<T>(Address address, out T value, out MemoryAccessFailure failure)
		{
			int index = ReadInvocationCount++;
			if (index == ReadFailureIndex)
			{
				value = default!;
				failure = MemoryAccessFailure.ReadFailed;
				return false;
			}

			value = (T) (object) (100 + index);
			failure = MemoryAccessFailure.None;
			return true;
		}

		public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source, out MemoryAccessFailure failure)
		{
			RawWriteInvocationCount++;
			failure = MemoryAccessFailure.None;
			return true;
		}

		public bool TryWritePrimitive<T>(Address address, T value, out MemoryAccessFailure failure)
		{
			int index = WriteInvocationCount++;
			if (index == WriteFailureIndex)
			{
				failure = MemoryAccessFailure.WriteFailed;
				return false;
			}

			CommittedValues.Add((int) (object) value!);
			failure = MemoryAccessFailure.None;
			return true;
		}
	}

	private sealed class ThrowingCodec : IMemoryCodec<int>
	{
		public bool TryRead(IMemoryReadContext context, Address address, out int value, out CheatEngineFailure failure)
		{
			failure = default;
			throw new InvalidOperationException("Codec programming failures must not be mapped.");
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in int value, out CheatEngineFailure failure)
		{
			failure = default;
			throw new InvalidOperationException("Codec programming failures must not be mapped.");
		}
	}

	private sealed class OversizedReadCodec : IMemoryCodec<int>
	{
		public bool TryRead(IMemoryReadContext context, Address address, out int value, out CheatEngineFailure failure)
		{
			failure = default;
			Span<byte> destination = stackalloc byte[2];
			bool succeeded = context.TryReadBytes(address, destination, out _);
			value = 0;
			return succeeded;
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in int value, out CheatEngineFailure failure)
		{
			failure = default;
			return false;
		}
	}

	private sealed class OversizedWriteCodec : IMemoryCodec<int>
	{
		public bool TryRead(IMemoryReadContext context, Address address, out int value, out CheatEngineFailure failure)
		{
			failure = default;
			value = default;
			return false;
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in int value, out CheatEngineFailure failure)
		{
			failure = default;
			Span<byte> source = stackalloc byte[2];
			return context.TryWriteBytes(address, source, out _);
		}
	}

	private sealed class CountingDispatcher : ICheatEngineDispatcher
	{
		internal int InvocationCount
		{
			get;
			private set;
		}

		public bool IsMainThread => true;

		public bool TryInvoke(Action callback, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			InvocationCount++;
			callback();
			failure = default;
			return true;
		}

		public bool TryInvoke<T>(Func<T> callback, [MaybeNullWhen(false)] out T result,
			out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			InvocationCount++;
			result = callback();
			failure = default;
			return true;
		}

		public void Invoke(Action callback, CancellationToken cancellationToken = default)
		{
			_ = TryInvoke(callback, out _, cancellationToken);
		}

		public T Invoke<T>(Func<T> callback, CancellationToken cancellationToken = default)
		{
			_ = TryInvoke(callback, out T? result, out _, cancellationToken);
			return result!;
		}
	}

	private sealed class RejectingDispatcher(CheatEngineFailure failure) : ICheatEngineDispatcher
	{
		private readonly CheatEngineFailure _failure = failure;

		internal int InvocationCount
		{
			get;
			private set;
		}

		public bool IsMainThread => false;

		public bool TryInvoke(Action callback, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			InvocationCount++;
			failure = _failure;
			return false;
		}

		public bool TryInvoke<T>(Func<T> callback, [MaybeNullWhen(false)] out T result,
			out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			InvocationCount++;
			result = default;
			failure = _failure;
			return false;
		}

		public void Invoke(Action callback, CancellationToken cancellationToken = default)
		{
			_ = TryInvoke(callback, out _, cancellationToken);
		}

		public T Invoke<T>(Func<T> callback, CancellationToken cancellationToken = default)
		{
			_ = TryInvoke(callback, out T? result, out _, cancellationToken);
			return result!;
		}
	}
}
