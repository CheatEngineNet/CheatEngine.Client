using System.Text.Json;
using System.Text.RegularExpressions;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.Client.Repository.Tests.Governance;

/// <summary>
/// Repository settings as code (PR-CQ-17): the desired state in <c>eng/github/</c> requires exactly the two frozen
/// check contexts (shared-contracts §1.2), accepts squash merges only, has no bypass actor, protects release tags
/// without blocking their creation, and <c>Set-RepositorySettings.ps1</c> writes only through its single guarded
/// mutation path. The maintainer applies it (Wave 5); CI never runs it.
/// </summary>
public sealed class RepositorySettingsTests
{
	private const string Folder = "eng/github";
	private const string ScriptPath = Folder + "/Set-RepositorySettings.ps1";
	private const int GitHubActionsIntegrationId = 15368;

	[Fact]
	public void RequiredChecksAreTheFrozenGateAndPolicyContexts()
	{
		using JsonDocument main = Load("rulesets/protect-main.json");
		JsonElement rule = Rule(main, "required_status_checks");
		List<(string, int)> checks = [.. rule.GetProperty("parameters").GetProperty("required_status_checks").EnumerateArray()
			.Select(check => (check.GetProperty("context").GetString()!, check.GetProperty("integration_id").GetInt32()))];

		Assert.Equal([("CI / Gate", GitHubActionsIntegrationId), ("PR policy", GitHubActionsIntegrationId)], checks);
	}

	[Fact]
	public void RequiredCheckNamesMatchTheWorkflowJobNames()
	{
		Assert.Equal("PR policy", JobName(".github/workflows/pr-policy.yml", "policy"));
		Assert.Equal("Gate", JobName(".github/workflows/ci.yml", "gate"));
		Assert.Equal("CI", JobName(".github/workflows/pull-request-ci.yml", "ci"));
		Assert.Equal("CI", JobName(".github/workflows/main-ci.yml", "ci"));
	}

	[Fact]
	public void MainAcceptsSquashMergesOnly()
	{
		using JsonDocument main = Load("rulesets/protect-main.json");
		using JsonDocument repository = Load("repository.json");
		JsonElement pullRequest = Rule(main, "pull_request").GetProperty("parameters");
		JsonElement settings = repository.RootElement;

		Assert.Equal(["squash"], Strings(pullRequest.GetProperty("allowed_merge_methods")));
		Assert.False(pullRequest.GetProperty("require_code_owner_review").GetBoolean());
		Assert.Equal(0, pullRequest.GetProperty("required_approving_review_count").GetInt32());
		Assert.True(settings.GetProperty("allow_squash_merge").GetBoolean());
		Assert.False(settings.GetProperty("allow_merge_commit").GetBoolean());
		Assert.False(settings.GetProperty("allow_rebase_merge").GetBoolean());
		Assert.Equal("PR_TITLE", settings.GetProperty("squash_merge_commit_title").GetString());
		Assert.Equal(["~DEFAULT_BRANCH"], Strings(RefNameIncludes(main)));
	}

	[Fact]
	public void NoRulesetHasBypassActors()
	{
		foreach (string file in Directory.EnumerateFiles(GovernanceFile.FullPath(Folder + "/rulesets"), "*.json"))
		{
			using JsonDocument ruleset = JsonDocument.Parse(File.ReadAllText(file));
			Assert.Equal(0, ruleset.RootElement.GetProperty("bypass_actors").GetArrayLength());
			Assert.Equal("active", ruleset.RootElement.GetProperty("enforcement").GetString());
		}
	}

	[Fact]
	public void TagRulesetProtectsReleaseTagsWithoutBlockingCreation()
	{
		using JsonDocument tags = Load("rulesets/protect-release-tags.json");
		List<string> types = [.. tags.RootElement.GetProperty("rules").EnumerateArray().Select(RuleType)];

		Assert.Equal("tag", tags.RootElement.GetProperty("target").GetString());
		Assert.Equal(["refs/tags/v*"], Strings(RefNameIncludes(tags)));
		Assert.Equal(["deletion", "non_fast_forward", "update"], types.Order(StringComparer.Ordinal));
		Assert.DoesNotContain("creation", types);
	}

