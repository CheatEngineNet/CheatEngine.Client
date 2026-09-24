using System.Globalization;
using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.Client.Repository.Tests.Workflows;

/// <summary>
/// Freezes the CI contract shared with CheatEngine.SDK: the required check "CI / Gate" and how it is produced, the job
/// ids and names, the Sonar expectation, runner labels, timeouts, permissions, action pins, artifact names, the absence
/// of NuGet caches on release-reachable paths, locked restores, and the build-test order that tests the packages CI
/// publishes. The rules iterate the workflows that exist, so workflows added later are held to them too. They assert
/// structure, not long literal text, so the YAML can be reformatted safely.
/// </summary>
public sealed partial class WorkflowContractTests
{
	private const string CiWorkflow = ".github/workflows/ci.yml";
	private const string SonarWorkflow = ".github/workflows/sonar.yml";
	private const string MainCiWorkflow = ".github/workflows/main-ci.yml";
	private const string PullRequestCiWorkflow = ".github/workflows/pull-request-ci.yml";
	private const string PolicyWorkflow = ".github/workflows/pr-policy.yml";
	private const string ReleaseWorkflow = ".github/workflows/release.yml";
	private const string SetupAction = ".github/actions/setup-dotnet/action.yml";
	private const string SetupActionReference = "./.github/actions/setup-dotnet";
	private const string ZizmorConfig = ".github/zizmor.yml";

	private static readonly string[] PinnedRunners = ["windows-2025", "ubuntu-24.04"];

	/// <summary>The four workflows of the CI pipeline itself; other workflows (for example Scorecard) may omit defaults.</summary>
	private static readonly string[] PipelineWorkflows = [CiWorkflow, SonarWorkflow, MainCiWorkflow, PullRequestCiWorkflow];

	/// <summary>Workflows every job of which a release run, Sonar or CodeQL can reach: none may use a NuGet package cache.</summary>
	private static readonly string[] ReleaseReachableWorkflows =
	[
		CiWorkflow, SonarWorkflow, ".github/workflows/codeql.yml", ReleaseWorkflow
	];

	/// <summary>The frozen job ids and names of ci.yml (a check is named "CI / &lt;name&gt;").</summary>
	private static readonly Dictionary<string, string> CiJobs = new(StringComparer.Ordinal)
	{
		["build-test"] = "Build and test (${{ matrix.configuration }})",
		["aot"] = "Native AOT publication probe",
		["sonar"] = "Sonar",
		["lint"] = "Lint",
		["format"] = "Format",
		["dependency-review"] = "Dependency review",
		["lock-files"] = "Lock files",
		["gate"] = "Gate"
	};

	/// <summary>Advisory ci.yml jobs outside the Gate (continue-on-error). The Client has none; keep the mechanism.</summary>
	private static readonly HashSet<string> AdvisoryJobs = new(StringComparer.Ordinal);

	/// <summary>Job ids the Client pipeline retired; they must not come back.</summary>
	private static readonly string[] RetiredJobs = ["validate", "lint-workflows"];

	/// <summary>
	/// Every artifact name a workflow may upload. Names are reserved so producers and consumers cannot drift and an
	/// upload never collides; adding one is a reviewed change of this list.
	/// </summary>
	private static readonly HashSet<string> ReservedArtifacts = new(StringComparer.Ordinal)
	{
		"nuget-packages",
		"coverage",
		"test-results-Debug",
		"test-results-Release",
		"test-dumps-Debug",
		"test-dumps-Release",
		"release-notes",
		"attestation-bundles",
		// Advisory governance workflows (scorecard.yml).
		"scorecard-results"
	};

	/// <summary>Binary logs: binlogs-&lt;job&gt; or binlogs-&lt;job&gt;-&lt;configuration&gt;.</summary>
	private static readonly Regex BinlogArtifact = new("^binlogs-[a-z0-9-]+?(-(Debug|Release))?$");

	/// <summary>Names the old pipeline used; reusing one would silently feed an obsolete consumer.</summary>
	private static readonly string[] RetiredArtifacts = ["test-results", "sonar-coverage", "native-aot-probe"];

	/// <summary>The canonical pin of each action used by either repository (owner/repository, commit SHA, release tag).</summary>
	private static readonly Dictionary<string, (string Sha, string Version)> CanonicalPins = new(StringComparer.Ordinal)
	{
		["actions/checkout"] = ("3d3c42e5aac5ba805825da76410c181273ba90b1", "v7.0.1"),
		["actions/upload-artifact"] = ("043fb46d1a93c77aae656e7c1c64a875d1fc6a0a", "v7.0.1"),
		["actions/download-artifact"] = ("3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c", "v8.0.1"),
		["actions/setup-dotnet"] = ("a98b56852c35b8e3190ac28c8c2271da59106c68", "v6.0.0"),
		["actions/setup-java"] = ("de7274f081f381c8f8158605e0321c36c376e2e6", "v6.0.1"),
		["actions/attest"] = ("1e69f48acb82d1966a394da916b4c1698aa569d6", "v4.2.2"),
		["NuGet/login"] = ("8d196754b4036150537f80ac539e15c2f1028841", "v1.2.0"),
		["xmake-io/github-action-setup-xmake"] = ("3a1a5dddfc7fa625d9a698738334bf55655a861a", "v1.2.5"),
		["actions/dependency-review-action"] = ("a1d282b36b6f3519aa1f3fc636f609c47dddb294", "v5.0.0"),
		["zizmorcore/zizmor-action"] = ("cc914d7f3750a2d13d75c7f184a1060aa0e9d482", "v0.6.4"),
		["github/codeql-action"] = ("1c5b675653bb5c22dbe9b12b556ec555138e09fd", "v4.38.1"),
		["ossf/scorecard-action"] = ("2d1146689b8cda280b9bc96326124645441f03bc", "v2.4.4"),
		["actions/cache"] = ("55cc8345863c7cc4c66a329aec7e433d2d1c52a9", "v6.1.0")
	};

	// ---- Callers, triggers and the required check -------------------------------------------------------------------

