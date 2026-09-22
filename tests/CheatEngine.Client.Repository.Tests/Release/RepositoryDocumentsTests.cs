using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.Release;

/// <summary>The files a published repository needs exist and agree with the package metadata.</summary>
public sealed class RepositoryDocumentsTests
{
	private static readonly string[] _releaseCategories = ["Added", "Changed", "Security", "Deployment"];

	[Fact]
	public void LicenseIsMitAndMatchesThePackageLicenseExpression()
	{
		string[] license = File.ReadAllLines(Path.Combine(RepositoryRoot.Path, "LICENSE"));
		XDocument buildProperties = XDocument.Load(Path.Combine(RepositoryRoot.Path, "Directory.Build.props"));
		string? expression = buildProperties.Descendants("PackageLicenseExpression").SingleOrDefault()?.Value;

		Assert.True(license.Length > 2 && license[0] == "MIT License",
			"LICENSE must start with the line 'MIT License'.");
		Assert.True(expression == "MIT",
			$"Directory.Build.props declares PackageLicenseExpression '{expression}', but LICENSE is the MIT license.");
	}

	[Fact]
	public void ChangelogHasAnUnreleasedSectionWithTheFourReleaseCategories()
	{
		string[] changelog = File.ReadAllLines(Path.Combine(RepositoryRoot.Path, "CHANGELOG.md"));
		int start = Array.IndexOf(changelog, "## [Unreleased]");
		Assert.True(start >= 0, "CHANGELOG.md has no '## [Unreleased]' heading; the release workflow reads that exact syntax.");

		List<string> categories = [];
		for (int index = start + 1; index < changelog.Length && !changelog[index].StartsWith("## ", StringComparison.Ordinal); index++)
		{
			if (changelog[index].StartsWith("### ", StringComparison.Ordinal))
			{
				categories.Add(changelog[index][4..].Trim());
			}
		}

		Assert.True(categories.SequenceEqual(_releaseCategories),
			$"The [Unreleased] section must list exactly {string.Join(", ", _releaseCategories)} in that order (audit A21-17), but lists: {string.Join(", ", categories)}.");
	}

	[Fact]
	public void ReleasingDocumentsTheTrustedPublishingPolicyForTheClientPackageGlob()
	{
		string releasing = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "RELEASING.md"));
		string[] required = ["`CheatEngine.Client*`", "`release.yml`", "`nuget`", "`NUGET_USER`", "`CheatEngineNet`", "`CheatEngine.Client`"];
		List<string> missing = [];
		foreach (string value in required)
		{
			if (!releasing.Contains(value, StringComparison.Ordinal))
			{
				missing.Add(value);
			}
		}

		Assert.True(missing.Count == 0,
			$"RELEASING.md must document the nuget.org trusted publishing policy; it does not mention: {string.Join(", ", missing)}.");
	}
}
