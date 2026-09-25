using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Memory;
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

using SdkPointerSize = CheatEngine.SDK.Engine.Runtime.PointerSize;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class MemoryCodecContextLifetimeTests
{
	private const string ReadOperation = "Memory.Read";
	private const string WriteOperation = "Memory.Write";

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
				Assert.Equal(SdkPointerSize.Bit64, context.Bitness);
				Assert.Equal(SdkPointerSize.Bit64, context.Bitness);
				byte[] buffer = new byte[sizeof(int)];
				Assert.True(context.TryReadBytes(TestAddress, buffer, out _));
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
				Assert.Equal(SdkPointerSize.Bit32, context.Bitness);
				Assert.Equal(SdkPointerSize.Bit32, context.Bitness);
				Assert.True(context.TryWriteBytes(TestAddress, [0x0A, 0x0B], out _));
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
		AssertExpired(ReadOperation, () => _ = readContext.Bitness);
		AssertExpired(ReadOperation, () => readContext.TryReadBytes(TestAddress, new byte[1], out _));
		AssertExpired(WriteOperation, () => _ = writeContext.Bitness);
		AssertExpired(WriteOperation, () => writeContext.TryWriteBytes(TestAddress, [0x0A], out _));
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
		AssertExpired(ReadOperation, () => _ = codec.ReadContext!.Bitness);
		AssertExpired(WriteOperation, () => _ = codec.WriteContext!.Bitness);
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
		AssertExpired(ReadOperation, () => _ = readCodec.ReadContext!.Bitness);
		AssertExpired(WriteOperation, () => _ = writeCodec.WriteContext!.Bitness);
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
			ReadAction = _ => AssertExpired(ReadOperation, () => ConsumeBitness(firstContext))
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
				activeUseFailure = CaptureException(() => _ = context.Bitness);
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

		AssertExpired(ReadOperation, () => _ = oldCodec.ReadContext!.Bitness);
		Assert.Equal(0, oldPort.TotalCallCount);
	}

	/// <summary>An expired context names the public call that ran its codec: Memory.Read or Memory.Write.</summary>
	private static void AssertExpired(string expectedOperation, Action operation)
	{
		CheatEngineActivationExpiredException exception =
			Assert.Throws<CheatEngineActivationExpiredException>(operation);
		Assert.Equal(expectedOperation, exception.Failure.Operation);
	}

	private static void ConsumeBitness(IMemoryReadContext context)
	{
		_ = context.Bitness;
	}

	private static void AssertWorkerReadIsRejected(IMemoryReadContext context)
	{
		Exception? pointerFailure = CaptureWorkerException(() => _ = context.Bitness);
		Exception? readFailure = CaptureWorkerException(() => context.TryReadBytes(TestAddress, new byte[1], out _));

		Assert.IsType<CheatEngineActivationExpiredException>(pointerFailure);
		Assert.IsType<CheatEngineActivationExpiredException>(readFailure);
	}

	private static void AssertWorkerWriteIsRejected(IMemoryWriteContext context)
	{
		Exception? pointerFailure = CaptureWorkerException(() => _ = context.Bitness);
		Exception? writeFailure = CaptureWorkerException(() => context.TryWriteBytes(TestAddress, [0x0A], out _));

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

		public bool TryRead(IMemoryReadContext context, Address address, out int value, out CheatEngineFailure failure)
		{
			failure = default;
			ReadContext = context;
			ReadAction?.Invoke(context);
			if (ReadException is not null)
			{
				throw ReadException;
			}

			value = 123;
			return ReadSucceeds;
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in int value, out CheatEngineFailure failure)
		{
			failure = default;
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

		private SdkPointerSize Bitness => PointerSize == sizeof(ulong) ? SdkPointerSize.Bit64 : SdkPointerSize.Bit32;

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

		/// <summary>Gets the number of target observations (each one reads every target fact).</summary>
		internal int FactCallCount
		{
			get;
			private set;
		}

		internal int TotalCallCount => FactCallCount + ReadBytesCallCount + WriteBytesCallCount;

		public ProcessOperationStatus ObserveCurrent(out CurrentProcessObservation observation)
		{
			FactCallCount++;
			observation = new CurrentProcessObservation(new TargetProcessId(42), Bitness);
			return ProcessOperationStatus.Success;
		}

		public ProcessOperationStatus ObserveTargetArchitecture(out TargetArchitectureObservation observation)
		{
			FactCallCount++;
			PointerSizeReadCount++;
			observation = TargetObservations.Create(42, PointerSize == sizeof(ulong), configuredPointerSizeBytes: PointerSize);
			return ProcessOperationStatus.Success;
		}

		public ProcessOperationStatus TryGetConfiguredPointerSize(out int rawBytes, out SdkPointerSize pointerSize)
		{
			FactCallCount++;
			rawBytes = PointerSize;
			pointerSize = Bitness;
			return ProcessOperationStatus.Success;
		}

		internal int WriteBytesCallCount
		{
			get;
			private set;
		}

		public bool TryReadBytes(Address address, Span<byte> destination, out int written,
			out MemoryAccessFailure failure)
		{
			ReadBytesCallCount++;
			destination.Clear();
			written = destination.Length;
			failure = MemoryAccessFailure.None;
			return true;
		}

		public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source, out MemoryAccessFailure failure)
		{
			WriteBytesCallCount++;
			failure = MemoryAccessFailure.None;
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
}