	[Fact]
	public void CallersInvokeCiThroughJobCiNamedCi()
	{
		List<string> callers = [];
		foreach (WorkflowFile workflow in WorkflowFile.Workflows())
		{
			foreach (WorkflowJob job in workflow.Jobs)
			{
				if (job.Uses == "./.github/workflows/ci.yml")
				{
					callers.Add(workflow.RelativePath);
					Assert.True(job.Id == "ci" && job.Name == "CI",
						$"{workflow.RelativePath} calls ci.yml from job '{job.Id}' named '{job.Name}'; the required check is 'CI / Gate', so the caller must be job 'ci' named 'CI'.");
				}
			}
		}

		Assert.Contains(PullRequestCiWorkflow, callers);
		Assert.Contains(MainCiWorkflow, callers);
		if (WorkflowFile.Exists(ReleaseWorkflow))
		{
			Assert.Contains(ReleaseWorkflow, callers);
		}
	}

	[Fact]
	public void NoWorkflowUsesPullRequestTargetOrAMergeGroupTrigger()
	{
		foreach (WorkflowFile workflow in WorkflowFile.Workflows())
		{
			string[] triggers = Triggers(workflow);
			Assert.False(triggers.Contains("pull_request_target"),
				$"{workflow.RelativePath} uses pull_request_target, which runs untrusted code with repository secrets.");
			Assert.False(triggers.Contains("merge_group"),
				$"{workflow.RelativePath} listens to merge_group; there is no merge queue, and an untested event path must not produce the required check.");
		}
	}

	[Fact]
	public void PullRequestAndPolicyWorkflowsHaveNoPathFilters()
	{
		foreach (string path in new[] { PullRequestCiWorkflow, PolicyWorkflow })
		{
			if (!WorkflowFile.Exists(path))
			{
				continue;
			}

			WorkflowFile workflow = WorkflowFile.Load(path);
			if (Yaml.Get(workflow.Root, "on") is YamlMappingNode on)
			{
				foreach (KeyValuePair<YamlNode, YamlNode> trigger in on.Children)
				{
					YamlMappingNode? filters = trigger.Value as YamlMappingNode;
					Assert.True(Yaml.Get(filters, "paths") is null && Yaml.Get(filters, "paths-ignore") is null,
						$"{path} filters its '{((YamlScalarNode) trigger.Key).Value}' trigger by path; a required check that does not run on some pull requests never reports and blocks them, or lets them through unchecked.");
				}
			}
		}
	}

	[Fact]
	public void MainCiHasNoConcurrencyGroup()
	{
		WorkflowFile workflow = WorkflowFile.Load(MainCiWorkflow);
		Assert.Null(Yaml.Get(workflow.Root, "concurrency"));
		Assert.Null(Yaml.Get(workflow.Job("ci").Node, "concurrency"));
		string[] expectedTriggers = ["push", "workflow_dispatch"];
		Assert.Equal(expectedTriggers, Triggers(workflow).Order(StringComparer.Ordinal).ToArray());
		Assert.Equal("true", Yaml.Scalar(Yaml.Mapping(workflow.Job("ci").Node, "with"), "sonar"));
	}

	[Fact]
	public void PullRequestCiCancelsSupersededRunsByPullRequestNumber()
	{
		WorkflowFile workflow = WorkflowFile.Load(PullRequestCiWorkflow);
		YamlMappingNode concurrency = Assert.IsType<YamlMappingNode>(Yaml.Get(workflow.Root, "concurrency"));
		Assert.Contains("github.event.pull_request.number", Yaml.Scalar(concurrency, "group") ?? string.Empty, StringComparison.Ordinal);
		Assert.Equal("true", Yaml.Scalar(concurrency, "cancel-in-progress"));

		YamlMappingNode pullRequest = Assert.IsType<YamlMappingNode>(Yaml.Get(Yaml.Mapping(workflow.Root, "on"), "pull_request"));
		string[] types = Yaml.Sequence(pullRequest, "types")!.Children.Select(static type => ((YamlScalarNode) type).Value!).ToArray();
		string[] expectedTypes = ["opened", "synchronize", "reopened", "ready_for_review"];
		Assert.Equal(expectedTypes, types);

		WorkflowJob ci = workflow.Job("ci");
		Assert.Equal("!github.event.pull_request.draft", Yaml.NormalizeExpression(ci.Condition));
		Assert.Equal("true", Yaml.Scalar(Yaml.Mapping(ci.Node, "with"), "sonar"));
	}

	// ---- ci.yml jobs and the Gate -------------------------------------------------------------------------------------

	[Fact]
	public void CiJobsMatchTheFrozenContractIdsAndNames()
	{
		WorkflowFile ci = WorkflowFile.Load(CiWorkflow);
		Dictionary<string, string?> jobs = ci.Jobs.ToDictionary(static job => job.Id, static job => job.Name, StringComparer.Ordinal);

		Assert.Equal(CiJobs.Keys.Order(StringComparer.Ordinal), jobs.Keys.Order(StringComparer.Ordinal));
		foreach ((string id, string name) in CiJobs)
		{
			Assert.True(jobs[id] == name, $"ci.yml job '{id}' is named '{jobs[id]}'; the contract name is '{name}'.");
		}

		foreach (string retired in RetiredJobs)
		{
			Assert.False(jobs.ContainsKey(retired), $"ci.yml brings back the retired job '{retired}'.");
		}

		Assert.Equal("CI", Yaml.Scalar(ci.Root, "name"));
		string[] expectedTriggers = ["workflow_call"];
		Assert.Equal(expectedTriggers, Triggers(ci));
	}

	[Fact]
	public void GateJobIsNamedGateRunsAlwaysAndHasNoPermissions()
	{
		WorkflowJob gate = WorkflowFile.Load(CiWorkflow).Job("gate");

		Assert.Equal("Gate", gate.Name);
		Assert.Equal("always()", Yaml.NormalizeExpression(gate.Condition));
		YamlMappingNode permissions = Assert.IsType<YamlMappingNode>(Yaml.Get(gate.Node, "permissions"));
		Assert.Empty(permissions.Children);
		Assert.Equal("ubuntu-24.04", gate.RunsOn);
		Assert.DoesNotContain(gate.Steps, static step => step.UsesAction("actions/checkout"));

		WorkflowStep check = Assert.Single(gate.Steps);
		Assert.Equal("toJSON(needs)", Yaml.NormalizeExpression(check.Env("NEEDS")));
		Assert.Contains("ConvertFrom-Json", check.Run, StringComparison.Ordinal);
		Assert.Contains("exit 1", check.Run, StringComparison.Ordinal);
	}

