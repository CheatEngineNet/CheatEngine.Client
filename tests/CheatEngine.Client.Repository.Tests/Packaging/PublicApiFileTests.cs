using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.Packaging;

/// <summary>
///     The PublicAPI baselines of the shipping libraries stay truthful before and after the first release: every library
///     declares both files, entries are ordinally sorted, and nothing counts as shipped until the release pull request
///     promotes a dated CHANGELOG.md release.
/// </summary>
/// <remarks>
///     The promotion is the <c>## Release X.Y.Z</c> section that the release pull request adds to
///     <c>AnalyzerReleases.Shipped.md</c> in the commit that moves every <c>PublicAPI.Unshipped.txt</c> into
///     <c>PublicAPI.Shipped.txt</c> (RELEASING.md, "Prepare a release"). A dated CHANGELOG heading is not the
///     promotion: the release section is dated before the pull request ships its API.
/// </remarks>
public sealed partial class PublicApiFileTests
{
	private const string NullableHeader = "#nullable enable";

	private const string ChangelogPath = "CHANGELOG.md";

	private const int RegexTimeoutMilliseconds = 1000;

	/// <summary>
	///     Files that still suppress RS0026 or RS0027 around an overload group. The list may only shrink: reshape the
	///     overloads instead of suppressing the rule, then remove the file from this list.
	/// </summary>
	private static readonly string[] PendingOverloadSuppressions = [];

