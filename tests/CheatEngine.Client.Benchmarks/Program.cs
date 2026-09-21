using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

using CheatEngine.Client.Benchmarks;

return BenchmarkEntryPoint.Run(args);

internal static class BenchmarkEntryPoint
{
	public static int Run(string[] args)
	{
		string artifactsDirectory = BenchmarkArtifactsDirectory.Resolve(args);
		IConfig config = DefaultConfig.Instance
			.WithArtifactsPath(artifactsDirectory)
			.AddExporter(JsonExporter.Full);

		Summary[] summaries = BenchmarkSwitcher
			.FromAssembly(typeof(Program).Assembly)
			.Run(args, config)
			.ToArray();

		if (summaries.Length == 0)
		{
			return BenchmarkInvocation.IsInformational(args) ? 0 : 1;
		}

		if (summaries.HasError())
		{
			return 1;
		}

		BenchmarkSuiteMetadata.WriteTo(artifactsDirectory);
		return 0;
	}
}

internal static class BenchmarkArtifactsDirectory
{
	private const string DefaultDirectoryName = "BenchmarkDotNet.Artifacts";

	public static string Resolve(IReadOnlyList<string> args)
	{
		ArgumentNullException.ThrowIfNull(args);

		string artifactsPath = Path.Combine(Environment.CurrentDirectory, DefaultDirectoryName);
		for (int index = 0; index < args.Count; index++)
		{
			if (TryReadArtifactsPath(args, ref index, out string? specifiedPath))
			{
				artifactsPath = specifiedPath!;
			}
		}

		return new DirectoryInfo(artifactsPath).FullName;
	}

	private static bool TryReadArtifactsPath(
		IReadOnlyList<string> args,
		ref int index,
		out string? artifactsPath)
	{
		string argument = args[index]!;
		if (argument.Equals("-a", StringComparison.OrdinalIgnoreCase) ||
			argument.Equals("--artifacts", StringComparison.OrdinalIgnoreCase))
		{
			artifactsPath = index + 1 < args.Count ? args[++index] : null;
			return artifactsPath is not null;
		}

		return TryReadAssignedArtifactsPath(argument, "-a=", out artifactsPath) ||
			TryReadAssignedArtifactsPath(argument, "--artifacts=", out artifactsPath);
	}

	private static bool TryReadAssignedArtifactsPath(
		string argument,
		string prefix,
		out string? artifactsPath)
	{
		if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
		{
			artifactsPath = argument[prefix.Length..];
			return true;
		}

		artifactsPath = null;
		return false;
	}
}

internal static class BenchmarkInvocation
{
	public static bool IsInformational(IReadOnlyList<string> args)
	{
		ArgumentNullException.ThrowIfNull(args);

		return args.Any(static argument =>
			argument.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
			argument.Equals("-?", StringComparison.Ordinal) ||
			argument.Equals("--version", StringComparison.OrdinalIgnoreCase) ||
			argument.Equals("--info", StringComparison.OrdinalIgnoreCase) ||
			argument.Equals("--list", StringComparison.OrdinalIgnoreCase) ||
			argument.StartsWith("--list=", StringComparison.OrdinalIgnoreCase));
	}
}

internal static class BenchmarkSummaryExtensions
{
	/// <summary>
	/// Determines whether BenchmarkDotNet 0.15.8 reported a critical validation failure or an unsuccessful benchmark.
	/// </summary>
	/// <remarks>
	/// Version 0.15.8 does not expose the <c>HasError()</c> helper suggested by the review. Its public result
	/// surface exposes these two signals instead: <see cref="Summary.HasCriticalValidationErrors"/> and
	/// <see cref="BenchmarkReport.Success"/>.
	/// </remarks>
	public static bool HasError(this IEnumerable<Summary> summaries)
	{
		ArgumentNullException.ThrowIfNull(summaries);

		return summaries.Any(static summary =>
			summary.HasCriticalValidationErrors ||
			summary.Reports.Any(static report => !report.Success));
	}
}
