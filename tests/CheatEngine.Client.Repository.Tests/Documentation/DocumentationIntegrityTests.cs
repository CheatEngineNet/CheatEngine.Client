using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.Documentation;

/// <summary>
/// Blocking documentation rules (audit F14, ADR-12): a reader without the author's workspace can follow every link, and
/// the pages published on nuget.org and GitHub make no claim that the repository cannot back.
/// </summary>
public sealed partial class DocumentationIntegrityTests
{
	private const int RegexTimeoutMilliseconds = 1000;

	private static readonly Regex _thisRepositoryOnMain = new(
		@"https://github\.com/" + Regex.Escape(DocumentationConventions.RepositorySlug) +
		@"/(?:blob|tree)/main/(?<path>[^\s)\]>""'`#?]*)(?:#(?<fragment>[^\s)\]>""'`]*))?",
		RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(RegexTimeoutMilliseconds));

	private static readonly Regex _placeholder = new(DocumentationConventions.PlaceholderPattern,
		RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(RegexTimeoutMilliseconds));

	[Fact]
	public void EveryRelativeMarkdownLinkResolvesWithExactCasing()
	{
		List<string> offenders = [];
		foreach (MarkdownDocument document in RepositoryPaths.Documents.Values)
		{
			foreach (MarkdownLink link in document.Links)
			{
				if (link.Target.Length == 0)
				{
					offenders.Add($"{document.Path}:{link.Line} → (empty target)");
					continue;
				}

				if (IsExternal(link.Target) || link.Target.StartsWith('#'))
				{
					continue;
				}

				string path = PathPart(link.Target);
				string? resolved = RepositoryPaths.Resolve(document.Path, path, out string? reason);
				if (resolved is null)
				{
					offenders.Add($"{document.Path}:{link.Line} → {link.Target} ({reason})");
				}
				else if (!RepositoryPaths.ExistsWithExactCase(resolved))
				{
					offenders.Add($"{document.Path}:{link.Line} → {link.Target} (no file or directory '{resolved}' with this exact casing)");
				}
			}
		}

		AssertNoOffenders(offenders, "Relative Markdown links must resolve on GitHub, which is case-sensitive");
	}

	[Fact]
	public void EveryMarkdownAnchorMatchesAHeadingOfItsTargetPage()
	{
		List<string> offenders = [];
		foreach (MarkdownDocument document in RepositoryPaths.Documents.Values)
		{
			foreach (MarkdownLink link in document.Links)
			{
				int hash = link.Target.IndexOf('#', StringComparison.Ordinal);
				if (hash < 0 || IsExternal(link.Target))
				{
					continue;
				}

				string fragment = link.Target[(hash + 1)..];
				MarkdownDocument? target = FindTargetDocument(document, PathPart(link.Target));
				if (target is not null && !target.Anchors.Contains(fragment))
				{
					offenders.Add($"{document.Path}:{link.Line} → {link.Target} (no heading or anchor '{fragment}' in {target.Path})");
				}
			}
		}

		AssertNoOffenders(offenders, "Markdown anchors must match a GitHub heading slug or an explicit anchor of their target page");
	}

	[Fact]
	public void NoMarkdownFileContainsADeveloperLocalPath()
	{
		List<string> offenders = [];
		foreach (MarkdownDocument document in RepositoryPaths.Documents.Values)
		{
			foreach (MarkdownMatch match in document.FindLocalPaths(DocumentationConventions.IllustrativeLocalRoots))
			{
				offenders.Add($"{document.Path}:{match.Line} → {match.Text} (absolute local path)");
			}
		}

		AssertNoOffenders(offenders,
			"Documentation must not depend on a developer's machine (F14); use a repository path, a %VARIABLE% placeholder or the illustrative deployment root");
	}

	[Fact]
	public void NoMarkdownFileLinksTheRetiredDocsTree()
	{
		List<string> offenders = [];
		foreach (MarkdownDocument document in RepositoryPaths.Documents.Values)
		{
			foreach (MarkdownLink link in document.Links)
			{
				string? resolved = IsExternal(link.Target)
					? null
					: RepositoryPaths.Resolve(document.Path, PathPart(link.Target), out _);
				foreach (string prefix in DocumentationConventions.RetiredTreePrefixes)
				{
					if (link.Target.Contains(prefix, StringComparison.Ordinal)
						|| (resolved is not null && resolved.StartsWith(prefix, StringComparison.Ordinal)))
					{
						offenders.Add($"{document.Path}:{link.Line} → {link.Target} (links the retired {prefix} tree)");
					}
				}
			}

			if (document.Path == DocumentationConventions.RetiredReferenceExemption)
			{
				continue;
			}

			for (int index = 0; index < document.Lines.Count; index++)
			{
				if (RetiredTreeMention().IsMatch(document.Lines[index]))
				{
					offenders.Add($"{document.Path}:{index + 1} → {document.Lines[index].Trim()} (names a retired page outside {DocumentationConventions.RetiredReferenceExemption})");
				}
			}
		}

		AssertNoOffenders(offenders, "The docs/ tree deleted by d06fd2e is neither linked nor restored; see the old -> new table in docs/README.md");
	}

