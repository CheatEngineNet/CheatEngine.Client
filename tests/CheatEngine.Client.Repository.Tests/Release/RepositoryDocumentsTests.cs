using System.Globalization;
using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.Release;

/// <summary>The files a published repository needs exist and agree with the package metadata.</summary>
public sealed partial class RepositoryDocumentsTests
{
	private const int RegexTimeoutMilliseconds = 1000;

	private static readonly string[] ReleaseCategories = ["Added", "Changed", "Security", "Deployment"];

	[Fact]
	public void LicenseIsMitAndMatchesThePackageLicenseExpression()
	{
		string[] license = File.ReadAllLines(Path.Combine(RepositoryRoot.Path, "LICENSE"));
		XDocument buildProperties = XDocument.Load(Path.Combine(RepositoryRoot.Path, "Directory.Build.props"));
		string? expression = buildProperties.Descendants("PackageLicenseExpression").SingleOrDefault()?.Value;

		Assert.True(license.Length > 2 && license[0] == "MIT License",
			"LICENSE must start with the line 'MIT License'.");
		Assert.True(expression == "MIT",
			$"Directory.Build.props declares PackageLicenseExpression '{expression}', but LICENSE is the MIT license.");
	}

	[Fact]
	public void ChangelogHasAnUnreleasedSectionWithTheFourReleaseCategories()
	{
		string[] changelog = File.ReadAllLines(Path.Combine(RepositoryRoot.Path, "CHANGELOG.md"));
		int start = Array.IndexOf(changelog, "## [Unreleased]");
		Assert.True(start >= 0, "CHANGELOG.md has no '## [Unreleased]' heading; the release workflow reads that exact syntax.");

		List<string> categories = [];
		for (int index = start + 1; index < changelog.Length && !changelog[index].StartsWith("## ", StringComparison.Ordinal); index++)
		{
			if (changelog[index].StartsWith("### ", StringComparison.Ordinal))
			{
				categories.Add(changelog[index][4..].Trim());
			}
		}

		Assert.True(categories.SequenceEqual(ReleaseCategories),
			$"The [Unreleased] section must list exactly {string.Join(", ", ReleaseCategories)} in that order (audit A21-17), but lists: {string.Join(", ", categories)}.");
	}

	[Fact]
	public void ChangelogReleasesAreDatedInIsoFormatNewestFirstAndHaveEntries()
	{
		string[] changelog = File.ReadAllLines(Path.Combine(RepositoryRoot.Path, "CHANGELOG.md"));
		List<string> offenders = [];
		bool unreleasedSeen = false;
		(Version Core, bool IsPrerelease, DateOnly Date)? newer = null;
		for (int index = 0; index < changelog.Length; index++)
		{
			string line = changelog[index];
			if (!line.StartsWith("## ", StringComparison.Ordinal))
			{
				continue;
			}

			if (line == "## [Unreleased]")
			{
				if (unreleasedSeen || newer is not null)
				{
					offenders.Add($"line {index + 1}: '## [Unreleased]' must appear once, above every release");
				}

				unreleasedSeen = true;
				continue;
			}

			Match heading = ReleaseHeading().Match(line);
			if (!heading.Success)
			{
				offenders.Add($"line {index + 1}: '{line}' is not '## [X.Y.Z] - YYYY-MM-DD'; the release " +
							  "workflow reads every level-2 heading as the end of the section above it");
				continue;
			}

			string date = heading.Groups["date"].Value;
			if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
					out DateOnly released))
			{
				offenders.Add($"line {index + 1}: '{date}' is not an ISO 8601 calendar date (YYYY-MM-DD)");
				continue;
			}

			Version core = Version.Parse(heading.Groups["core"].Value);
			bool isPrerelease = heading.Groups["prerelease"].Success;
			if (newer is { } above)
			{
				// A prerelease precedes the release of its version; two prereleases of one version are not
				// ordered here.
				if (core > above.Core || (core == above.Core && !isPrerelease))
				{
					offenders.Add($"line {index + 1}: {line[3..]} is not older than the release above it; the newest " +
								  "release comes first");
				}

				if (released > above.Date)
				{
					offenders.Add($"line {index + 1}: {line[3..]} is dated after the newer release above it");
				}
			}

