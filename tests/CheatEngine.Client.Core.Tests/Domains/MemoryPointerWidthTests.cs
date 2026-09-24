using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Tests.Domains;

/// <summary>
///     The codec width is the target process width (spike C3 D3(d): Cheat Engine's readPointer follows it); Cheat Engine's
///     configured pointer size is a separate fact, and the Client's own pointer-typed paths are refused when it differs
///     (audit A10-18, A12-24, A18-20, CLI-MEM-1).
/// </summary>
public sealed class MemoryPointerWidthTests
{
	private static readonly Address TestAddress = new(0x405000);

	[Fact]
	[Trait("Qualification", "Q31")]
	public void CodecContextReportsConfiguredPointerSizeSeparatelyFromProcessWidth()
	{
		PointerWidthPort port = new()
		{
			ConfiguredPointerSize = 4
		};
		FactCodec codec = new();

		Assert.True(CreateClient(port).TryRead(new MemoryReadRequest<int>(TestAddress, codec), out _, out _,
			TestContext.Current.CancellationToken));

		Assert.Equal(sizeof(ulong), codec.PointerSize);
		Assert.Equal(PointerSize.Bit64, codec.ProcessPointerSize);
		Assert.Equal(4, codec.ConfiguredPointerSizeBytes);
		Assert.Equal(PointerSize.Bit32, codec.ConfiguredPointerSize);
		Assert.True(codec.Differs);
	}

	[Fact]
	[Trait("Qualification", "Q21")]
	public void CodecContextPointerSizeFollowsTheTargetProcessWidthNotThePluginProcessWidth()
	{
		// A 32-bit target observed from a 64-bit test process: the width comes from targetIs64Bit, never IntPtr.Size.
		Assert.Equal(sizeof(ulong), IntPtr.Size);
		PointerWidthPort port = new()
		{
			Is64Bit = false,
			ConfiguredPointerSize = 4
		};
		FactCodec codec = new();

		Assert.True(CreateClient(port).TryRead(new MemoryReadRequest<int>(TestAddress, codec), out _, out _,
			TestContext.Current.CancellationToken));

		Assert.Equal(sizeof(uint), codec.PointerSize);
		Assert.Equal(PointerSize.Bit32, codec.ProcessPointerSize);
		Assert.False(codec.Differs);
	}

