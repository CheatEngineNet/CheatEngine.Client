namespace CheatEngine.Client.Repository.Tests.Documentation;

/// <summary>
/// Self-tests of the Markdown subset on in-memory text. They prove that the documentation gate fails on the regressions
/// it exists for (DOC-LNK-3: a change that introduces a broken link fails), independently of the committed files.
/// </summary>
public sealed class MarkdownDocumentTests
{
	[Fact]
	public void SlugFollowsGitHubRulesForPunctuationDotsAndDuplicates()
	{
		Assert.Equal("v010-capability-status", MarkdownDocument.Slug("v0.1.0 capability status"));
		Assert.Equal("packages-and-direct-sdk-reference", MarkdownDocument.Slug("Packages and direct SDK reference"));
		Assert.Equal("aot-trimming-and-deployment", MarkdownDocument.Slug("AOT, trimming, and deployment"));
		Assert.Equal("100---2026-09-20", MarkdownDocument.Slug("[1.0.0] - 2026-09-20"));
		Assert.Equal("the-dotnet-new-ceplugin-template", MarkdownDocument.Slug("The `dotnet new ceplugin` template"));

		MarkdownDocument document = MarkdownDocument.Parse("x.md", "# Run\n\ntext\n\n## Run\n\n### Run\n");

		string[] expected = ["run", "run-1", "run-2"];

		Assert.Equal(expected, document.Anchors.Order(StringComparer.Ordinal));
	}

	[Fact]
	public void MultiLineLinkTextReportsTheLineOfItsTarget()
	{
		const string text = "See the\n" +
							"[repository README](https://example.org/README.md) for installation and\n" +
							"deployment guidance, its\n" +
							"[package\n" +
							"architecture](../../README.md#packages)\n" +
							"section.\n";

		MarkdownDocument document = MarkdownDocument.Parse("src/x/README.md", text);

		Assert.Collection(document.Links,
			first =>
			{
				Assert.Equal(2, first.Line);
				Assert.Equal("https://example.org/README.md", first.Target);
			},
			second =>
			{
				Assert.Equal(5, second.Line);
				Assert.Equal(MarkdownLinkKind.Inline, second.Kind);
				Assert.Equal("package\narchitecture", second.Text);
				Assert.Equal("../../README.md#packages", second.Target);
			});
	}

	[Fact]
	public void LinksInsideFencesCodeSpansAndHtmlCommentsAreIgnored()
	{
		const string text = "```xml\n[fenced](fenced.md)\n```\n\n" +
							"~~~~\n[tilde](tilde.md)\n~~~~\n\n" +
							"Inline `[code](code.md)` and ``[double](double.md)`` spans.\n\n" +
							"<!-- [comment](comment.md)\n[still comment](comment2.md) -->\n\n" +
							"   ```powershell\n   [indented fence](indented.md)\n   ```\n\n" +
							"[kept](kept.md)\n";

		MarkdownDocument document = MarkdownDocument.Parse("x.md", text);

		MarkdownLink link = Assert.Single(document.Links);
		Assert.Equal("kept.md", link.Target);
		Assert.Equal(18, link.Line);
	}

	[Fact]
	public void ReferenceDefinitionsHtmlAttributesAndBadgeImagesAreExtracted()
	{
		const string text = "[![CI](https://img.example/badge.svg)](https://ci.example/run)\n\n" +
							"[label]: docs/README.md \"Title\"\n" +
							"[^1]: a footnote, not a link\n\n" +
							"<img src=\"images/logo.png\" alt=\"logo\"> <a href='RELEASING.md'>releasing</a>\n\n" +
							"<a id=\"custom-anchor\"></a>\n\n" +
							"[spaced](<path with spaces.md> \"title\") and [encoded](a%20b.md)\n";

		MarkdownDocument document = MarkdownDocument.Parse("x.md", text);
		string[] targets = document.Links.Select(static link => $"{link.Kind}:{link.Target}").ToArray();

		string[] expected =
		[
			"Inline:https://ci.example/run",
			"Image:https://img.example/badge.svg",
			"ReferenceDefinition:docs/README.md",
			"HtmlAttribute:images/logo.png",
			"HtmlAttribute:RELEASING.md",
			"Inline:path with spaces.md",
			"Inline:a b.md"
		];

		Assert.Equal(expected, targets);
		Assert.Contains("custom-anchor", document.Anchors);
	}

	[Fact]
	public void DeveloperPathsAreDetectedButIllustrativeRootsAndPlaceholdersAreNot()
	{
		const string text = "A D:\\CheatEngine\\x path.\n" +
							"```\ndotnet build -o C:\\Users\\a\\out\n```\n" +
							"A file:///c:/x URI.\n" +
							"A /home/a folder and a C:/Temp/y folder.\n" +
							"Deploy to C:\\CheatEngineDeploy\\MyPlugin.\n" +
							"Logs go to %APPDATA%\\TrainerLog.\n" +
							"See https://x.example/a and HKCU:Software.\n";

		MarkdownDocument document = MarkdownDocument.Parse("x.md", text);
		IReadOnlyList<MarkdownMatch> matches = document.FindLocalPaths(DocumentationConventions.IllustrativeLocalRoots);

		int[] expectedLines = [1, 3, 5, 5, 6, 6];

		Assert.Equal(expectedLines, matches.Select(static match => match.Line));
	}

	[Fact]
	public void BrokenExactCaseTargetIsReported()
	{
		string? resolved = RepositoryPaths.Resolve("docs/README.md", "../readme.md", out string? reason);
		string? exact = RepositoryPaths.Resolve("docs/README.md", "../README.md", out _);
		string? escaping = RepositoryPaths.Resolve("docs/README.md", "../../outside.md", out string? escapeReason);
		string? buildOutput = RepositoryPaths.Resolve("README.md", "artifacts/nuget/x.nupkg", out string? outputReason);

		Assert.Null(reason);
		Assert.Equal("readme.md", resolved);
		Assert.False(RepositoryPaths.ExistsWithExactCase(resolved!));
		Assert.True(RepositoryPaths.ExistsWithExactCase(exact!));
		Assert.True(RepositoryPaths.ExistsWithExactCase("tests/CheatEngine.Client.Repository.Tests"));
		Assert.False(RepositoryPaths.ExistsWithExactCase("Tests/CheatEngine.Client.Repository.Tests"));
		Assert.Null(escaping);
		Assert.Equal("escapes the repository", escapeReason);
		Assert.Null(buildOutput);
		Assert.NotNull(outputReason);
	}
}