	[Fact]
	public void GateNeedsEveryOtherCiJobExceptTheAdvisoryAllowlist()
	{
		WorkflowFile ci = WorkflowFile.Load(CiWorkflow);
		string[] expected = ci.Jobs.Select(static job => job.Id)
			.Where(static id => id != "gate" && !AdvisoryJobs.Contains(id))
			.Order(StringComparer.Ordinal)
			.ToArray();
		string[] needs = ci.Job("gate").Needs.Order(StringComparer.Ordinal).ToArray();

		Assert.True(expected.SequenceEqual(needs),
			$"gate.needs = [{string.Join(", ", needs)}] but must list every other ci.yml job: [{string.Join(", ", expected)}]. A job missing from the Gate can fail without failing 'CI / Gate'.");

		foreach (string advisory in AdvisoryJobs)
		{
			Assert.Equal("true", Yaml.Scalar(ci.Job(advisory).Node, "continue-on-error"));
		}
	}

	[Fact]
	public void OnlySonarAndGateHaveJobLevelConditions()
	{
		foreach (WorkflowJob job in WorkflowFile.Load(CiWorkflow).Jobs)
		{
			if (job.Id is "sonar" or "gate")
			{
				Assert.False(string.IsNullOrWhiteSpace(job.Condition), $"ci.yml job '{job.Id}' must keep its condition.");
				continue;
			}

			Assert.True(job.Condition is null,
				$"ci.yml job '{job.Id}' has a job-level condition; the Gate accepts no skip except sonar, so decide per event at step level.");
		}
	}

	[Fact]
	public void SonarConditionEqualsTheGateSonarExpectedExpression()
	{
		WorkflowFile ci = WorkflowFile.Load(CiWorkflow);
		string condition = Yaml.NormalizeExpression(ci.Job("sonar").Condition);
		string expected = Yaml.NormalizeExpression(Assert.Single(ci.Job("gate").Steps).Env("SONAR_EXPECTED"));

		Assert.Equal(expected, condition);
		Assert.StartsWith("inputs.sonar &&", condition, StringComparison.Ordinal);
		Assert.Contains("github.event_name != 'merge_group'", condition, StringComparison.Ordinal);
		Assert.Contains("github.actor != 'dependabot[bot]'", condition, StringComparison.Ordinal);
		Assert.Contains("github.event.pull_request.head.repo.full_name == github.repository", condition, StringComparison.Ordinal);
	}

	[Fact]
	public void SonarWaitsForTheQualityGateOutsidePushEvents()
	{
		WorkflowJob sonar = WorkflowFile.Load(CiWorkflow).Job("sonar");
		Assert.Equal("./.github/workflows/sonar.yml", sonar.Uses);
		Assert.Equal("github.event_name != 'push'",
			Yaml.NormalizeExpression(Yaml.Scalar(Yaml.Mapping(sonar.Node, "with"), "wait-quality-gate")));
		Assert.Equal("build-test", Assert.Single(sonar.Needs));

		string[] callerInputs = [.. Yaml.Keys(Yaml.Mapping(sonar.Node, "with"))];
		Assert.Equal("wait-quality-gate", Assert.Single(callerInputs));

		// CI-based analysis is the only method: no opt-out input, no repository variable deciding whether Sonar runs.
		WorkflowFile workflow = WorkflowFile.Load(SonarWorkflow);
		YamlMappingNode? call = Yaml.Mapping(Yaml.Mapping(workflow.Root, "on"), "workflow_call");
		string[] inputs = [.. Yaml.Keys(Yaml.Mapping(call, "inputs")).Order(StringComparer.Ordinal)];
		string[] expectedInputs = ["organization", "project-key", "wait-quality-gate"];
		Assert.Equal(expectedInputs, inputs);
		Assert.DoesNotContain("SONAR_CI_ENABLED", workflow.Text, StringComparison.Ordinal);
		WorkflowJob analyze = workflow.Job("analyze");
		Assert.Equal("inputs.wait-quality-gate",
			Yaml.NormalizeExpression(Yaml.Scalar(Yaml.Mapping(analyze.Node, "env"), "SONAR_WAIT_QUALITY_GATE")));
		Assert.Contains(analyze.Steps, static step => step.Run.Contains("sonar.qualitygate.wait=$env:SONAR_WAIT_QUALITY_GATE", StringComparison.Ordinal));
	}

	[Fact]
	public void SonarRestoresLockedBeforeScannerBegin()
	{
		IReadOnlyList<WorkflowStep> steps = WorkflowFile.Load(SonarWorkflow).Job("analyze").Steps;
		WorkflowStep restore = Assert.Single(steps, static step => Regex.IsMatch(step.Run, @"\bdotnet restore\b"));
		WorkflowStep begin = Assert.Single(steps, static step => step.Run.Contains("dotnet-sonarscanner.exe\" begin", StringComparison.Ordinal));
		WorkflowStep build = Assert.Single(steps, static step => Regex.IsMatch(step.Run, @"\bdotnet build\b"));

		Assert.Contains("--locked-mode", restore.Run, StringComparison.Ordinal);
		Assert.Contains("--configfile", restore.Run, StringComparison.Ordinal);
		Assert.True(restore.Index < begin.Index, "sonar.yml must restore before scanner begin, while no credential is configured.");
		Assert.True(begin.Index < build.Index);
		Assert.Contains("--no-restore", build.Run, StringComparison.Ordinal);
		Assert.Contains("-c Debug", build.Run, StringComparison.Ordinal);
		Assert.Contains(steps, static step => step.With("name") == "coverage");
	}

	// ---- build-test ---------------------------------------------------------------------------------------------------

