using BenchmarkDotNet.Attributes;

using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Memory;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Memory;
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Benchmarks;

/// <summary>
///     Measures only the Client cost of a primitive batch (audit A24-04): the budget admission, one dispatch, the
///     per-element port calls and the outcome materialization, over a fake port that answers from memory. Cheat Engine's
///     own cost per read or write is deliberately excluded (audit ch.24 "separate the costs"): it is a live-host
///     measurement, and this suite publishes no host number.
/// </summary>
/// <remarks>
///     The Address batch also pays its one target observation before the first element, as a real batch does; the
///     integer batches never observe the target.
/// </remarks>
[MemoryDiagnoser]
[BenchmarkCategory("MemoryBatch", "Informational")]
public class MemoryBatchBenchmarks
{
	private MemoryClient _client = null!;
	private MemoryPrimitiveBatchReadRequest<Address> _pointerReads;
	private MemoryPrimitiveBatchReadRequest<int> _reads;
	private MemoryPrimitiveBatchWriteRequest<int> _writes;

	/// <summary>Gets or sets the number of operations in each batch, up to the hard per-batch limit.</summary>
	[Params(1, 64, MemoryBatchLimits.MaximumOperationCount)]
	public int OperationCount
	{
		get;
		set;
	}

	/// <summary>Builds the batch requests and a memory client whose dispatcher runs inline over the fake port.</summary>
	[GlobalSetup]
	public void Setup()
	{
		Address[] addresses = new Address[OperationCount];
		MemoryAddressValue<int>[] values = new MemoryAddressValue<int>[OperationCount];
		for (int index = 0; index < OperationCount; index++)
		{
			addresses[index] = new Address(0x0040_0000UL + ((ulong) index * sizeof(int)));
			values[index] = new MemoryAddressValue<int>(addresses[index], index);
		}

		_reads = new MemoryPrimitiveBatchReadRequest<int>(addresses);
		_writes = new MemoryPrimitiveBatchWriteRequest<int>(values);
		_pointerReads = new MemoryPrimitiveBatchReadRequest<Address>(addresses);
		CoreLifetime lifetime = InlineCoreHost.CreateLifetime();
		_client = new MemoryClient(InlineCoreHost.CreateDispatcher(lifetime), lifetime, new InMemoryPort(),
			new MemoryResourceLimits());
	}

	/// <summary>Reads one homogeneous 32-bit integer batch and copies its immutable values.</summary>
	/// <returns>The completed count, so the JIT cannot discard the work.</returns>
	[Benchmark]
	public int ReadInt32Batch()
	{
		MemoryPrimitiveBatchReadOutcome<int> outcome = _client.ReadPrimitiveBatchDetailed(_reads);
		return outcome.IsSuccess
			? outcome.CompletedCount
			: throw new InvalidOperationException("The benchmark batch read unexpectedly failed.");
	}

	/// <summary>Writes one homogeneous 32-bit integer batch and reports its effect state.</summary>
	/// <returns>The completed count, so the JIT cannot discard the work.</returns>
	[Benchmark]
	public int WriteInt32Batch()
	{
		MemoryPrimitiveBatchWriteOutcome outcome = _client.WritePrimitiveBatchDetailed(_writes);
		return outcome.IsSuccess
			? outcome.CompletedCount
			: throw new InvalidOperationException("The benchmark batch write unexpectedly failed.");
	}

	/// <summary>Reads one Address batch, which observes the target width once before its first element.</summary>
	/// <returns>The completed count, so the JIT cannot discard the work.</returns>
	[Benchmark]
	public int ReadAddressBatch()
	{
		MemoryPrimitiveBatchReadOutcome<Address> outcome = _client.ReadPrimitiveBatchDetailed(_pointerReads);
		return outcome.IsSuccess
			? outcome.CompletedCount
			: throw new InvalidOperationException("The benchmark pointer batch read unexpectedly failed.");
	}

	/// <summary>An x64 local target whose every read returns zero and every write succeeds, without CheatEngine.SDK.</summary>
	private sealed class InMemoryPort : IMemoryCodecContextPort
	{
		private static readonly TargetArchitectureObservation Target = new(new TargetProcessId(42),
			TargetBackend.LocalProcess, PointerSize.Bit64, true, false, false, 0, sizeof(ulong));

		public ProcessOperationStatus ObserveCurrent(out CurrentProcessObservation observation)
		{
			observation = new CurrentProcessObservation(Target.ProcessId, Target.Bitness);
			return ProcessOperationStatus.Success;
		}

		public ProcessOperationStatus ObserveTargetArchitecture(out TargetArchitectureObservation observation)
		{
			observation = Target;
			return ProcessOperationStatus.Success;
		}

		public ProcessOperationStatus TryGetConfiguredPointerSize(out int rawBytes, out PointerSize pointerSize)
		{
			rawBytes = sizeof(ulong);
			pointerSize = PointerSize.Bit64;
			return ProcessOperationStatus.Success;
		}

		public bool TryReadBytes(Address address, Span<byte> destination, out int written,
			out MemoryAccessFailure failure)
		{
			destination.Clear();
			written = destination.Length;
			failure = MemoryAccessFailure.None;
			return true;
		}

		public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source, out MemoryAccessFailure failure)
		{
			failure = MemoryAccessFailure.None;
			return true;
		}

		public bool TryReadPrimitive<T>(Address address, out T value, out MemoryAccessFailure failure)
		{
			value = default!;
			failure = MemoryAccessFailure.None;
			return true;
		}

		public bool TryWritePrimitive<T>(Address address, T value, out MemoryAccessFailure failure)
		{
			failure = MemoryAccessFailure.None;
			return true;
		}

		public bool TryReadPointer(Address address, PointerSize pointerSize, out Address value,
			out MemoryAccessFailure failure)
		{
			value = default;
			failure = MemoryAccessFailure.None;
			return true;
		}

		public bool TryWritePointer(Address address, Address value, PointerSize pointerSize,
			out MemoryAccessFailure failure)
		{
			failure = MemoryAccessFailure.None;
			return true;
		}
	}
}
