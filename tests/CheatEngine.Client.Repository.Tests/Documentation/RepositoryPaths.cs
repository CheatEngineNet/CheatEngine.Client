using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.Documentation;

/// <summary>Repository file queries with the case sensitivity of GitHub, not of the Windows file system.</summary>
internal static class RepositoryPaths
{
	private static readonly string[] _buildOutputSegments = ["artifacts", "bin", "obj"];
	private static readonly string[] _packedProjectRoots = ["src/", "libs/", "templates/"];

	private static readonly Lazy<IReadOnlyDictionary<string, MarkdownDocument>> _documents =
		new(LoadDocuments, LazyThreadSafetyMode.ExecutionAndPublication);

	/// <summary>Every Markdown document the documentation rules apply to, keyed by repository-relative path.</summary>
	internal static IReadOnlyDictionary<string, MarkdownDocument> Documents => _documents.Value;

	/// <summary>
	/// Whether a repository-relative file or directory exists with exactly this spelling. Every segment is matched with an
	/// ordinal comparison against the directory listing, because GitHub resolves links case-sensitively while Windows
	/// does not; <see cref="File.Exists(string)"/> alone would accept <c>readme.md</c> for <c>README.md</c>.
	/// </summary>
	internal static bool ExistsWithExactCase(string repositoryRelativePath)
	{
		ArgumentNullException.ThrowIfNull(repositoryRelativePath);

		string current = RepositoryRoot.Path;
		foreach (string segment in repositoryRelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
		{
			if (!Directory.Exists(current))
			{
				return false;
			}

			string? match = null;
			foreach (string entry in Directory.EnumerateFileSystemEntries(current))
			{
				if (string.Equals(Path.GetFileName(entry), segment, StringComparison.Ordinal))
				{
					match = entry;
					break;
				}
			}

			if (match is null)
			{
				return false;
			}

			current = match;
		}

		return true;
	}

	/// <summary>
	/// Resolves the path part of a link target (no fragment, no query) against the document that contains it. A target
	/// that starts with <c>/</c> is relative to the repository root. The result is a normalized repository-relative path,
	/// or <see langword="null"/> with the reason when the target leaves the repository or points into build output.
	/// </summary>
	internal static string? Resolve(string documentPath, string targetPath, out string? reason)
	{
		ArgumentNullException.ThrowIfNull(documentPath);
		ArgumentNullException.ThrowIfNull(targetPath);

		List<string> segments = [];
		if (!targetPath.StartsWith('/'))
		{
			string[] documentSegments = documentPath.Split('/');
			segments.AddRange(documentSegments[..^1]);
		}

		foreach (string segment in targetPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
		{
			if (segment == ".")
			{
				continue;
			}

			if (segment == "..")
			{
				if (segments.Count == 0)
				{
					reason = "escapes the repository";
					return null;
				}

				segments.RemoveAt(segments.Count - 1);
				continue;
			}

			segments.Add(segment);
		}

		foreach (string segment in segments)
		{
			if (Array.IndexOf(_buildOutputSegments, segment) >= 0)
			{
				reason = "points into build output, which is never committed";
				return null;
			}
		}

		reason = null;
		return string.Join('/', segments);
	}

	/// <summary>
	/// The README files packed into the shipped packages: the sibling <c>README.md</c> of every project under
	/// <c>src/</c>, <c>libs/</c> and <c>templates/</c>, except template content and projects that are not packable.
	/// </summary>
	internal static IReadOnlyList<string> PackedReadmes()
	{
		List<string> readmes = [];
		foreach (string project in RepositoryRoot.EnumerateSourceFiles("*.csproj"))
		{
			if (!StartsWithAny(project, _packedProjectRoots)
				|| (project.StartsWith("templates/", StringComparison.Ordinal) && project.Contains("/content/", StringComparison.Ordinal)))
			{
				continue;
			}

			XDocument document = XDocument.Load(Path.Combine(RepositoryRoot.Path, project));
			bool notPackable = false;
			foreach (XElement element in document.Descendants("IsPackable"))
			{
				notPackable |= string.Equals(element.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase);
			}

			if (!notPackable)
			{
				readmes.Add(project[..(project.LastIndexOf('/') + 1)] + "README.md");
			}
		}

		readmes.Sort(StringComparer.Ordinal);
		return readmes;
	}

	private static bool StartsWithAny(string value, string[] prefixes)
	{
		foreach (string prefix in prefixes)
		{
			if (value.StartsWith(prefix, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	private static Dictionary<string, MarkdownDocument> LoadDocuments()
	{
		Dictionary<string, MarkdownDocument> documents = new(StringComparer.Ordinal);
		foreach (string file in RepositoryRoot.EnumerateSourceFiles("*.md"))
		{
			if (IsExcluded(file))
			{
				continue;
			}

			string text = File.ReadAllText(Path.Combine(RepositoryRoot.Path, file));
			documents.Add(file, MarkdownDocument.Parse(file, text));
		}

		return documents;
	}

	private static bool IsExcluded(string relativePath)
	{
		string[] segments = relativePath.Split('/');
		if (Array.IndexOf(DocumentationConventions.ExcludedRootEntries, segments[0]) >= 0)
		{
			return true;
		}

		foreach (string segment in segments)
		{
			if (Array.IndexOf(DocumentationConventions.ExcludedSegments, segment) >= 0)
			{
				return true;
			}
		}

		return false;
	}
}