	[Fact]
	public void ReleaseLegPacksBeforeTestingAndExportsThePackageSource()
	{
		WorkflowJob buildTest = WorkflowFile.Load(CiWorkflow).Job("build-test");
		IReadOnlyList<WorkflowStep> steps = buildTest.Steps;
		WorkflowStep build = Assert.Single(steps, static step => Regex.IsMatch(step.Run, @"\bdotnet build\b"));
		WorkflowStep pack = Assert.Single(steps, static step => Regex.IsMatch(step.Run, @"\bdotnet pack\b"));
		WorkflowStep test = Assert.Single(steps, static step => Regex.IsMatch(step.Run, @"\bdotnet test\b"));

		Assert.True(build.Index < pack.Index && pack.Index < test.Index, "build-test must run Build, then Pack, then Test.");
		Assert.Equal("pack", pack.Id);
		Assert.Contains("'Release'", pack.Condition ?? string.Empty, StringComparison.Ordinal);
		Assert.Contains("--no-build", pack.Run, StringComparison.Ordinal);
		Assert.Contains("manifest.spdx.json", pack.Run, StringComparison.Ordinal);

		Assert.Equal("steps.pack.outputs.package-source", Yaml.NormalizeExpression(test.Env("PACKAGE_SOURCE")));
		Assert.Contains("CHEATENGINE_CLIENT_PACKAGE_SOURCE", test.Run, StringComparison.Ordinal);
		Assert.DoesNotContain("CHEATENGINE_CLIENT_PACKAGE_SOURCE", Yaml.Keys(Yaml.Mapping(buildTest.Node, "env")));
		foreach (WorkflowStep step in steps)
		{
			Assert.Null(step.Env("CHEATENGINE_CLIENT_PACKAGE_SOURCE"));
		}
	}

	[Fact]
	public void DebugLegExcludesPackageConsumptionTestsByTraitNeverBySkip()
	{
		WorkflowStep test = TestStep();
		IReadOnlyList<string> tokens = Yaml.Tokens(test.Run);

		Assert.Equal("Category=PackageConsumption", TokenAfter(tokens, "--filter-not-trait"));
		Assert.Equal("on", TokenAfter(tokens, "--fail-skips"));
		Assert.Equal("CheatEngine.Client.slnx", TokenAfter(tokens, "--solution"));
		Assert.Contains("--no-build", tokens);
		Assert.DoesNotContain("--", tokens);
		Assert.DoesNotContain("--filter-class", tokens);
		Assert.DoesNotContain("--filter-not-class", tokens);
	}

	[Fact]
	public void HangDumpTimeoutIsWellBelowTheBuildTestJobTimeout()
	{
		WorkflowJob buildTest = WorkflowFile.Load(CiWorkflow).Job("build-test");
		IReadOnlyList<string> tokens = Yaml.Tokens(TestStep().Run);
		Assert.Contains("--hangdump", tokens);
		Assert.Contains("--crashdump", tokens);

		string timeout = TokenAfter(tokens, "--hangdump-timeout");
		Match duration = Regex.Match(timeout, @"^(?<value>\d+(\.\d+)?)(?<unit>m|min|s|h)$");
		Assert.True(duration.Success, $"--hangdump-timeout '{timeout}' must use m, min, s or h.");
		double minutes = double.Parse(duration.Groups["value"].Value, CultureInfo.InvariantCulture) *
						 duration.Groups["unit"].Value switch
						 {
							 "s" => 1.0 / 60.0,
							 "h" => 60.0,
							 _ => 1.0
						 };
		int jobTimeout = int.Parse(buildTest.TimeoutMinutes!, CultureInfo.InvariantCulture);
		Assert.True(minutes <= jobTimeout / 2.0,
			$"The hang dump fires after {minutes} minutes without test activity; keep it at most half of build-test's {jobTimeout}-minute timeout so the dump is written and uploaded.");
	}

	[Fact]
	public void BuildTestNeverPromotesEveryWarningToAnError()
	{
		foreach (WorkflowFile workflow in WorkflowFile.WorkflowsAndActions())
		{
			foreach (WorkflowStep step in workflow.Jobs.SelectMany(static job => job.Steps).Concat(workflow.CompositeSteps))
			{
				Assert.False(Regex.IsMatch(step.Run, @"(--|-|/)warnaserror\b", RegexOptions.IgnoreCase),
					$"{workflow.RelativePath} step '{step.Name}' passes -warnaserror, which also promotes the NU1901/NU1902 audit warnings and overrides the NuGet audit policy; TreatWarningsAsErrors already covers compiler and analyzer warnings.");
			}
		}
	}

	[Fact]
	public void BenchmarksAreCompiledByTheSolutionBuild()
	{
		XDocument solution = XDocument.Load(RepositoryRoot.SolutionPath);
		Assert.Contains(solution.Descendants("Project"),
			static project => (string?) project.Attribute("Path") == "tests/CheatEngine.Client.Benchmarks/CheatEngine.Client.Benchmarks.csproj");

		IReadOnlyList<WorkflowStep> steps = WorkflowFile.Load(CiWorkflow).Job("build-test").Steps;
		WorkflowStep build = Assert.Single(steps, static step => Regex.IsMatch(step.Run, @"\bdotnet build\b"));
		Assert.Contains("CheatEngine.Client.slnx", build.Run, StringComparison.Ordinal);
		Assert.Contains(steps, static step => step.Run.Contains("CheatEngine.Client.Benchmarks.csproj", StringComparison.Ordinal) &&
											  step.Run.Contains("--list", StringComparison.Ordinal));
	}

	[Fact]
	public void AotJobPublishesAndRunsTheAotProbe()
	{
		WorkflowJob aot = WorkflowFile.Load(CiWorkflow).Job("aot");
		Assert.Equal("windows-2025", aot.RunsOn);
		Assert.Empty(aot.Needs);

		WorkflowStep setup = Assert.Single(aot.Steps, static step => step.Uses == SetupActionReference);
		Assert.Contains("tests/CheatEngine.Client.AotProbe/CheatEngine.Client.AotProbe.csproj", setup.With("restore") ?? string.Empty, StringComparison.Ordinal);

		WorkflowStep publish = Assert.Single(aot.Steps, static step => Regex.IsMatch(step.Run, @"\bdotnet publish\b"));
		Assert.Contains("tests/CheatEngine.Client.AotProbe/CheatEngine.Client.AotProbe.csproj", publish.Run, StringComparison.Ordinal);
		Assert.Contains("--no-restore", publish.Run, StringComparison.Ordinal);
		Assert.DoesNotContain("--runtime", publish.Run, StringComparison.Ordinal);
		Assert.Contains("CheatEngine.Client.AotProbe.exe", publish.Run, StringComparison.Ordinal);
		Assert.Contains("$LASTEXITCODE", publish.Run, StringComparison.Ordinal);

		foreach (WorkflowStep upload in aot.Steps.Where(static step => step.UsesAction("actions/upload-artifact")))
		{
			Assert.DoesNotContain("aot-probe", upload.With("path") ?? string.Empty, StringComparison.Ordinal);
		}
	}