	[Fact]
	public void CodecContextWithoutASelectedTargetReportsTargetNotAttachedWithoutReadingWidthFacts()
	{
		// Spike C3 D2: with no target opened Cheat Engine reports x86, 64-bit and pointer size 8; nothing is read.
		PointerWidthPort port = new()
		{
			ProcessId = 0
		};
		FactCodec codec = new();

		bool succeeded = CreateClient(port).TryRead(new MemoryReadRequest<int>(TestAddress, codec), out _,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.TargetNotAttached, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal("Memory.Read", failure.Operation);
		Assert.Equal([nameof(PointerWidthPort.GetOpenedProcessId)], port.FactCalls);
		Assert.Equal(0, port.ByteReads);
	}

	[Theory]
	[InlineData("FaultedOpenedProcessRead")]
	[InlineData("FileAsProcessSentinel")]
	[InlineData("FaultedClosingProcessRead")]
	public void CodecContextReportsAnUnobservableWidthAsIndeterminateInsteadOfNoTarget(string scenario)
	{
		// ADR-08: only an observed PID of zero means that no target is selected. A faulted opened-process read, a PID
		// that is not a local target, or an unconfirmed observation leaves the width unobservable.
		PointerWidthPort port = scenario switch
		{
			"FaultedOpenedProcessRead" => new PointerWidthPort
			{
				ProcessIdException = new LuaException("getOpenedProcessID failed")
			},
			"FileAsProcessSentinel" => new PointerWidthPort
			{
				ProcessId = 4294967295L
			},
			_ => new PointerWidthPort
			{
				ClosingProcessIdException = new LuaException("getOpenedProcessID failed")
			}
		};

		bool succeeded = CreateClient(port).TryRead(new MemoryReadRequest<int>(TestAddress, new FactCodec()), out _,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.IndeterminateHostResult, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Contains("process width is unobservable", failure.Message, StringComparison.Ordinal);
		Assert.DoesNotContain("No target process is selected", failure.Message, StringComparison.Ordinal);
		Assert.Equal(0, port.ByteReads);
	}

	[Fact]
	[Trait("Qualification", "Q31")]
	public void CustomCodecSeesTheProcessWidthAndTheConfiguredSizeSeparately()
	{
		// An application codec is never refused by the Client: it receives the facts and decides.
		PointerWidthPort port = new()
		{
			ConfiguredPointerSize = 4,
			Bytes = [1, 2, 3, 4, 5, 6, 7, 8]
		};
		FactCodec codec = new()
		{
			ReadBytes = true
		};

		bool succeeded = CreateClient(port).TryRead(new MemoryReadRequest<int>(TestAddress, codec), out int value,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(sizeof(ulong), value);
		Assert.Equal(PointerSize.Bit64, codec.ProcessPointerSize);
		Assert.Equal(PointerSize.Bit32, codec.ConfiguredPointerSize);
		Assert.Equal(1, port.ByteReads);
	}

	[Fact]
	[Trait("Qualification", "Q31")]
	public void PointerCodecPolicyRefusalIsReportedAsOperationRejectedBeforeAnyRead()
	{
		// The built-in Address codec (dependency-injection package) asks the Core context to admit it first.
		PointerWidthPort port = new()
		{
			ConfiguredPointerSize = 4
		};
		PolicyCodec codec = new();

		bool readSucceeded = CreateClient(port).TryRead(new MemoryReadRequest<Address>(TestAddress, codec), out _,
			out CheatEngineFailure readFailure, TestContext.Current.CancellationToken);
		bool writeSucceeded = CreateClient(port).TryWrite(new MemoryWriteRequest<Address>(TestAddress, TestAddress, codec),
			out CheatEngineFailure writeFailure, TestContext.Current.CancellationToken);

		Assert.False(readSucceeded);
		Assert.False(writeSucceeded);
		foreach (CheatEngineFailure failure in (CheatEngineFailure[]) [readFailure, writeFailure])
		{
			Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
			Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
			Assert.Contains("configured pointer size (4 bytes)", failure.Message, StringComparison.Ordinal);
			Assert.Contains("process width (8 bytes)", failure.Message, StringComparison.Ordinal);
		}

		Assert.Equal(0, port.ByteReads);
		Assert.Equal(0, port.ByteWrites);
	}

	[Fact]
	[Trait("Qualification", "Q31")]
	public void ReadPrimitiveAddressIsRefusedBeforeAnyHostReadOnPointerWidthMismatch()
	{
		PointerWidthPort port = new()
		{
			ConfiguredPointerSize = 4
		};
		MemoryClient client = CreateClient(port);

		bool readSucceeded = client.TryReadPrimitive(TestAddress, out Address read, out CheatEngineFailure readFailure,
			TestContext.Current.CancellationToken);
		bool writeSucceeded = client.TryWritePrimitive(TestAddress, TestAddress, out CheatEngineFailure writeFailure,
			TestContext.Current.CancellationToken);
		bool intSucceeded = client.TryReadPrimitive(TestAddress, out int _, out _, TestContext.Current.CancellationToken);

		Assert.False(readSucceeded);
		Assert.Equal(default, read);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, readFailure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, readFailure.HostEffect);
		Assert.Equal("Memory.ReadPrimitive", readFailure.Operation);
		Assert.Contains("Cheat Engine's readPointer follows the process width", readFailure.Message,
			StringComparison.Ordinal);
		Assert.False(writeSucceeded);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, writeFailure.Kind);
		Assert.Equal("Memory.WritePrimitive", writeFailure.Operation);
		Assert.Equal(0, port.PointerReads);
		Assert.Equal(0, port.PointerWrites);
		Assert.True(intSucceeded, "An explicit integer read is not pointer-typed and is never refused.");
	}

	[Fact]
	[Trait("Qualification", "Q31")]
	public void AddressPrimitiveBatchIsRefusedAsNotStartedOnPointerWidthMismatch()
	{
		PointerWidthPort port = new()
		{
			ConfiguredPointerSize = 4
		};
		MemoryClient client = CreateClient(port);

		MemoryPrimitiveBatchWriteOutcome write = client.WritePrimitiveBatchDetailed(
			new MemoryPrimitiveBatchWriteRequest<Address>([
				new MemoryAddressValue<Address>(TestAddress, TestAddress), new MemoryAddressValue<Address>(TestAddress + 8, 0)
			]), TestContext.Current.CancellationToken);
		MemoryPrimitiveBatchReadOutcome<Address> read = client.ReadPrimitiveBatchDetailed(
			new MemoryPrimitiveBatchReadRequest<Address>([TestAddress, TestAddress + 8]),
			TestContext.Current.CancellationToken);

		Assert.False(write.Succeeded);
		Assert.Equal(2, write.AttemptedCount);
		Assert.Equal(0, write.CompletedCount);
		Assert.Null(write.FailedIndex);
		Assert.Equal(MemoryBatchWriteEffectState.NotStarted, write.EffectState);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, write.Cause!.Value.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, write.Cause.Value.HostEffect);
		Assert.False(read.Succeeded);
		Assert.Equal(0, read.CompletedCount);
		Assert.True(read.ReadPrefix.IsEmpty);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, read.Cause!.Value.Kind);
		Assert.Equal(0, port.PointerReads);
		Assert.Equal(0, port.PointerWrites);
	}

	[Fact]
	[Trait("Qualification", "Q31")]
	public void ResolvePointerChainIsRefusedBeforeTheFirstHopOnPointerWidthMismatch()
	{
		PointerWidthPort port = new()
		{
			ConfiguredPointerSize = 4
		};

		bool succeeded = CreateClient(port).TryResolvePointerChain(new PointerChainRequest(TestAddress, [0x10L, 0x20L]),
			out Address resolved, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, resolved);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal("Memory.ResolvePointerChain", failure.Operation);
		Assert.Equal(0, port.PointerReads);
	}

	[Fact]
	[Trait("Qualification", "Q21")]
	public void ResolvePointerChainRefusesAnIntermediateAddressBeyondTheProcessWidth()
	{
		// A 32-bit target: 0xFFFFFFF0 + 0x20 leaves the 32-bit address space after the first hop.
		PointerWidthPort port = new()
		{
			Is64Bit = false,
			ConfiguredPointerSize = 4,
			Pointers = { [TestAddress] = new Address(0xFFFFFFF0) }
		};

		bool succeeded = CreateClient(port).TryResolvePointerChain(new PointerChainRequest(TestAddress, [0x20L, 0x8L]),
			out Address resolved, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, resolved);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Started, failure.HostEffect);
		Assert.Contains("hop 1", failure.Message, StringComparison.Ordinal);
		Assert.DoesNotContain("100000010", failure.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(1, port.PointerReads);
	}

	[Fact]
	public void UnobservableConfiguredPointerSizeDoesNotRefusePointerOperations()
	{
		// No evidence of a mismatch: the Client keeps the process width and proceeds.
		PointerWidthPort port = new()
		{
			ConfiguredPointerSizeException = new LuaException("getPointerSize failed"),
			Pointers = { [TestAddress] = new Address(0x500000) }
		};
		MemoryClient client = CreateClient(port);

		bool readSucceeded = client.TryReadPrimitive(TestAddress, out Address read, out _,
			TestContext.Current.CancellationToken);
		bool chainSucceeded = client.TryResolvePointerChain(new PointerChainRequest(TestAddress, [0x10L]),
			out Address resolved, out _, TestContext.Current.CancellationToken);

		Assert.True(readSucceeded);
		Assert.Equal(new Address(0x500000), read);
		Assert.True(chainSucceeded);
		Assert.Equal(new Address(0x500010), resolved);
	}

	[Fact]
	public void CodecContextExceptionIsReturnedAsAFailureButConsumerCodecExceptionsAreRethrown()
	{
		// The width fault is the exact instance the context threw: Core converts it. An application-owned
		// CheatEngineOperationException with the same shape is not that instance and is rethrown unchanged.
		PointerWidthPort noTarget = new()
		{
			ProcessId = 0
		};
		CheatEngineOperationException applicationFault = new(new CheatEngineFailure(
			CheatEngineFailureKind.TargetNotAttached, "Memory.CodecContext", "application-owned"));

		bool succeeded = CreateClient(noTarget).TryRead(new MemoryReadRequest<int>(TestAddress, new FactCodec()), out _,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);
		CheatEngineOperationException rethrown = Assert.Throws<CheatEngineOperationException>(() =>
			CreateClient(new PointerWidthPort()).TryRead(
				new MemoryReadRequest<int>(TestAddress, new FactCodec { Throw = applicationFault }), out _, out _,
				TestContext.Current.CancellationToken));

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.TargetNotAttached, failure.Kind);
		Assert.Same(applicationFault, rethrown);
	}

	private static MemoryClient CreateClient(PointerWidthPort port)
	{
		return new MemoryClient(new ThreadBoundDispatcher(), InertCoreLifetime.Create(), port);
	}

	/// <summary>Records the pointer-width facts that an application codec observes through its context.</summary>
	private sealed class FactCodec : IMemoryCodec<int>
	{
		internal bool ReadBytes
		{
			get;
			init;
		}

		internal Exception? Throw
		{
			get;
			init;
		}

		internal int PointerSize
		{
			get;
			private set;
		}

		internal PointerSize ProcessPointerSize
		{
			get;
			private set;
		}

		internal int? ConfiguredPointerSizeBytes
		{
			get;
			private set;
		}

		internal PointerSize ConfiguredPointerSize
		{
			get;
			private set;
		}

		internal bool Differs
		{
			get;
			private set;
		}

		public bool TryRead(IMemoryReadContext context, Address address, out int value)
		{
			if (Throw is not null)
			{
				throw Throw;
			}

			PointerSize = context.PointerSize;
			IMemoryPointerWidthContext widths = Assert.IsAssignableFrom<IMemoryPointerWidthContext>(context);
			ProcessPointerSize = widths.ProcessPointerSize;
			ConfiguredPointerSizeBytes = widths.ConfiguredPointerSizeBytes;
			ConfiguredPointerSize = widths.ConfiguredPointerSize;
			Differs = widths.ConfiguredPointerSizeDiffersFromProcessWidth;
			if (ReadBytes && !context.TryReadBytes(address, new byte[PointerSize]))
			{
				value = 0;
				return false;
			}

			value = PointerSize;
			return true;
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in int value)
		{
			return true;
		}
	}

	/// <summary>Behaves like the built-in Address codec: it asks the Core context to admit it before any access.</summary>
	private sealed class PolicyCodec : IMemoryCodec<Address>
	{
		public bool TryRead(IMemoryReadContext context, Address address, out Address value)
		{
			value = default;
			return ((ICorePointerCodecPolicy) context).TryAdmitPointerCodec() &&
				   context.TryReadBytes(address, new byte[context.PointerSize]);
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in Address value)
		{
			return ((ICorePointerCodecPolicy) context).TryAdmitPointerCodec() &&
				   context.TryWriteBytes(address, new byte[context.PointerSize]);
		}
	}

	/// <summary>An x64 target (PID 42) whose facts, pointers and failures are configurable.</summary>
	private sealed class PointerWidthPort : IMemoryCodecContextPort
	{
		internal long ProcessId
		{
			get;
			init;
		} = 42;

		internal bool Is64Bit
		{
			get;
			init;
		} = true;

		internal int ConfiguredPointerSize
		{
			get;
			init;
		} = sizeof(ulong);

		internal Exception? ConfiguredPointerSizeException
		{
			get;
			init;
		}

		internal byte[] Bytes
		{
			get;
			init;
		} = new byte[8];

		internal Dictionary<Address, Address> Pointers
		{
			get;
		} = [];

		internal List<string> FactCalls
		{
			get;
		} = [];

		internal int ByteReads
		{
			get;
			private set;
		}

		internal int ByteWrites
		{
			get;
			private set;
		}

		internal int PointerReads
		{
			get;
			private set;
		}

		internal int PointerWrites
		{
			get;
			private set;
		}

		/// <summary>Thrown by every opened-process read (the opening read of the bracket faults).</summary>
		internal Exception? ProcessIdException
		{
			get;
			init;
		}

		/// <summary>Thrown by the opened-process reads after the first one (the closing read of the bracket faults).</summary>
		internal Exception? ClosingProcessIdException
		{
			get;
			init;
		}

		public long GetOpenedProcessId()
		{
			FactCalls.Add(nameof(GetOpenedProcessId));
			if (ProcessIdException is { } exception)
			{
				throw exception;
			}

			return ClosingProcessIdException is { } closing &&
				   FactCalls.Count(static call => call == nameof(GetOpenedProcessId)) > 1
				? throw closing
				: ProcessId;
		}

		public bool TargetIs64Bit()
		{
			FactCalls.Add(nameof(TargetIs64Bit));
			return Is64Bit;
		}

		public bool TargetIsX86()
		{
			FactCalls.Add(nameof(TargetIsX86));
			return true;
		}

		public bool TargetIsArm()
		{
			FactCalls.Add(nameof(TargetIsArm));
			return false;
		}

		public int GetConfiguredPointerSize()
		{
			FactCalls.Add(nameof(GetConfiguredPointerSize));
			return ConfiguredPointerSizeException is { } exception ? throw exception : ConfiguredPointerSize;
		}

		public bool TryReadBytes(Address address, Span<byte> destination, out string? failure)
		{
			ByteReads++;
			Bytes.AsSpan(0, destination.Length).CopyTo(destination);
			failure = null;
			return true;
		}

		public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source, out string? failure)
		{
			ByteWrites++;
			failure = null;
			return true;
		}

		public bool TryReadPrimitive<T>(Address address, out T value, out string? failure)
		{
			if (typeof(T) == typeof(Address))
			{
				PointerReads++;
				Address pointer = Pointers.GetValueOrDefault(address);
				value = (T) (object) pointer;
			}
			else
			{
				value = default!;
			}

			failure = null;
			return true;
		}

		public bool TryWritePrimitive<T>(Address address, T value, out string? failure)
		{
			if (typeof(T) == typeof(Address))
			{
				PointerWrites++;
			}

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
