using System.Globalization;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.Client.Repository.Tests.Governance;

/// <summary>
/// Hardened Dependabot configuration (PR-CQ-08, A21-36, CI-BOTH-6): a cooldown on every ecosystem (zizmor
/// dependabot-cooldown threshold: 7 days, https://docs.zizmor.sh/audits/#dependabot-cooldown), NuGet, GitHub Actions
/// (workflows and composite actions) and the .NET SDK covered, and the ignores that protect frozen decisions: the
/// Client stays on CheatEngine.SDK 1.x, Roslyn moves with the generator floor, SDK-implicit packages move with
/// global.json.
/// Options: https://docs.github.com/en/code-security/dependabot/working-with-dependabot/dependabot-options-reference
/// </summary>
public sealed class DependabotConfigurationTests
{
	private const string ConfigurationPath = ".github/dependabot.yml";
	private const int MinimumCooldownDays = 7;

	private static readonly YamlMappingNode Configuration = GovernanceFile.LoadYaml(ConfigurationPath);

	[Fact]
	public void EveryEcosystemWaitsAtLeastSevenDays()
	{
		foreach (YamlMappingNode update in Updates())
		{
			YamlMappingNode cooldown = Assert.IsType<YamlMappingNode>(GovernanceFile.Child(update, "cooldown"));
			Assert.NotNull(GovernanceFile.Scalar(cooldown, "default-days"));
			foreach (KeyValuePair<YamlNode, YamlNode> entry in cooldown.Children)
			{
				string key = ((YamlScalarNode) entry.Key).Value!;
				if (key.EndsWith("-days", StringComparison.Ordinal))
				{
					int days = int.Parse(((YamlScalarNode) entry.Value).Value!, NumberStyles.None, CultureInfo.InvariantCulture);
					Assert.True(days >= MinimumCooldownDays, $"{Ecosystem(update)}: cooldown {key} is only {days} days.");
				}
			}
		}
	}

	[Fact]
	public void DependabotCoversNuGetActionsAndTheDotNetSdk()
	{
		Assert.Equal("2", GovernanceFile.Scalar(Configuration, "version"));
		Assert.Equal(["dotnet-sdk", "github-actions", "nuget"], Updates().Select(Ecosystem).Order(StringComparer.Ordinal));
		Assert.Equal("/", GovernanceFile.Scalar(Update("nuget"), "directory"));
		Assert.Equal("/", GovernanceFile.Scalar(Update("dotnet-sdk"), "directory"));
		Assert.All(Updates(), update => Assert.NotNull(GovernanceFile.Mapping(update, "schedule")));
	}

	[Fact]
	public void CompositeActionsAreUpdatedWithTheWorkflows()
	{
		YamlMappingNode actions = Update("github-actions");

		Assert.Null(GovernanceFile.Child(actions, "directory"));
		Assert.Equal(["/", "/.github/actions/*"], GovernanceFile.Strings(GovernanceFile.Child(actions, "directories")));
	}

	[Fact]
	public void CheatEngineSdkMajorUpdatesAreIgnored()
	{
		YamlMappingNode ignore = Ignore("nuget", "CheatEngine.SDK");

		Assert.Equal(["version-update:semver-major"], GovernanceFile.Strings(GovernanceFile.Child(ignore, "update-types")));
		Assert.Null(GovernanceFile.Child(ignore, "versions"));
	}

	[Fact]
	public void RoslynAndSdkImplicitPackagesAreIgnored()
	{
		string[] pinned =
		[
			"Microsoft.CodeAnalysis.CSharp",
			"Microsoft.CodeAnalysis.Analyzers",
			"Microsoft.NET.ILLink.Tasks",
			"Microsoft.DotNet.ILCompiler",
			"runtime.*.Microsoft.DotNet.ILCompiler"
		];

		foreach (string name in pinned)
		{
			Assert.True(Ignore("nuget", name).Children.Count == 1, $"'{name}' must be ignored for every update and version.");
		}
	}

	[Fact]
	public void DotNetSdkMajorUpdatesAreIgnored()
	{
		YamlMappingNode ignore = Assert.Single(Ignores("dotnet-sdk"));

		Assert.Equal("*", GovernanceFile.Scalar(ignore, "dependency-name"));
		Assert.Equal(["version-update:semver-major"], GovernanceFile.Strings(GovernanceFile.Child(ignore, "update-types")));
	}

	[Fact]
	public void NoCommitMessagePrefixIsConfigured()
	{
		// A prefix produces "deps: ..." subjects, which the pull request title policy forbids.
		Assert.All(Updates(), update => Assert.False(GovernanceFile.Has(update, "commit-message"), Ecosystem(update)));
	}

	[Fact]
	public void DependabotNeverAllowsExternalCodeExecution()
	{
		string configuration = GovernanceFile.ReadText(ConfigurationPath);

		Assert.DoesNotContain("insecure-external-code-execution", configuration, StringComparison.Ordinal);
		Assert.False(GovernanceFile.Has(Configuration, "registries"));
	}

	[Fact]
	public void DependabotLabelsAreKnownRepositoryLabels()
	{
		foreach (YamlMappingNode update in Updates())
		{
			IReadOnlyList<string> labels = GovernanceFile.Strings(GovernanceFile.Child(update, "labels"));

			Assert.Contains("dependencies", labels);
			Assert.All(labels, label => Assert.Contains(label, RepositoryLabels.Known));
		}
	}

	private static IReadOnlyList<YamlMappingNode> Updates()
	{
		return [.. (GovernanceFile.Sequence(Configuration, "updates")?.Children ?? []).Cast<YamlMappingNode>()];
	}

	private static YamlMappingNode Update(string ecosystem)
	{
		return Assert.Single(Updates(), update => Ecosystem(update) == ecosystem);
	}

	private static IReadOnlyList<YamlMappingNode> Ignores(string ecosystem)
	{
		return [.. (GovernanceFile.Sequence(Update(ecosystem), "ignore")?.Children ?? []).Cast<YamlMappingNode>()];
	}

	private static YamlMappingNode Ignore(string ecosystem, string dependency)
	{
		return Assert.Single(Ignores(ecosystem), entry => GovernanceFile.Scalar(entry, "dependency-name") == dependency);
	}

	private static string Ecosystem(YamlMappingNode update)
	{
		return GovernanceFile.Scalar(update, "package-ecosystem") ?? string.Empty;
	}
}
