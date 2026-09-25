using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Memory;
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

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

		Assert.Equal(PointerSize.Bit64, codec.Bitness);
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

		Assert.Equal(PointerSize.Bit32, codec.Bitness);
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
		Assert.Equal([nameof(ITargetObservationPort.ObserveTargetArchitecture)], port.TargetCalls);
		Assert.Equal(0, port.ByteReads);
	}

	[Theory]
	[InlineData("FaultedOpenedProcessRead", CheatEngineFailureKind.LuaError)]
	[InlineData("FileAsProcessSentinel", CheatEngineFailureKind.TargetIdentityUnavailable)]
	[InlineData("FaultedClosingProcessRead", CheatEngineFailureKind.LuaError)]
	public void CodecContextReportsAnUnobservableWidthWithItsOwnKindInsteadOfNoTarget(string scenario,
		CheatEngineFailureKind expected)
	{
		// ADR-08: only an observed "no process selected" means that no target is selected. A raising selection read or
		// a file opened as a process leaves the width unobservable, with the kind of what CheatEngine.SDK reported.
		PointerWidthPort port = scenario switch
		{
			"FaultedOpenedProcessRead" => new PointerWidthPort
			{
				ProcessIdFails = true
			},
			"FileAsProcessSentinel" => new PointerWidthPort
			{
				ProcessId = 4294967295L
			},
			_ => new PointerWidthPort
			{
				ClosingProcessIdFails = true
			}
		};

		bool succeeded = CreateClient(port).TryRead(new MemoryReadRequest<int>(TestAddress, new FactCodec()), out _,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(expected, failure.Kind);
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
		Assert.Equal(PointerSize.Bit64, codec.Bitness);
		Assert.Equal(PointerSize.Bit32, codec.ConfiguredPointerSize);
		Assert.Equal(1, port.ByteReads);
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

		Assert.False(write.IsSuccess);
		Assert.Equal(2, write.RequestedCount);
		Assert.Equal(0, write.CompletedCount);
		Assert.Null(write.FailedIndex);
		Assert.Equal(MemoryBatchWriteEffectState.NotStarted, write.EffectState);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, write.Failure!.Value.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, write.Failure.Value.HostEffect);
		Assert.False(read.IsSuccess);
		Assert.Equal(0, read.CompletedCount);
		Assert.True(read.Values.IsEmpty);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, read.Failure!.Value.Kind);
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
		// The one read returned; the Client refused the address it computed, and a read leaves no target effect.
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Contains("hop 1 of 2", failure.Message, StringComparison.Ordinal);
		Assert.DoesNotContain("100000010", failure.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(1, port.PointerReads);
		Assert.Equal([PointerSize.Bit32], port.Widths);
	}

	[Fact]
	[Trait("Qualification", "Q21")]
	public void ResolvePointerChainRefusesABaseAddressBeyondAThirtyTwoBitTargetBeforeAnyRead()
	{
		PointerWidthPort port = new()
		{
			Is64Bit = false,
			ConfiguredPointerSize = 4
		};

		bool succeeded = CreateClient(port).TryResolvePointerChain(
			new PointerChainRequest(new Address(0x1_0000_0000), [0x10L]), out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(0, port.PointerReads);
	}

	[Fact]
	[Trait("Qualification", "Q21")]
	public void ResolvePointerChainReportsTheHopWhosePointerExceedsTheTargetWidth()
	{
		// The second hop reads a value above 4 GiB on a 32-bit target: the SDK overload refuses it and the chain names
		// the hop instead of truncating the value.
		PointerWidthPort port = new()
		{
			Is64Bit = false,
			ConfiguredPointerSize = 4,
			Pointers =
			{
				[TestAddress] = new Address(0x500000),
				[new Address(0x500010)] = new Address(0x1_0000_0000)
			}
		};

		bool succeeded = CreateClient(port).TryResolvePointerChain(
			new PointerChainRequest(TestAddress, [0x10L, 0x8L, 0x4L]), out Address resolved,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, resolved);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Contains("hop 2 of 3", failure.Message, StringComparison.Ordinal);
		Assert.Equal(2, port.PointerReads);
		Assert.All(port.Widths, static width => Assert.Equal(PointerSize.Bit32, width));
	}

	[Fact]
	[Trait("Qualification", "Q21")]
	public void WritingAnAddressAboveFourGibibytesToAThirtyTwoBitTargetIsRefusedBeforeCheatEngineIsCalled()
	{
		PointerWidthPort port = new()
		{
			Is64Bit = false,
			ConfiguredPointerSize = 4
		};
		MemoryClient client = CreateClient(port);

		bool refused = client.TryWritePrimitive(TestAddress, new Address(0x1_0000_0000),
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);
		bool admitted = client.TryWritePrimitive(TestAddress, new Address(uint.MaxValue), out _,
			TestContext.Current.CancellationToken);

		Assert.False(refused);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal("Memory.WritePrimitive", failure.Operation);
		Assert.True(admitted);
		Assert.Equal(new Address(uint.MaxValue), port.Pointers[TestAddress]);
		Assert.Equal([PointerSize.Bit32, PointerSize.Bit32], port.Widths);
	}

	[Fact]
	[Trait("Qualification", "Q21")]
	public void AnAddressAboveFourGibibytesRoundTripsOnASixtyFourBitTarget()
	{
		// 0x7FF612345678 is a typical x64 image address; the SDK receives the observed 64-bit width both ways.
		PointerWidthPort port = new();
		MemoryClient client = CreateClient(port);
		Address value = new(0x7FF6_1234_5678);

		bool written = client.TryWritePrimitive(TestAddress, value, out CheatEngineFailure writeFailure,
			TestContext.Current.CancellationToken);
		bool read = client.TryReadPrimitive(TestAddress, out Address readBack, out CheatEngineFailure readFailure,
			TestContext.Current.CancellationToken);

		Assert.True(written, writeFailure.ToString());
		Assert.True(read, readFailure.ToString());
		Assert.Equal(value, readBack);
		Assert.Equal([PointerSize.Bit64, PointerSize.Bit64], port.Widths);
	}

	[Fact]
	[Trait("Qualification", "Q33")]
	public void AnAddressBatchWriteStopsAtTheFirstValueWiderThanAThirtyTwoBitTarget()
	{
		PointerWidthPort port = new()
		{
			Is64Bit = false,
			ConfiguredPointerSize = 4
		};

		MemoryPrimitiveBatchWriteOutcome outcome = CreateClient(port).WritePrimitiveBatchDetailed(
			new MemoryPrimitiveBatchWriteRequest<Address>([
				new MemoryAddressValue<Address>(TestAddress, new Address(0x401000)),
				new MemoryAddressValue<Address>(TestAddress + 4, new Address(0x1_0000_0000)),
				new MemoryAddressValue<Address>(TestAddress + 8, new Address(0x402000))
			]), TestContext.Current.CancellationToken);

		Assert.Equal(1, outcome.CompletedCount);
		Assert.Equal(1, outcome.FailedIndex);
		Assert.Equal(MemoryBatchWriteEffectState.Partial, outcome.EffectState);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, outcome.Failure!.Value.Kind);
		Assert.Equal(CheatEngineHostEffect.Started, outcome.Failure.Value.HostEffect);
		Assert.Equal(2, port.PointerWrites);
		Assert.Single(port.Pointers);
	}

	[Theory]
	[Trait("Qualification", "Q21")]
	[InlineData("ReadPrimitive")]
	[InlineData("WritePrimitive")]
	[InlineData("ReadBatch")]
	[InlineData("WriteBatch")]
	[InlineData("PointerChain")]
	public void AnUnknownTargetBitnessRefusesEveryPointerPathBeforeAnyAccess(string path)
	{
		PointerWidthPort port = new()
		{
			UnknownBitness = true
		};
		MemoryClient client = CreateClient(port);
		CancellationToken token = TestContext.Current.CancellationToken;

		CheatEngineFailure failure = path switch
		{
			"ReadPrimitive" => Refused(client.TryReadPrimitive(TestAddress, out Address _, out CheatEngineFailure f,
				token), f),
			"WritePrimitive" => Refused(client.TryWritePrimitive(TestAddress, TestAddress, out CheatEngineFailure f,
				token), f),
			"ReadBatch" => client.ReadPrimitiveBatchDetailed(new MemoryPrimitiveBatchReadRequest<Address>([TestAddress]),
				token).Failure!.Value,
			"WriteBatch" => client.WritePrimitiveBatchDetailed(new MemoryPrimitiveBatchWriteRequest<Address>([
				new MemoryAddressValue<Address>(TestAddress, TestAddress)
			]), token).Failure!.Value,
			"PointerChain" => Refused(client.TryResolvePointerChain(new PointerChainRequest(TestAddress, [0x10L]),
				out _, out CheatEngineFailure f, token), f),
			_ => throw new ArgumentOutOfRangeException(nameof(path), path, null)
		};

		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(0, port.PointerReads + port.PointerWrites + port.ByteReads + port.ByteWrites);
	}

	[Fact]
	[Trait("Qualification", "Q21")]
	public void ACodecThatStopsOnAnUnknownTargetBitnessReportsTheRefusalItsContextRecorded()
	{
		// Core never refuses a codec: reading the context's unknown bitness records why the width is unknown, and a
		// codec that then returns false with the default failure reports that refusal. Whether a codec still accesses
		// memory is its own decision; this one stops, so nothing is read.
		PointerWidthPort port = new()
		{
			UnknownBitness = true
		};
		FactCodec codec = new();

		bool succeeded = CreateClient(port).TryRead(new MemoryReadRequest<int>(TestAddress, codec), out _,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(PointerSize.Unknown, codec.Bitness);
		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
	}

	[Fact]
	public void AnAddressPrimitiveWithoutASelectedTargetIsRefusedAsTargetNotAttached()
	{
		PointerWidthPort port = new()
		{
			ProcessId = 0
		};

		bool succeeded = CreateClient(port).TryReadPrimitive(TestAddress, out Address _,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.TargetNotAttached, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(0, port.PointerReads);
	}

	[Fact]
	public void UnobservableConfiguredPointerSizeDoesNotRefusePointerOperations()
	{
		// No evidence of a mismatch: the Client keeps the process width and proceeds.
		PointerWidthPort port = new()
		{
			ConfiguredPointerSizeFails = true,
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
		// An SDK fault while the context observes the width reaches the codec as the exact instance the context threw:
		// Core converts it. An application-owned CheatEngineOperationException with the same shape is not that instance
		// and is rethrown unchanged.
		InvalidOperationException sdkFault = new("detached while the width was observed");
		PointerWidthPort faulting = new()
		{
			TargetFault = sdkFault
		};
		CheatEngineOperationException applicationFault = (CheatEngineOperationException) new CheatEngineFailure(
			CheatEngineFailureKind.TargetNotAttached, "Memory.Read", "application-owned").ToException(TestContext.Current.CancellationToken);

		bool succeeded = CreateClient(faulting).TryRead(new MemoryReadRequest<int>(TestAddress, new FactCodec()), out _,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);
		CheatEngineOperationException rethrown = Assert.Throws<CheatEngineOperationException>(() =>
			CreateClient(new PointerWidthPort()).TryRead(
				new MemoryReadRequest<int>(TestAddress, new FactCodec { Throw = applicationFault }), out _, out _,
				TestContext.Current.CancellationToken));

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Same(sdkFault, failure.Exception);
		Assert.Same(applicationFault, rethrown);
	}

	private static MemoryClient CreateClient(PointerWidthPort port)
	{
		return new MemoryClient(new ThreadBoundDispatcher(), InertCoreLifetime.Create(), port);
	}

	private static CheatEngineFailure Refused(bool succeeded, CheatEngineFailure failure)
	{
		Assert.False(succeeded);
		return failure;
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

		internal PointerSize Bitness
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

		internal bool? Differs
		{
			get;
			private set;
		}

		public bool TryRead(IMemoryReadContext context, Address address, out int value, out CheatEngineFailure failure)
		{
			failure = default;
			if (Throw is not null)
			{
				throw Throw;
			}

			Bitness = context.Bitness;
			if (!Bitness.IsKnown)
			{
				// The context recorded why the width is unknown; returning false reports it.
				value = 0;
				return false;
			}

			ConfiguredPointerSizeBytes = context.ConfiguredPointerSizeBytes;
			ConfiguredPointerSize = context.ConfiguredPointerSize;
			Differs = context.ConfiguredPointerSizeDiffersFromBitness;
			if (ReadBytes && !context.TryReadBytes(address, new byte[Bitness.Bytes], out _))
			{
				value = 0;
				return false;
			}

			value = Bitness.Bytes;
			return true;
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in int value, out CheatEngineFailure failure)
		{
			failure = default;
			return true;
		}
	}

	/// <summary>An x64 target (PID 42) whose facts, pointers and failures are configurable.</summary>
	private sealed class PointerWidthPort : TargetObservationDouble, IMemoryCodecContextPort
	{
		/// <summary>Sets Cheat Engine's selected PID: zero is no target, 4294967295 the file-as-process sentinel.</summary>
		internal long ProcessId
		{
			init
			{
				TargetStatus = value switch
				{
					0 => ProcessOperationStatus.TargetNotAttached,
					4294967295L => ProcessOperationStatus.FileAsProcessTarget,
					_ => ProcessOperationStatus.Success
				};
				Target = TargetObservations.Create((int) Math.Clamp(value, 1, int.MaxValue),
					Target.Bitness == PointerSize.Bit64, configuredPointerSizeBytes: Target.ConfiguredPointerSizeBytes);
			}
		}

		internal bool Is64Bit
		{
			init => Target = TargetObservations.Create(Target.ProcessId.Value, value,
				configuredPointerSizeBytes: Target.ConfiguredPointerSizeBytes);
		}

		internal int ConfiguredPointerSize
		{
			init => Target = TargetObservations.Create(Target.ProcessId.Value, Target.Bitness == PointerSize.Bit64,
				configuredPointerSizeBytes: value);
		}

		/// <summary>Makes getPointerSize raise: the full observation narrows and the configured size stays unknown.</summary>
		internal bool ConfiguredPointerSizeFails
		{
			init
			{
				if (value)
				{
					TargetStatus = TargetObservations.LuaFailure;
					ConfiguredStatus = TargetObservations.LuaFailure;
				}
			}
		}

		/// <summary>Makes every selected-PID read raise.</summary>
		internal bool ProcessIdFails
		{
			init
			{
				if (value)
				{
					TargetStatus = TargetObservations.LuaFailure;
					CurrentReads = [(TargetObservations.LuaFailure, 0)];
				}
			}
		}

		/// <summary>Makes the closing selected-PID read of the narrowed observation raise.</summary>
		internal bool ClosingProcessIdFails
		{
			init
			{
				if (value)
				{
					TargetStatus = TargetObservations.LuaFailure;
					CurrentReads = [(ProcessOperationStatus.Success, 42), (TargetObservations.LuaFailure, 0)];
				}
			}
		}

		internal byte[] Bytes
		{
			get;
			init;
		} = new byte[8];

		/// <summary>Reports a selected target whose bitness Cheat Engine did not establish.</summary>
		internal bool UnknownBitness
		{
			init
			{
				if (value)
				{
					Target = new TargetArchitectureObservation(Target.ProcessId, TargetBackend.LocalProcess,
						PointerSize.Unknown, true, false, false, 0, null);
				}
			}
		}

		internal Dictionary<Address, Address> Pointers
		{
			get;
		} = [];

		/// <summary>Gets the pointer width passed to every pointer read and write, in call order.</summary>
		internal List<PointerSize> Widths
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

		public bool TryReadBytes(Address address, Span<byte> destination, out int written,
			out MemoryAccessFailure failure)
		{
			ByteReads++;
			Bytes.AsSpan(0, destination.Length).CopyTo(destination);
			written = destination.Length;
			failure = MemoryAccessFailure.None;
			return true;
		}

		public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source, out MemoryAccessFailure failure)
		{
			ByteWrites++;
			failure = MemoryAccessFailure.None;
			return true;
		}

		public bool TryReadPrimitive<T>(Address address, out T value, out MemoryAccessFailure failure)
		{
			Assert.NotEqual(typeof(Address), typeof(T));
			value = default!;
			failure = MemoryAccessFailure.None;
			return true;
		}

		public bool TryWritePrimitive<T>(Address address, T value, out MemoryAccessFailure failure)
		{
			Assert.NotEqual(typeof(Address), typeof(T));
			failure = MemoryAccessFailure.None;
			return true;
		}

		/// <summary>Behaves like <c>TargetMemory.TryReadPointer(Address, PointerSize, …)</c> over <see cref="Pointers" />.</summary>
		public bool TryReadPointer(Address address, PointerSize pointerSize, out Address value,
			out MemoryAccessFailure failure)
		{
			PointerReads++;
			Widths.Add(pointerSize);
			Address pointer = Pointers.GetValueOrDefault(address);
			failure = !pointerSize.IsKnown
				? MemoryAccessFailure.PointerWidthUnknown
				: pointerSize == PointerSize.Bit32 && pointer.Value > uint.MaxValue
					? MemoryAccessFailure.PointerValueExceedsTargetWidth
					: MemoryAccessFailure.None;
			value = failure == MemoryAccessFailure.None ? pointer : default;
			return failure == MemoryAccessFailure.None;
		}

		/// <summary>
		///     Behaves like <c>TargetMemory.TryWritePointer(Address, Address, PointerSize, …)</c>: a value wider than a
		///     32-bit target is refused before anything is stored.
		/// </summary>
		public bool TryWritePointer(Address address, Address value, PointerSize pointerSize,
			out MemoryAccessFailure failure)
		{
			PointerWrites++;
			Widths.Add(pointerSize);
			failure = !pointerSize.IsKnown
				? MemoryAccessFailure.PointerWidthUnknown
				: pointerSize == PointerSize.Bit32 && value.Value > uint.MaxValue
					? MemoryAccessFailure.PointerValueExceedsTargetWidth
					: MemoryAccessFailure.None;
			if (failure == MemoryAccessFailure.None)
			{
				Pointers[address] = value;
			}

			return failure == MemoryAccessFailure.None;
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
