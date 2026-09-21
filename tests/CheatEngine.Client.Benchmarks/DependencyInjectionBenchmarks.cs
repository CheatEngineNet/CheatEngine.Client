using BenchmarkDotNet.Attributes;

using CheatEngine.Client.Extensions.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;

namespace CheatEngine.Client.Benchmarks;

/// <summary>Tracks the intentionally informative cost of starting Client dependency-injection composition.</summary>
[MemoryDiagnoser(false)]
[RankColumn]
public class DependencyInjectionBenchmarks
{
	private int _invocationCount;

	/// <summary>Measures explicit registration of the Client service graph without activating a Cheat Engine host.</summary>
	[Benchmark]
	[BenchmarkCategory("DependencyInjection", "Informational")]
	public int AddClientServices()
	{
		ServiceCollection services = new();
		_ = services.AddCheatEngineClient();
		return services.Count + _invocationCount++;
	}
}
