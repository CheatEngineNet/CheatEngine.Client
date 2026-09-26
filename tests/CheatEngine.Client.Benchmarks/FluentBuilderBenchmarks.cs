using System.Reflection;

using BenchmarkDotNet.Attributes;

using CheatEngine.Client.Memory;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Benchmarks;

/// <summary>Measures allocation-free construction of immutable fluent address builders.</summary>
[MemoryDiagnoser(false)]
[RankColumn]
public class FluentBuilderBenchmarks
{
	private Address _address;
	private IMemoryClient _memory = null!;

	/// <summary>
	///     Initializes a non-constant input so the JIT cannot fold the fluent operation, and the memory service the
	///     builders are bound to; building never calls that service.
	/// </summary>
	[GlobalSetup]
	public void Setup()
	{
		_address = 0x401000UL;
		_memory = DispatchProxy.Create<IMemoryClient, UnusedMemoryProxy>();
	}

	/// <summary>Measures one bound pure-builder creation without a Client-to-SDK transition.</summary>
	[Benchmark]
	[BenchmarkCategory("PureBuilder", "AllocationGate")]
	public MemoryAddressBuilder CreateBoundBuilder()
	{
		return _memory.At(_address);
	}

	/// <summary>Measures the builder construction and its copied address projection.</summary>
	[Benchmark]
	[BenchmarkCategory("PureBuilder", "AllocationGate")]
	public Address CreateAndProjectAddress()
	{
		return _memory.At(_address).Address;
	}

	/// <summary>The memory service a measured builder is bound to; a benchmark never runs a terminal.</summary>
	public class UnusedMemoryProxy : DispatchProxy
	{
		/// <inheritdoc />
		protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
		{
			throw new NotSupportedException("Building a Fluent memory operation must not call the memory service.");
		}
	}
}
