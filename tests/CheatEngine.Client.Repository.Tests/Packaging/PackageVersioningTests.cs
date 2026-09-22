using System.Text.Json;
using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.Packaging;

/// <summary>Versions that ship inside or alongside the packages come from one declaration each.</summary>
public sealed partial class PackageVersioningTests
{
	private const string GeneratorProject =
		"source-generators/CheatEngine.Client.SourceGenerators.Lua/CheatEngine.Client.SourceGenerators.Lua.csproj";

	private const string TemplateManifest =
		"templates/CheatEngine.Client.Templates/content/CheatEngine.Plugin/.template.config/template.json";

	private static readonly string[] _roslynPackages = ["Microsoft.CodeAnalysis.CSharp", "Microsoft.CodeAnalysis.Analyzers"];

	[Fact]
	public void RoslynPinsEqualTheDeclaredComponentFloor()
	{
		string floor = BuildProperty("CheatEngineClientRoslynComponentFloor");
		XDocument generator = LoadXml(GeneratorProject);
		List<string> offenders = [];
		foreach (string package in _roslynPackages)
		{
			string? pinned = CentralVersion(package);
			if (pinned != floor)
			{
				offenders.Add($"Directory.Packages.props pins {package} '{pinned}', expected the floor '{floor}'");
			}

			if (!generator.Descendants("PackageReference").Any(reference => (string?) reference.Attribute("Include") == package
																		  && (string?) reference.Attribute("PrivateAssets") == "all"))
			{
				offenders.Add($"{GeneratorProject} must reference {package} with PrivateAssets=\"all\"");
			}
		}

		Assert.True(offenders.Count == 0,
			$"The Lua generator packed in CheatEngine.Client.Hosting must be compiled against the declared Roslyn floor (CHEATENGINECLIENT9020):{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
	}

	[Fact]
	public void TemplateSdkConstraintDoesNotExceedTheRepositorySdk()
	{
		using JsonDocument globalJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "global.json")));
		using JsonDocument template = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, TemplateManifest)));
		Version repositorySdk = Version.Parse(globalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString()!);

		List<Version> lowerBounds = [];
		foreach (JsonProperty constraint in template.RootElement.GetProperty("constraints").EnumerateObject())
		{
			if (constraint.Value.GetProperty("type").GetString() == "sdk-version")
			{
				Match range = LowerBound().Match(constraint.Value.GetProperty("args").GetString()!);
				Assert.True(range.Success, $"The sdk-version constraint '{constraint.Name}' must have an inclusive lower bound.");
				lowerBounds.Add(Version.Parse(range.Groups["version"].Value));
			}
		}

		Version lowerBound = Assert.Single(lowerBounds);
		Assert.True(lowerBound <= repositorySdk,
			$"The template requires .NET SDK {lowerBound}, above the SDK the repository builds and tests with ({repositorySdk}).");
	}

	internal static XDocument LoadXml(string repositoryRelativePath)
	{
		return XDocument.Load(Path.Combine(RepositoryRoot.Path, repositoryRelativePath));
	}

	internal static string BuildProperty(string name)
	{
		XElement element = Assert.Single(LoadXml("Directory.Build.props").Descendants(name));
		return element.Value.Trim();
	}

	internal static string? CentralVersion(string packageId)
	{
		XElement? element = LoadXml("Directory.Packages.props").Descendants("PackageVersion")
			.SingleOrDefault(version => (string?) version.Attribute("Include") == packageId);
		return (string?) element?.Attribute("Version");
	}

	[GeneratedRegex(@"^\[(?<version>\d+\.\d+\.\d+),", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex LowerBound();
}
