using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class MemoryCodecContextLifetimeTests
{
	private static readonly Address TestAddress = new(0x405000);

	[Fact]
	public void ReadContextAllowsPointerMetadataAndBytesOnlyDuringCodecInvocation()
	{
		using ControlledCoreLifetimeContext activation = new();
		using CoreLifetime lifetime = new(activation);
		RecordingCodecContextPort port = new()
		{
			PointerSize = sizeof(ulong)
		};
		MemoryClient client = CreateClient(lifetime, port);
		CapturingCodec codec = new()
		{
			ReadAction = static context =>
			{
				Assert.Equal(sizeof(ulong), context.PointerSize);
				Assert.Equal(sizeof(ulong), context.PointerSize);
				byte[] buffer = new byte[sizeof(int)];
				Assert.True(context.TryReadBytes(TestAddress, buffer));
			}
		};

		bool succeeded = client.TryRead(new MemoryReadRequest<int>(TestAddress, codec), out int value,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(123, value);
		Assert.Equal(default, failure);
		Assert.Equal(1, port.PointerSizeReadCount);
		Assert.Equal(1, port.ReadBytesCallCount);
	}

	[Fact]
	public void WriteContextAllowsPointerMetadataAndBytesOnlyDuringCodecInvocation()
	{
		using ControlledCoreLifetimeContext activation = new();
		using CoreLifetime lifetime = new(activation);
		RecordingCodecContextPort port = new()
		{
			PointerSize = sizeof(uint)
		};
		MemoryClient client = CreateClient(lifetime, port);
		CapturingCodec codec = new()
		{
			WriteAction = static context =>
			{
				Assert.Equal(sizeof(uint), context.PointerSize);
				Assert.Equal(sizeof(uint), context.PointerSize);
				Assert.True(context.TryWriteBytes(TestAddress, [0x0A, 0x0B]));
			}
		};

		bool succeeded = client.TryWrite(new MemoryWriteRequest<int>(TestAddress, 456, codec),
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(1, port.PointerSizeReadCount);
		Assert.Equal(1, port.WriteBytesCallCount);
		Assert.Equal(456, codec.LastWriteValue);
	}

	[Fact]
	public void RetainedContextsExpireAfterSuccessfulReturnBeforeAnyPortCall()
	{
		using ControlledCoreLifetimeContext activation = new();
		using CoreLifetime lifetime = new(activation);
		RecordingCodecContextPort port = new();
		MemoryClient client = CreateClient(lifetime, port);
		CapturingCodec codec = new();

		Assert.True(client.TryRead(new MemoryReadRequest<int>(TestAddress, codec), out _, out _,
			TestContext.Current.CancellationToken));
		Assert.True(client.TryWrite(new MemoryWriteRequest<int>(TestAddress, 456, codec), out _,
			TestContext.Current.CancellationToken));

		IMemoryReadContext readContext = Assert.IsAssignableFrom<IMemoryReadContext>(codec.ReadContext);
		IMemoryWriteContext writeContext = Assert.IsAssignableFrom<IMemoryWriteContext>(codec.WriteContext);
		AssertExpired(() => _ = readContext.PointerSize);
		AssertExpired(() => readContext.TryReadBytes(TestAddress, new byte[1]));
		AssertExpired(() => _ = writeContext.PointerSize);
		AssertExpired(() => writeContext.TryWriteBytes(TestAddress, [0x0A]));
		Assert.Equal(0, port.TotalCallCount);
	}

	[Fact]
	public void FailedCodecsExpireCapturedContextsAndPreserveMemoryFailures()
	{
		using ControlledCoreLifetimeContext activation = new();
		using CoreLifetime lifetime = new(activation);
		RecordingCodecContextPort port = new();
		MemoryClient client = CreateClient(lifetime, port);
		CapturingCodec codec = new()
		{
			ReadSucceeds = false,
			WriteSucceeds = false
		};

		bool readSucceeded = client.TryRead(new MemoryReadRequest<int>(TestAddress, codec), out int readValue,
			out CheatEngineFailure readFailure, TestContext.Current.CancellationToken);
		bool writeSucceeded = client.TryWrite(new MemoryWriteRequest<int>(TestAddress, 456, codec),
			out CheatEngineFailure writeFailure, TestContext.Current.CancellationToken);

		Assert.False(readSucceeded);
		Assert.Equal(default, readValue);
		Assert.Equal(CheatEngineFailureKind.MemoryReadFailed, readFailure.Kind);
		Assert.Equal("Memory.Read", readFailure.Operation);
		Assert.False(writeSucceeded);
		Assert.Equal(CheatEngineFailureKind.MemoryWriteFailed, writeFailure.Kind);
		Assert.Equal("Memory.Write", writeFailure.Operation);
		AssertExpired(() => _ = codec.ReadContext!.PointerSize);
		AssertExpired(() => _ = codec.WriteContext!.PointerSize);
		Assert.Equal(0, port.TotalCallCount);
	}

	[Fact]
	public void ThrowingCodecsExpireCapturedContextsAndRethrowOriginalExceptions()
	{
		using ControlledCoreLifetimeContext activation = new();
		using CoreLifetime lifetime = new(activation);
		RecordingCodecContextPort port = new();
		MemoryClient client = CreateClient(lifetime, port);
		InvalidOperationException readException = new("read failure");
		InvalidOperationException writeException = new("write failure");
		CapturingCodec readCodec = new()
		{
			ReadException = readException
		};
		CapturingCodec writeCodec = new()
		{
			WriteException = writeException
		};

		InvalidOperationException actualRead = Assert.Throws<InvalidOperationException>(() =>
			client.TryRead(new MemoryReadRequest<int>(TestAddress, readCodec), out _, out _,
				TestContext.Current.CancellationToken));
		InvalidOperationException actualWrite = Assert.Throws<InvalidOperationException>(() =>
			client.TryWrite(new MemoryWriteRequest<int>(TestAddress, 456, writeCodec), out _,
				TestContext.Current.CancellationToken));

		Assert.Same(readException, actualRead);
		Assert.Same(writeException, actualWrite);
		AssertExpired(() => _ = readCodec.ReadContext!.PointerSize);
		AssertExpired(() => _ = writeCodec.WriteContext!.PointerSize);
		Assert.Equal(0, port.TotalCallCount);
	}

	[Fact]
	public void WorkerThreadCannotUseContextsWhileTheirCodecInvocationsRemainActive()
	{
		using ControlledCoreLifetimeContext activation = new();
		using CoreLifetime lifetime = new(activation);
		RecordingCodecContextPort port = new();
		MemoryClient client = CreateClient(lifetime, port);
		CapturingCodec codec = new()
		{
			ReadAction = static context => AssertWorkerReadIsRejected(context),
			WriteAction = static context => AssertWorkerWriteIsRejected(context)
		};

		Assert.True(client.TryRead(new MemoryReadRequest<int>(TestAddress, codec), out _, out _,
			TestContext.Current.CancellationToken));
		Assert.True(client.TryWrite(new MemoryWriteRequest<int>(TestAddress, 456, codec), out _,
			TestContext.Current.CancellationToken));

		Assert.Equal(0, port.TotalCallCount);
	}

	[Fact]
	public void ContextFromPriorInvocationCannotBeRevivedDuringALaterInvocation()
	{
		using ControlledCoreLifetimeContext activation = new();
		using CoreLifetime lifetime = new(activation);
		RecordingCodecContextPort port = new();
		MemoryClient client = CreateClient(lifetime, port);
		CapturingCodec firstCodec = new();

		Assert.True(client.TryRead(new MemoryReadRequest<int>(TestAddress, firstCodec), out _, out _,
			TestContext.Current.CancellationToken));
		IMemoryReadContext firstContext = Assert.IsAssignableFrom<IMemoryReadContext>(firstCodec.ReadContext);
		CapturingCodec secondCodec = new()
		{
			ReadAction = _ => AssertExpired(() => ConsumePointerSize(firstContext))
		};

		Assert.True(client.TryRead(new MemoryReadRequest<int>(TestAddress, secondCodec), out _, out _,
			TestContext.Current.CancellationToken));
		Assert.NotSame(firstContext, secondCodec.ReadContext);
		Assert.Equal(0, port.TotalCallCount);
	}

	[Fact]
	public void ContextRejectsActivationEpochChangesAndAReenabledClientDoesNotReviveIt()
	{
		using ControlledCoreLifetimeContext oldActivation = new();
		using CoreLifetime oldLifetime = new(oldActivation);
		RecordingCodecContextPort oldPort = new();
		MemoryClient oldClient = CreateClient(oldLifetime, oldPort);
		Exception? activeUseFailure = null;
		CapturingCodec oldCodec = new()
		{
			ReadAction = context =>
			{
				oldActivation.Epoch++;
				activeUseFailure = CaptureException(() => _ = context.PointerSize);
			}
		};

		Assert.True(oldClient.TryRead(new MemoryReadRequest<int>(TestAddress, oldCodec), out _, out _,
			TestContext.Current.CancellationToken));
		Assert.IsType<CheatEngineActivationExpiredException>(activeUseFailure);
		Assert.Equal(0, oldPort.TotalCallCount);
		oldActivation.IsCurrent = false;

		using ControlledCoreLifetimeContext newActivation = new();
		using CoreLifetime newLifetime = new(newActivation);
		RecordingCodecContextPort newPort = new();
		MemoryClient newClient = CreateClient(newLifetime, newPort);
		CapturingCodec newCodec = new();
		Assert.True(newClient.TryRead(new MemoryReadRequest<int>(TestAddress, newCodec), out _, out _,
			TestContext.Current.CancellationToken));

		AssertExpired(() => _ = oldCodec.ReadContext!.PointerSize);
		Assert.Equal(0, oldPort.TotalCallCount);
	}

	private static void AssertExpired(Action operation)
	{
		CheatEngineActivationExpiredException exception =
			Assert.Throws<CheatEngineActivationExpiredException>(operation);
		Assert.Equal("Memory.CodecContext", exception.Failure.Operation);
	}

	private static void ConsumePointerSize(IMemoryReadContext context)
	{
		_ = context.PointerSize;
	}

	private static void AssertWorkerReadIsRejected(IMemoryReadContext context)
	{
		Exception? pointerFailure = CaptureWorkerException(() => _ = context.PointerSize);
		Exception? readFailure = CaptureWorkerException(() => context.TryReadBytes(TestAddress, new byte[1]));

		Assert.IsType<CheatEngineActivationExpiredException>(pointerFailure);
		Assert.IsType<CheatEngineActivationExpiredException>(readFailure);
	}

	private static void AssertWorkerWriteIsRejected(IMemoryWriteContext context)
	{
		Exception? pointerFailure = CaptureWorkerException(() => _ = context.PointerSize);
		Exception? writeFailure = CaptureWorkerException(() => context.TryWriteBytes(TestAddress, [0x0A]));

		Assert.IsType<CheatEngineActivationExpiredException>(pointerFailure);
		Assert.IsType<CheatEngineActivationExpiredException>(writeFailure);
	}

	private static Exception? CaptureException(Action operation)
	{
		try
		{
			operation();
			return null;
		}
		catch (Exception exception)
		{
			return exception;
		}
	}

	private static Exception? CaptureWorkerException(Action operation)
	{
		Exception? captured = null;
		Thread worker = new(() => captured = CaptureException(operation));
		worker.Start();
		worker.Join();
		return captured;
	}

	private static MemoryClient CreateClient(CoreLifetime lifetime, RecordingCodecContextPort port)
	{
		return new MemoryClient(new ThreadBoundDispatcher(), lifetime, port);
	}

	private sealed class CapturingCodec : IMemoryCodec<int>
	{
		internal Action<IMemoryReadContext>? ReadAction
		{
			get;
			init;
		}

		internal Exception? ReadException
		{
			get;
			init;
		}

		internal bool ReadSucceeds
		{
			get;
			init;
		} = true;

		internal IMemoryReadContext? ReadContext
		{
			get;
			private set;
		}

		internal Action<IMemoryWriteContext>? WriteAction
		{
			get;
			init;
		}

		internal Exception? WriteException
		{
			get;
			init;
		}

		internal bool WriteSucceeds
		{
			get;
			init;
		} = true;

		internal IMemoryWriteContext? WriteContext
		{
			get;
			private set;
		}

		internal int LastWriteValue
		{
			get;
			private set;
		}

		public bool TryRead(IMemoryReadContext context, Address address, out int value)
		{
			ReadContext = context;
			ReadAction?.Invoke(context);
			if (ReadException is not null)
			{
				throw ReadException;
			}

			value = 123;
			return ReadSucceeds;
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in int value)
		{
			WriteContext = context;
			WriteAction?.Invoke(context);
			if (WriteException is not null)
			{
				throw WriteException;
			}

			LastWriteValue = value;
			return WriteSucceeds;
		}
	}

	private sealed class RecordingCodecContextPort : IMemoryCodecContextPort
	{
		internal int PointerSize
		{
			get;
			init;
		} = sizeof(ulong);

		internal int PointerSizeReadCount
		{
			get;
			private set;
		}

		internal int ReadBytesCallCount
		{
			get;
			private set;
		}

		/// <summary>Gets the number of target-fact reads (PID, width, families, configured pointer size).</summary>
		internal int FactCallCount
		{
			get;
			private set;
		}

		internal int TotalCallCount => FactCallCount + ReadBytesCallCount + WriteBytesCallCount;

		internal int WriteBytesCallCount
		{
			get;
			private set;
		}

		public long GetOpenedProcessId()
		{
			FactCallCount++;
			return 42;
		}

		public bool TargetIs64Bit()
		{
			FactCallCount++;
			PointerSizeReadCount++;
			return PointerSize == sizeof(ulong);
		}

		public bool TargetIsX86()
		{
			FactCallCount++;
			return true;
		}

		public bool TargetIsArm()
		{
			FactCallCount++;
			return false;
		}

		public int GetConfiguredPointerSize()
		{
			FactCallCount++;
			return PointerSize;
		}

		public bool TryReadBytes(Address address, Span<byte> destination, out string? failure)
		{
			ReadBytesCallCount++;
			destination.Clear();
			failure = null;
			return true;
		}

		public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source, out string? failure)
		{
			WriteBytesCallCount++;
			failure = null;
			return true;
		}
	}

	private sealed class ThreadBoundDispatcher : ICheatEngineDispatcher
	{
		private readonly int _mainThreadId = Environment.CurrentManagedThreadId;

		public bool IsMainThread => Environment.CurrentManagedThreadId == _mainThreadId;

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
}