	// ---- lint, format, dependency review, lock files ------------------------------------------------------------------

	[Fact]
	public void LintJobRunsActionlintAndZizmorOnEveryEvent()
	{
		WorkflowJob lint = WorkflowFile.Load(CiWorkflow).Job("lint");
		Assert.Null(lint.Condition);
		Assert.Equal("ubuntu-24.04", lint.RunsOn);

		WorkflowStep checkout = Assert.Single(lint.Steps, static step => step.UsesAction("actions/checkout"));
		Assert.Null(checkout.With("sparse-checkout"));

		Assert.Contains(lint.Steps, static step => step.Run.Contains("actionlint", StringComparison.Ordinal) &&
												   step.Run.Contains("Get-FileHash", StringComparison.Ordinal));
		WorkflowStep zizmor = Assert.Single(lint.Steps, static step => step.UsesAction("zizmorcore/zizmor-action"));
		Assert.Equal("false", zizmor.With("online-audits"));
		Assert.Equal("false", zizmor.With("advanced-security"));
		Assert.Equal(ZizmorConfig, zizmor.With("config"));
		foreach (WorkflowStep step in lint.Steps)
		{
			Assert.Null(step.Condition);
		}
	}

	[Fact]
	public void ZizmorAndActionlintArePinnedByVersionAndChecksum()
	{
		WorkflowJob lint = WorkflowFile.Load(CiWorkflow).Job("lint");
		YamlMappingNode? env = Yaml.Mapping(lint.Node, "env");
		Assert.Matches(new Regex(@"^\d+\.\d+\.\d+$"), Yaml.Scalar(env, "ACTIONLINT_VERSION") ?? string.Empty);
		Assert.Matches(new Regex("^[0-9a-f]{64}$"), Yaml.Scalar(env, "ACTIONLINT_SHA256") ?? string.Empty);

		WorkflowStep zizmor = Assert.Single(lint.Steps, static step => step.UsesAction("zizmorcore/zizmor-action"));
		Assert.Matches(new Regex(@"^\d+\.\d+\.\d+$"), zizmor.With("version") ?? "latest");
	}

	[Fact]
	public void EveryZizmorExceptionCarriesAJustificationComment()
	{
		string[] lines = File.ReadAllLines(Path.Combine(RepositoryRoot.Path, ZizmorConfig));
		int rules = 0;
		for (int index = 0; index < lines.Length; index++)
		{
			if (!RuleEntry().IsMatch(lines[index]))
			{
				continue;
			}

			rules++;
			int previous = index - 1;
			while (previous >= 0 && lines[previous].Trim().Length == 0)
			{
				previous--;
			}

			Assert.True(previous >= 0 && lines[previous].TrimStart().StartsWith('#'),
				$"{ZizmorConfig}:{index + 1} '{lines[index].Trim()}' has no justification comment above it.");
		}

		Assert.True(rules > 0, $"{ZizmorConfig} declares no rule.");

		WorkflowFile config = WorkflowFile.Load(ZizmorConfig);
		foreach (KeyValuePair<YamlNode, YamlNode> rule in Yaml.Mapping(config.Root, "rules")!.Children)
		{
			foreach (YamlNode entry in Yaml.Sequence((YamlMappingNode) rule.Value, "ignore")?.Children ?? [])
			{
				string[] parts = ((YamlScalarNode) entry).Value!.Split(':');
				string? file = WorkflowFile.WorkflowsAndActions().Select(static workflow => workflow.RelativePath)
					.Concat([".github/dependabot.yml"])
					.FirstOrDefault(path => Path.GetFileName(path) == parts[0]);
				Assert.True(file is not null && WorkflowFile.Exists(file),
					$"{ZizmorConfig} ignores '{parts[0]}', which is not a file under .github; remove the stale entry.");
				if (parts.Length > 1)
				{
					string[] target = File.ReadAllLines(Path.Combine(RepositoryRoot.Path, file!));
					int line = int.Parse(parts[1], CultureInfo.InvariantCulture);
					Assert.True(line <= target.Length, $"{ZizmorConfig} ignores {parts[0]}:{line}, past the end of the file.");
					if (((YamlScalarNode) rule.Key).Value == "github-env")
					{
						string window = string.Join('\n', target.Skip(line - 1).Take(4));
						Assert.True(window.Contains("run:", StringComparison.Ordinal) && window.Contains("GITHUB_ENV", StringComparison.Ordinal),
							$"{ZizmorConfig} ignores github-env at {parts[0]}:{line}, which is no longer the constant GITHUB_ENV write; update the line.");
					}
				}
			}
		}
	}

	[Fact]
	public void FormatJobVerifiesWhitespaceWithoutRestore()
	{
		WorkflowJob format = WorkflowFile.Load(CiWorkflow).Job("format");
		Assert.Null(format.Condition);
		Assert.Equal("ubuntu-24.04", format.RunsOn);

		WorkflowStep setup = Assert.Single(format.Steps, static step => step.Uses == SetupActionReference);
		Assert.Null(setup.With("restore"));

		WorkflowStep verify = Assert.Single(format.Steps, static step => step.Run.Contains("dotnet format", StringComparison.Ordinal));
		IReadOnlyList<string> tokens = Yaml.Tokens(verify.Run);
		Assert.Equal("whitespace", TokenAfter(tokens, "format"));
		Assert.Contains("--folder", tokens);
		Assert.Contains("--verify-no-changes", tokens);
		Assert.DoesNotContain(format.Steps, static step => Regex.IsMatch(step.Run, @"\bdotnet restore\b"));
	}

