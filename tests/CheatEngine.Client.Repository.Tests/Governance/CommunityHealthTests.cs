using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.Governance;

/// <summary>
/// Community health files (PR-CQ-37, CI-BOTH-7): the security policy routes vulnerabilities to GitHub private
/// vulnerability reporting, the code of conduct names a private channel, and the informational CODEOWNERS file uses
/// only syntax GitHub understands and paths that exist with their exact case. No local path anywhere (ADR-12).
/// </summary>
public sealed class CommunityHealthTests
{
	private const string ClientAdvisories = "https://github.com/CheatEngineNet/CheatEngine.Client/security/advisories/new";
	private const string SdkAdvisories = "https://github.com/CheatEngineNet/CheatEngine.SDK/security/advisories/new";
	private const string CodeOwnersPath = ".github/CODEOWNERS";

	private static readonly Regex LocalPath = new(
		@"[A-Za-z]:\\|\\Users\\|/Users/|file://",
		RegexOptions.CultureInvariant,
		TimeSpan.FromSeconds(1));

	private static readonly Regex EmailAddress = new(
		@"[\w.+-]+@[\w-]+\.[\w.]+",
		RegexOptions.None,
		TimeSpan.FromSeconds(1));

	/// <summary>Owners allowed in CODEOWNERS. Co-owners are a maintainer decision (orchestrator decision O5).</summary>
	private static readonly HashSet<string> KnownOwners = new(StringComparer.Ordinal) { "@AriusII" };

	[Fact]
	public void SecurityPolicyPointsToPrivateVulnerabilityReporting()
	{
		string policy = GovernanceFile.ReadText("SECURITY.md");
		string[] headings = ["## Supported versions", "## Reporting a vulnerability", "## What to expect", "## Scope"];

		Assert.All(headings, heading => Assert.Contains(heading, policy, StringComparison.Ordinal));
		Assert.Contains(ClientAdvisories, policy, StringComparison.Ordinal);
		Assert.Contains(SdkAdvisories, policy, StringComparison.Ordinal);
		Assert.Contains("Never report a vulnerability in a public issue", policy, StringComparison.Ordinal);
		Assert.Contains("contentHash", policy, StringComparison.Ordinal);
		Assert.Contains("cheatengine-sdk-lua-bridge.dll", policy, StringComparison.Ordinal);
		Assert.Contains("within 7 days", policy, StringComparison.Ordinal);
	}

	[Fact]
	public void SecurityPolicyContainsNoLocalPath()
	{
		List<string> offending = [];
		foreach (string file in GovernanceDocuments())
		{
			offending.AddRange(LocalPath.Matches(GovernanceFile.ReadText(file)).Select(match => $"{file}: '{match.Value}'"));
		}

		Assert.True(offending.Count == 0,
			$"Governance documents must stay usable outside any workspace (ADR-12): {string.Join("; ", offending)}");
	}

	[Fact]
	public void CodeOfConductIsTheContributorCovenantWithAPrivateContact()
	{
		string conduct = GovernanceFile.ReadText("CODE_OF_CONDUCT.md");

		Assert.StartsWith("# Contributor Covenant Code of Conduct", conduct, StringComparison.Ordinal);
		Assert.Contains("version 2.1", conduct, StringComparison.Ordinal);
		Assert.DoesNotContain("[INSERT CONTACT METHOD]", conduct, StringComparison.Ordinal);
		Assert.Contains(ClientAdvisories, conduct, StringComparison.Ordinal);
		Assert.DoesNotMatch(EmailAddress, conduct);
	}

	[Fact]
	public void OnlyOneCodeOwnersFileExists()
	{
		Assert.Equal([CodeOwnersPath], RepositoryRoot.EnumerateSourceFiles("CODEOWNERS"));
	}

	[Fact]
	public void CodeOwnersUsesSupportedSyntax()
	{
		List<(string Pattern, string[] Owners)> rules = ReadCodeOwners();

		Assert.NotEmpty(rules);
		Assert.Equal("*", rules[0].Pattern);
		Assert.Equal(rules.Count, rules.Select(rule => rule.Pattern).Distinct(StringComparer.Ordinal).Count());
		foreach ((string pattern, string[] owners) in rules)
		{
			bool supported = pattern.IndexOfAny(['!', '[', ']']) < 0 && !pattern.Contains(@"\#", StringComparison.Ordinal);
			Assert.True(supported, $"CODEOWNERS pattern '{pattern}' uses syntax GitHub does not support (!, [ ], \\#).");
			Assert.NotEmpty(owners);
			Assert.All(owners, owner => Assert.Contains(owner, KnownOwners));
		}
	}

	[Fact]
	public void CodeOwnersNamesOnlyExistingPaths()
	{
		List<string> missing = [];
		foreach ((string pattern, _) in ReadCodeOwners().Where(rule => rule.Pattern != "*"))
		{
			Assert.StartsWith("/", pattern, StringComparison.Ordinal);
			if (!ExistsWithExactCase(pattern.Trim('/')))
			{
				missing.Add(pattern);
			}
		}

		Assert.True(missing.Count == 0, $"CODEOWNERS paths missing with this exact case: {string.Join(", ", missing)}");
	}

	private static IEnumerable<string> GovernanceDocuments()
	{
		yield return "SECURITY.md";
		yield return "CODE_OF_CONDUCT.md";
		yield return CodeOwnersPath;
		foreach (string form in Directory.EnumerateFiles(GovernanceFile.FullPath(".github/ISSUE_TEMPLATE"), "*.yml"))
		{
			yield return RepositoryRoot.ToRelative(form);
		}
	}

	private static List<(string Pattern, string[] Owners)> ReadCodeOwners()
	{
		List<(string, string[])> rules = [];
		foreach (string rawLine in GovernanceFile.ReadText(CodeOwnersPath).Split('\n'))
		{
			string line = rawLine.Trim();
			if (line.Length == 0 || line.StartsWith('#'))
			{
				continue;
			}

			string[] parts = line.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries);
			rules.Add((parts[0], parts[1..]));
		}

		return rules;
	}

	/// <summary>Case-sensitive existence check: Windows file systems ignore case, GitHub does not.</summary>
	private static bool ExistsWithExactCase(string relativePath)
	{
		string current = RepositoryRoot.Path;
		foreach (string segment in relativePath.Split('/'))
		{
			string? next = Directory.EnumerateFileSystemEntries(current)
				.FirstOrDefault(entry => string.Equals(Path.GetFileName(entry), segment, StringComparison.Ordinal));
			if (next is null)
			{
				return false;
			}

			current = next;
		}

		return true;
	}
}