			if (!SectionHasEntries(changelog, index))
			{
				offenders.Add($"line {index + 1}: {line[3..]} has no entry; its body becomes the GitHub release notes");
			}

			newer = (core, isPrerelease, released);
		}

		Assert.True(unreleasedSeen, "CHANGELOG.md has no '## [Unreleased]' heading.");
		Assert.True(offenders.Count == 0,
			"CHANGELOG.md releases are '## [X.Y.Z] - YYYY-MM-DD' sections in ISO 8601, newest first, each with " +
			$"entries:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
	}

	[Fact]
	public void ReleasingDocumentsTheTrustedPublishingPolicyForTheClientPackageGlob()
	{
		string releasing = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "RELEASING.md"));
		string[] required = ["`CheatEngine.Client*`", "`release.yml`", "`nuget`", "`NUGET_USER`", "`CheatEngineNet`", "`CheatEngine.Client`"];
		List<string> missing = [];
		foreach (string value in required)
		{
			if (!releasing.Contains(value, StringComparison.Ordinal))
			{
				missing.Add(value);
			}
		}

		Assert.True(missing.Count == 0,
			$"RELEASING.md must document the nuget.org trusted publishing policy; it does not mention: {string.Join(", ", missing)}.");
	}

	[Fact]
	public void ReleasingNamesTheOrganizationAsPolicyOwnerAndItsMemberAsNuGetUser()
	{
		string[] releasing = File.ReadAllLines(Path.Combine(RepositoryRoot.Path, "RELEASING.md"));
		string[] owners = [.. releasing.Select(static line => PolicyOwnerRow().Match(line))
			.Where(static match => match.Success).Select(static match => match.Groups["owner"].Value)];
		string[] users = [.. releasing.Select(static line => NuGetUserSecret().Match(line))
			.Where(static match => match.Success).Select(static match => match.Groups["user"].Value)];

		// The policy belongs to the nuget.org organization, so it survives a change of maintainer; NuGet/login still
		// needs the profile name of the member who created it, never the organization name or an e-mail address.
		Assert.True(owners.SequenceEqual(["`CheatEngine` (organization)"], StringComparer.Ordinal),
			"RELEASING.md must have one trusted publishing row '| Policy owner | `CheatEngine` (organization) |', " +
			$"found: {string.Join(", ", owners)}.");
		Assert.True(users.SequenceEqual(["AriusII"], StringComparer.Ordinal),
			"RELEASING.md must set the environment secret `NUGET_USER` to `AriusII` once, found: " +
			$"{string.Join(", ", users)}.");
	}

	/// <summary>Whether a release section holds a line other than a blank line or a category heading.</summary>
	private static bool SectionHasEntries(string[] changelog, int heading)
	{
		for (int index = heading + 1;
			 index < changelog.Length && !changelog[index].StartsWith("## ", StringComparison.Ordinal);
			 index++)
		{
			string line = changelog[index].Trim();
			if (line.Length > 0 && !line.StartsWith("### ", StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>A release heading as the release workflow reads it: <c>## [X.Y.Z(-prerelease)] - date</c>.</summary>
	[GeneratedRegex(@"^## \[(?<core>\d+\.\d+\.\d+)(?<prerelease>-[0-9A-Za-z.-]+)?\] - (?<date>\S+)$",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex ReleaseHeading();

	/// <summary>The <c>Policy owner</c> row of the trusted publishing table.</summary>
	[GeneratedRegex(@"^\|\s*Policy owner\s*\|\s*(?<owner>[^|]*?)\s*\|\s*$", RegexOptions.CultureInvariant,
		RegexTimeoutMilliseconds)]
	private static partial Regex PolicyOwnerRow();

	/// <summary>The value the setup gives the <c>NUGET_USER</c> environment secret.</summary>
	[GeneratedRegex(@"secret `NUGET_USER`:\*\*\s*`(?<user>[^`]+)`", RegexOptions.CultureInvariant,
		RegexTimeoutMilliseconds)]
	private static partial Regex NuGetUserSecret();
}
