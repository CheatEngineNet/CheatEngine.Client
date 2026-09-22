using System.Text.RegularExpressions;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.Client.Repository.Tests.Governance;

/// <summary>
/// Advisory scheduled health (PR-CQ-56, PR-CQ-31 flaky-test policy, CI-MS-03 strict audit): jobs select their schedule
/// by the exact cron text, only the report job can write issues and only for scheduled failures, the canary regenerates
/// lock files only through the repository script and keeps global.json's other settings, and nothing retries tests.
/// </summary>
public sealed class ScheduledHealthWorkflowTests
{
	private const string WorkflowPath = ".github/workflows/scheduled-health.yml";
	private const string ScriptPath = "eng/ci/Invoke-ScheduledHealth.ps1";
	private const string SetupAction = "./.github/actions/setup-dotnet";

	private static readonly YamlMappingNode _workflow = GovernanceFile.LoadYaml(WorkflowPath);

	[Fact]
	public void ScheduleStringsMatchTheJobConditions()
	{
		HashSet<string> crons = new(
			(GovernanceFile.Sequence(GovernanceFile.Triggers(_workflow), "schedule")?.Children ?? [])
			.Cast<YamlMappingNode>()
			.Select(entry => GovernanceFile.Scalar(entry, "cron") ?? string.Empty),
			StringComparer.Ordinal);
		Regex scheduleCondition = new(@"github\.event\.schedule == '([^']*)'", RegexOptions.None, TimeSpan.FromSeconds(1));
		List<string> referenced = [];
		foreach (YamlMappingNode job in GovernanceFile.Jobs(_workflow).Values)
		{
			MatchCollection matches = scheduleCondition.Matches(GovernanceFile.Condition(job));
			referenced.AddRange(matches.Select(match => match.Groups[1].Value));
		}

		Assert.Equal(2, crons.Count);
		Assert.NotEmpty(referenced);
		Assert.All(referenced, cron => Assert.Contains(cron, crons));
	}

	[Fact]
	public void OnlyTheReportJobCanWriteIssues()
	{
		Assert.Equal(new Dictionary<string, string> { ["contents"] = "read" }, GovernanceFile.Permissions(_workflow));
		foreach ((string id, YamlMappingNode job) in GovernanceFile.Jobs(_workflow))
		{
			IReadOnlyDictionary<string, string> permissions = GovernanceFile.Permissions(job);
			if (id == "report")
			{
				Assert.Equal(new Dictionary<string, string> { ["issues"] = "write" }, permissions);
			}
			else
			{
				Assert.DoesNotContain("write", permissions.Values);
			}
		}
	}

	[Fact]
	public void IssuesAreOpenedOnlyForScheduledFailures()
	{
		YamlMappingNode report = GovernanceFile.Jobs(_workflow)["report"];
		YamlMappingNode step = Assert.Single(GovernanceFile.Steps(report));

		Assert.Equal(
			"always() && github.event_name == 'schedule' && contains(needs.*.result, 'failure')",
			GovernanceFile.Condition(report));
		Assert.Equal(["audit", "canary", "repeat"], GovernanceFile.Strings(GovernanceFile.Child(report, "needs")));
		Assert.DoesNotContain("${{", GovernanceFile.Scalar(step, "run"), StringComparison.Ordinal);
		Assert.Contains("gh issue", GovernanceFile.Scalar(step, "run"), StringComparison.Ordinal);
	}

	[Fact]
	public void CanaryRegeneratesLocksOnlyThroughTheRepositoryScript()
	{
		string script = GovernanceFile.ReadText(ScriptPath);

		Assert.Contains("eng/Update-LockFiles.ps1", script, StringComparison.Ordinal);
		Assert.DoesNotContain("--force-evaluate", script, StringComparison.Ordinal);
		Assert.DoesNotContain("--force-evaluate", GovernanceFile.ReadText(WorkflowPath), StringComparison.Ordinal);
		Assert.Contains("sdk-canary.patch", script, StringComparison.Ordinal);
	}

	[Fact]
	public void CanaryKeepsTheTestRunnerSection()
	{
		string script = GovernanceFile.ReadText(ScriptPath);
		IReadOnlyList<YamlMappingNode> steps = GovernanceFile.Steps(GovernanceFile.Jobs(_workflow)["canary"]);
		int select = GovernanceFile.IndexOfRun(steps, "-Mode SelectNewestSdk");
		int setup = GovernanceFile.IndexOfAction(steps, SetupAction);

		Assert.DoesNotContain("ConvertTo-Json", script, StringComparison.Ordinal);
		Assert.Contains("changed more than sdk.version", script, StringComparison.Ordinal);
		Assert.True(select >= 0 && select < setup, "Rewrite global.json before the composite action installs its SDK.");
	}

	[Fact]
	public void RepeatRunNeverUsesTheRetryExtension()
	{
		foreach (string file in new[] { ScriptPath, WorkflowPath, "eng/Tests.props", "Directory.Packages.props" })
		{
			string text = GovernanceFile.ReadText(file);
			Assert.DoesNotContain("Microsoft.Testing.Extensions.Retry", text, StringComparison.Ordinal);
			Assert.DoesNotContain("--retry-failed-tests", text, StringComparison.Ordinal);
		}

		Assert.Contains("-Mode Repeat -Iterations 5", GovernanceFile.ReadText(WorkflowPath), StringComparison.Ordinal);
	}

	[Fact]
	public void EveryDotNetJobUsesTheCompositeActionWithoutCache()
	{
		foreach (string id in new[] { "audit", "canary", "repeat" })
		{
			IReadOnlyList<YamlMappingNode> steps = GovernanceFile.Steps(GovernanceFile.Jobs(_workflow)[id]);
			Assert.Equal("false", GovernanceFile.With(GovernanceFile.StepUsing(steps, SetupAction), "cache"));
		}
	}

	[Fact]
	public void PullRequestRunsAreLabelGatedDryRuns()
	{
		YamlNode? trigger = GovernanceFile.Child(GovernanceFile.Triggers(_workflow), "pull_request");
		YamlMappingNode pullRequest = Assert.IsType<YamlMappingNode>(trigger);

		Assert.Equal(["labeled"], GovernanceFile.Strings(GovernanceFile.Child(pullRequest, "types")));
		foreach (string id in new[] { "audit", "canary", "repeat" })
		{
			string condition = GovernanceFile.Condition(GovernanceFile.Jobs(_workflow)[id]);
			Assert.Contains("github.event.label.name == 'dry-run'", condition, StringComparison.Ordinal);
		}
	}

}