	[Fact]
	public void DependencyReviewJobAlwaysRunsAndReviewsOnlyPullRequests()
	{
		WorkflowJob review = WorkflowFile.Load(CiWorkflow).Job("dependency-review");
		Assert.Null(review.Condition);
		Assert.Null(Yaml.Get(review.Node, "permissions"));

		WorkflowStep action = Assert.Single(review.Steps, static step => step.UsesAction("actions/dependency-review-action"));
		Assert.Equal("github.event_name == 'pull_request'", Yaml.NormalizeExpression(action.Condition));
		Assert.Equal("./.github/dependency-review-config.yml", action.With("config-file"));
		Assert.Equal("never", action.With("comment-summary-in-pr"));
		Assert.True(WorkflowFile.Exists(".github/dependency-review-config.yml"));

		Assert.Contains(review.Steps, static step => Yaml.NormalizeExpression(step.Condition) == "github.event_name != 'pull_request'" &&
													 step.Run.Contains("::notice", StringComparison.Ordinal));
	}

	[Fact]
	public void LockFileJobRunsTheVerificationScriptOnWindows()
	{
		WorkflowJob lockFiles = WorkflowFile.Load(CiWorkflow).Job("lock-files");
		Assert.Equal("windows-2025", lockFiles.RunsOn);
		Assert.Null(lockFiles.Condition);

		WorkflowStep setup = Assert.Single(lockFiles.Steps, static step => step.Uses == SetupActionReference);
		Assert.Null(setup.With("restore"));
		Assert.Contains(lockFiles.Steps, static step => Regex.IsMatch(step.Run, @"\bdotnet restore CheatEngine\.Client\.slnx --locked-mode\b"));
	}

	// ---- Setup, caches and restores ------------------------------------------------------------------------------------

	[Fact]
	public void CompositeSetupRestoresInLockedModeAndDefaultsCacheToFalse()
	{
		WorkflowFile action = WorkflowFile.Load(SetupAction);
		YamlMappingNode inputs = Yaml.Mapping(action.Root, "inputs")!;
		Assert.Equal("false", Yaml.Scalar(Yaml.Mapping(inputs, "cache"), "default"));
		Assert.Equal(string.Empty, Yaml.Scalar(Yaml.Mapping(inputs, "restore"), "default"));

		IReadOnlyList<WorkflowStep> steps = action.CompositeSteps;
		WorkflowStep install = Assert.Single(steps, static step => step.UsesAction("actions/setup-dotnet"));
		Assert.Equal("global.json", install.With("global-json-file"));
		Assert.Equal("inputs.cache", Yaml.NormalizeExpression(install.With("cache")));

		WorkflowStep restore = Assert.Single(steps, static step => Regex.IsMatch(step.Run, @"\bdotnet restore\b"));
		Assert.Contains("--locked-mode", restore.Run, StringComparison.Ordinal);
		string restoreLine = Assert.Single(restore.Run.Split('\n'), static line => Regex.IsMatch(line, @"\bdotnet restore \$target\b"));
		Assert.DoesNotContain("--force-evaluate", restoreLine, StringComparison.Ordinal);
		Assert.Contains("$LASTEXITCODE", restore.Run, StringComparison.Ordinal);
		Assert.Contains("dotnet restore <project> --force-evaluate", restore.Run, StringComparison.Ordinal);
		Assert.Equal("inputs.restore", Yaml.NormalizeExpression(restore.Env("RESTORE_TARGETS")));
		Assert.DoesNotContain("${{", restore.Run, StringComparison.Ordinal);
	}

	[Fact]
	public void EveryDotnetJobUsesTheCompositeSetupAction()
	{
		foreach (WorkflowFile workflow in WorkflowFile.Workflows())
		{
			foreach (WorkflowJob job in workflow.Jobs)
			{
				WorkflowStep? firstDotnet = job.Steps.FirstOrDefault(static step => RunsDotnet(step.Run));
				if (firstDotnet is null)
				{
					continue;
				}

				WorkflowStep? setup = job.Steps.FirstOrDefault(static step => step.Uses == SetupActionReference);
				Assert.True(setup is not null && setup.Index < firstDotnet.Index,
					$"{workflow.RelativePath} job '{job.Id}' runs dotnet without {SetupActionReference} first; global.json requires an exact SDK that runner images do not ship.");
				Assert.DoesNotContain(job.Steps, static step => step.UsesAction("actions/setup-dotnet"));
			}
		}
	}

	[Fact]
	public void ReleaseReachableWorkflowsNeverEnableAPackageCache()
	{
		foreach (string path in ReleaseReachableWorkflows.Where(WorkflowFile.Exists))
		{
			WorkflowFile workflow = WorkflowFile.Load(path);
			foreach (WorkflowJob job in workflow.Jobs)
			{
				foreach (WorkflowStep step in job.Steps)
				{
					if (step.Uses == SetupActionReference || step.UsesAction("actions/setup-dotnet"))
					{
						string cache = step.With("cache") ?? "false";
						Assert.True(cache == "false",
							$"{path} job '{job.Id}' enables a NuGet cache (cache: {cache}); a release, Sonar or CodeQL run must never restore packages another run could have written.");
					}

					if (!step.UsesAction("actions/cache"))
					{
						continue;
					}

					// The single documented exception: Sonar analyzer plugins, restored on any event, saved from main only.
					Assert.True(path == SonarWorkflow, $"{path} job '{job.Id}' uses actions/cache.");
					Assert.Contains("sonar-user-home/cache", step.With("path") ?? string.Empty, StringComparison.Ordinal);
					if (step.Uses!.StartsWith("actions/cache/save@", StringComparison.Ordinal))
					{
						Assert.Contains("github.ref == 'refs/heads/main'", Yaml.NormalizeExpression(step.Condition), StringComparison.Ordinal);
					}
					else
					{
						Assert.StartsWith("actions/cache/restore@", step.Uses, StringComparison.Ordinal);
					}
				}
			}
		}
	}

	[Fact]
	public void NoWorkflowReferencesTheLocalQualificationRunner()
	{
		foreach (WorkflowFile workflow in WorkflowFile.WorkflowsAndActions())
		{
			Assert.DoesNotContain("eng/qualification", workflow.Text, StringComparison.Ordinal);
		}
	}

	[Fact]
	public void NoWorkflowReadsRepositoryVariables()
	{
		foreach (WorkflowFile workflow in WorkflowFile.WorkflowsAndActions())
		{
			Assert.False(Regex.IsMatch(workflow.Text, @"\bvars\."),
				$"{workflow.RelativePath} reads a repository variable (vars.*); a setting that is not in the commit cannot be reviewed or reproduced.");
		}
	}

