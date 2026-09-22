using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CheatEngine.Client.Repository.Tests.Governance;

/// <summary>A title rule of <c>eng/ci/pr-policy.json</c>: exactly one of the two patterns is set.</summary>
internal sealed record TitleRule(string Id, string? MustMatch, string? MustNotMatch, string Message);

/// <summary>The first-word (imperative) heuristic of <c>eng/ci/pr-policy.json</c>.</summary>
internal sealed record FirstWordRule(
	string Id,
	string Pattern,
	string NonImperativePattern,
	IReadOnlyList<string> Denied,
	IReadOnlyList<string> Allowed,
	string Message);

/// <summary>The CHANGELOG rule of <c>eng/ci/pr-policy.json</c>.</summary>
internal sealed record ChangelogRule(
	string Id,
	string File,
	string ConsumerVisiblePathPattern,
	IReadOnlyList<string> ExcludedPathPatterns,
	string OptOutMarkerPattern,
	string Message);

/// <summary>The parsed policy data file.</summary>
internal sealed record PullRequestPolicy(
	string Schema,
	IReadOnlyList<string> ExemptAuthors,
	string MaxLengthId,
	int MaxLength,
	IReadOnlyList<TitleRule> TitleRules,
	FirstWordRule FirstWord,
	ChangelogRule Changelog);

/// <summary>The verdict of one rule.</summary>
internal sealed record PolicyVerdict(string RuleId, bool Passed);

/// <summary>
/// Executable specification of the required "PR policy" check. It evaluates <c>eng/ci/pr-policy.json</c> exactly like
/// <c>eng/ci/Test-PullRequestPolicy.ps1</c>: same .NET regular-expression engine, case-sensitive matching, title length
/// in text elements, exempt authors first, title rules in file order, then the first word, then the CHANGELOG rule.
/// The script is kept in line by the raw-text tests of <see cref="PullRequestPolicyTests"/>.
/// </summary>
internal static class PullRequestPolicyRules
{
	/// <summary>Repository-relative path of the policy data file.</summary>
	public const string PolicyPath = "eng/ci/pr-policy.json";

	/// <summary>The rule id reported for an exempt author.</summary>
	public const string ExemptAuthorRuleId = "ExemptAuthor";

	private static readonly TimeSpan _regexTimeout = TimeSpan.FromSeconds(2);

	/// <summary>Loads and parses the committed policy file.</summary>
	public static PullRequestPolicy Load()
	{
		using JsonDocument document = JsonDocument.Parse(GovernanceFile.ReadText(PolicyPath));
		JsonElement root = document.RootElement;
		JsonElement title = root.GetProperty("title");
		JsonElement maxLength = title.GetProperty("maxLength");
		JsonElement firstWord = title.GetProperty("firstWord");
		JsonElement changelog = root.GetProperty("changelog");

		List<TitleRule> rules = [];
		foreach (JsonElement rule in title.GetProperty("rules").EnumerateArray())
		{
			rules.Add(new TitleRule(
				Text(rule, "id"),
				OptionalText(rule, "mustMatch"),
				OptionalText(rule, "mustNotMatch"),
				Text(rule, "message")));
		}

		return new PullRequestPolicy(
			Text(root, "schema"),
			Texts(root, "exemptAuthors"),
			Text(maxLength, "id"),
			maxLength.GetProperty("limit").GetInt32(),
			rules,
			new FirstWordRule(
				Text(firstWord, "id"),
				Text(firstWord, "pattern"),
				Text(firstWord, "nonImperativePattern"),
				Texts(firstWord, "denied"),
				Texts(firstWord, "allowed"),
				Text(firstWord, "message")),
			new ChangelogRule(
				Text(changelog, "id"),
				Text(changelog, "file"),
				Text(changelog, "consumerVisiblePathPattern"),
				Texts(changelog, "excludedPathPatterns"),
				Text(changelog, "optOutMarkerPattern"),
				Text(changelog, "message")));
	}

	/// <summary>Evaluates every rule for one pull request.</summary>
	public static IReadOnlyList<PolicyVerdict> Evaluate(
		PullRequestPolicy policy,
		string title,
		string? body,
		string author,
		IReadOnlyCollection<string> changedPaths)
	{
		if (policy.ExemptAuthors.Contains(author, StringComparer.Ordinal))
		{
			return [new PolicyVerdict(ExemptAuthorRuleId, true)];
		}

		List<PolicyVerdict> verdicts =
		[
			new PolicyVerdict(policy.MaxLengthId, new StringInfo(title).LengthInTextElements <= policy.MaxLength)
		];

		foreach (TitleRule rule in policy.TitleRules)
		{
			bool passed = rule.MustMatch is not null
				? IsMatch(title, rule.MustMatch)
				: !IsMatch(title, rule.MustNotMatch ?? throw new InvalidOperationException($"Rule {rule.Id} has no pattern."));
			verdicts.Add(new PolicyVerdict(rule.Id, passed));
		}

		verdicts.Add(new PolicyVerdict(policy.FirstWord.Id, FirstWordPasses(policy.FirstWord, title)));
		bool changelog = ChangelogPasses(policy.Changelog, body ?? string.Empty, changedPaths);
		verdicts.Add(new PolicyVerdict(policy.Changelog.Id, changelog));
		return verdicts;
	}

	/// <summary>The ids of the failed rules, in evaluation order.</summary>
	public static IReadOnlyList<string> FailedRules(IReadOnlyList<PolicyVerdict> verdicts)
	{
		return [.. verdicts.Where(verdict => !verdict.Passed).Select(verdict => verdict.RuleId)];
	}

	/// <summary>Whether the changed paths contain a consumer-visible path that is not excluded.</summary>
	public static bool RequiresChangelog(ChangelogRule rule, IEnumerable<string> changedPaths)
	{
		return changedPaths.Any(path =>
			IsMatch(path, rule.ConsumerVisiblePathPattern)
			&& !rule.ExcludedPathPatterns.Any(excluded => IsMatch(path, excluded)));
	}

	/// <summary>Case-sensitive match with the same engine and timeout as the script.</summary>
	public static bool IsMatch(string value, string pattern)
	{
		return Regex.IsMatch(value, pattern, RegexOptions.None, _regexTimeout);
	}

	private static bool FirstWordPasses(FirstWordRule rule, string title)
	{
		Match match = Regex.Match(title, rule.Pattern, RegexOptions.None, _regexTimeout);
		if (!match.Success)
		{
			return true;
		}

		string word = match.Value;
		if (rule.Denied.Contains(word, StringComparer.Ordinal))
		{
			return false;
		}

		return !IsMatch(word, rule.NonImperativePattern) || rule.Allowed.Contains(word, StringComparer.Ordinal);
	}

	private static bool ChangelogPasses(ChangelogRule rule, string body, IReadOnlyCollection<string> changedPaths)
	{
		return !RequiresChangelog(rule, changedPaths)
			|| changedPaths.Contains(rule.File, StringComparer.Ordinal)
			|| IsMatch(body, rule.OptOutMarkerPattern);
	}

	private static string Text(JsonElement element, string name)
	{
		return element.GetProperty(name).GetString() ?? throw new InvalidOperationException($"'{name}' is null.");
	}

	private static string? OptionalText(JsonElement element, string name)
	{
		return element.TryGetProperty(name, out JsonElement value) ? value.GetString() : null;
	}

	private static List<string> Texts(JsonElement element, string name)
	{
		return [.. element.GetProperty(name).EnumerateArray().Select(item => item.GetString() ?? string.Empty)];
	}
}
