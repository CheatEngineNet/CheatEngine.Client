using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.Client.Repository.Tests.Release;

/// <summary>
/// The release workflow keeps the contract order (verify, ci, attest, draft-release, publish, verify-publication,
/// finalize-release), publishes only from a tag of this repository through the nuget environment, and never caches
/// packages (audit A21-06). Pins, runners, timeouts and checkout credentials are checked for every workflow by the
/// workflow contract tests.
/// </summary>
public sealed partial class ReleaseWorkflowTests
{
	private const string WorkflowPath = ".github/workflows/release.yml";

	private static readonly string[] _jobOrder = ["verify", "ci", "attest", "draft-release", "publish", "verify-publication", "finalize-release"];

	private static readonly Dictionary<string, string[]> _needs = new(StringComparer.Ordinal)
	{
		["verify"] = [],
		["ci"] = ["verify"],
		["attest"] = ["verify", "ci"],
		["draft-release"] = ["verify", "ci", "attest"],
		["publish"] = ["verify", "ci", "draft-release"],
		["verify-publication"] = ["verify", "publish"],
		["finalize-release"] = ["verify", "draft-release", "verify-publication"]
	};

	/// <summary>Every package after the Client packages it depends on.</summary>
	private static readonly string[] _pushOrder =
	[
		"CheatEngine.Client.Abstractions", "CheatEngine.Client.Fluent", "CheatEngine.Client.Core",
		"CheatEngine.Client.Extensions.DependencyInjection", "CheatEngine.Client.Hosting", "CheatEngine.Client",
		"CheatEngine.Client.Templates"
	];

	private static readonly Lazy<YamlMappingNode> _workflow = new(LoadWorkflow);

	[Fact]
	public void ReleaseJobsFollowTheContractOrder()
	{
		YamlMappingNode jobs = Mapping(_workflow.Value, "jobs");
		YamlMappingNode on = Mapping(_workflow.Value, "on");

		Assert.Equal(_jobOrder, jobs.Children.Keys.Select(static key => ((YamlScalarNode) key).Value));
		foreach ((string job, string[] needs) in _needs)
		{
			Assert.Equal(needs, Needs(Mapping(jobs, job)));
		}

		string[] triggers = ["push", "workflow_dispatch"];
		string[] tags = ["v*.*.*"];
		Assert.Equal(triggers, on.Children.Keys.Select(static key => ((YamlScalarNode) key).Value));
		Assert.Equal(tags, Sequence(Mapping(on, "push"), "tags"));
		Assert.Equal("false", Scalar(Mapping(_workflow.Value, "concurrency"), "cancel-in-progress"));
		Assert.Equal("${{ github.workflow }}-${{ github.ref }}", Scalar(Mapping(_workflow.Value, "concurrency"), "group"));
		YamlMappingNode permissions = Mapping(_workflow.Value, "permissions");
		Assert.Equal("read", Text(Assert.Single(permissions.Children, static entry => ((YamlScalarNode) entry.Key).Value == "contents").Value));
		Assert.Single(permissions.Children);
	}

	[Fact]
	public void ReleaseCallsCiWithThePackageVersionAndNinetyDayRetentionButNoSonar()
	{
		YamlMappingNode ci = Mapping(Mapping(_workflow.Value, "jobs"), "ci");
		YamlMappingNode with = Mapping(ci, "with");

		Assert.Equal("CI", Scalar(ci, "name"));
		Assert.Equal("./.github/workflows/ci.yml", Scalar(ci, "uses"));
		Assert.Equal("${{ needs.verify.outputs.version }}", Scalar(with, "package-version"));
		Assert.Equal("90", Scalar(with, "package-retention-days"));
		Assert.Equal(2, with.Children.Count);
		Assert.False(ci.Children.ContainsKey(new YamlScalarNode("secrets")), "release.yml must not pass secrets to ci.yml (no Sonar on the release path).");
	}