	// ---- Hygiene: runners, timeouts, permissions, pins, checkout, history ---------------------------------------------

	[Fact]
	public void EveryJobHasATimeoutAndAPinnedRunnerLabel()
	{
		foreach (WorkflowFile workflow in WorkflowFile.Workflows())
		{
			foreach (WorkflowJob job in workflow.Jobs)
			{
				if (job.Uses is not null)
				{
					// GitHub rejects runs-on and timeout-minutes on a reusable-workflow call; the callee's jobs are checked.
					Assert.Null(job.RunsOn);
					Assert.Null(job.TimeoutMinutes);
					continue;
				}

				Assert.True(job.RunsOn is not null && PinnedRunners.Contains(job.RunsOn),
					$"{workflow.RelativePath} job '{job.Id}' runs on '{job.RunsOn}'; use one of {string.Join(", ", PinnedRunners)} (never -latest, which moves without a commit).");
				Assert.True(int.TryParse(job.TimeoutMinutes, out int minutes) && minutes > 0,
					$"{workflow.RelativePath} job '{job.Id}' has no timeout-minutes.");
			}
		}
	}

	[Fact]
	public void WorkflowsGrantOnlyReadPermissionsAtTheTopLevel()
	{
		foreach (WorkflowFile workflow in WorkflowFile.Workflows())
		{
			YamlNode? permissions = Yaml.Get(workflow.Root, "permissions");
			Assert.True(permissions is not null,
				$"{workflow.RelativePath} has no top-level permissions; the default token would be broader than needed.");
			if (permissions is YamlMappingNode mapping)
			{
				foreach (KeyValuePair<YamlNode, YamlNode> permission in mapping.Children)
				{
					string level = ((YamlScalarNode) permission.Value).Value!;
					Assert.True(level is "read" or "none",
						$"{workflow.RelativePath} grants '{((YamlScalarNode) permission.Key).Value}: {level}' at the top level; elevate per job only.");
				}
			}
			else
			{
				Assert.Equal("read-all", ((YamlScalarNode) permissions!).Value);
			}
		}

		foreach (string path in PipelineWorkflows)
		{
			YamlMappingNode permissions = Assert.IsType<YamlMappingNode>(Yaml.Get(WorkflowFile.Load(path).Root, "permissions"));
			Assert.Equal("read", Yaml.Scalar(permissions, "contents"));
			Assert.Single(permissions.Children);
		}

		foreach (WorkflowJob job in WorkflowFile.Load(CiWorkflow).Jobs)
		{
			if (job.Id != "gate")
			{
				Assert.True(Yaml.Get(job.Node, "permissions") is null, $"ci.yml job '{job.Id}' changes permissions; no ci.yml job elevates.");
			}
		}
	}

	[Fact]
	public void DefaultShellIsPwshInTheCiWorkflows()
	{
		foreach (string path in PipelineWorkflows)
		{
			YamlMappingNode? run = Yaml.Mapping(Yaml.Mapping(WorkflowFile.Load(path).Root, "defaults"), "run");
			Assert.True(Yaml.Scalar(run, "shell") == "pwsh", $"{path} must set defaults.run.shell: pwsh.");
		}
	}

	[Fact]
	public void EveryRemoteActionIsPinnedToAFullShaWithAVersionComment()
	{
		foreach (WorkflowFile workflow in WorkflowFile.WorkflowsAndActions())
		{
			foreach ((int line, string reference, string? version) in UsesLines(workflow))
			{
				if (reference.StartsWith("./", StringComparison.Ordinal))
				{
					continue;
				}

				Assert.True(PinnedReference().IsMatch(reference) && version is not null && Version().IsMatch(version),
					$"{workflow.RelativePath}:{line} uses '{reference}'; pin every action to a full commit SHA followed by '# vX.Y.Z'.");
			}
		}
	}

	[Fact]
	public void ActionsUseOnePinEverywhereAndMatchTheCanonicalTable()
	{
		Dictionary<string, (string Sha, string Version, string Where)> seen = new(StringComparer.Ordinal);
		foreach (WorkflowFile workflow in WorkflowFile.WorkflowsAndActions())
		{
			foreach ((int line, string reference, string? version) in UsesLines(workflow))
			{
				Match pinned = PinnedReference().Match(reference);
				if (!pinned.Success)
				{
					continue;
				}

				string action = pinned.Groups["action"].Value;
				string sha = pinned.Groups["sha"].Value;
				string where = $"{workflow.RelativePath}:{line}";
				if (CanonicalPins.TryGetValue(action, out (string Sha, string Version) canonical))
				{
					Assert.True(sha == canonical.Sha && version == canonical.Version,
						$"{where} pins {action} to {sha} {version}; the canonical pin shared with CheatEngine.SDK is {canonical.Sha} # {canonical.Version}.");
				}

				if (seen.TryGetValue(action, out (string Sha, string Version, string Where) first))
				{
					Assert.True(first.Sha == sha && first.Version == version,
						$"{where} pins {action} to {sha}, but {first.Where} pins it to {first.Sha}; use one pin everywhere.");
				}
				else
				{
					seen[action] = (sha, version ?? string.Empty, where);
				}
			}
		}
	}

	[Fact]
	public void EveryCheckoutDisablesCredentialPersistence()
	{
		foreach (WorkflowFile workflow in WorkflowFile.WorkflowsAndActions())
		{
			foreach (WorkflowStep step in workflow.Jobs.SelectMany(static job => job.Steps).Concat(workflow.CompositeSteps))
			{
				if (step.UsesAction("actions/checkout"))
				{
					Assert.True(step.With("persist-credentials") == "false",
						$"{workflow.RelativePath} step '{step.Name}' checks out without persist-credentials: false; the token must not stay in .git/config for later steps.");
				}
			}
		}
	}

