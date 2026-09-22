using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.Packaging;

/// <summary>The package metadata declared in the repository is complete and has one source per value.</summary>
public sealed class PackageMetadataTests
{
	/// <summary>The seven packable projects, one per shipped package id.</summary>
	private static readonly string[] _packableProjects =
	[
		"libs/CheatEngine.Client.Abstractions/CheatEngine.Client.Abstractions.csproj",
		"libs/CheatEngine.Client.Core/CheatEngine.Client.Core.csproj",
		"libs/CheatEngine.Client.Extensions.DependencyInjection/CheatEngine.Client.Extensions.DependencyInjection.csproj",
		"libs/CheatEngine.Client.Fluent/CheatEngine.Client.Fluent.csproj",
		"libs/CheatEngine.Client.Hosting/CheatEngine.Client.Hosting.csproj",
		"src/CheatEngine.Client/CheatEngine.Client.csproj",
		"templates/CheatEngine.Client.Templates/CheatEngine.Client.Templates.csproj"
	];

	[Fact]
	public void EveryPackableProjectDeclaresADistinctDescription()
	{
		Dictionary<string, string> descriptions = new(StringComparer.Ordinal);
		List<string> offenders = [];
		foreach (string project in _packableProjects)
		{
			XElement[] declared = PackageVersioningTests.LoadXml(project).Descendants("Description").ToArray();
			if (declared.Length != 1 || declared[0].Value.Trim().Length < 40)
			{
				offenders.Add($"{project} must declare one meaningful <Description>");
				continue;
			}

			if (!descriptions.TryAdd(declared[0].Value.Trim(), project))
			{
				offenders.Add($"{project} repeats the description of {descriptions[declared[0].Value.Trim()]}");
			}
		}

		foreach (string props in (string[]) ["eng/Shipping.props", "eng/Templates.props", "Directory.Build.props"])
		{
			XDocument document = PackageVersioningTests.LoadXml(props);
			if (document.Descendants("PackageDescription").Any() || document.Descendants("Description").Any())
			{
				offenders.Add($"{props} declares a shared description, which would hide the project descriptions");
			}
		}

		Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void EveryPackagingProfileEmbedsTheSameSpdxSbom()
	{
		List<string> offenders = [];
		foreach (string props in (string[]) ["eng/Shipping.props", "eng/Templates.props"])
		{
			XDocument document = PackageVersioningTests.LoadXml(props);
			string?[] values =
			[
				document.Descendants("GenerateSBOM").SingleOrDefault()?.Value,
				document.Descendants("SbomGenerationPackageSupplier").SingleOrDefault()?.Value,
				document.Descendants("SbomGenerationNamespaceBaseUri").SingleOrDefault()?.Value
			];
			if (values[0] != "true" || values[1] != "CheatEngineNet" || values[2] != "https://github.com/CheatEngineNet/CheatEngine.Client")
			{
				offenders.Add($"{props} must set GenerateSBOM=true, SbomGenerationPackageSupplier=CheatEngineNet and the repository namespace, found {string.Join(", ", values)}");
			}

			if (!document.Descendants("PackageReference").Any(static reference => (string?) reference.Attribute("Include") == "Microsoft.Sbom.Targets"
																				  && (string?) reference.Attribute("PrivateAssets") == "all"))
			{
				offenders.Add($"{props} must reference Microsoft.Sbom.Targets with PrivateAssets=\"all\"");
			}
		}

		Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
		Assert.NotNull(PackageVersioningTests.CentralVersion("Microsoft.Sbom.Targets"));
	}

	[Fact]
	public void PackageCopyrightMatchesTheLicenseHolderLine()
	{
		string[] license = File.ReadAllLines(Path.Combine(RepositoryRoot.Path, "LICENSE"));
		string holderLine = Assert.Single(license, static line => line.StartsWith("Copyright (c) ", StringComparison.Ordinal));

		Assert.Equal(holderLine, PackageVersioningTests.BuildProperty("Copyright"));
		Assert.Equal("https://github.com/CheatEngineNet/CheatEngine.Client", PackageVersioningTests.BuildProperty("PackageProjectUrl"));
		Assert.Equal("https://github.com/CheatEngineNet/CheatEngine.Client", PackageVersioningTests.BuildProperty("RepositoryUrl"));
		Assert.Equal("$(PackageProjectUrl)/blob/main/CHANGELOG.md", PackageVersioningTests.BuildProperty("PackageReleaseNotes"));
	}

	[Fact]
	public void PackagesAreWrittenToTheContinuousIntegrationFolderWithSourceLinkFromTheDotNetSdk()
	{
		List<string> offenders = [];
		foreach (string props in (string[]) ["eng/Shipping.props", "eng/Templates.props"])
		{
			string? output = PackageVersioningTests.LoadXml(props).Descendants("PackageOutputPath").SingleOrDefault()?.Value;
			if (output != "$(ArtifactsPath)/nuget")
			{
				offenders.Add($"{props} writes packages to '{output}', not $(ArtifactsPath)/nuget (the CI Release leg and smoke tests read artifacts/nuget)");
			}
		}

		foreach (XElement reference in PackageVersioningTests.LoadXml("Directory.Packages.props").Descendants())
		{
			string? include = (string?) reference.Attribute("Include");
			if (include is not null && include.StartsWith("Microsoft.SourceLink.", StringComparison.Ordinal))
			{
				offenders.Add($"Directory.Packages.props references {include}; the .NET SDK provides Source Link (https://learn.microsoft.com/dotnet/core/compatibility/sdk/8.0/source-link)");
			}
		}

		Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
	}
}
