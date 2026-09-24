using YamlDotNet.RepresentationModel;

namespace CheatEngine.Client.Repository.Tests.Governance;

/// <summary>
/// Advisory online zizmor audits (PR-CQ-21): the same zizmor version as the blocking offline run of the CI Gate, online
/// audits with SARIF upload to code scanning, never from a fork pull request (read-only token).
/// </summary>
public sealed class OnlineZizmorWorkflowTests
{
	private const string WorkflowPath = ".github/workflows/zizmor-online.yml";
	private const string ZizmorAction = "zizmorcore/zizmor-action";

	private static readonly YamlMappingNode Workflow = GovernanceFile.LoadYaml(WorkflowPath);

	[Fact]
	public void OnlineZizmorPinsTheSameVersionAsTheGate()
	{
		List<YamlMappingNode> gateSteps = [.. GovernanceFile.Jobs(GovernanceFile.LoadYaml(".github/workflows/ci.yml")).Values
			.SelectMany(GovernanceFile.Steps)
			.Where(step => GovernanceFile.ActionName(step) == ZizmorAction)];

		Assert.True(gateSteps.Count == 1,
			$"ci.yml must run {ZizmorAction} once (offline, lint job, shared-contracts §1.6); found {gateSteps.Count}.");
		YamlMappingNode gateStep = gateSteps[0];
		string gateVersion = GovernanceFile.With(gateStep, "version") ?? string.Empty;
		Assert.Matches(@"^\d+\.\d+\.\d+$", gateVersion);
		Assert.Equal(gateVersion, GovernanceFile.With(ZizmorStep(), "version"));
		Assert.Equal(GovernanceFile.Scalar(gateStep, "uses"), GovernanceFile.Scalar(ZizmorStep(), "uses"));
	}

	[Fact]
	public void OnlineZizmorRunsOnlineAuditsWithSarif()
	{
		YamlMappingNode step = ZizmorStep();
		YamlMappingNode job = Assert.Single(GovernanceFile.Jobs(Workflow)).Value;

		Assert.Equal("true", GovernanceFile.With(step, "online-audits"));
		Assert.Equal("true", GovernanceFile.With(step, "advanced-security"));
		Assert.Equal(".github/zizmor.yml", GovernanceFile.With(step, "config"));
		Assert.Equal("write", GovernanceFile.Permissions(job)["security-events"]);
		Assert.Null(GovernanceFile.Child(job, "continue-on-error"));
	}

	[Fact]
	public void OnlineZizmorNeverUploadsFromForks()
	{
		YamlMappingNode job = Assert.Single(GovernanceFile.Jobs(Workflow)).Value;

		Assert.Equal(
			"github.event_name != 'pull_request' || github.event.pull_request.head.repo.full_name == github.repository",
			GovernanceFile.Condition(job));
		Assert.False(GovernanceFile.Has(GovernanceFile.Triggers(Workflow), "pull_request_target"));
	}

	private static YamlMappingNode ZizmorStep()
	{
		YamlMappingNode job = Assert.Single(GovernanceFile.Jobs(Workflow)).Value;
		return Assert.Single(GovernanceFile.Steps(job), step => GovernanceFile.ActionName(step) == ZizmorAction);
	}
}
