using System.Text.RegularExpressions;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.Client.Repository.Tests.Governance;

/// <summary>
/// CI-side dependency submission (PR-CQ-46, CI-BOTH-1): detection runs a pinned, hash-verified Component Detection
/// binary with a read-only token; the only job holding <c>contents: write</c> runs no third-party code; fork pull
/// requests never reach it; the snapshot names the pull request head commit it describes.
/// </summary>
public sealed class DependencySubmissionWorkflowTests
{
	private const string WorkflowPath = ".github/workflows/dependency-submission.yml";
	private const string ScriptPath = "eng/ci/New-DependencySnapshot.ps1";
	private const string ForkGuard =
		"github.event_name != 'pull_request' || github.event.pull_request.head.repo.full_name == github.repository";

	private const string SnapshotCommit = "${{ github.event.pull_request.head.sha || github.sha }}";

	private static readonly YamlMappingNode _workflow = GovernanceFile.LoadYaml(WorkflowPath);

	[Fact]
	public void OnlyTheSubmitJobHoldsContentsWrite()
	{
		Assert.Equal(new Dictionary<string, string> { ["contents"] = "read" }, GovernanceFile.Permissions(_workflow));
		foreach ((string id, YamlMappingNode job) in GovernanceFile.Jobs(_workflow))
		{
			IReadOnlyDictionary<string, string> expected = id == "submit"
				? new Dictionary<string, string> { ["contents"] = "write" }
				: new Dictionary<string, string>();
			Assert.Equal(expected, GovernanceFile.Permissions(job));
		}
	}

	[Fact]
	public void SubmitJobRunsNoThirdPartyCode()
	{
		YamlMappingNode submit = GovernanceFile.Jobs(_workflow)["submit"];
		IReadOnlyList<YamlMappingNode> steps = GovernanceFile.Steps(submit);

		Assert.Equal(2, steps.Count);
		Assert.Equal("actions/download-artifact", GovernanceFile.ActionName(steps[0]));
		string run = GovernanceFile.Scalar(steps[1], "run") ?? string.Empty;
		Assert.Null(GovernanceFile.ActionName(steps[1]));
		Assert.Contains("gh api --method POST", run, StringComparison.Ordinal);
		Assert.Contains("/dependency-graph/snapshots", run, StringComparison.Ordinal);
		Assert.DoesNotContain("${{", run, StringComparison.Ordinal);
		Assert.Equal(["detect"], GovernanceFile.Strings(GovernanceFile.Child(submit, "needs")));
	}

	[Fact]
	public void ForkPullRequestsNeverRunTheSubmission()
	{
		YamlMappingNode triggers = GovernanceFile.Triggers(_workflow);

		Assert.Equal(["push", "pull_request", "workflow_dispatch"], GovernanceFile.Keys(triggers));
		Assert.Equal(["main"], GovernanceFile.PushBranches(_workflow));
		Assert.All(GovernanceFile.Jobs(_workflow).Values, job => Assert.Equal(ForkGuard, GovernanceFile.Condition(job)));
	}

	[Fact]
	public void SnapshotDescribesThePullRequestHeadCommit()
	{
		IReadOnlyList<YamlMappingNode> steps = GovernanceFile.Steps(GovernanceFile.Jobs(_workflow)["detect"]);
		YamlMappingNode checkout = GovernanceFile.StepUsing(steps, "actions/checkout");
		int detection = GovernanceFile.IndexOfRun(steps, ScriptPath);

		Assert.True(detection >= 0, $"The detect job must run {ScriptPath}.");
		YamlMappingNode environment = Assert.IsType<YamlMappingNode>(GovernanceFile.Child(steps[detection], "env"));
		Assert.Equal(SnapshotCommit, GovernanceFile.With(checkout, "ref"));
		Assert.Equal(SnapshotCommit, GovernanceFile.Scalar(environment, "SNAPSHOT_SHA"));
		Assert.Equal("${{ github.ref }}", GovernanceFile.Scalar(environment, "SNAPSHOT_REF"));
	}

	[Fact]
	public void DetectionUsesAPinnedHashVerifiedComponentDetection()
	{
		string script = GovernanceFile.ReadText(ScriptPath);

		Assert.Contains("$DetectorVersion = '8.0.1'", script, StringComparison.Ordinal);
		Assert.Matches(new Regex(@"\$DetectorSha256 = '[0-9a-f]{64}'", RegexOptions.None, TimeSpan.FromSeconds(1)), script);
		Assert.Contains("Get-FileHash", script, StringComparison.Ordinal);
		Assert.DoesNotContain("releases/latest", script, StringComparison.Ordinal);
		Assert.DoesNotContain(
			"component-detection-dependency-submission-action", GovernanceFile.ReadText(WorkflowPath), StringComparison.Ordinal);
		Assert.DoesNotContain("--method", script, StringComparison.Ordinal);
	}
}