	[Fact]
	public void NuGetEnvironmentRequiresAReviewerWithoutAdminBypass()
	{
		using JsonDocument environment = Load("environments/nuget.json");
		JsonElement root = environment.RootElement;

		Assert.Equal("nuget", root.GetProperty("name").GetString());
		Assert.False(root.GetProperty("can_admins_bypass").GetBoolean());
		Assert.False(root.GetProperty("prevent_self_review").GetBoolean());
		JsonElement policy = Assert.Single(root.GetProperty("deployment_policies").EnumerateArray());
		string script = GovernanceFile.ReadText(ScriptPath);

		Assert.Equal("v*.*.*", policy.GetProperty("name").GetString());
		Assert.Equal("tag", policy.GetProperty("type").GetString());
		Assert.Contains("[string[]] $NuGetReviewer = @('AriusII')", script, StringComparison.Ordinal);
	}

	[Fact]
	public void ActionsRequireShaPinningAndAReadOnlyToken()
	{
		using JsonDocument actions = Load("actions-permissions.json");
		using JsonDocument security = Load("security.json");

		JsonElement permissions = actions.RootElement.GetProperty("permissions");
		JsonElement workflow = actions.RootElement.GetProperty("workflow");

		Assert.True(permissions.GetProperty("sha_pinning_required").GetBoolean());
		Assert.Equal("read", workflow.GetProperty("default_workflow_permissions").GetString());
		Assert.False(workflow.GetProperty("can_approve_pull_request_reviews").GetBoolean());
		Assert.Equal("not-configured", security.RootElement.GetProperty("code_scanning_default_setup").GetString());
		Assert.True(security.RootElement.GetProperty("private_vulnerability_reporting").GetBoolean());
	}

	[Fact]
	public void ScriptMutatesOnlyThroughTheApplyGuard()
	{
		string script = GovernanceFile.ReadText(ScriptPath);
		Match mutation = Regex.Match(
			script, @"function Invoke-GitHubMutation \{(?<body>.*?)\r?\n\}", RegexOptions.Singleline, TimeSpan.FromSeconds(1));

		int methodOptions = Regex.Count(script, "--method", RegexOptions.None, TimeSpan.FromSeconds(1));

		Assert.True(methodOptions == 1, $"Only Invoke-GitHubMutation may pass --method to gh api; found {methodOptions}.");
		Assert.True(mutation.Success, "Invoke-GitHubMutation must exist.");
		Assert.Contains("--method", mutation.Groups["body"].Value, StringComparison.Ordinal);
		Assert.Contains("if (-not ($Apply -and $script:Writing))", mutation.Groups["body"].Value, StringComparison.Ordinal);
		Assert.Contains("[ValidateSet('PUT', 'POST', 'PATCH')]", mutation.Groups["body"].Value, StringComparison.Ordinal);
		// Fields (-f/-F) or -X would turn a read into a write outside the guard.
		Regex implicitWrite = new(@"gh\s+api\s+[^\r\n]*\s-(f|F|X)\s", RegexOptions.None, TimeSpan.FromSeconds(1));
		Assert.DoesNotMatch(implicitWrite, script);
		Assert.Contains("never by CI", script, StringComparison.Ordinal);
	}

	private static JsonDocument Load(string relativePath)
	{
		return JsonDocument.Parse(GovernanceFile.ReadText($"{Folder}/{relativePath}"));
	}

	private static JsonElement Rule(JsonDocument ruleset, string type)
	{
		return Assert.Single(ruleset.RootElement.GetProperty("rules").EnumerateArray(), rule => RuleType(rule) == type);
	}

	private static string RuleType(JsonElement rule)
	{
		return rule.GetProperty("type").GetString() ?? "";
	}

	private static JsonElement RefNameIncludes(JsonDocument ruleset)
	{
		return ruleset.RootElement.GetProperty("conditions").GetProperty("ref_name").GetProperty("include");
	}

	private static List<string> Strings(JsonElement array)
	{
		return [.. array.EnumerateArray().Select(item => item.GetString() ?? "")];
	}

	private static string? JobName(string workflow, string job)
	{
		YamlMappingNode node = GovernanceFile.Jobs(GovernanceFile.LoadYaml(workflow))[job];
		return GovernanceFile.Scalar(node, "name");
	}
}
