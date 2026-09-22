using CheatEngine.Client.Repository.Tests.Infrastructure;
using CheatEngine.Client.Repository.Tests.Workflows;

namespace CheatEngine.Client.Repository.Tests.Toolchain;

/// <summary>
/// CI runs every test module in one <c>dotnet test --solution</c> call with a single option set. Microsoft.Testing.Platform
/// fails a module with exit code 5 when an option belongs to an extension the module does not reference, so the test
/// profile (<c>eng/Tests.props</c>) must give every <c>*.Tests</c> project the extension of every option CI passes.
/// </summary>
public sealed class TestProfileTests
{
	/// <summary>The options of the CI test command and the Microsoft.Testing.Platform extension that owns each.</summary>
	private static readonly Dictionary<string, string> _optionOwners = new(StringComparer.Ordinal)
	{
		["--report-trx"] = "Microsoft.Testing.Extensions.TrxReport",
		["--coverage"] = "Microsoft.Testing.Extensions.CodeCoverage",
		["--report-gh"] = "Microsoft.Testing.Extensions.GitHubActionsReport",
		["--hangdump"] = "Microsoft.Testing.Extensions.HangDump",
		["--crashdump"] = "Microsoft.Testing.Extensions.CrashDump"
	};

	[Fact]
	public void TestModulesReferenceEveryExtensionTheCiCommandUses()
	{
		WorkflowStep test = Assert.Single(WorkflowFile.Load(".github/workflows/ci.yml").Job("build-test").Steps,
			static step => step.Run.Contains("dotnet test", StringComparison.Ordinal));
		IReadOnlyList<string> tokens = Yaml.Tokens(test.Run);

		XDocument profile = XDocument.Load(Path.Combine(RepositoryRoot.Path, "eng/Tests.props"));
		XElement testModules = Assert.Single(profile.Root!.Elements("ItemGroup"),
			static group => ((string?) group.Attribute("Condition") ?? string.Empty).Contains(".EndsWith('.Tests')",
				StringComparison.Ordinal));
		HashSet<string> referenced = testModules.Elements("PackageReference")
			.Select(static reference => (string?) reference.Attribute("Include") ?? string.Empty)
			.ToHashSet(StringComparer.Ordinal);

		XDocument packages = XDocument.Load(Path.Combine(RepositoryRoot.Path, "Directory.Packages.props"));
		HashSet<string> versioned = packages.Descendants("PackageVersion")
			.Select(static version => (string?) version.Attribute("Include") ?? string.Empty)
			.ToHashSet(StringComparer.Ordinal);

		foreach ((string option, string package) in _optionOwners)
		{
			Assert.True(tokens.Contains(option),
				$"The CI test command no longer passes {option}; remove it from this table (and {package} from eng/Tests.props if nothing else needs it).");
			Assert.True(referenced.Contains(package),
				$"CI passes {option}, which {package} owns, but eng/Tests.props does not reference it for every *.Tests project (exit code 5).");
			Assert.True(versioned.Contains(package), $"{package} has no version in Directory.Packages.props.");
		}

		foreach (string token in tokens.Where(static token => token.StartsWith("--report-", StringComparison.Ordinal) ||
															  token.StartsWith("--hangdump", StringComparison.Ordinal) ||
															  token.StartsWith("--crash", StringComparison.Ordinal) ||
															  token.StartsWith("--coverage", StringComparison.Ordinal)))
		{
			Assert.True(_optionOwners.Keys.Any(option => token == option || token.StartsWith(option + "-", StringComparison.Ordinal)),
				$"The CI test command passes {token}; add its owning extension to this table and to eng/Tests.props.");
		}
	}
}