	[Fact]
	public void OnlyThePublishJobUsesTheNugetEnvironment()
	{
		YamlMappingNode jobs = Mapping(_workflow.Value, "jobs");
		foreach ((YamlNode key, YamlNode value) in jobs.Children)
		{
			string job = ((YamlScalarNode) key).Value!;
			YamlMappingNode definition = (YamlMappingNode) value;
			bool hasEnvironment = definition.Children.ContainsKey(new YamlScalarNode("environment"));
			bool readsSecrets = JobText(job).Contains("secrets.", StringComparison.Ordinal);
			bool requestsIdToken = definition.Children.TryGetValue(new YamlScalarNode("permissions"), out YamlNode? permissions)
								   && ((YamlMappingNode) permissions).Children.TryGetValue(new YamlScalarNode("id-token"), out YamlNode? idToken)
								   && Text(idToken) == "write";

			Assert.Equal(job == "publish", hasEnvironment);
			Assert.Equal(job == "publish", readsSecrets);
			Assert.Equal(job is "publish" or "attest", requestsIdToken);
		}

		YamlMappingNode environment = Mapping(Mapping(jobs, "publish"), "environment");
		Assert.Equal("nuget", Scalar(environment, "name"));
	}

	/// <summary>
	/// A workflow_dispatch started from a tag has ref type 'tag' as well, so the event is part of every guard: attest,
	/// draft-release and publish share one condition, verify receives the event name and writes empty outputs for
	/// anything but a push, and no later job can run once they are skipped.
	/// </summary>
	[Fact]
	public void DraftAndPublishRunOnlyForTagPushesOfThisRepository()
	{
		YamlMappingNode jobs = Mapping(_workflow.Value, "jobs");
		string[] clauses =
		[
			"github.event_name == 'push'", "github.ref_type == 'tag'",
			"github.repository == 'CheatEngineNet/CheatEngine.Client'", "needs.verify.outputs.version != ''"
		];
		string[] guardedJobs = ["attest", "draft-release", "publish"];
		string attest = Scalar(Mapping(jobs, "attest"), "if");

		foreach (string job in guardedJobs)
		{
			string condition = Scalar(Mapping(jobs, job), "if");
			Assert.Equal(attest, condition);
			foreach (string clause in clauses)
			{
				Assert.True(condition.Contains(clause, StringComparison.Ordinal), $"The '{job}' condition '{condition}' lacks {clause}.");
			}

			Assert.DoesNotContain("||", condition, StringComparison.Ordinal);
		}

		// Later jobs have no condition of their own that could run them after a skipped need.
		List<string> bypasses = [];
		foreach ((YamlNode key, YamlNode value) in jobs.Children)
		{
			if (((YamlMappingNode) value).Children.TryGetValue(new YamlScalarNode("if"), out YamlNode? condition)
				&& StatusFunction().IsMatch(Text(condition)))
			{
				bypasses.Add($"{((YamlScalarNode) key).Value}: {Text(condition)}");
			}
		}

		Assert.True(bypasses.Count == 0, $"No release job may run after a skipped or failed need:{Environment.NewLine}{string.Join(Environment.NewLine, bypasses)}");
		string[] publicationNeeds = ["verify", "publish"];
		Assert.Equal(publicationNeeds, Needs(Mapping(jobs, "verify-publication")));
		Assert.Contains("verify-publication", Needs(Mapping(jobs, "finalize-release")));

		YamlMappingNode verifyTag = Step(Mapping(jobs, "verify"), "tag");
		Assert.Equal("${{ github.event_name }}", Scalar(Mapping(verifyTag, "env"), "EVENT_NAME"));
		Assert.Contains("-EventName $env:EVENT_NAME", Scalar(verifyTag, "run"), StringComparison.Ordinal);

		string text = File.ReadAllText(Path.Combine(RepositoryRoot.Path, WorkflowPath));
		Assert.DoesNotContain("gh release upload", text, StringComparison.Ordinal);
		Assert.DoesNotContain(Mapping(_workflow.Value, "on").Children.Keys, static key => ((YamlScalarNode) key).Value!.StartsWith("pull_request", StringComparison.Ordinal));
	}

