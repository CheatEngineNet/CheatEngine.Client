using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.Workflows;

/// <summary>
/// The Debug CI leg merges the per-module coverage reports and fails when a shipping assembly's line coverage drops
/// below its floor in <c>eng/coverage-baseline.json</c> (eng/ci/Test-CoverageBaseline.ps1). These tests keep the floors
/// file aligned with the shipping assemblies and the merge tool pinned to the collector's version line.
/// </summary>
public sealed class CoverageBaselineTests
{
	private const string BaselinePath = "eng/coverage-baseline.json";
	private const string ToolManifestPath = ".config/dotnet-tools.json";

	private static readonly string[] _shippingFolders = ["src/", "libs/", "source-generators/"];

	[Fact]
	public void CoverageBaselineListsExactlyTheShippingAssemblies()
	{
		using JsonDocument baseline = ReadJson(BaselinePath);
		string[] listed = baseline.RootElement.GetProperty("assemblies").EnumerateObject()
			.Select(static property => property.Name)
			.Order(StringComparer.Ordinal)
			.ToArray();

		Assert.Equal(MeasurableShippingAssemblies(), listed);
	}

	[Fact]
	public void CoverageFloorsArePercentagesAndTheToleranceIsExplicit()
	{
		using JsonDocument baseline = ReadJson(BaselinePath);
		JsonElement root = baseline.RootElement;

		Assert.Equal("cheatengine-coverage-baseline/v0", root.GetProperty("schema").GetString());
		JsonElement tolerance = root.GetProperty("tolerance");
		Assert.Equal(JsonValueKind.Number, tolerance.ValueKind);
		Assert.InRange(tolerance.GetDouble(), 0.0, 2.0);

		foreach (JsonProperty assembly in root.GetProperty("assemblies").EnumerateObject())
		{
			string[] metrics = assembly.Value.EnumerateObject().Select(static metric => metric.Name).ToArray();
			Assert.True(metrics.SequenceEqual(["line"]),
				$"{assembly.Name} must declare exactly a 'line' floor; found {string.Join(", ", metrics)}.");

			JsonElement line = assembly.Value.GetProperty("line");
			Assert.Equal(JsonValueKind.Number, line.ValueKind);
			double floor = line.GetDouble();
			Assert.InRange(floor, 0.0, 100.0);
			Assert.True(Math.Round(floor, 1) == floor,
				$"{assembly.Name} floor {floor.ToString(CultureInfo.InvariantCulture)} must have at most one decimal (the CI suggestion floors to 0.1).");
		}
	}

	[Fact]
	public void CoverageToolIsPinnedInTheLocalToolManifest()
	{
		using JsonDocument manifest = ReadJson(ToolManifestPath);
		JsonElement root = manifest.RootElement;
		Assert.True(root.GetProperty("isRoot").GetBoolean(), $"{ToolManifestPath} must be the root manifest.");

		JsonElement tool = root.GetProperty("tools").GetProperty("dotnet-coverage");
		string version = tool.GetProperty("version").GetString() ?? string.Empty;
		Assert.Matches(new Regex(@"^\d+\.\d+\.\d+$"), version);
		Assert.False(tool.GetProperty("rollForward").GetBoolean(), "The coverage tool runs on the runtime it was built for.");

		XDocument packages = XDocument.Load(Path.Combine(RepositoryRoot.Path, "Directory.Packages.props"));
		XElement collector = Assert.Single(packages.Descendants("PackageVersion"),
			static element => (string?) element.Attribute("Include") == "Microsoft.Testing.Extensions.CodeCoverage");
		Assert.Equal((string?) collector.Attribute("Version"), version);

		string script = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "eng/ci/Test-CoverageBaseline.ps1"));
		Assert.Contains("dotnet tool restore", script, StringComparison.Ordinal);
		Assert.Contains("dotnet tool run dotnet-coverage merge", script, StringComparison.Ordinal);
	}

	/// <summary>
	/// The solution projects under src/, libs/ and source-generators/ that compile at least one source file. The
	/// CheatEngine.Client facade has none (it only aggregates package dependencies), so no coverage can exist for it.
	/// </summary>
	private static string[] MeasurableShippingAssemblies()
	{
		XDocument solution = XDocument.Load(RepositoryRoot.SolutionPath);
		List<string> assemblies = [];
		foreach (XElement project in solution.Descendants("Project"))
		{
			string path = ((string?) project.Attribute("Path") ?? string.Empty).Replace('\\', '/');
			if (!_shippingFolders.Any(folder => path.StartsWith(folder, StringComparison.Ordinal)))
			{
				continue;
			}

			string folderPath = Path.GetDirectoryName(Path.Combine(RepositoryRoot.Path, path))!;
			bool hasSource = Directory.EnumerateFiles(folderPath, "*.cs", SearchOption.AllDirectories)
				.Any(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
										StringComparison.Ordinal) &&
									!file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
										StringComparison.Ordinal));
			if (hasSource)
			{
				assemblies.Add(Path.GetFileNameWithoutExtension(path));
			}
		}

		return [.. assemblies.Order(StringComparer.Ordinal)];
	}

	private static JsonDocument ReadJson(string relativePath)
	{
		return JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, relativePath)));
	}
}