	[Fact]
	public void RoadmapWorkItemIdentifiersAreNotLinks()
	{
		MarkdownDocument roadmap = RepositoryPaths.Documents["ROADMAP.md"];
		List<string> offenders = [];
		foreach (MarkdownLink link in roadmap.Links)
		{
			if (WorkItemIdentifier().IsMatch(link.Text))
			{
				offenders.Add($"ROADMAP.md:{link.Line} → {link.Target} (work item {link.Text} is a link)");
			}
		}

		AssertNoOffenders(offenders, "CLI-0xx work item pages were not restored and issues are disabled; keep the identifier as plain text");
	}

	[Fact]
	public void AbsoluteLinksToThisRepositoryOnMainResolveOnTheCurrentTree()
	{
		List<string> offenders = [];
		foreach (MarkdownDocument document in RepositoryPaths.Documents.Values)
		{
			foreach (MarkdownMatch line in document.ProseLines())
			{
				foreach (Match match in _thisRepositoryOnMain.Matches(line.Text))
				{
					string path = Uri.UnescapeDataString(match.Groups["path"].Value).TrimEnd('/');
					if (path.AsSpan().IndexOfAny('<', '{', '*') >= 0)
					{
						continue;
					}

					if (!RepositoryPaths.ExistsWithExactCase(path))
					{
						offenders.Add($"{document.Path}:{line.Line} → {match.Value} (no '{path}' on the current tree)");
						continue;
					}

					string fragment = match.Groups["fragment"].Value;
					if (fragment.Length > 0 && path.EndsWith(".md", StringComparison.Ordinal)
						&& RepositoryPaths.Documents.TryGetValue(path, out MarkdownDocument? target)
						&& !target.Anchors.Contains(fragment))
					{
						offenders.Add($"{document.Path}:{line.Line} → {match.Value} (no heading or anchor '{fragment}' in {path})");
					}
				}
			}
		}

		AssertNoOffenders(offenders,
			$"Absolute links to {DocumentationConventions.RepositorySlug} on main must resolve on this tree, so packed READMEs keep working");
	}

	[Fact]
	public void PackedReadmesContainOnlyAbsoluteLinks()
	{
		IReadOnlyList<string> readmes = RepositoryPaths.PackedReadmes();
		Assert.True(readmes.Count == DocumentationConventions.PackedReadmeCount,
			$"Expected {DocumentationConventions.PackedReadmeCount} packed READMEs, found {readmes.Count}: {string.Join(", ", readmes)}.");

		List<string> offenders = [];
		foreach (string readme in readmes)
		{
			Assert.True(RepositoryPaths.Documents.ContainsKey(readme), $"The packed README '{readme}' does not exist.");
			foreach (MarkdownLink link in RepositoryPaths.Documents[readme].Links)
			{
				if (!link.Target.StartsWith("https://", StringComparison.Ordinal))
				{
					offenders.Add($"{readme}:{link.Line} → {link.Target} (not an absolute https:// URL)");
				}
			}
		}

		AssertNoOffenders(offenders,
			"nuget.org cannot resolve repository-relative links in a packed README (https://learn.microsoft.com/nuget/nuget-org/package-readme-on-nuget-org)");
	}

	[Fact]
	public void EveryRebuiltDocsPageStartsWithTheRecreatedHeader()
	{
		List<string> offenders = [];
		foreach (MarkdownDocument document in RepositoryPaths.Documents.Values)
		{
			if (!document.Path.StartsWith("docs/", StringComparison.Ordinal))
			{
				continue;
			}

			List<string> firstLines = [];
			foreach (string line in document.Lines)
			{
				if (line.Trim().Length > 0)
				{
					firstLines.Add(line.Trim());
					if (firstLines.Count == 2)
					{
						break;
					}
				}
			}

			if (!firstLines.Contains(DocumentationConventions.RecreatedHeader))
			{
				offenders.Add($"{document.Path}:1 → {string.Join(" | ", firstLines)} (missing header)");
			}
		}

		AssertNoOffenders(offenders, $"Every page under docs/ starts with '{DocumentationConventions.RecreatedHeader}'");
	}

