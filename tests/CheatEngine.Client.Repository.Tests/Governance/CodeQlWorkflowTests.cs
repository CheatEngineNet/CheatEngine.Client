using CheatEngine.Client.Repository.Tests.Infrastructure;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.Client.Repository.Tests.Governance;

/// <summary>
/// Advisory CodeQL (PR-CQ-23, CI-CLI-11, CI-BOTH-4): C# is analysed from a manual, traced build of the whole shipped
/// product graph, so source-generator output is analysed; GitHub Actions without a build; and nothing on that path
/// uses a dependency cache (shared-contracts §1.5).
/// </summary>
public sealed class CodeQlWorkflowTests
{
	private const string WorkflowPath = ".github/workflows/codeql.yml";
	private const string ProductProject = "src/CheatEngine.Client/CheatEngine.Client.csproj";
	private const string InitAction = "github/codeql-action/init";
	private const string AnalyzeAction = "github/codeql-action/analyze";
	private const string SetupAction = "./.github/actions/setup-dotnet";

	private static readonly YamlMappingNode _workflow = GovernanceFile.LoadYaml(WorkflowPath);

	[Fact]
	public void CSharpIsAnalysedWithAManualBuildOfTheShippedProductGraph()
	{
		YamlMappingNode job = GovernanceFile.Jobs(_workflow)["csharp"];
		IReadOnlyList<YamlMappingNode> steps = GovernanceFile.Steps(job);
		int init = GovernanceFile.IndexOfAction(steps, InitAction);
		int build = GovernanceFile.IndexOfRun(steps, "dotnet build");
		int analyze = GovernanceFile.IndexOfAction(steps, AnalyzeAction);

		Assert.True(init >= 0 && init < build && build < analyze, "The traced build must run between init and analyze.");
		Assert.Equal("windows-2025", GovernanceFile.Scalar(job, "runs-on"));
		Assert.Equal("csharp", GovernanceFile.With(steps[init], "languages"));
		Assert.Equal("manual", GovernanceFile.With(steps[init], "build-mode"));
		Assert.Equal(ProductProject, GovernanceFile.With(GovernanceFile.StepUsing(steps, SetupAction), "restore"));

		string script = GovernanceFile.Scalar(steps[build], "run")!.Replace("`", " ", StringComparison.Ordinal);
		string command = GovernanceFile.NormalizeWhitespace(script);
		string[] required =
		[
			$"dotnet build {ProductProject}", "--configuration Release", "--no-restore", "--no-incremental",
			"--disable-build-servers", "-p:UseSharedCompilation=false", "$LASTEXITCODE"
		];
		foreach (string fragment in required)
		{
			Assert.Contains(fragment, command, StringComparison.Ordinal);
		}
	}

	[Fact]
	public void TheBuiltProjectReachesEveryShippedAssemblyAndTheSourceGenerator()
	{
		HashSet<string> reached = new(StringComparer.Ordinal);
		Queue<string> pending = new([ProductProject]);
		while (pending.TryDequeue(out string? project))
		{
			if (!reached.Add(project))
			{
				continue;
			}

			string directory = Path.Combine(RepositoryRoot.Path, Path.GetDirectoryName(project)!);
			foreach (XElement reference in XDocument.Load(GovernanceFile.FullPath(project)).Descendants("ProjectReference"))
			{
				string include = ((string) reference.Attribute("Include")!).Replace('\\', '/');
				pending.Enqueue(RepositoryRoot.ToRelative(Path.GetFullPath(Path.Combine(directory, include))));
			}
		}

		List<string> shipped = [.. RepositoryRoot.EnumerateSourceFiles("*.csproj").Where(IsShippedOrGenerator)];

		Assert.NotEmpty(shipped);
		Assert.All(shipped, project => Assert.Contains(project, reached));
	}

	[Fact]
	public void CodeQlNeverUsesADependencyCache()
	{
		IReadOnlyList<YamlMappingNode> steps = GovernanceFile.Steps(GovernanceFile.Jobs(_workflow)["csharp"]);
		YamlMappingNode init = GovernanceFile.StepUsing(steps, InitAction);

		Assert.Equal("false", GovernanceFile.With(init, "dependency-caching"));
		Assert.Equal("false", GovernanceFile.With(init, "trap-caching"));
		foreach (YamlMappingNode step in GovernanceFile.Jobs(_workflow).Values.SelectMany(GovernanceFile.Steps))
		{
			string action = GovernanceFile.ActionName(step) ?? "";
			Assert.False(action.StartsWith("actions/cache", StringComparison.Ordinal), "CodeQL must not use actions/cache.");
			Assert.NotEqual("true", GovernanceFile.With(step, "cache"));
		}
	}

	[Fact]
	public void CodeQlAnalysesExactlyCSharpAndActions()
	{
		IReadOnlyDictionary<string, YamlMappingNode> jobs = GovernanceFile.Jobs(_workflow);
		List<string> languages = [];
		List<string> categories = [];
		foreach (YamlMappingNode job in jobs.Values)
		{
			IReadOnlyList<YamlMappingNode> steps = GovernanceFile.Steps(job);
			languages.Add(GovernanceFile.With(GovernanceFile.StepUsing(steps, InitAction), "languages") ?? "");
			categories.Add(GovernanceFile.With(GovernanceFile.StepUsing(steps, AnalyzeAction), "category") ?? "");
			Assert.Equal("write", GovernanceFile.Permissions(job)["security-events"]);
			Assert.Null(GovernanceFile.Child(job, "strategy"));
		}

		YamlMappingNode actionsInit = GovernanceFile.StepUsing(GovernanceFile.Steps(jobs["actions"]), InitAction);
		Assert.Equal(["actions", "csharp"], languages.Order(StringComparer.Ordinal));
		Assert.Equal(["/language:actions", "/language:csharp"], categories.Order(StringComparer.Ordinal));
		Assert.Equal("none", GovernanceFile.With(actionsInit, "build-mode"));
		Assert.Equal(new Dictionary<string, string> { ["contents"] = "read" }, GovernanceFile.Permissions(_workflow));
	}

	[Fact]
	public void CodeQlHasNoMergeGroupTrigger()
	{
		YamlMappingNode triggers = GovernanceFile.Triggers(_workflow);

		Assert.False(GovernanceFile.Has(triggers, "merge_group"));
		Assert.False(GovernanceFile.Has(triggers, "pull_request_target"));
		Assert.True(GovernanceFile.Has(triggers, "pull_request"));
		Assert.Equal(["main"], GovernanceFile.PushBranches(_workflow));
		Assert.NotNull(GovernanceFile.Sequence(triggers, "schedule"));
	}

	private static bool IsShippedOrGenerator(string project)
	{
		return project.StartsWith("libs/", StringComparison.Ordinal)
			|| project.StartsWith("source-generators/", StringComparison.Ordinal);
	}
}