	[Fact]
	public void JobsThatPackTestOrPublishFetchFullHistory()
	{
		foreach (WorkflowFile workflow in WorkflowFile.Workflows())
		{
			foreach (WorkflowJob job in workflow.Jobs)
			{
				WorkflowStep[] versioned = job.Steps.Where(static step =>
					Regex.IsMatch(step.Run, @"\bdotnet (build|pack|test|publish)\b") ||
					step.Run.Contains("dotnet-sonarscanner", StringComparison.Ordinal)).ToArray();
				// A job that never needs the package version may opt out explicitly with MinVerSkip instead.
				if (versioned.Length == 0 || versioned.All(static step => step.Run.Contains("MinVerSkip=true", StringComparison.Ordinal)))
				{
					continue;
				}

				WorkflowStep checkout = Assert.Single(job.Steps, static step => step.UsesAction("actions/checkout"));
				Assert.True(checkout.With("fetch-depth") == "0",
					$"{workflow.RelativePath} job '{job.Id}' builds or packs without fetch-depth: 0; a shallow clone makes the package version fall back silently.");
			}
		}

		Assert.Equal("0", Assert.Single(WorkflowFile.Load(SonarWorkflow).Job("analyze").Steps,
			static step => step.UsesAction("actions/checkout")).With("fetch-depth"));
	}

	// ---- Artifacts --------------------------------------------------------------------------------------------------------

	[Fact]
	public void EveryUploadedArtifactNameIsReserved()
	{
		foreach (WorkflowFile workflow in WorkflowFile.Workflows())
		{
			HashSet<string> names = new(StringComparer.Ordinal);
			foreach (WorkflowJob job in workflow.Jobs)
			{
				foreach (WorkflowStep step in job.Steps.Where(static step => step.UsesAction("actions/upload-artifact")))
				{
					string? name = step.With("name");
					Assert.True(name is not null, $"{workflow.RelativePath} job '{job.Id}' uploads an artifact without a name.");
					foreach (string expanded in job.ExpandMatrix(name!))
					{
						Assert.DoesNotContain(expanded, RetiredArtifacts);
						Assert.True(ReservedArtifacts.Contains(expanded) || BinlogArtifact.IsMatch(expanded),
							$"{workflow.RelativePath} job '{job.Id}' uploads '{expanded}', which is not a reserved artifact name.");
						Assert.True(names.Add(expanded), $"{workflow.RelativePath} uploads '{expanded}' twice in one run.");
					}
				}
			}
		}
	}

	[Fact]
	public void BinlogsAreUploadedOnlyOnFailureAndNeverFromSonarOrRelease()
	{
		foreach (WorkflowFile workflow in WorkflowFile.Workflows())
		{
			foreach (WorkflowJob job in workflow.Jobs)
			{
				foreach (WorkflowStep step in job.Steps.Where(static step => step.UsesAction("actions/upload-artifact")))
				{
					bool binlogs = (step.With("name") ?? string.Empty).StartsWith("binlogs-", StringComparison.Ordinal) ||
								   (step.With("path") ?? string.Empty).Contains(".binlog", StringComparison.Ordinal);
					if (binlogs)
					{
						Assert.True(Yaml.NormalizeExpression(step.Condition) == "failure()",
							$"{workflow.RelativePath} job '{job.Id}' uploads binary logs without if: failure(); they capture environment variables.");
					}
				}
			}
		}

		foreach (string path in new[] { SonarWorkflow, ReleaseWorkflow }.Where(WorkflowFile.Exists))
		{
			string text = WorkflowFile.Load(path).Text;
			Assert.False(Regex.IsMatch(text, @"(-bl\b|-bl:|/bl\b|\.binlog|binlogs-)"),
				$"{path} must not write or upload binary logs: they capture the environment of a credentialed job.");
		}
	}

	private static WorkflowStep TestStep()
	{
		return Assert.Single(WorkflowFile.Load(CiWorkflow).Job("build-test").Steps,
			static step => Regex.IsMatch(step.Run, @"\bdotnet test\b"));
	}

	private static string TokenAfter(IReadOnlyList<string> tokens, string option)
	{
		for (int index = 0; index < tokens.Count - 1; index++)
		{
			if (tokens[index] == option)
			{
				return tokens[index + 1];
			}
		}

		throw new Xunit.Sdk.XunitException($"The command does not pass {option} with a value.");
	}

	private static string[] Triggers(WorkflowFile workflow)
	{
		return Yaml.Get(workflow.Root, "on") switch
		{
			YamlScalarNode scalar => [scalar.Value!],
			YamlSequenceNode sequence => sequence.Children.Select(static item => ((YamlScalarNode) item).Value!).ToArray(),
			YamlMappingNode mapping => Yaml.Keys(mapping).ToArray(),
			_ => []
		};
	}

	/// <summary>True when a script runs dotnet directly or through a repository script that does.</summary>
	private static bool RunsDotnet(string run)
	{
		if (Regex.IsMatch(run, @"(^|[\s;&(])dotnet\s", RegexOptions.Multiline))
		{
			return true;
		}

		foreach (Match script in Regex.Matches(run, @"eng/[\w./-]+\.ps1"))
		{
			string path = Path.Combine(RepositoryRoot.Path, script.Value);
			if (File.Exists(path) && Regex.IsMatch(File.ReadAllText(path), @"&\s*dotnet\b|^\s*dotnet\s", RegexOptions.Multiline))
			{
				return true;
			}
		}

		return false;
	}

	private static IEnumerable<(int Line, string Reference, string? Version)> UsesLines(WorkflowFile workflow)
	{
		string[] lines = workflow.Text.Split('\n');
		for (int index = 0; index < lines.Length; index++)
		{
			Match uses = UsesLine().Match(lines[index].TrimEnd('\r'));
			if (uses.Success)
			{
				string? version = uses.Groups["comment"].Success ? uses.Groups["comment"].Value.Trim() : null;
				yield return (index + 1, uses.Groups["reference"].Value, version);
			}
		}
	}

	[GeneratedRegex(@"^\s*(-\s+)?uses:\s*(?<reference>[^\s#]+)\s*(#\s*(?<comment>.*))?$")]
	private static partial Regex UsesLine();

	[GeneratedRegex(@"^(?<action>[A-Za-z0-9-]+/[A-Za-z0-9._-]+)(/[A-Za-z0-9._/-]+)?@(?<sha>[0-9a-f]{40})$")]
	private static partial Regex PinnedReference();

	[GeneratedRegex(@"^v\d+\.\d+\.\d+$")]
	private static partial Regex Version();

	[GeneratedRegex(@"^  [a-z][a-z0-9-]*:\s*$")]
	private static partial Regex RuleEntry();
}
