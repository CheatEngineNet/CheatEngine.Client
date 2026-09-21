using BenchmarkDotNet.Attributes;

using CheatEngine.Client.Lua;

namespace CheatEngine.Client.Benchmarks;

/// <summary>Measures direct static dispatch through the copied scalar Lua mapper contract.</summary>
[MemoryDiagnoser(false)]
[RankColumn]
public class LuaMapperBenchmarks
{
	private int _source;

	/// <summary>Initializes a non-constant scalar input for the mapper benchmark.</summary>
	[GlobalSetup]
	public void Setup()
	{
		_source = 42;
	}

	/// <summary>Measures the mapper path used by generated scalar Lua operations after they receive an SDK scalar.</summary>
	[Benchmark]
	[BenchmarkCategory("LuaMapper", "AllocationGate")]
	public int MapCopiedScalar()
	{
		return Map<IdentityInt32Mapper, int>(_source);
	}

	private static TResult Map<TMapper, TResult>(TResult source)
		where TMapper : ILuaResultMapper<TResult, TResult>
	{
		return TMapper.Map(source);
	}

	private readonly struct IdentityInt32Mapper : ILuaResultMapper<int, int>
	{
		public static int Map(int source)
		{
			return source;
		}
	}
}
