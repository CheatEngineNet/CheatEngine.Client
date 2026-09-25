using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.SourcePolicy;

/// <summary>
///     The shipped Client reads an assembly file location in exactly one place: <c>CheatEnginePluginBuilder.PluginDirectory</c>
///     of Hosting, whose single-file warning (IL3000) is suppressed under ADR-02 (Cheat Engine loads a managed plugin from its
///     deployment folder, never from a single-file bundle). Any other file-location read must be designed, not suppressed.
/// </summary>
public sealed partial class SingleFileSuppressionTests
{
	private const string PluginBuilderSource = "libs/CheatEngine.Client.Hosting/CheatEnginePluginBuilder.cs";

	private static readonly string[] SourcePatterns = ["*.cs", "*.csproj", "*.props", "*.targets", "*.editorconfig"];

	[Fact]
	public void ShippedSourcesSuppressIL3000ExactlyOnceForThePluginDirectory()
	{
		List<string> hits = [];
		foreach (string file in SourcePatterns.SelectMany(RepositoryRoot.EnumerateSourceFiles)
					 .Where(static path => !path.StartsWith("tests/", StringComparison.Ordinal)))
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
		int getter = source.IndexOf("get", suppression, StringComparison.Ordinal);
		int nextMember = source.IndexOf("internal ServiceProvider BuildServiceProvider", StringComparison.Ordinal);

		Assert.True(property >= 0 && property < suppression && suppression < getter && getter < nextMember,
			"The IL3000 suppression must sit on the PluginDirectory getter.");
		Assert.Contains("ADR-02", source[suppression..getter], StringComparison.Ordinal);
	}

	[GeneratedRegex(@"\bIL3000\b")]
	private static partial Regex Il3000();
}