	[Fact]
	public void EveryShippingLibraryHasBothPublicApiFiles()
	{
		List<string> offenders = [];
		foreach (string project in ShippingProjects())
		{
			string directory = Path.GetDirectoryName(project)!;
			bool hasShipped = File.Exists(Path.Combine(RepositoryRoot.Path, directory, "PublicAPI.Shipped.txt"));
			bool hasUnshipped = File.Exists(Path.Combine(RepositoryRoot.Path, directory, "PublicAPI.Unshipped.txt"));
			if (ShipsNoAssembly(project))
			{
				if (hasShipped || hasUnshipped)
				{
					offenders.Add($"{directory} packs no assembly, so it must not carry PublicAPI files");
				}

				continue;
			}

			if (!hasShipped || !hasUnshipped)
			{
				offenders.Add($"{directory} must declare both PublicAPI.Shipped.txt and PublicAPI.Unshipped.txt (RS0048)");
			}
		}

		Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void PublicApiEntriesAreOrdinallySorted()
	{
		List<string> offenders = [];
		foreach (string file in PublicApiFiles())
		{
			string[] lines = File.ReadAllLines(Path.Combine(RepositoryRoot.Path, file));
			if (lines.Length == 0 || lines[0] != NullableHeader)
			{
				offenders.Add($"{file} must start with '{NullableHeader}'");
				continue;
			}

			string[] entries = lines[1..];
			if (entries.Any(string.IsNullOrWhiteSpace))
			{
				offenders.Add($"{file} contains a blank line");
			}

			string[] sorted = [.. entries];
			Array.Sort(sorted, StringComparer.Ordinal);
			if (!entries.SequenceEqual(sorted, StringComparer.Ordinal))
			{
				int first = Enumerable.Range(0, entries.Length).First(index => entries[index] != sorted[index]);
				offenders.Add($"{file} is not ordinally sorted; first out-of-order entry at line {first + 2}: {entries[first]}");
			}

			if (entries.Distinct(StringComparer.Ordinal).Count() != entries.Length)
			{
				offenders.Add($"{file} declares an entry twice");
			}
		}

		Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void NoRemovedEntriesBeforeTheFirstRelease()
	{
		if (PromotedReleases().Length > 0)
		{
			return;
		}

		List<string> offenders = [];
		foreach (string file in PublicApiFiles())
		{
			string[] lines = File.ReadAllLines(Path.Combine(RepositoryRoot.Path, file));
			offenders.AddRange(lines.Where(static line => line.StartsWith("*REMOVED*", StringComparison.Ordinal))
				.Select(line => $"{file}: {line}"));
		}

		Assert.True(offenders.Count == 0,
			"No release has been promoted, so an API is deleted, never marked *REMOVED*:" + Environment.NewLine +
			string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void ShippedIsEmptyUntilTheReleasePullRequestPromotesADatedRelease()
	{
		string[] promoted = PromotedReleases();
		if (promoted.Length > 0)
		{
			string[] undated = FindUndatedPromotions(promoted,
				File.ReadAllLines(Path.Combine(RepositoryRoot.Path, ChangelogPath)));
			Assert.True(undated.Length == 0,
				"AnalyzerReleases.Shipped.md promotes a release that CHANGELOG.md does not record as " +
				$"'## [X.Y.Z] - YYYY-MM-DD': {string.Join(", ", undated)}.");
			return;
		}

		List<string> offenders = [];
		foreach (string file in PublicApiFiles().Where(static file => file.EndsWith("/PublicAPI.Shipped.txt",
					 StringComparison.Ordinal)))
		{
			string[] lines = File.ReadAllLines(Path.Combine(RepositoryRoot.Path, file));
			if (!lines.SequenceEqual([NullableHeader], StringComparer.Ordinal))
			{
				offenders.Add(file);
			}
		}

		Assert.True(offenders.Count == 0,
			"No AnalyzerReleases.Shipped.md has a '## Release X.Y.Z' section yet, so every PublicAPI.Shipped.txt " +
			$"must contain only '{NullableHeader}'. The release pull request promotes Unshipped and the analyzer " +
			"rules once, in one commit, as its last API commit: " + string.Join(", ", offenders));
	}

	[Fact]
	public void ThePromotionIsAShippedAnalyzerReleaseThatTheChangelogDates()
	{
		// Until the release pull request promotes a release, the facts above never take their promoted path.
		Assert.Empty(FindPromotedReleases(["; Shipped analyzer releases", "", "## Releases to come"]));
		Assert.Equal(["1.0.0", "1.1.0"],
			FindPromotedReleases(["## Release 1.0.0", "", "### New Rules", "## Release 1.1.0", "### Removed Rules"]));

		string[] changelog = ["## [Unreleased]", "### Added", "## [1.0.0] - 2026-09-25", "- first"];
		Assert.Empty(FindUndatedPromotions(["1.0.0"], changelog));
		Assert.Equal(["1.1.0", "1.0"], FindUndatedPromotions(["1.0.0", "1.1.0", "1.0"], changelog));
		Assert.Equal(["1.0.0"], FindUndatedPromotions(["1.0.0"], ["## [Unreleased]", "## [1.0.0]", "- undated"]));
	}

	[Fact]
	public void PendingOverloadSuppressionsOnlyShrink()
	{
		string[] suppressing = RepositoryRoot.EnumerateSourceFiles("*.cs")
			.Where(static file => file.StartsWith("libs/", StringComparison.Ordinal) ||
								  file.StartsWith("src/", StringComparison.Ordinal))
			.Where(static file => OverloadSuppression().IsMatch(File.ReadAllText(Path.Combine(RepositoryRoot.Path, file))))
			.Order(StringComparer.Ordinal)
			.ToArray();

		string[] added = [.. suppressing.Except(PendingOverloadSuppressions, StringComparer.Ordinal)];
		string[] resolved = [.. PendingOverloadSuppressions.Except(suppressing, StringComparer.Ordinal)];
		Assert.True(added.Length == 0,
			"Reshape the overloads instead of suppressing RS0026/RS0027 in: " + string.Join(", ", added));
		Assert.True(resolved.Length == 0,
			"These files no longer suppress RS0026/RS0027; remove them from PendingOverloadSuppressions: " +
			string.Join(", ", resolved));
	}

	private static IEnumerable<string> ShippingProjects()
	{
		return RepositoryRoot.EnumerateSourceFiles("*.csproj")
			.Where(static project => project.StartsWith("libs/", StringComparison.Ordinal) ||
									 project.StartsWith("src/", StringComparison.Ordinal))
			.Order(StringComparer.Ordinal);
	}

	private static bool ShipsNoAssembly(string project)
	{
		XDocument document = XDocument.Load(Path.Combine(RepositoryRoot.Path, project));
		return document.Descendants("IncludeBuildOutput")
			.Any(static element => string.Equals(element.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase));
	}

	private static IEnumerable<string> PublicApiFiles()
	{
		return RepositoryRoot.EnumerateSourceFiles("PublicAPI.*.txt").Order(StringComparer.Ordinal);
	}

	/// <summary>
	///     The releases whose API the repository has promoted: the <c>## Release X.Y.Z</c> sections of every
	///     <c>AnalyzerReleases.Shipped.md</c>, of which at least one must exist.
	/// </summary>
	private static string[] PromotedReleases()
	{
		// .claude/ holds local agent state (for example worktree copies of other commits), never repository content.
		string[] files =
		[
			.. RepositoryRoot.EnumerateSourceFiles("AnalyzerReleases.Shipped.md")
				.Where(static file => !file.StartsWith(".claude/", StringComparison.Ordinal))
		];
		Assert.True(files.Length > 0,
			"No AnalyzerReleases.Shipped.md exists; the PublicAPI guards read the promotion of a release from it.");
		List<string> lines = [];
		foreach (string file in files)
		{
			lines.AddRange(File.ReadLines(Path.Combine(RepositoryRoot.Path, file)));
		}

		return FindPromotedReleases(lines);
	}

	/// <summary>The versions of the <c>## Release</c> sections of analyzer release lines, distinct, in order.</summary>
	private static string[] FindPromotedReleases(IEnumerable<string> analyzerReleaseLines)
	{
		return
		[
			.. analyzerReleaseLines.Select(static line => PromotedReleaseHeading().Match(line))
				.Where(static match => match.Success)
				.Select(static match => match.Groups["version"].Value)
				.Distinct(StringComparer.Ordinal)
		];
	}

	/// <summary>The promoted versions that no <c>## [X.Y.Z] - YYYY-MM-DD</c> heading of the changelog dates.</summary>
	private static string[] FindUndatedPromotions(string[] promoted, string[] changelog)
	{
		HashSet<string> dated = new(changelog.Select(static line => DatedReleaseHeading().Match(line))
			.Where(static match => match.Success)
			.Select(static match => match.Groups["version"].Value), StringComparer.Ordinal);
		return [.. promoted.Where(version => !dated.Contains(version))];
	}

	[GeneratedRegex(@"^## \[(?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)\] - \d{4}-\d{2}-\d{2}$",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex DatedReleaseHeading();

	/// <summary>A release section of an analyzer release tracking file.</summary>
	[GeneratedRegex(@"^## Release (?<version>\S+)\s*$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex PromotedReleaseHeading();

	[GeneratedRegex(@"#pragma\s+warning\s+disable\s+[^\r\n]*\bRS002[67]\b", RegexOptions.CultureInvariant,
		RegexTimeoutMilliseconds)]
	private static partial Regex OverloadSuppression();
}
