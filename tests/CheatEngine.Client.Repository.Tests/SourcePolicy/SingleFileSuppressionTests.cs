using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.SourcePolicy;

/// <summary>
///     The repository reads an assembly file location in exactly one place:
///     <c>CheatEnginePluginBuilder.PluginDirectory</c> of Hosting, whose single-file warning (IL3000) is suppressed
///     under ADR-02 (Cheat Engine loads a managed plugin from its deployment folder, never from a single-file bundle).
///     The shipped code, the qualification harness, the fixtures and the tests all go through that property; any
///     other file-location read must be designed, not suppressed.
/// </summary>
public sealed partial class SingleFileSuppressionTests
{
	private const string PluginBuilderSource = "libs/CheatEngine.Client.Hosting/CheatEnginePluginBuilder.cs";

	/// <summary>This policy test, which names the diagnostic in order to look for it.</summary>
	private const string PolicySource =
		$"tests/CheatEngine.Client.Repository.Tests/SourcePolicy/{nameof(SingleFileSuppressionTests)}.cs";

	private static readonly string[] SourcePatterns = ["*.cs", "*.csproj", "*.props", "*.targets", "*.editorconfig"];

	[Fact]
	public void TheRepositorySuppressesIL3000ExactlyOnceForThePluginDirectory()
	{
		List<string> hits = [];
		foreach (string file in SourcePatterns.SelectMany(RepositoryRoot.EnumerateSourceFiles)
					 .Where(static path => !string.Equals(path, PolicySource, StringComparison.Ordinal)))
		{
			string[] lines = File.ReadAllLines(Path.Combine(RepositoryRoot.Path, file));
			for (int index = 0; index < lines.Length; index++)
			{
				if (Il3000().IsMatch(lines[index]))
				{
					hits.Add($"{file}:{index + 1}: {lines[index].Trim()}");
				}
			}
		}

		string hit = Assert.Single(hits);
		Assert.StartsWith(PluginBuilderSource + ":", hit, StringComparison.Ordinal);
		Assert.Contains("[UnconditionalSuppressMessage(\"SingleFile\", \"IL3000:", hit, StringComparison.Ordinal);
	}

	[Fact]
	public void TheSuppressionCoversOnlyThePluginDirectoryGetterAndCitesAdr02()
	{
		string source = File.ReadAllText(Path.Combine(RepositoryRoot.Path, PluginBuilderSource));
		int property = source.IndexOf("public string PluginDirectory", StringComparison.Ordinal);
		int suppression = source.IndexOf("IL3000", StringComparison.Ordinal);
		Match getterBody = GetterBody().Match(source, Math.Max(suppression, 0));
		int getter = getterBody.Success ? getterBody.Index : -1;
		int nextMember = source.IndexOf("internal ServiceProvider BuildServiceProvider", StringComparison.Ordinal);

		Assert.True(property >= 0 && property < suppression && suppression < getter && getter < nextMember,
			"The IL3000 suppression must sit on the PluginDirectory getter.");
		Assert.Contains("ADR-02", source[suppression..getter], StringComparison.Ordinal);
	}

	[GeneratedRegex(@"\bIL3000\b")]
	private static partial Regex Il3000();

	/// <summary>The body of an accessor; a word of the justification such as "target" never matches it.</summary>
	[GeneratedRegex(@"\bget\s*\{")]
	private static partial Regex GetterBody();
}
