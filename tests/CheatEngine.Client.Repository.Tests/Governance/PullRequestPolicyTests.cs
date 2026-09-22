using System.Text.RegularExpressions;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.Client.Repository.Tests.Governance;

/// <summary>
/// The required "PR policy" check (shared-contracts §1.2, §1.12): an imperative title of at most 72 characters
/// without a Conventional-Commit prefix, a CHANGELOG entry for consumer-visible changes, Dependabot exempt. The rules
/// are data (<c>eng/ci/pr-policy.json</c>); <see cref="PullRequestPolicyRules"/> is their executable specification and
/// the raw-text tests keep <c>eng/ci/Test-PullRequestPolicy.ps1</c> free of rule literals and case-insensitive
/// operators.
/// </summary>
public sealed class PullRequestPolicyTests
{
	private const string ScriptPath = "eng/ci/Test-PullRequestPolicy.ps1";
	private const string WorkflowPath = ".github/workflows/pr-policy.yml";
	private const string Maintainer = "AriusII";

	private static readonly PullRequestPolicy _policy = PullRequestPolicyRules.Load();

	[Fact]
	public void PolicyFileMatchesTheFrozenContract()
	{
		Assert.Equal("cheatengine-pr-policy/v0", _policy.Schema);
		Assert.Equal(["dependabot[bot]"], _policy.ExemptAuthors);
		Assert.Equal(72, _policy.MaxLength);
		TitleRule prefix = Assert.Single(_policy.TitleRules, rule => rule.Id == "NoConventionalCommitPrefix");

		Assert.Equal(@"^\w+(\([^)]*\))?!?:\s", prefix.MustNotMatch);
		Assert.Equal("CHANGELOG.md", _policy.Changelog.File);
		Assert.Equal("^(libs|src|source-generators|templates)/", _policy.Changelog.ConsumerVisiblePathPattern);
		Assert.Equal([@"(^|/)packages\.lock\.json$"], _policy.Changelog.ExcludedPathPatterns);
		Assert.Equal(@"(?m)^[ \t]*<!-- changelog: not-needed -->[ \t]*\r?$", _policy.Changelog.OptOutMarkerPattern);

		List<string> ids =
			[_policy.MaxLengthId, .. _policy.TitleRules.Select(rule => rule.Id), _policy.FirstWord.Id, _policy.Changelog.Id];
		Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
		foreach (TitleRule rule in _policy.TitleRules)
		{
			Assert.True(rule.MustMatch is null ^ rule.MustNotMatch is null, $"Title rule {rule.Id} needs exactly one pattern.");
		}

		foreach (string pattern in AllPatterns())
		{
			_ = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
		}
	}

	[Theory]
	[InlineData("Restore consistent coexistence fixture lock files")]
	[InlineData("Stop template smoke tests from editing the user PATH")]
	[InlineData("Scaffold repository tests and shared package versions")]
	[InlineData("Expose structured capability evidence reason code")]
	[InlineData("Address Client code review findings")]
	[InlineData("Bound event stream consumers and completion")]
	[InlineData("Unify stale client and local process semantics")]
	[InlineData("Declare CRLF line endings in .editorconfig")]
	[InlineData("Remediate the 2026-09-22 audit and overhaul CI/CD")]
	[InlineData("Rename CESDK references to CheatEngine.SDK in READMEs")]
	[InlineData("Remove deprecated issue templates and streamline table record lookups")]
	[InlineData("Embed the SBOM in the packages")]
	[InlineData("Revert \"Remove SonarCloud CI integration\"")]
	public void RepositoryHistoryTitlesPass(string title)
	{
		Assert.Empty(FailedRules(title, changedPaths: []));
	}