	/// <summary>
	/// The contents: write token of draft-release and finalize-release reaches only the steps that call gh: never the
	/// restore and test steps, which run repository MSBuild targets, NuGet package targets and test code.
	/// </summary>
	[Fact]
	public void TheWriteTokenReachesOnlyTheStepsThatCallGitHub()
	{
		List<string> offenders = [];
		foreach ((YamlNode key, YamlNode value) in Mapping(_workflow.Value, "jobs").Children)
		{
			string job = ((YamlScalarNode) key).Value!;
			YamlMappingNode definition = (YamlMappingNode) value;
			if (definition.Children.TryGetValue(new YamlScalarNode("env"), out YamlNode? jobEnvironment)
				&& ((YamlMappingNode) jobEnvironment).Children.ContainsKey(new YamlScalarNode("GH_TOKEN")))
			{
				offenders.Add($"{job}: GH_TOKEN is set for the whole job");
			}

			if (!definition.Children.TryGetValue(new YamlScalarNode("steps"), out YamlNode? steps))
			{
				continue;
			}

			foreach (YamlMappingNode step in ((YamlSequenceNode) steps).Children.Cast<YamlMappingNode>())
			{
				string name = step.Children.TryGetValue(new YamlScalarNode("name"), out YamlNode? nameNode) ? Text(nameNode) : "(unnamed)";
				bool hasToken = step.Children.TryGetValue(new YamlScalarNode("env"), out YamlNode? stepEnvironment)
								&& ((YamlMappingNode) stepEnvironment).Children.ContainsKey(new YamlScalarNode("GH_TOKEN"));
				string run = step.Children.TryGetValue(new YamlScalarNode("run"), out YamlNode? runNode) ? Text(runNode) : string.Empty;
				if (hasToken && (run.Length == 0 || run.Contains("dotnet ", StringComparison.Ordinal)))
				{
					offenders.Add($"{job} / {name}: GH_TOKEN reaches a step that is not a gh call");
				}
			}
		}

		Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void NoReleaseJobCachesPackages()
	{
		List<string> offenders = [];
		foreach ((YamlNode key, YamlNode value) in Mapping(_workflow.Value, "jobs").Children)
		{
			string job = ((YamlScalarNode) key).Value!;
			if (!((YamlMappingNode) value).Children.TryGetValue(new YamlScalarNode("steps"), out YamlNode? steps))
			{
				continue;
			}

			foreach (YamlMappingNode step in ((YamlSequenceNode) steps).Children.Cast<YamlMappingNode>())
			{
				string uses = step.Children.TryGetValue(new YamlScalarNode("uses"), out YamlNode? usesNode) ? Text(usesNode) : string.Empty;
				YamlMappingNode? with = step.Children.TryGetValue(new YamlScalarNode("with"), out YamlNode? withNode) ? (YamlMappingNode) withNode : null;
				string? cache = with is not null && with.Children.TryGetValue(new YamlScalarNode("cache"), out YamlNode? cacheNode) ? Text(cacheNode) : null;
				if (uses.StartsWith("actions/cache", StringComparison.Ordinal))
				{
					offenders.Add($"{job}: uses {uses}");
				}

				if (uses == "./.github/actions/setup-dotnet" && cache != "false")
				{
					offenders.Add($"{job}: the .NET setup must pass cache: 'false' (found '{cache}')");
				}

				if (uses.StartsWith("actions/setup-dotnet", StringComparison.Ordinal))
				{
					offenders.Add($"{job}: uses actions/setup-dotnet directly instead of the repository setup action");
				}
			}
		}

		Assert.True(offenders.Count == 0,
			$"No release job may restore from or save to a NuGet cache (cache poisoning of the published packages):{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
	}

	[Fact]
	public void PublishPushesTheSevenPackagesInDependencyOrder()
	{
		YamlSequenceNode steps = (YamlSequenceNode) Mapping(Mapping(_workflow.Value, "jobs"), "publish").Children[new YamlScalarNode("steps")];
		List<YamlMappingNode> stepList = steps.Children.Cast<YamlMappingNode>().ToList();
		int login = stepList.FindIndex(static step => step.Children.TryGetValue(new YamlScalarNode("uses"), out YamlNode? uses)
													 && Text(uses).StartsWith("NuGet/login@", StringComparison.Ordinal));
		int push = stepList.FindIndex(static step => step.Children.TryGetValue(new YamlScalarNode("run"), out YamlNode? run)
													&& Text(run).Contains("dotnet nuget push", StringComparison.Ordinal));
		Assert.True(login >= 0 && push > login, "The publish job must log in with NuGet/login right before the push step.");
		Assert.Equal("${{ secrets.NUGET_USER }}", Scalar(Mapping(stepList[login], "with"), "user"));

		string script = Text(stepList[push].Children[new YamlScalarNode("run")]);
		string[] pushed = QuotedPackageId().Matches(script).Select(static match => match.Groups["id"].Value).ToArray();
		Assert.Equal(_pushOrder, pushed);
		Assert.Contains("--skip-duplicate", script, StringComparison.Ordinal);
		Assert.Contains("https://api.nuget.org/v3/index.json", script, StringComparison.Ordinal);
		Assert.DoesNotContain("--no-symbols", script, StringComparison.Ordinal);
		Assert.Contains("$LASTEXITCODE", script, StringComparison.Ordinal);
	}

	private static YamlMappingNode LoadWorkflow()
	{
		YamlStream stream = new();
		using StreamReader reader = new(Path.Combine(RepositoryRoot.Path, WorkflowPath));
		stream.Load(reader);
		return (YamlMappingNode) stream.Documents[0].RootNode;
	}

	/// <summary>The value of a scalar node; fails for a mapping or a sequence.</summary>
	private static string Text(YamlNode node)
	{
		return Assert.IsType<YamlScalarNode>(node).Value ?? string.Empty;
	}

	/// <summary>Every key and scalar value of a job, joined, so an expression anywhere in the job is found.</summary>
	private static string JobText(string job)
	{
		List<string> scalars = [];
		Stack<YamlNode> pending = new([Mapping(Mapping(_workflow.Value, "jobs"), job)]);
		while (pending.Count > 0)
		{
			switch (pending.Pop())
			{
				case YamlScalarNode scalar:
					scalars.Add(scalar.Value ?? string.Empty);
					break;
				case YamlSequenceNode sequence:
					foreach (YamlNode item in sequence.Children)
					{
						pending.Push(item);
					}

					break;
				case YamlMappingNode mapping:
					foreach ((YamlNode key, YamlNode value) in mapping.Children)
					{
						pending.Push(key);
						pending.Push(value);
					}

					break;
			}
		}

		return string.Join('\n', scalars);
	}

	private static YamlMappingNode Mapping(YamlMappingNode parent, string key)
	{
		Assert.True(parent.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? node), $"{WorkflowPath} has no '{key}'.");
		return Assert.IsType<YamlMappingNode>(node);
	}

	private static string Scalar(YamlMappingNode parent, string key)
	{
		Assert.True(parent.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? node), $"{WorkflowPath} has no '{key}'.");
		return Assert.IsType<YamlScalarNode>(node).Value ?? string.Empty;
	}

