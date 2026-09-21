using BenchmarkDotNet.Attributes;

using CheatEngine.Client.Memory;
using CheatEngine.SDK.Engine.Values;

using MemoryFluent = CheatEngine.Client.Memory.Memory;

namespace CheatEngine.Client.Benchmarks;

/// <summary>Measures allocation-free construction of immutable fluent address builders.</summary>
[MemoryDiagnoser(false)]
[RankColumn]
public class FluentBuilderBenchmarks
{
	private Address _address;

	/// <summary>Initializes a non-constant input so the JIT cannot fold the fluent operation.</summary>
	[GlobalSetup]
	public void Setup()
	{
		_address = 0x401000UL;
	}

	/// <summary>Measures one unbound pure-builder creation without a Client-to-SDK transition.</summary>
	[Benchmark]
	[BenchmarkCategory("PureBuilder", "AllocationGate")]
	public MemoryAddressBuilder CreateUnboundBuilder()
	{
		return MemoryFluent.At(_address);
	}

	/// <summary>Measures the builder construction and its copied address projection.</summary>
	[Benchmark]
	[BenchmarkCategory("PureBuilder", "AllocationGate")]
	public Address CreateAndProjectAddress()
	{
		return MemoryFluent.At(_address).Address;
	}
}
