using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
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
		MemoryStringReadRequest stringRead = new MemoryStringReadRequest(Address, 12, MemoryStringEncoding.Utf16);
		MemoryStringWriteRequest stringWrite =
			new MemoryStringWriteRequest(Address, "health", 6, MemoryStringEncoding.Utf16);

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
	[Trait("Qualification", "Q33")]
	public void PreAdmissionCancelledBatchWriteReportsNotStarted()
	{
		CoreLifetime lifetime = InertCoreLifetime.Create();
		MemoryClient client = new(new SdkMainThreadDispatcher(lifetime, new InlineMainThreadInvoker()), lifetime);
		MemoryPrimitiveBatchWriteRequest<int> writes = new([new MemoryAddressValue<int>(Address, 12)]);

		MemoryPrimitiveBatchWriteOutcome outcome =
			client.WritePrimitiveBatchDetailed(writes, new CancellationToken(true));

		Assert.False(outcome.IsSuccess);
		Assert.Equal(0, outcome.CompletedCount);
		Assert.Null(outcome.FailedIndex);
		Assert.Equal(MemoryBatchWriteEffectState.NotStarted, outcome.EffectState);
		Assert.Equal(CheatEngineFailureKind.Cancelled, outcome.Failure!.Value.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, outcome.Failure.Value.HostEffect);
	}

	[Fact]
	public void NonCancellationDispatchFailureOfABatchWriteKeepsAnUnknownEffect()
	{
		CheatEngineFailure expected = Failure("Test.Batch");
		MemoryClient client = new(new RejectingDispatcher(expected), InertCoreLifetime.Create());

		MemoryPrimitiveBatchWriteOutcome outcome = client.WritePrimitiveBatchDetailed(
			new MemoryPrimitiveBatchWriteRequest<int>([new MemoryAddressValue<int>(Address, 12)]),
			TestContext.Current.CancellationToken);

		Assert.Equal(MemoryBatchWriteEffectState.Unknown, outcome.EffectState);
		Assert.Equal(expected, outcome.Failure);
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

	/// <summary>
	///     A5: the primitive members support exactly the 8- to 64-bit integers, float, double and Address. Any other
	///     unmanaged type is refused before dispatch: a rejecting dispatcher would otherwise report its own failure.
	/// </summary>
	[Fact]
	[Trait("Qualification", "Q20")]
	public void UnsupportedPrimitiveTypesAreRefusedBeforeDispatchWithoutAHostCall()
	{
		MemoryClient client = new(new RejectingDispatcher(Failure("Test.ShouldNotDispatch")),
			InertCoreLifetime.Create());

		AssertRefusedBeforeDispatch(client, DateTime.UnixEpoch);
		AssertRefusedBeforeDispatch(client, 'A');
		AssertRefusedBeforeDispatch(client, true);
		AssertRefusedBeforeDispatch(client, (nint) 1);
		AssertRefusedBeforeDispatch(client, 1m);
		AssertRefusedBeforeDispatch(client, Guid.Empty);
		AssertRefusedBeforeDispatch(client, (Half) 1);
	}

	[Fact]
	public void ReadAndWriteConvenienceMethodsThrowTheClassifiedDispatcherFailure()
	{
		MemoryClient client = new(new RejectingDispatcher(Failure("Test.Convenience")), InertCoreLifetime.Create());
		MemoryReadRequest<int> read = new(Address, new NeverUsedCodec());
		MemoryWriteRequest<int> write = new(Address, 42, new NeverUsedCodec());

		CheatEngineInvalidStateException primitiveException =
			Assert.Throws<CheatEngineInvalidStateException>(() =>
				client.ReadPrimitive<int>(Address, TestContext.Current.CancellationToken));
		CheatEngineInvalidStateException readException = Assert.Throws<CheatEngineInvalidStateException>(() =>
			client.Read(read, TestContext.Current.CancellationToken));
		CheatEngineInvalidStateException writeException = Assert.Throws<CheatEngineInvalidStateException>(() =>
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

	/// <summary>
	///     A string request tampered past its constructor throws what that constructor throws for the same value, from
	///     both forms and before dispatch: an undefined encoding would otherwise be read or written as UTF-8.
	/// </summary>
	[Theory]
	[InlineData("Read.Encoding", "request")]
	[InlineData("Write.Encoding", "request")]
	[InlineData("Write.MaximumLength", "request.MaximumLength")]
	public void ATamperedStringRequestThrowsBeforeDispatch(string tampered, string parameter)
	{
		MemoryClient client = new(new RejectingDispatcher(Failure("Test.ShouldNotDispatch")),
			InertCoreLifetime.Create());
		CancellationToken token = TestContext.Current.CancellationToken;
		MemoryStringReadRequest read = TamperedValues.WithBackingField(
			new MemoryStringReadRequest(Address, 16, MemoryStringEncoding.Utf8),
			nameof(MemoryStringReadRequest.Encoding), (MemoryStringEncoding) 7);
		MemoryStringWriteRequest write = new(Address, string.Empty, 16, MemoryStringEncoding.Utf8);
		MemoryStringWriteRequest tamperedWrite = tampered == "Write.MaximumLength"
			? TamperedValues.WithBackingField(write, nameof(MemoryStringWriteRequest.MaximumLength), 0)
			: TamperedValues.WithBackingField(write, nameof(MemoryStringWriteRequest.Encoding),
				(MemoryStringEncoding) 7);
		(Action TryForm, Action ThrowingForm) forms = tampered switch
		{
			"Read.Encoding" => (() => client.TryReadString(read, out _, out _, token),
				() => client.ReadString(read, token)),
			"Write.Encoding" or "Write.MaximumLength" => (() => client.TryWriteString(tamperedWrite, out _, token),
				() => client.WriteString(tamperedWrite, token)),
			_ => throw new ArgumentOutOfRangeException(nameof(tampered), tampered, null)
		};

		ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(forms.TryForm);
		ArgumentOutOfRangeException throwingForm = Assert.Throws<ArgumentOutOfRangeException>(forms.ThrowingForm);

		Assert.Equal(parameter, thrown.ParamName);
		Assert.Equal(thrown.Message, throwingForm.Message);
	}

	private static void AssertRefusedBeforeDispatch<T>(MemoryClient client, T sample)
		where T : unmanaged
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		bool read = client.TryReadPrimitive(Address, out T _, out CheatEngineFailure readFailure, token);
		bool written = client.TryWritePrimitive(Address, sample, out CheatEngineFailure writeFailure, token);
		MemoryPrimitiveBatchReadOutcome<T> readBatch =
			client.ReadPrimitiveBatchDetailed(new MemoryPrimitiveBatchReadRequest<T>([Address]), token);
		MemoryPrimitiveBatchWriteOutcome writeBatch = client.WritePrimitiveBatchDetailed(
			new MemoryPrimitiveBatchWriteRequest<T>([new MemoryAddressValue<T>(Address, sample)]), token);

		Assert.False(read);
		Assert.False(written);
		Assert.Equal(MemoryBatchWriteEffectState.NotStarted, writeBatch.EffectState);
		Assert.Equal(0, readBatch.CompletedCount);
		foreach ((CheatEngineFailure failure, string operation) in (ReadOnlySpan<(CheatEngineFailure, string)>)
				 [
					 (readFailure, "Memory.ReadPrimitive"), (writeFailure, "Memory.WritePrimitive"),
					 (readBatch.Failure!.Value, "Memory.ReadPrimitiveBatch"),
					 (writeBatch.Failure!.Value, "Memory.WritePrimitiveBatch")
				 ])
		{
			Assert.Equal((CheatEngineFailureKind.OperationRejected, CheatEngineHostEffect.NotStarted, operation),
				(failure.Kind, failure.HostEffect, failure.Operation));
		}
	}

	private static CheatEngineFailure Failure(string operation)
	{
		return new CheatEngineFailure(CheatEngineFailureKind.InvalidState, operation, "The dispatcher rejected work.");
	}

	private sealed class NeverUsedCodec : IMemoryCodec<int>
	{
		public bool TryRead(IMemoryReadContext context, Address address, out int value, out CheatEngineFailure failure)
		{
			failure = default;
			throw new InvalidOperationException("The rejecting dispatcher must prevent codec execution.");
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in int value, out CheatEngineFailure failure)
		{
			failure = default;
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
				failure.Throw(cancellationToken);
			}
		}

		public T Invoke<T>(Func<T> callback, CancellationToken cancellationToken = default)
		{
			if (TryInvoke(callback, out T? result, out CheatEngineFailure failure, cancellationToken))
			{
				return result;
			}

			failure.Throw(cancellationToken);
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