	private static string[] Sequence(YamlMappingNode parent, string key)
	{
		Assert.True(parent.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? node), $"{WorkflowPath} has no '{key}'.");
		return Assert.IsType<YamlSequenceNode>(node).Children.Select(static item => ((YamlScalarNode) item).Value!).ToArray();
	}

	private static YamlMappingNode Step(YamlMappingNode job, string id)
	{
		YamlSequenceNode steps = Assert.IsType<YamlSequenceNode>(job.Children[new YamlScalarNode("steps")]);
		return Assert.Single(steps.Children.Cast<YamlMappingNode>(),
			step => step.Children.TryGetValue(new YamlScalarNode("id"), out YamlNode? stepId) && Text(stepId) == id);
	}

	private static string[] Needs(YamlMappingNode job)
	{
		if (!job.Children.TryGetValue(new YamlScalarNode("needs"), out YamlNode? needs))
		{
			return [];
		}

		return needs is YamlSequenceNode sequence
			? sequence.Children.Select(static item => ((YamlScalarNode) item).Value!).ToArray()
			: [((YamlScalarNode) needs).Value!];
	}

	[GeneratedRegex(@"'(?<id>CheatEngine\.Client(?:\.[A-Za-z.]+)?)'", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex QuotedPackageId();

	[GeneratedRegex(@"\b(always|cancelled|failure)\s*\(", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex StatusFunction();
}
