using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.Client.Repository.Tests.Governance;

/// <summary>
/// Advisory scheduled health (PR-CQ-56, PR-CQ-31 flaky-test policy, CI-MS-03 strict audit): jobs select their schedule
/// by the exact cron text, only the report job can write issues and only for scheduled failures, the canary points
/// global.json at the newest SDK without running the .NET CLI and keeps its errorMessage on that SDK, it regenerates
/// lock files only through the repository script, and nothing retries tests.
/// </summary>
public sealed class ScheduledHealthWorkflowTests
{
	private const string WorkflowPath = ".github/workflows/scheduled-health.yml";
	private const string ScriptPath = "eng/ci/Invoke-ScheduledHealth.ps1";
	private const string SetupAction = "./.github/actions/setup-dotnet";

	private static readonly YamlMappingNode _workflow = GovernanceFile.LoadYaml(WorkflowPath);

	private static readonly JsonDocumentOptions _jsonOptions = new()
	{
		CommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true
	};

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
		string script = GovernanceFile.ReadText(GlobalJsonSdkRewrite.ScriptPath);
		IReadOnlyList<YamlMappingNode> steps = GovernanceFile.Steps(GovernanceFile.Jobs(_workflow)["canary"]);
		int select = GovernanceFile.IndexOfRun(steps, GlobalJsonSdkRewrite.ScriptPath);
		int setup = GovernanceFile.IndexOfAction(steps, SetupAction);

		// The script edits the text in place; it never writes a new JSON document, which would drop or reorder sections.
		Assert.DoesNotContain("ConvertTo-Json", script, StringComparison.Ordinal);
		Assert.Contains("changed more than sdk.version and the SDK version named by sdk.errorMessage", script, StringComparison.Ordinal);
		Assert.True(select >= 0 && select < setup, "Rewrite global.json before the composite action installs its SDK.");
	}

	[Fact]
	public void SdkSelectionNeverRunsTheDotNetCli()
	{
		// It runs before the composite action installs any SDK: the same heuristic as
		// WorkflowContractTests.EveryDotnetJobUsesTheCompositeSetupAction, applied to the script and to its step.
		Regex invocation = new(@"&\s*dotnet\b|^\s*dotnet\s", RegexOptions.Multiline, TimeSpan.FromSeconds(1));
		IReadOnlyList<YamlMappingNode> steps = GovernanceFile.Steps(GovernanceFile.Jobs(_workflow)["canary"]);
		string run = GovernanceFile.Scalar(steps[GovernanceFile.IndexOfRun(steps, GlobalJsonSdkRewrite.ScriptPath)], "run") ?? "";

		Assert.DoesNotMatch(invocation, GovernanceFile.ReadText(GlobalJsonSdkRewrite.ScriptPath));
		Assert.Equal("./" + GlobalJsonSdkRewrite.ScriptPath, run.Trim());
		Assert.DoesNotContain("SelectNewestSdk", GovernanceFile.ReadText(ScriptPath), StringComparison.Ordinal);
	}

	[Fact]
	public void SdkSelectionUsesTheMirroredRewritePatterns()
	{
		string script = GovernanceFile.ReadText(GlobalJsonSdkRewrite.ScriptPath);

		Assert.Contains($"$SdkVersionPattern = '{GlobalJsonSdkRewrite.SdkVersionPattern}'", script, StringComparison.Ordinal);
		Assert.Contains($"$ErrorMessagePattern = '{GlobalJsonSdkRewrite.ErrorMessagePattern}'", script, StringComparison.Ordinal);
		Assert.Contains(
			$"'{GlobalJsonSdkRewrite.VersionStart}' + [regex]::Escape($Pinned) + '{GlobalJsonSdkRewrite.VersionEnd}'",
			script,
			StringComparison.Ordinal);
	}

	[Fact]
	public void SdkSelectionMovesTheErrorMessageToTheSelectedVersion()
	{
		string original = GovernanceFile.ReadText("global.json");
		(string pinned, _) = SdkSettings(original);
		string[] parts = pinned.Split('.');
		string selected = $"{parts[0]}.{parts[1]}.{int.Parse(parts[2], CultureInfo.InvariantCulture) + 1}";

		string updated = GlobalJsonSdkRewrite.Apply(original, pinned, selected);
		(string version, string message) = SdkSettings(updated);

		Assert.Equal(selected, version);
		// The rule of ToolchainPinTests.GlobalJsonErrorMessageNamesThePinnedSdkVersion, which the canary's Release tests run.
		Assert.Contains($"--version {selected}", message, StringComparison.Ordinal);
		Assert.DoesNotMatch(GlobalJsonSdkRewrite.WholeVersion(pinned), message);
		Assert.True(JsonNode.DeepEquals(WithoutRewrittenValues(original), WithoutRewrittenValues(updated)),
			"The rewrite changed global.json beyond sdk.version and the SDK version named by sdk.errorMessage.");

		string[] before = original.Split('\n');
		string[] after = updated.Split('\n');
		Assert.Equal(before.Length, after.Length);
		for (int line = 0; line < before.Length; line++)
		{
			if (!before[line].Contains("\"version\"", StringComparison.Ordinal) &&
				!before[line].Contains("\"errorMessage\"", StringComparison.Ordinal))
			{
				Assert.Equal(before[line], after[line]);
			}
		}
	}

	[Fact]
	public void SdkSelectionLeavesGlobalJsonUntouchedWhenThePinIsTheNewestSdk()
	{
		string original = GovernanceFile.ReadText("global.json");
		(string pinned, _) = SdkSettings(original);

		Assert.Equal(original, GlobalJsonSdkRewrite.Apply(original, pinned, pinned));
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

		// Any later label event of the same pull request must not replace a pending dry run.
		string group = GovernanceFile.Scalar(GovernanceFile.Mapping(_workflow, "concurrency")!, "group") ?? "";
		Assert.Contains("github.event.label.name", group, StringComparison.Ordinal);
	}

	private static (string Version, string ErrorMessage) SdkSettings(string globalJson)
	{
		JsonNode sdk = JsonNode.Parse(globalJson, documentOptions: _jsonOptions)!["sdk"]!;
		return (sdk["version"]!.GetValue<string>(), sdk["errorMessage"]!.GetValue<string>());
	}

	private static JsonNode WithoutRewrittenValues(string globalJson)
	{
		JsonNode root = JsonNode.Parse(globalJson, documentOptions: _jsonOptions)!;
		JsonObject sdk = root["sdk"]!.AsObject();
		sdk.Remove("version");
		sdk.Remove("errorMessage");
		return root;
	}
}