	[Fact]
	public void PlaceholderPagesNameTheirOwningLotAndWave()
	{
		List<string> offenders = [];
		foreach (MarkdownDocument document in RepositoryPaths.Documents.Values)
		{
			for (int index = 0; index < document.Lines.Count; index++)
			{
				string line = document.Lines[index].Trim();
				if (line.StartsWith("Status: placeholder", StringComparison.Ordinal) && !_placeholder.IsMatch(line))
				{
					offenders.Add($"{document.Path}:{index + 1} → {line}");
				}
			}
		}

		AssertNoOffenders(offenders, $"A placeholder line must match {DocumentationConventions.PlaceholderPattern}");
	}

	[Fact]
	public void DocsIndexLinksEveryTopLevelPageAndFolder()
	{
		string docs = Path.Combine(RepositoryRoot.Path, "docs");
		MarkdownDocument index = RepositoryPaths.Documents["docs/README.md"];
		List<string> linked = [];
		foreach (MarkdownLink link in index.Links)
		{
			if (!IsExternal(link.Target) && !link.Target.StartsWith('#'))
			{
				string? resolved = RepositoryPaths.Resolve(index.Path, PathPart(link.Target), out _);
				if (resolved is not null)
				{
					linked.Add(resolved);
				}
			}
		}

		List<string> offenders = [];
		foreach (string entry in Directory.EnumerateFileSystemEntries(docs))
		{
			string relative = "docs/" + Path.GetFileName(entry);
			if (relative == "docs/README.md")
			{
				continue;
			}

			bool found = false;
			foreach (string target in linked)
			{
				found |= target == relative || target.StartsWith(relative + "/", StringComparison.Ordinal);
			}

			if (!found)
			{
				offenders.Add($"{relative} (not linked from docs/README.md)");
			}
		}

		AssertNoOffenders(offenders, "docs/README.md is the index of the docs/ folder");
	}

	[Fact]
	public void NoMarkdownFileClaimsCompleteCoverageOrUniversalSupport()
	{
		List<string> offenders = [];
		foreach (MarkdownDocument document in RepositoryPaths.Documents.Values)
		{
			foreach (MarkdownMatch line in document.ProseLines())
			{
				foreach (Match claim in UniversalClaim().Matches(line.Text))
				{
					if (!Negation().IsMatch(line.Text[..claim.Index]))
					{
						offenders.Add($"{document.Path}:{line.Line} → {line.Text.Trim()} ('{claim.Value}')");
					}
				}
			}
		}

		AssertNoOffenders(offenders,
			"Documentation never claims complete coverage or universal support (A00-03, A20-15, A20-18); state the measured scope instead");
	}

	private static MarkdownDocument? FindTargetDocument(MarkdownDocument document, string path)
	{
		if (path.Length == 0)
		{
			return document;
		}

		string? resolved = RepositoryPaths.Resolve(document.Path, path, out _);
		return resolved is not null && resolved.EndsWith(".md", StringComparison.Ordinal)
			   && RepositoryPaths.Documents.TryGetValue(resolved, out MarkdownDocument? target)
			? target
			: null;
	}

	private static bool IsExternal(string target)
	{
		return target.StartsWith("//", StringComparison.Ordinal) || UriScheme().IsMatch(target);
	}

	private static string PathPart(string target)
	{
		int end = target.AsSpan().IndexOfAny('#', '?');
		return end < 0 ? target : target[..end];
	}

	private static void AssertNoOffenders(List<string> offenders, string rule)
	{
		offenders.Sort(StringComparer.Ordinal);
		Assert.True(offenders.Count == 0,
			$"{rule}. Offenders ({offenders.Count}):{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
	}

	[GeneratedRegex("^[A-Za-z][A-Za-z0-9+.-]*:", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex UriScheme();

	[GeneratedRegex("docs/(?:engineering|adr)/[A-Za-z]", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex RetiredTreeMention();

	[GeneratedRegex(@"CLI-\d{3}", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex WorkItemIdentifier();

	[GeneratedRegex(@"\b100 ?%|\bcomplete coverage\b|\bfull coverage\b|\bfully supported\b|\bfully compatible\b",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex UniversalClaim();

	[GeneratedRegex(@"\b(?:not|never|no|without)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
		RegexTimeoutMilliseconds)]
	private static partial Regex Negation();
}