	[Theory]
	[InlineData("fix: harden delivery gates and template cleanup", "UppercaseFirstLetter,NoConventionalCommitPrefix")]
	[InlineData("ci: enforce Sonar quality gate coverage", "UppercaseFirstLetter,NoConventionalCommitPrefix")]
	[InlineData("chore(deps): bump the github-actions group", "UppercaseFirstLetter,NoConventionalCommitPrefix")]
	[InlineData("SDK: finalize runtime ownership and scan contracts", "NoConventionalCommitPrefix")]
	[InlineData("Fix(ci)!: tidy the gate", "NoConventionalCommitPrefix")]
	[InlineData("fix sonar key", "UppercaseFirstLetter")]
	[InlineData(".", "UppercaseFirstLetter,NoTrailingPeriod")]
	[InlineData("", "UppercaseFirstLetter")]
	[InlineData(" Add padding", "NoSurroundingWhitespace,UppercaseFirstLetter")]
	[InlineData("Fix the gate.", "NoTrailingPeriod")]
	[InlineData("Added debugger and fix some issues", "ImperativeFirstWord")]
	[InlineData("Adds lock files", "ImperativeFirstWord")]
	[InlineData("Adding lock files", "ImperativeFirstWord")]
	[InlineData("WIP audit remediation", "ImperativeFirstWord")]
	[InlineData("Add xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", "MaxLength")]
	public void NonCompliantTitlesAreRejected(string title, string expectedRules)
	{
		Assert.Equal(expectedRules.Split(','), FailedRules(title, changedPaths: []));
	}

	[Fact]
	public void TitleLengthCountsTextElementsUpToTheLimit()
	{
		string atLimit = "Add " + new string('x', 68);
		string composed = "Add " + new string('x', 67) + "e\u0301"; // e + combining acute accent

		Assert.Equal(72, atLimit.Length);
		Assert.Empty(FailedRules(atLimit, changedPaths: []));
		Assert.Equal(73, composed.Length);
		Assert.Empty(FailedRules(composed, changedPaths: []));
		Assert.Equal(["MaxLength"], FailedRules(atLimit + "x", changedPaths: []));
	}

	[Theory]
	[InlineData("libs/CheatEngine.Client.Core/Domains/MemoryClient.cs", true)]
	[InlineData("src/CheatEngine.Client/README.md", true)]
	[InlineData("templates/CheatEngine.Client.Templates/content/CheatEngine.Plugin/Plugin.cs", true)]
	[InlineData("source-generators/CheatEngine.Client.SourceGenerators.Lua/LuaModuleGenerator.cs", true)]
	[InlineData("tests/CheatEngine.Client.Core.Tests/MemoryClientTests.cs", false)]
	[InlineData(".github/workflows/ci.yml", false)]
	[InlineData("eng/ci/Test-PullRequestPolicy.ps1", false)]
	[InlineData("docs/README.md", false)]
	[InlineData("Directory.Packages.props", false)]
	[InlineData("Libs/CheatEngine.Client.Core/X.cs", false)]
	public void ConsumerVisibleChangesRequireAChangelogEntry(string path, bool required)
	{
		Assert.Equal(required, PullRequestPolicyRules.RequiresChangelog(_policy.Changelog, [path]));
		Assert.Equal(required, FailedRules("Change one file", changedPaths: [path]).Contains("ChangelogEntry"));
	}

	[Fact]
	public void LockFileOnlyChangesNeedNoChangelogEntry()
	{
		string[] paths =
		[
			"libs/CheatEngine.Client.Core/packages.lock.json",
			"src/CheatEngine.Client/packages.lock.json",
			"tests/CheatEngine.Client.Core.Tests/packages.lock.json"
		];

		Assert.Empty(FailedRules("Regenerate lock files after the SDK update", paths));
	}

	[Theory]
	[InlineData("", true)]
	[InlineData("Why\r\n<!-- changelog: not-needed -->\r\n", false)]
	[InlineData("Why\n<!-- changelog: not-needed -->", false)]
	[InlineData("<!-- changelog: not-needed -->", false)]
	[InlineData("Why\r\n  <!-- changelog: not-needed -->  \r\nMore", false)]
	public void ChangelogEntryOrOwnLineMarkerSatisfiesThePolicy(string body, bool changelogChanged)
	{
		List<string> paths = ["libs/CheatEngine.Client.Core/Domains/MemoryClient.cs"];
		if (changelogChanged)
		{
			paths.Add("CHANGELOG.md");
		}

		Assert.Empty(PullRequestPolicyRules.FailedRules(Evaluate("Fix the memory batch outcome", body, Maintainer, paths)));
	}

