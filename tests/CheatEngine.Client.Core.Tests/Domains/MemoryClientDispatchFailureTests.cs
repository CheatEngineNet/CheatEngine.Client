using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class MemoryClientDispatchFailureTests
{
	private static readonly Address Address = new(0x1234);

	[Fact]
	public void PrimitiveAndCodecReadsPreserveTheDispatcherFailureWithoutExecutingTargetMemory()
	{
		CheatEngineFailure expected = Failure("Test.Read");
		MemoryClient client = new(new RejectingDispatcher(expected), InertCoreLifetime.Create());
		MemoryReadRequest<int> request = new(Address, new NeverUsedCodec());

		bool primitiveSucceeded = client.TryReadPrimitive(Address, out int primitive,
			out CheatEngineFailure primitiveFailure,
			TestContext.Current.CancellationToken);
		bool codecSucceeded = client.TryRead(request, out int value, out CheatEngineFailure codecFailure,
			TestContext.Current.CancellationToken);

		Assert.False(primitiveSucceeded);
		Assert.Equal(0, primitive);
		Assert.Equal(expected, primitiveFailure);
		Assert.False(codecSucceeded);
		Assert.Equal(0, value);
		Assert.Equal(expected, codecFailure);
	}

	[Fact]
	public void PrimitiveAndCodecWritesPreserveTheDispatcherFailureWithoutExecutingTargetMemory()
	{
		CheatEngineFailure expected = Failure("Test.Write");
		MemoryClient client = new(new RejectingDispatcher(expected), InertCoreLifetime.Create());
		MemoryWriteRequest<int> request = new(Address, 42, new NeverUsedCodec());

		bool primitiveSucceeded = client.TryWritePrimitive(Address, 42, out CheatEngineFailure primitiveFailure,
			TestContext.Current.CancellationToken);
		bool codecSucceeded = client.TryWrite(request, out CheatEngineFailure codecFailure,
			TestContext.Current.CancellationToken);

		Assert.False(primitiveSucceeded);
		Assert.Equal(expected, primitiveFailure);
		Assert.False(codecSucceeded);
		Assert.Equal(expected, codecFailure);
	}

	[Fact]
	public void ByteAndStringOperationsPreserveTheDispatcherFailureAndEmptyReadResults()
	{
		CheatEngineFailure expected = Failure("Test.Copy");
		MemoryClient client = new(new RejectingDispatcher(expected), InertCoreLifetime.Create());
		MemoryBytesReadRequest byteRead = new(Address, 2);
		MemoryBytesWriteRequest byteWrite = new(Address, [0x10, 0x20]);
		MemoryStringReadRequest stringRead = new(Address, 12, true);
		MemoryStringWriteRequest stringWrite = new(Address, "health", true);

		Assert.False(client.TryReadBytes(byteRead, out ImmutableArray<byte> bytes,
			out CheatEngineFailure byteReadFailure,
			TestContext.Current.CancellationToken));
		Assert.True(bytes.IsEmpty);
		Assert.Equal(expected, byteReadFailure);
		Assert.False(client.TryWriteBytes(byteWrite, out CheatEngineFailure byteWriteFailure,
			TestContext.Current.CancellationToken));
		Assert.Equal(expected, byteWriteFailure);
		Assert.False(client.TryReadString(stringRead, out string? text, out CheatEngineFailure stringReadFailure,
			TestContext.Current.CancellationToken));
		Assert.Null(text);
		Assert.Equal(expected, stringReadFailure);
		Assert.False(client.TryWriteString(stringWrite, out CheatEngineFailure stringWriteFailure,
			TestContext.Current.CancellationToken));
		Assert.Equal(expected, stringWriteFailure);
	}

	[Fact]
	public void PointerResolutionPreservesTheDispatcherFailureAndDefaultAddress()
	{
		CheatEngineFailure expected = Failure("Test.Pointer");
		MemoryClient client = new(new RejectingDispatcher(expected), InertCoreLifetime.Create());
		PointerChainRequest request = new(Address, [4L, 8L]);

		bool succeeded = client.TryResolvePointerChain(request, out Address actual, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, actual);
		Assert.Equal(expected, failure);
	}

	[Fact]
	public void PrimitiveBatchesPreserveTheDispatcherFailureWithoutAdmittingAnyTargetOperation()
	{
		CheatEngineFailure expected = Failure("Test.Batch");
		MemoryClient client = new(new RejectingDispatcher(expected), InertCoreLifetime.Create());
		MemoryPrimitiveBatchReadRequest<int> reads = new([Address, Address + 4]);
		MemoryPrimitiveBatchWriteRequest<int> writes = new([new MemoryAddressValue<int>(Address, 12)]);

		Assert.False(client.TryReadPrimitiveBatch(reads, out ImmutableArray<int> values,
			out CheatEngineFailure readFailure,
			TestContext.Current.CancellationToken));
		Assert.True(values.IsEmpty);
		Assert.Equal(expected, readFailure);
		Assert.False(client.TryWritePrimitiveBatch(writes, out CheatEngineFailure writeFailure,
			TestContext.Current.CancellationToken));
		Assert.Equal(expected, writeFailure);
	}

	[Fact]
	public void DefaultPrimitiveBatchesAreRejectedBeforeDispatch()
	{
		MemoryClient client = new(new RejectingDispatcher(Failure("Test.ShouldNotDispatch")),
			InertCoreLifetime.Create());

		Assert.Throws<ArgumentException>(() => client.TryReadPrimitiveBatch(default, out ImmutableArray<int> _, out _,
			TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentException>(() => client.TryWritePrimitiveBatch(
			default(MemoryPrimitiveBatchWriteRequest<int>), out _,
			TestContext.Current.CancellationToken));
	}

	[Fact]
	public void UnsupportedPrimitiveTypesReturnTheSpecificUnsupportedFailureWithoutAccessingTheHost()
	{
		MemoryClient client = new(new InlineDispatcher(), InertCoreLifetime.Create());

		bool readSucceeded = client.TryReadPrimitive(Address, out DateTime readValue,
			out CheatEngineFailure readFailure, TestContext.Current.CancellationToken);
		bool writeSucceeded = client.TryWritePrimitive(Address, DateTime.UnixEpoch, out CheatEngineFailure writeFailure,
			TestContext.Current.CancellationToken);

		Assert.False(readSucceeded);
		Assert.Equal(default, readValue);
		Assert.Equal(CheatEngineFailureKind.Unsupported, readFailure.Kind);
		Assert.Equal("Memory.ReadPrimitive", readFailure.Operation);
		Assert.False(writeSucceeded);
		Assert.Equal(CheatEngineFailureKind.Unsupported, writeFailure.Kind);
		Assert.Equal("Memory.WritePrimitive", writeFailure.Operation);
	}

	[Fact]
	public void ReadAndWriteConvenienceMethodsThrowTheClassifiedDispatcherFailure()
	{
		MemoryClient client = new(new RejectingDispatcher(Failure("Test.Convenience")), InertCoreLifetime.Create());
		MemoryReadRequest<int> read = new(Address, new NeverUsedCodec());
		MemoryWriteRequest<int> write = new(Address, 42, new NeverUsedCodec());

		CheatEngineClientLifecycleException primitiveException =
			Assert.Throws<CheatEngineClientLifecycleException>(() =>
				client.ReadPrimitive<int>(Address, TestContext.Current.CancellationToken));
		CheatEngineClientLifecycleException readException = Assert.Throws<CheatEngineClientLifecycleException>(() =>
			client.Read(read, TestContext.Current.CancellationToken));
		CheatEngineClientLifecycleException writeException = Assert.Throws<CheatEngineClientLifecycleException>(() =>
			client.Write(write, TestContext.Current.CancellationToken));

		Assert.Equal("Test.Convenience", primitiveException.Failure.Operation);
		Assert.Equal("Test.Convenience", readException.Failure.Operation);
		Assert.Equal("Test.Convenience", writeException.Failure.Operation);
	}

	[Theory]
	[InlineData("bytes")]
	[InlineData("string")]
	[InlineData("pointer")]
	public void InvalidDefaultRequestCannotReachTheDispatcher(string requestKind)
	{
		MemoryClient client = new(new RejectingDispatcher(Failure("Test.ShouldNotDispatch")),
			InertCoreLifetime.Create());

		switch (requestKind)
		{
			case "bytes":
				Assert.Throws<ArgumentOutOfRangeException>(() =>
					client.TryReadBytes(default, out _, out _, TestContext.Current.CancellationToken));
				break;
			case "string":
				Assert.Throws<ArgumentOutOfRangeException>(() =>
					client.TryReadString(default, out _, out _, TestContext.Current.CancellationToken));
				break;
			default:
				Assert.Throws<ArgumentException>(() =>
					client.TryResolvePointerChain(default, out _, out _, TestContext.Current.CancellationToken));
				break;
		}
	}

	[Fact]
	public void DefaultStringAndByteWritesAreRejectedBeforeDispatch()
	{
		MemoryClient client = new(new RejectingDispatcher(Failure("Test.ShouldNotDispatch")),
			InertCoreLifetime.Create());

		Assert.Throws<ArgumentException>(() =>
			client.TryWriteBytes(default, out _, TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentNullException>(() =>
			client.TryWriteString(default, out _, TestContext.Current.CancellationToken));
	}

	private static CheatEngineFailure Failure(string operation)
	{
		return new CheatEngineFailure(CheatEngineFailureKind.InvalidState, operation, "The dispatcher rejected work.");
	}

	private sealed class NeverUsedCodec : IMemoryCodec<int>
	{
		public bool TryRead(IMemoryReadContext context, Address address, out int value)
		{
			throw new InvalidOperationException("The rejecting dispatcher must prevent codec execution.");
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in int value)
		{
			throw new InvalidOperationException("The rejecting dispatcher must prevent codec execution.");
		}
	}

	private sealed class RejectingDispatcher : ICheatEngineDispatcher
	{
		private readonly CheatEngineFailure _failure;

		internal RejectingDispatcher(CheatEngineFailure failure)
		{
			_failure = failure;
		}

		public bool IsMainThread => false;

		public bool TryInvoke(Action callback, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			failure = _failure;
			return false;
		}

		public bool TryInvoke<T>(Func<T> callback, [MaybeNullWhen(false)] out T result,
			out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			result = default;
			failure = _failure;
			return false;
		}

		public void Invoke(Action callback, CancellationToken cancellationToken = default)
		{
			if (!TryInvoke(callback, out CheatEngineFailure failure, cancellationToken))
			{
				failure.Throw();
			}
		}

		public T Invoke<T>(Func<T> callback, CancellationToken cancellationToken = default)
		{
			if (TryInvoke(callback, out T? result, out CheatEngineFailure failure, cancellationToken))
			{
				return result;
			}

			failure.Throw();
			return default!;
		}
	}

	private sealed class InlineDispatcher : ICheatEngineDispatcher
	{
		public bool IsMainThread => true;

		public bool TryInvoke(Action callback, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			callback();
			failure = default;
			return true;
		}

		public bool TryInvoke<T>(Func<T> callback, [MaybeNullWhen(false)] out T result,
			out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			result = callback();
			failure = default;
			return true;
		}

		public void Invoke(Action callback, CancellationToken cancellationToken = default)
		{
			callback();
		}

		public T Invoke<T>(Func<T> callback, CancellationToken cancellationToken = default)
		{
			return callback();
		}
	}
}
