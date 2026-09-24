using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Tests.Domains;

/// <summary>
///     Resource budgets and batch outcomes of the memory client. The batch-outcome tests carry the Q33 trait: every
///     effect category (not started, partial with its completed prefix, complete, unknown) stays observable (A24-10,
///     AX06-30).
/// </summary>
public sealed class MemoryClientResourceLimitsAndBatchOutcomeTests
{
	private static readonly Address _address = new(0x700000);

	[Fact]
	public void EveryDirectBudgetRejectsWorkBeforeDispatcherAdmission()
	{
		CountingDispatcher dispatcher = new();
		MemoryClient byteClient = CreateClient(dispatcher, new MemoryResourceLimits(1, 1, 1, 64, 2));

		Assert.False(byteClient.TryReadBytes(new MemoryBytesReadRequest(_address, 2), out ImmutableArray<byte> bytes,
			out CheatEngineFailure readFailure, TestContext.Current.CancellationToken));
		Assert.Empty(bytes);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, readFailure.Kind);
		Assert.False(byteClient.TryWriteBytes(new MemoryBytesWriteRequest(_address, [1, 2]),
			out CheatEngineFailure writeFailure, TestContext.Current.CancellationToken));
		Assert.Equal(CheatEngineFailureKind.OperationRejected, writeFailure.Kind);
		Assert.False(byteClient.TryReadString(new MemoryStringReadRequest(_address, 1, true), out string? text,
			out CheatEngineFailure stringReadFailure, TestContext.Current.CancellationToken));
		Assert.Null(text);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, stringReadFailure.Kind);
		Assert.False(byteClient.TryWriteString(new MemoryStringWriteRequest(_address, "A", true),
			out CheatEngineFailure stringWriteFailure, TestContext.Current.CancellationToken));
		Assert.Equal(CheatEngineFailureKind.OperationRejected, stringWriteFailure.Kind);
		Assert.Equal(0, dispatcher.InvocationCount);

		MemoryClient countClient = CreateClient(dispatcher, new MemoryResourceLimits(4, 4, 4, 64, 1));
		MemoryPrimitiveBatchReadOutcome<int> countOutcome = countClient.ReadPrimitiveBatchDetailed(
			new MemoryPrimitiveBatchReadRequest<int>([_address, _address + 4]), TestContext.Current.CancellationToken);
		Assert.Equal(MemoryBatchWriteEffectState.NotStarted, countClient.WritePrimitiveBatchDetailed(
			new MemoryPrimitiveBatchWriteRequest<int>([
				new MemoryAddressValue<int>(_address, 1), new MemoryAddressValue<int>(_address + 4, 2)
			]),
			TestContext.Current.CancellationToken).EffectState);
		Assert.Equal(0, countOutcome.CompletedCount);
		Assert.Null(countOutcome.FailedIndex);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, countOutcome.Cause?.Kind);
		Assert.Equal(0, dispatcher.InvocationCount);

		MemoryClient payloadClient = CreateClient(dispatcher, new MemoryResourceLimits(8, 8, 8, sizeof(int), 2));
		MemoryPrimitiveBatchReadOutcome<int> payloadOutcome = payloadClient.ReadPrimitiveBatchDetailed(
			new MemoryPrimitiveBatchReadRequest<int>([_address, _address + 4]), TestContext.Current.CancellationToken);
		MemoryPrimitiveBatchWriteOutcome payloadWriteOutcome = payloadClient.WritePrimitiveBatchDetailed(
			new MemoryPrimitiveBatchWriteRequest<int>([
				new MemoryAddressValue<int>(_address, 1), new MemoryAddressValue<int>(_address + 4, 2)
			]),
			TestContext.Current.CancellationToken);
		Assert.Equal(0, payloadOutcome.CompletedCount);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, payloadOutcome.Cause?.Kind);
		Assert.Equal(MemoryBatchWriteEffectState.NotStarted, payloadWriteOutcome.EffectState);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, payloadWriteOutcome.Cause?.Kind);
		Assert.Equal(0, dispatcher.InvocationCount);
	}

	[Fact]
	public void DirectByteReadAndWriteBudgetsUseTheirIndependentlyConfiguredLimits()
	{
		CheatEngineFailure dispatchFailure = new(CheatEngineFailureKind.InvalidState, "Test.Dispatcher", "Rejected.");
		RejectingDispatcher dispatcher = new(dispatchFailure);
		MemoryClient client = CreateClient(dispatcher, new MemoryResourceLimits(2, 1, 32, 32, 2));

		Assert.False(client.TryReadBytes(new MemoryBytesReadRequest(_address, 2), out _,
			out CheatEngineFailure readFailure, TestContext.Current.CancellationToken));
		Assert.False(client.TryWriteBytes(new MemoryBytesWriteRequest(_address, [1, 2]),
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
			new MemoryPrimitiveBatchWriteRequest<long>([new MemoryAddressValue<long>(_address, 10L)]),
			TestContext.Current.CancellationToken);

		Assert.False(outcome.Succeeded);
		Assert.Equal(1, outcome.AttemptedCount);
		Assert.Equal(0, outcome.CompletedCount);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, outcome.Cause?.Kind);
		Assert.Equal(MemoryBatchWriteEffectState.NotStarted, outcome.EffectState);
		Assert.Equal(0, dispatcher.InvocationCount);
	}

	[Fact]
	public void ExactResourceBoundariesAreAdmittedToTheDispatcher()
	{
		CheatEngineFailure expected = new(CheatEngineFailureKind.InvalidState, "Test.Dispatcher", "Rejected.");
		MemoryClient client = CreateClient(new RejectingDispatcher(expected), new MemoryResourceLimits(2, 2, 2, 8, 2));
		MemoryPrimitiveBatchReadRequest<int> reads = new([_address, _address + 4]);
		MemoryPrimitiveBatchWriteRequest<int> writes = new([
			new MemoryAddressValue<int>(_address, 1), new MemoryAddressValue<int>(_address + 4, 2)
		]);

		Assert.False(client.TryReadBytes(new MemoryBytesReadRequest(_address, 2), out _,
			out CheatEngineFailure byteReadFailure,
			TestContext.Current.CancellationToken));
		Assert.False(client.TryWriteBytes(new MemoryBytesWriteRequest(_address, [1, 2]),
			out CheatEngineFailure byteWriteFailure,
			TestContext.Current.CancellationToken));
		Assert.False(client.TryReadString(new MemoryStringReadRequest(_address, 1, true), out _,
			out CheatEngineFailure stringReadFailure, TestContext.Current.CancellationToken));
		Assert.False(client.TryWriteString(new MemoryStringWriteRequest(_address, "A", true),
			out CheatEngineFailure stringWriteFailure, TestContext.Current.CancellationToken));

		Assert.Equal(expected, byteReadFailure);
		Assert.Equal(expected, byteWriteFailure);
		Assert.Equal(expected, stringReadFailure);
		Assert.Equal(expected, stringWriteFailure);
		Assert.Equal(expected, client.ReadPrimitiveBatchDetailed(reads, TestContext.Current.CancellationToken).Cause);
		Assert.Equal(expected, client.WritePrimitiveBatchDetailed(writes, TestContext.Current.CancellationToken).Cause);
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
			new MemoryPrimitiveBatchReadRequest<int>([_address, _address + 4, _address + 8]),
			TestContext.Current.CancellationToken);

		Assert.False(outcome.Succeeded);
		Assert.Equal(3, outcome.AttemptedCount);
		Assert.Equal(failedIndex, outcome.CompletedCount);
		Assert.Equal(failedIndex, outcome.FailedIndex);
		Assert.Equal(Enumerable.Range(0, failedIndex).Select(static index => 100 + index), outcome.ReadPrefix);
		Assert.Equal(CheatEngineFailureKind.MemoryReadFailed, outcome.Cause?.Kind);
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
				new MemoryAddressValue<int>(_address, 10), new MemoryAddressValue<int>(_address + 4, 20),
				new MemoryAddressValue<int>(_address + 8, 30)
			]),
			TestContext.Current.CancellationToken);

		Assert.False(outcome.Succeeded);
		Assert.Equal(3, outcome.AttemptedCount);
		Assert.Equal(failedIndex, outcome.CompletedCount);
		Assert.Equal(failedIndex, outcome.FailedIndex);
		Assert.Equal(expectedEffectState, outcome.EffectState);
		Assert.Equal(CheatEngineFailureKind.MemoryWriteFailed, outcome.Cause?.Kind);
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
				new MemoryAddressValue<int>(_address, 10), new MemoryAddressValue<int>(_address + 4, 20)
			]),
			TestContext.Current.CancellationToken);

		Assert.True(success.Succeeded);
		Assert.Equal(2, success.CompletedCount);
		Assert.Equal(MemoryBatchWriteEffectState.Complete, success.EffectState);
		Assert.Null(success.Cause);
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
		MemoryPrimitiveBatchReadRequest<int> reads = new([_address, _address + 4]);
		MemoryPrimitiveBatchWriteRequest<int> writes = new([
			new MemoryAddressValue<int>(_address, 10), new MemoryAddressValue<int>(_address + 4, 20)
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
	public void DispatcherFailureLeavesWriteEffectUnknownAndDoesNotExposeAFailedIndex()
	{
		CheatEngineFailure expected = new(CheatEngineFailureKind.InvalidState, "Test.Dispatcher", "Rejected.");
		MemoryClient client =
			CreateClient(new RejectingDispatcher(expected), new MemoryResourceLimits(32, 32, 32, 32, 3));

		MemoryPrimitiveBatchWriteOutcome outcome = client.WritePrimitiveBatchDetailed(
			new MemoryPrimitiveBatchWriteRequest<int>([new MemoryAddressValue<int>(_address, 10)]),
			TestContext.Current.CancellationToken);

		Assert.False(outcome.Succeeded);
		Assert.Equal(0, outcome.CompletedCount);
		Assert.Null(outcome.FailedIndex);
		Assert.Equal(expected, outcome.Cause);
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
			new MemoryPrimitiveBatchReadRequest<int>([_address, _address + 4]), TestContext.Current.CancellationToken);
		Assert.True(outcome.Succeeded);
		Assert.Equal(2, outcome.CompletedCount);
		Assert.Throws<InvalidOperationException>(() => client.TryRead(
			new MemoryReadRequest<int>(_address, new ThrowingCodec()),
			out _, out _, TestContext.Current.CancellationToken));
	}

	[Fact]
	public void DefaultLimitsAdmitANormalBatchAndCodecContextsStopOversizedUnknownBuffers()
	{
		BatchPort defaultPort = new();
		MemoryClient defaultClient = CreateClient(new CountingDispatcher(), new MemoryResourceLimits(), defaultPort);
		MemoryPrimitiveBatchReadOutcome<int> defaultOutcome = defaultClient.ReadPrimitiveBatchDetailed(
			new MemoryPrimitiveBatchReadRequest<int>([_address, _address + 4]), TestContext.Current.CancellationToken);

		Assert.True(defaultOutcome.Succeeded);
		Assert.Equal([100, 101], defaultOutcome.ReadPrefix);

		BatchPort constrainedPort = new();
		MemoryClient constrainedClient = CreateClient(new CountingDispatcher(),
			new MemoryResourceLimits(1, 1, 32, 32, 2),
			constrainedPort);
		bool succeeded = constrainedClient.TryRead(new MemoryReadRequest<int>(_address, new OversizedReadCodec()),
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

		bool succeeded = client.TryWrite(new MemoryWriteRequest<int>(_address, 42, new OversizedWriteCodec()),
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

	private sealed class BatchPort : IMemoryCodecContextPort
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

		public long GetOpenedProcessId()
		{
			return 42;
		}

		public bool TargetIs64Bit()
		{
			return true;
		}

		public bool TargetIsX86()
		{
			return true;
		}

		public bool TargetIsArm()
		{
			return false;
		}

		public int GetConfiguredPointerSize()
		{
			return sizeof(ulong);
		}

		public bool TryReadBytes(Address address, Span<byte> destination, out string? failure)
		{
			RawReadInvocationCount++;
			destination.Clear();
			failure = null;
			return true;
		}

		public bool TryReadPrimitive<T>(Address address, out T value, out string? failure)
		{
			int index = ReadInvocationCount++;
			if (index == ReadFailureIndex)
			{
				value = default!;
				failure = $"Read failure {index}.";
				return false;
			}

			value = (T) (object) (100 + index);
			failure = null;
			return true;
		}

		public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source, out string? failure)
		{
			RawWriteInvocationCount++;
			failure = null;
			return true;
		}

		public bool TryWritePrimitive<T>(Address address, T value, out string? failure)
		{
			int index = WriteInvocationCount++;
			if (index == WriteFailureIndex)
			{
				failure = $"Write failure {index}.";
				return false;
			}

			CommittedValues.Add((int) (object) value!);
			failure = null;
			return true;
		}
	}

	private sealed class ThrowingCodec : IMemoryCodec<int>
	{
		public bool TryRead(IMemoryReadContext context, Address address, out int value)
		{
			throw new InvalidOperationException("Codec programming failures must not be mapped.");
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in int value)
		{
			throw new InvalidOperationException("Codec programming failures must not be mapped.");
		}
	}

	private sealed class OversizedReadCodec : IMemoryCodec<int>
	{
		public bool TryRead(IMemoryReadContext context, Address address, out int value)
		{
			Span<byte> destination = stackalloc byte[2];
			bool succeeded = context.TryReadBytes(address, destination);
			value = 0;
			return succeeded;
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in int value)
		{
			return false;
		}
	}

	private sealed class OversizedWriteCodec : IMemoryCodec<int>
	{
		public bool TryRead(IMemoryReadContext context, Address address, out int value)
		{
			value = default;
			return false;
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in int value)
		{
			Span<byte> source = stackalloc byte[2];
			return context.TryWriteBytes(address, source);
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
