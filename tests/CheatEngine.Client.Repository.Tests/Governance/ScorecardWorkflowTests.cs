using System.Text.RegularExpressions;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.Client.Repository.Tests.Governance;

/// <summary>
/// Advisory OpenSSF Scorecard (PR-CQ-25, CI-BOTH-5). With <c>publish_results: true</c> the Scorecard API verifies the
/// workflow (ossf/scorecard-infra, api/app/server/verify_workflow.go) and rejects it when it has workflow- or job-level
/// <c>env</c>/<c>defaults</c>, workflow-level write permissions, <c>id-token: write</c> outside the Scorecard job,
/// containers or services, steps without <c>uses</c>, actions outside its allowlist, or an unsupported runner label.
/// These tests keep the file inside those limits (it deliberately breaks the repository's pwsh-defaults convention).
/// </summary>
public sealed class ScorecardWorkflowTests
{
	private const string WorkflowPath = ".github/workflows/scorecard.yml";

	/// <summary>The verifier's action allowlist minus step-security/harden-runner (excluded by decision).</summary>
	private static readonly HashSet<string> _approvedActions = new(StringComparer.Ordinal)
	{
		"actions/checkout",
		"actions/create-github-app-token",
		"actions/upload-artifact",
		"github/codeql-action/upload-sarif",
		"ossf/scorecard-action"
	};

	private static readonly YamlMappingNode _workflow = GovernanceFile.LoadYaml(WorkflowPath);

	[Fact]
	public void ScorecardHasNoDefaultsOrEnvironmentAtAnyLevel()
	{
		Assert.False(GovernanceFile.Has(_workflow, "env"));
		Assert.False(GovernanceFile.Has(_workflow, "defaults"));
		foreach (YamlMappingNode job in GovernanceFile.Jobs(_workflow).Values)
		{
			foreach (string key in new[] { "env", "defaults", "container", "services" })
			{
				Assert.False(GovernanceFile.Has(job, key), $"The Scorecard verifier rejects a job-level '{key}'.");
			}
		}
	}

	[Fact]
	public void ScorecardStepsOnlyUseApprovedActions()
	{
		YamlMappingNode job = Assert.Single(GovernanceFile.Jobs(_workflow)).Value;
		IReadOnlyList<YamlMappingNode> steps = GovernanceFile.Steps(job);

		Assert.NotEmpty(steps);
		Assert.All(steps, step =>
		{
			Assert.False(GovernanceFile.Has(step, "run"), "The Scorecard verifier rejects run: steps.");
			Assert.Contains(GovernanceFile.ActionName(step) ?? string.Empty, _approvedActions);
		});
		YamlMappingNode scorecard = GovernanceFile.StepUsing(steps, "ossf/scorecard-action");
		Assert.Equal("true", GovernanceFile.With(scorecard, "publish_results"));
	}

	[Fact]
	public void ScorecardRunsOnOneSupportedUbuntuLabel()
	{
		YamlMappingNode job = Assert.Single(GovernanceFile.Jobs(_workflow)).Value;
		string label = GovernanceFile.Scalar(job, "runs-on") ?? string.Empty;

		Regex verifierLabel = new(@"^ubuntu-(latest|\d{2}\.\d{2})(-arm)?$", RegexOptions.None, TimeSpan.FromSeconds(1));

		Assert.Matches(verifierLabel, label);
		Assert.Equal("ubuntu-24.04", label);
	}

	[Fact]
	public void OnlyTheScorecardJobRequestsAnIdToken()
	{
		KeyValuePair<string, YamlMappingNode> job = Assert.Single(GovernanceFile.Jobs(_workflow));

		Assert.Equal("analysis", job.Key);
		Assert.Equal("write", GovernanceFile.Permissions(job.Value)["id-token"]);
		Assert.False(GovernanceFile.Permissions(_workflow).ContainsKey("id-token"));
	}

	[Fact]
	public void ScorecardHasNoWorkflowLevelWritePermission()
	{
		IReadOnlyDictionary<string, string> permissions = GovernanceFile.Permissions(_workflow);

		Assert.NotEmpty(permissions);
		Assert.All(permissions.Values, level => Assert.Equal("read", level));
	}
}
