using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

using BenchmarkDotNet.Running;

using CheatEngine.Client.Memory;

namespace CheatEngine.Client.Benchmarks;

/// <summary>Versioned metadata that makes a benchmark result comparable across future suite revisions.</summary>
public static partial class BenchmarkSuiteMetadata
{
	/// <summary>Gets the schema version for <c>CheatEngine.Client.Benchmarks.metadata.json</c>.</summary>
	public const int SchemaVersion = 1;

	/// <summary>Writes the suite descriptor beside BenchmarkDotNet's versioned result artifacts.</summary>
	/// <param name="artifactsDirectory">The BenchmarkDotNet artifact directory for the current run.</param>
	public static void WriteTo(string artifactsDirectory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(artifactsDirectory);

		BenchmarkSuiteDescriptor descriptor = new(
			SchemaVersion,
			typeof(MemoryAddressBuilder).Assembly.GetName().Version?.ToString() ?? "0.0.0.0",
			typeof(BenchmarkSwitcher).Assembly.GetName().Version?.ToString() ?? "0.0.0.0",
			RuntimeInformation.FrameworkDescription,
			DateTimeOffset.UtcNow,
			[
				new BenchmarkWorkloadDescriptor(
					"fluent-pure-builder",
					1,
					true,
					"Pure immutable builder construction after warm-up."),
				new BenchmarkWorkloadDescriptor(
					"lua-scalar-mapper",
					1,
					true,
					"Static abstract scalar mapping used by generated Lua operations."),
				new BenchmarkWorkloadDescriptor(
					"di-client-registration",
					1,
					false,
					"Informational allocation and elapsed-time baseline for DI composition."),
				new BenchmarkWorkloadDescriptor(
					"aob-materialization",
					1,
					false,
					"Client copy/parse/filter cost over a fake port; excludes CE scan time."),
				new BenchmarkWorkloadDescriptor(
					"aob-route-comparison",
					1,
					false,
					"Client cost of a module request on the global post-filter route and on the bounded route over a " +
					"fake port; excludes CE scan time."),
				new BenchmarkWorkloadDescriptor(
					"memory-batch",
					1,
					false,
					"Client admission, dispatch and outcome cost of a primitive batch over a fake port; excludes CE " +
					"memory access time.")
			]);
		string content =
			JsonSerializer.Serialize(descriptor, BenchmarkSuiteJsonContext.Default.BenchmarkSuiteDescriptor);
		File.WriteAllText(Path.Combine(artifactsDirectory, "CheatEngine.Client.Benchmarks.metadata.json"), content);
	}

	private sealed record BenchmarkSuiteDescriptor(
		int SchemaVersion,
		string ClientAssemblyVersion,
		string BenchmarkDotNetVersion,
		string Runtime,
		DateTimeOffset CreatedUtc,
		BenchmarkWorkloadDescriptor[] Workloads);

	private sealed record BenchmarkWorkloadDescriptor(
		string Id,
		int Version,
		bool RequiresZeroAllocationAfterWarmup,
		string Description);

	[JsonSerializable(typeof(BenchmarkSuiteDescriptor))]
	private sealed partial class BenchmarkSuiteJsonContext : JsonSerializerContext;
}