	[Theory]
	[InlineData("Nothing visible changes; see <!-- changelog: not-needed --> above")]
	[InlineData("<!--changelog:not-needed-->")]
	[InlineData("<!-- CHANGELOG: NOT-NEEDED -->")]
	[InlineData("- [ ] `<!-- changelog: not-needed -->`")]
	public void InlineMarkerDoesNotOptOut(string body)
	{
		IReadOnlyList<PolicyVerdict> verdicts = Evaluate(
			"Fix the memory batch outcome", body, Maintainer, ["libs/CheatEngine.Client.Core/Domains/MemoryClient.cs"]);

		Assert.Equal(["ChangelogEntry"], PullRequestPolicyRules.FailedRules(verdicts));
	}

	[Fact]
	public void DependabotPullRequestsAreExempt()
	{
		IReadOnlyList<PolicyVerdict> verdicts = Evaluate(
			"chore(deps): bump the nuget-minor-and-patch group with 3 updates",
			body: null,
			"dependabot[bot]",
			["Directory.Packages.props", "libs/CheatEngine.Client.Core/packages.lock.json", "libs/X/X.cs"]);

		PolicyVerdict verdict = Assert.Single(verdicts);
		Assert.Equal(PullRequestPolicyRules.ExemptAuthorRuleId, verdict.RuleId);
		Assert.True(verdict.Passed);
		Assert.NotEmpty(FailedRules("chore(deps): bump the nuget group", ["libs/X/X.cs"], author: "Dependabot"));
	}

	[Fact]
	public void ScriptTakesEveryRuleFromThePolicyFile()
	{
		string script = GovernanceFile.ReadText(ScriptPath);

		Assert.Contains("pr-policy.json", script, StringComparison.Ordinal);
		string limit = _policy.MaxLength.ToString(System.Globalization.CultureInfo.InvariantCulture);

		Assert.DoesNotContain(limit, script, StringComparison.Ordinal);
		Assert.DoesNotContain("changelog: not-needed", script, StringComparison.Ordinal);
		Assert.DoesNotContain("CHANGELOG.md", script, StringComparison.Ordinal);
		Assert.DoesNotContain("dependabot", script, StringComparison.OrdinalIgnoreCase);
		foreach (string pattern in AllPatterns())
		{
			Assert.DoesNotContain(pattern, script, StringComparison.Ordinal);
		}
	}

	[Fact]
	public void ScriptUsesCaseSensitiveMatchingOnly()
	{
		string script = GovernanceFile.ReadText(ScriptPath);
		// String operators that ignore case by default. Numeric -eq/-ne/-gt stay allowed; string equality uses -ceq/-cne.
		Regex caseInsensitiveOperator = new(
			@"(?<![\w-])-i?(match|notmatch|like|notlike|contains|notcontains|in|notin|replace|split)\b",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
			TimeSpan.FromSeconds(1));

		List<string> offending = [.. caseInsensitiveOperator.Matches(script).Select(match => match.Value)];

		Assert.True(offending.Count == 0,
			$"{ScriptPath} must match case-sensitively like the C# mirror (-cmatch, -ccontains, [regex]::IsMatch); "
			+ $"found: {string.Join(", ", offending)}");
		Assert.Contains("[regex]::IsMatch", script, StringComparison.Ordinal);
	}

