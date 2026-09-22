namespace CheatEngine.Client.Repository.Tests.Documentation;

/// <summary>
/// The repository-specific values of the documentation rules. The SDK repository applies the same rules with its own
/// values; only this class differs between the two.
/// </summary>
internal static class DocumentationConventions
{
	/// <summary>The GitHub owner and repository whose <c>blob/main</c> links must resolve on the current tree.</summary>
	internal const string RepositorySlug = "CheatEngineNet/CheatEngine.Client";

	/// <summary>The header line that every rebuilt page under <c>docs/</c> carries in its first two non-blank lines.</summary>
	internal const string RecreatedHeader = "> Recreated 2026-09 from the audit, not the historical docs/ tree.";

	/// <summary>A placeholder page names the lot and the wave that replace it.</summary>
	internal const string PlaceholderPattern =
		@"^Status: placeholder — content arrives with (C-[A-Z]+(-[A-Z]+)*|DOCS-FINAL) \((V[1-4][a-c]?)\)$";

	/// <summary>The only page allowed to name the retired paths: its retired-documentation table maps them.</summary>
	internal const string RetiredReferenceExemption = "docs/README.md";

	/// <summary>The number of README files packed into the seven packages (six libraries and the template package).</summary>
	internal const int PackedReadmeCount = 7;

	/// <summary>The <c>docs/</c> subtrees deleted by <c>d06fd2e</c>; they are never linked or restored.</summary>
	internal static readonly string[] RetiredTreePrefixes = ["docs/engineering/", "docs/adr/"];

	/// <summary>
	/// Illustrative deployment target, allowed by DOC-LNK-3: the Hosting and generated-plugin READMEs show where a
	/// managed plugin folder could be deployed. It is an example value, not a path of the author's machine.
	/// </summary>
	internal static readonly string[] IllustrativeLocalRoots = [@"C:\CheatEngineDeploy\"];

	/// <summary>Folders that tools create next to sources and that hold generated Markdown.</summary>
	internal static readonly string[] ExcludedSegments = ["BenchmarkDotNet.Artifacts", "node_modules"];

	/// <summary>Git-ignored developer files at the repository root that a local checkout can contain.</summary>
	internal static readonly string[] ExcludedRootEntries = ["CLAUDE.md", ".claude"];
}
