using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.Packaging;

/// <summary>
///     The PublicAPI baselines of the shipping libraries stay truthful before and after the first release: every library
///     declares both files, entries are ordinally sorted, and nothing counts as shipped until CHANGELOG.md records a dated
///     release.
/// </summary>
public sealed partial class PublicApiFileTests
{
	private const string NullableHeader = "#nullable enable";

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
		if (HasDatedRelease())
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
			"Nothing has been released, so an API is deleted, never marked *REMOVED*:" + Environment.NewLine +
			string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void ShippedIsEmptyUntilTheChangelogHasADatedRelease()
	{
		if (HasDatedRelease())
		{
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
			"CHANGELOG.md records no dated release, so every PublicAPI.Shipped.txt must contain only " +
			$"'{NullableHeader}'. The release pull request promotes Unshipped once, as its last API commit: " +
			string.Join(", ", offenders));
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

	private static bool HasDatedRelease()
	{
		return File.ReadLines(Path.Combine(RepositoryRoot.Path, "CHANGELOG.md"))
			.Any(static line => DatedReleaseHeading().IsMatch(line));
	}

	[GeneratedRegex(@"^## \[\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?\] - \d{4}-\d{2}-\d{2}$", RegexOptions.CultureInvariant,
		RegexTimeoutMilliseconds)]
	private static partial Regex DatedReleaseHeading();

	[GeneratedRegex(@"#pragma\s+warning\s+disable\s+[^\r\n]*\bRS002[67]\b", RegexOptions.CultureInvariant,
		RegexTimeoutMilliseconds)]
	private static partial Regex OverloadSuppression();
}