	[Fact]
	public void PolicyWorkflowIsTheRequiredPrPolicyCheck()
	{
		YamlMappingNode workflow = GovernanceFile.LoadYaml(WorkflowPath);
		YamlMappingNode triggers = GovernanceFile.Triggers(workflow);
		YamlMappingNode pullRequest = Assert.IsType<YamlMappingNode>(GovernanceFile.Child(triggers, "pull_request"));
		YamlMappingNode job = Assert.Single(GovernanceFile.Jobs(workflow)).Value;

		Assert.Equal("PR policy", GovernanceFile.Scalar(workflow, "name"));
		Assert.Equal(["pull_request"], triggers.Children.Keys.Select(key => ((YamlScalarNode) key).Value));
		Assert.Equal(
			["opened", "edited", "synchronize", "reopened", "ready_for_review"],
			GovernanceFile.Strings(GovernanceFile.Child(pullRequest, "types")));
		Assert.False(GovernanceFile.Has(pullRequest, "paths"), "The required check must run for every pull request.");
		Assert.False(GovernanceFile.Has(pullRequest, "paths-ignore"), "The required check must run for every pull request.");
		Assert.True(GovernanceFile.Jobs(workflow).ContainsKey("policy"));
		Assert.Equal("PR policy", GovernanceFile.Scalar(job, "name"));
		Assert.Equal("ubuntu-24.04", GovernanceFile.Scalar(job, "runs-on"));
		Assert.Null(GovernanceFile.Child(job, "if"));
		Assert.Equal(["contents"], GovernanceFile.Permissions(workflow).Keys);
		Assert.Empty(GovernanceFile.Permissions(job));
		Assert.DoesNotContain(GovernanceFile.Steps(job), step => GovernanceFile.ActionName(step) == "actions/setup-dotnet");
	}

	[Fact]
	public void PolicyWorkflowPassesUserTextOnlyThroughEnvironment()
	{
		YamlMappingNode job = Assert.Single(GovernanceFile.Jobs(GovernanceFile.LoadYaml(WorkflowPath))).Value;
		IReadOnlyList<YamlMappingNode> steps = GovernanceFile.Steps(job);
		YamlMappingNode policyStep = Assert.Single(steps, step => GovernanceFile.Scalar(step, "run") is not null);

		foreach (YamlMappingNode step in steps)
		{
			string? run = GovernanceFile.Scalar(step, "run");
			Assert.True(run is null || !run.Contains("${{", StringComparison.Ordinal),
				"A run: script must never expand ${{ }} expressions; pass values through env:.");
		}

		Assert.Equal("./eng/ci/Test-PullRequestPolicy.ps1", GovernanceFile.Scalar(policyStep, "run"));
		YamlMappingNode environment = Assert.IsType<YamlMappingNode>(GovernanceFile.Child(policyStep, "env"));
		Assert.Equal("${{ github.event.pull_request.title }}", GovernanceFile.Scalar(environment, "PR_TITLE"));
		Assert.Equal("${{ github.event.pull_request.body }}", GovernanceFile.Scalar(environment, "PR_BODY"));
		Assert.Equal("${{ github.event.pull_request.user.login }}", GovernanceFile.Scalar(environment, "PR_AUTHOR"));
		Assert.Equal("${{ github.event.pull_request.base.sha }}", GovernanceFile.Scalar(environment, "PR_BASE_SHA"));
		Assert.Equal("${{ github.event.pull_request.head.sha }}", GovernanceFile.Scalar(environment, "PR_HEAD_SHA"));
	}

	[Fact]
	public void PullRequestTemplateNeverOptsOutByDefault()
	{
		string template = GovernanceFile.ReadText(".github/PULL_REQUEST_TEMPLATE.md");

		Assert.False(PullRequestPolicyRules.IsMatch(template, _policy.Changelog.OptOutMarkerPattern),
			"The template may mention the opt-out marker inline only: on its own line it opts every pull request out.");
	}

	private static IReadOnlyList<PolicyVerdict> Evaluate(
		string title,
		string? body,
		string author,
		IReadOnlyCollection<string> changedPaths)
	{
		return PullRequestPolicyRules.Evaluate(_policy, title, body, author, changedPaths);
	}

	private static IReadOnlyList<string> FailedRules(
		string title,
		IReadOnlyCollection<string> changedPaths,
		string author = Maintainer)
	{
		return PullRequestPolicyRules.FailedRules(Evaluate(title, body: string.Empty, author, changedPaths));
	}

	private static IEnumerable<string> AllPatterns()
	{
		foreach (TitleRule rule in _policy.TitleRules)
		{
			yield return rule.MustMatch ?? rule.MustNotMatch!;
		}

		yield return _policy.FirstWord.Pattern;
		yield return _policy.FirstWord.NonImperativePattern;
		yield return _policy.Changelog.ConsumerVisiblePathPattern;
		yield return _policy.Changelog.OptOutMarkerPattern;
		foreach (string excluded in _policy.Changelog.ExcludedPathPatterns)
		{
			yield return excluded;
		}
	}
}
