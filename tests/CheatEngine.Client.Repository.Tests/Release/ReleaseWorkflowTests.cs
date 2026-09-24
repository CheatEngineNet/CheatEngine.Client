using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;
using CheatEngine.Client.Repository.Tests.Packaging;
using CheatEngine.Client.Repository.Tests.Workflows;

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
	private const string ProvenancePredicate = "https://slsa.dev/provenance/v1";
	private const string SpdxPredicate = "https://spdx.dev/Document/v2.2";

	private static readonly string[] JobOrder = ["verify", "ci", "attest", "draft-release", "publish", "verify-publication", "finalize-release"];

	private static readonly Dictionary<string, string[]> ExpectedNeeds = new(StringComparer.Ordinal)
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
	private static readonly string[] PushOrder =
	[
		"CheatEngine.Client.Abstractions", "CheatEngine.Client.Fluent", "CheatEngine.Client.Core",
		"CheatEngine.Client.Extensions.DependencyInjection", "CheatEngine.Client.Hosting", "CheatEngine.Client",
		"CheatEngine.Client.Templates"
	];

	private static readonly Lazy<YamlMappingNode> Workflow = new(LoadWorkflow);

	[Fact]
	public void ReleaseJobsFollowTheContractOrder()
	{
		YamlMappingNode jobs = Mapping(Workflow.Value, "jobs");
		YamlMappingNode on = Mapping(Workflow.Value, "on");

		Assert.Equal(JobOrder, jobs.Children.Keys.Select(static key => ((YamlScalarNode) key).Value));
		foreach ((string job, string[] needs) in ExpectedNeeds)
		{
			Assert.Equal(needs, Needs(Mapping(jobs, job)));
		}

		string[] triggers = ["push", "workflow_dispatch"];
		string[] tags = ["v*.*.*"];
		Assert.Equal(triggers, on.Children.Keys.Select(static key => ((YamlScalarNode) key).Value));
		Assert.Equal(tags, Sequence(Mapping(on, "push"), "tags"));
		Assert.Equal("false", Scalar(Mapping(Workflow.Value, "concurrency"), "cancel-in-progress"));
		Assert.Equal("${{ github.workflow }}-${{ github.ref }}", Scalar(Mapping(Workflow.Value, "concurrency"), "group"));
		YamlMappingNode permissions = Mapping(Workflow.Value, "permissions");
		Assert.Equal("read", Text(Assert.Single(permissions.Children, static entry => ((YamlScalarNode) entry.Key).Value == "contents").Value));
		Assert.Single(permissions.Children);
	}

	[Fact]
	public void ReleaseCallsCiWithThePackageVersionAndNinetyDayRetentionButNoSonar()
	{
		YamlMappingNode ci = Mapping(Mapping(Workflow.Value, "jobs"), "ci");
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
		YamlMappingNode jobs = Mapping(Workflow.Value, "jobs");
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
		YamlMappingNode jobs = Mapping(Workflow.Value, "jobs");
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
		Assert.Contains("$env:EVENT_NAME -ne 'push'", Scalar(verifyTag, "run"), StringComparison.Ordinal);

		string text = File.ReadAllText(Path.Combine(RepositoryRoot.Path, WorkflowPath));
		Assert.DoesNotContain("gh release upload", text, StringComparison.Ordinal);
		Assert.DoesNotContain(Mapping(Workflow.Value, "on").Children.Keys, static key => ((YamlScalarNode) key).Value!.StartsWith("pull_request", StringComparison.Ordinal));
	}

	/// <summary>
	/// The contents: write token of draft-release and finalize-release reaches only the steps that call gh: never the
	/// restore and test steps, which run repository MSBuild targets, NuGet package targets and test code.
	/// </summary>
	[Fact]
	public void TheWriteTokenReachesOnlyTheStepsThatCallGitHub()
	{
		List<string> offenders = [];
		foreach ((YamlNode key, YamlNode value) in Mapping(Workflow.Value, "jobs").Children)
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
		foreach ((YamlNode key, YamlNode value) in Mapping(Workflow.Value, "jobs").Children)
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
		YamlSequenceNode steps = (YamlSequenceNode) Mapping(Mapping(Workflow.Value, "jobs"), "publish").Children[new YamlScalarNode("steps")];
		List<YamlMappingNode> stepList = steps.Children.Cast<YamlMappingNode>().ToList();
		int login = stepList.FindIndex(static step => step.Children.TryGetValue(new YamlScalarNode("uses"), out YamlNode? uses)
													 && Text(uses).StartsWith("NuGet/login@", StringComparison.Ordinal));
		int push = stepList.FindIndex(static step => step.Children.TryGetValue(new YamlScalarNode("run"), out YamlNode? run)
													&& Text(run).Contains("dotnet nuget push", StringComparison.Ordinal));
		Assert.True(login >= 0 && push > login, "The publish job must log in with NuGet/login right before the push step.");
		Assert.Equal("${{ secrets.NUGET_USER }}", Scalar(Mapping(stepList[login], "with"), "user"));

		string script = Text(stepList[push].Children[new YamlScalarNode("run")]);
		Assert.Equal(PushOrder, PackageIds());
		Assert.Contains("foreach ($id in @($env:PACKAGE_IDS -split", script, StringComparison.Ordinal);
		Assert.Contains("--skip-duplicate", script, StringComparison.Ordinal);
		Assert.Contains("https://api.nuget.org/v3/index.json", script, StringComparison.Ordinal);
		Assert.Contains("--no-symbols", script, StringComparison.Ordinal);
		Assert.Contains("$LASTEXITCODE", script, StringComparison.Ordinal);
	}

	/// <summary>
	/// attest writes SHA256SUMS next to the bundles, and publish checks every .nupkg and .snupkg against it before the
	/// NuGet login, then pushes the seven packages without their symbols and only then the symbol packages, so a failed
	/// symbol push never leaves a package unpublished and a re-run of the job completes it (PKG-08).
	/// </summary>
	[Fact]
	public void PublishChecksEveryPackageAgainstSha256SumsBeforePushing()
	{
		List<YamlMappingNode> attest = Steps("attest");
		int sums = attest.FindIndex(static step => Run(step).Contains("SHA256SUMS", StringComparison.Ordinal) && Run(step).Contains("WriteAllText", StringComparison.Ordinal));
		int upload = attest.FindIndex(static step => Uses(step).StartsWith("actions/upload-artifact@", StringComparison.Ordinal)
													 && Scalar(Mapping(step, "with"), "name") == "attestation-bundles");
		Assert.True(sums >= 0 && upload > sums, "attest must write SHA256SUMS before it uploads the attestation-bundles artifact.");
		Assert.DoesNotContain(Steps("draft-release"), static step => Run(step).Contains("WriteAllText", StringComparison.Ordinal));

		List<YamlMappingNode> publish = Steps("publish");
		int download = publish.FindIndex(static step => Uses(step).StartsWith("actions/download-artifact@", StringComparison.Ordinal)
														&& Scalar(Mapping(step, "with"), "name") == "attestation-bundles");
		int check = publish.FindIndex(static step => Run(step).Contains("SHA256SUMS", StringComparison.Ordinal) && Run(step).Contains("Get-FileHash", StringComparison.Ordinal));
		int login = publish.FindIndex(static step => Uses(step).StartsWith("NuGet/login@", StringComparison.Ordinal));
		int packages = publish.FindIndex(static step => Run(step).Contains("dotnet nuget push", StringComparison.Ordinal));
		int symbols = publish.FindLastIndex(static step => Run(step).Contains("dotnet nuget push", StringComparison.Ordinal));
		Assert.True(download >= 0 && check > download && login > check && packages > login && symbols > packages,
			"publish must download SHA256SUMS, check the packages against it, log in, push the packages, then push the symbol packages.");

		string checkScript = Run(publish[check]);
		string[] required = [".nupkg", ".snupkg", "throw", "Get-ChildItem artifacts/nuget"];
		foreach (string value in required)
		{
			Assert.Contains(value, checkScript, StringComparison.Ordinal);
		}

		Assert.Null(Yaml.Get(publish[check], "env"));
		Assert.Contains("--no-symbols", Run(publish[packages]), StringComparison.Ordinal);
		string symbolScript = Run(publish[symbols]);
		Assert.Contains("*.snupkg", symbolScript, StringComparison.Ordinal);
		Assert.Contains("--skip-duplicate", symbolScript, StringComparison.Ordinal);
		Assert.DoesNotContain("--no-symbols", symbolScript, StringComparison.Ordinal);
	}

	/// <summary>
	/// The REST lookup of a release by tag returns published releases only, so finalize-release reads the draft with gh
	/// release view, publishes it with gh release edit and then verifies what consumers download: every asset against
	/// SHA256SUMS and, for an immutable release, the release attestation (PKG-06).
	/// </summary>
	[Fact]
	public void FinalizeReadsTheReleaseWithGhReleaseViewAndPublishesWithGhReleaseEdit()
	{
		List<YamlMappingNode> steps = Steps("finalize-release");
		int state = steps.FindIndex(static step => StepId(step) == "state");
		int publish = steps.FindIndex(static step => Run(step).Contains("gh release edit", StringComparison.Ordinal));
		int verify = steps.FindIndex(static step => Run(step).Contains("gh release download", StringComparison.Ordinal));
		Assert.True(state >= 0 && publish > state && verify > publish,
			"finalize-release must read the release state, then publish the draft, then verify the published release.");

		Assert.Matches(GhReleaseView(), Run(steps[state]));
		Assert.Contains("draft=", Run(steps[state]), StringComparison.Ordinal);
		Assert.Equal("steps.state.outputs.draft == 'true'", Scalar(steps[publish], "if"));
		Assert.Matches(@"(?m)^\s*gh release edit \$env:TAG --draft=false\s*$", Run(steps[publish]));

		string verification = Run(steps[verify]);
		Assert.Matches(GhReleaseView(), verification);
		Assert.Matches(@"\bgh release download \$env:TAG --dir \$published\b", verification);
		Assert.Matches(@"\bgh release verify \$env:TAG\s", verification);
		Assert.Matches(@"\bgh release verify-asset \$env:TAG\s", verification);
		string[] required = ["SHA256SUMS", "Get-FileHash", "$state.isImmutable", "$state.isDraft", "GITHUB_STEP_SUMMARY"];
		foreach (string value in required)
		{
			Assert.Contains(value, verification, StringComparison.Ordinal);
		}

		Assert.DoesNotContain(steps, static step => Regex.IsMatch(Run(step), @"\bgh api\b"));
		string text = File.ReadAllText(Path.Combine(RepositoryRoot.Path, WorkflowPath));
		Assert.DoesNotContain("releases/tags/", text, StringComparison.Ordinal);
	}

	/// <summary>
	/// Every gh attestation verify of the workflow pins the identity RELEASING.md gives consumers (this repository, the
	/// release.yml signer workflow, the tag as source ref, GitHub-hosted runners) and names the predicate it checks;
	/// finalize-release checks both the provenance and the SPDX SBOM of each published package.
	/// </summary>
	[Fact]
	public void AttestationVerificationPinsSignerSourceRefAndHostedRunners()
	{
		string[] identity =
		[
			"--repo", "CheatEngineNet/CheatEngine.Client",
			"--signer-workflow", "CheatEngineNet/CheatEngine.Client/.github/workflows/release.yml",
			"--source-ref", "refs/tags/$env:TAG",
			"--deny-self-hosted-runners"
		];
		string[] predicates = [ProvenancePredicate, SpdxPredicate];
		List<string> offenders = [];
		foreach (YamlNode key in Mapping(Workflow.Value, "jobs").Children.Keys)
		{
			string job = ((YamlScalarNode) key).Value!;
			foreach (string script in Steps(job).Select(Run))
			{
				string[] verifications = AttestationVerification().Matches(script).Select(static match => match.Value).ToArray();
				if (verifications.Length == 0)
				{
					continue;
				}

				Match declaration = IdentityDeclaration().Match(script);
				if (!declaration.Success || !Yaml.Tokens(declaration.Groups["body"].Value).SequenceEqual(identity))
				{
					offenders.Add($"{job}: $identity is not {string.Join(' ', identity)}");
				}

				foreach (string verification in verifications)
				{
					// The tokenizer drops the splatting '@', so '@identity' reads as 'identity'.
					List<string> tokens = [.. Yaml.Tokens(verification)];
					int predicate = tokens.IndexOf("--predicate-type");
					bool pinned = tokens.Contains("identity") && predicate >= 0 && predicate < tokens.Count - 1 && predicates.Contains(tokens[predicate + 1]);
					if (!pinned)
					{
						offenders.Add($"{job}: '{verification.Trim()}' must pass @identity and --predicate-type {string.Join(" or ", predicates)}");
					}
				}
			}
		}

		Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
		string finalize = Run(Steps("finalize-release").Single(static step => Run(step).Contains("gh release download", StringComparison.Ordinal)));
		foreach (string predicate in predicates)
		{
			Assert.Contains($"@identity --predicate-type '{predicate}'", finalize, StringComparison.Ordinal);
		}

		// attest checks its own attestations, against the bundles and in the repository, before anything is public, and
		// the provenance covers the symbol packages too.
		List<YamlMappingNode> attest = Steps("attest");
		int verifyStep = attest.FindIndex(static step => Run(step).Contains("gh attestation verify", StringComparison.Ordinal));
		int upload = attest.FindIndex(static step => Uses(step).StartsWith("actions/upload-artifact@", StringComparison.Ordinal));
		int lastAttestation = attest.FindLastIndex(static step => Uses(step).StartsWith("actions/attest@", StringComparison.Ordinal));
		Assert.True(verifyStep > lastAttestation && verifyStep < upload, "attest must verify its attestations after creating them and before uploading the bundles.");
		string attestScript = Run(attest[verifyStep]);
		foreach (string predicate in predicates)
		{
			Assert.Matches($@"(?m)@identity --predicate-type '{Regex.Escape(predicate)}' --bundle \$\w+\s*$", attestScript);
			Assert.Matches($@"(?m)@identity --predicate-type '{Regex.Escape(predicate)}'\s*$", attestScript);
		}

		string[] subjects = Scalar(Mapping(Step(Mapping(Mapping(Workflow.Value, "jobs"), "attest"), "provenance"), "with"), "subject-path")
			.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		string[] expectedSubjects = ["artifacts/nuget/*.nupkg", "artifacts/nuget/*.snupkg"];
		Assert.Equal(expectedSubjects, subjects);
	}

	/// <summary>
	/// release.yml declares the seven package ids once, as the workflow-level PACKAGE_IDS in dependency order, and every
	/// job derives its list from it (the SBOM attestation steps take their subjects by position); ci.yml declares each id
	/// once in its Pack step. Both sets equal the packable projects.
	/// </summary>
	[Fact]
	public void PackageIdsAreDeclaredOnceAndMatchThePackableProjects()
	{
		string[] packable = PackageMetadataTests.PackableProjects.Select(Path.GetFileNameWithoutExtension).Select(static id => id!).Order(StringComparer.Ordinal).ToArray();
		string[] declared = PackageIds();

		Assert.Equal(declared.Length, declared.Distinct(StringComparer.Ordinal).Count());
		Assert.Equal(packable, declared.Order(StringComparer.Ordinal));
		Assert.Equal(PushOrder, declared);

		string release = File.ReadAllText(Path.Combine(RepositoryRoot.Path, WorkflowPath));
		Assert.Empty(QuotedPackageId().Matches(release));
		List<string> offenders = [];
		foreach (YamlNode key in Mapping(Workflow.Value, "jobs").Children.Keys)
		{
			string job = ((YamlScalarNode) key).Value!;
			foreach (YamlMappingNode step in Steps(job))
			{
				if (step.Children.TryGetValue(new YamlScalarNode("with"), out YamlNode? with))
				{
					offenders.AddRange(((YamlMappingNode) with).Children.Values.OfType<YamlScalarNode>()
						.Where(static value => PackageFileName().IsMatch(value.Value ?? string.Empty))
						.Select(value => $"{job}: {value.Value}"));
				}
			}
		}

		Assert.True(offenders.Count == 0, $"Step inputs name packages instead of deriving them from PACKAGE_IDS:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
		string[] readers = ["verify", "attest", "publish", "verify-publication", "finalize-release"];
		foreach (string job in readers)
		{
			Assert.Contains("$env:PACKAGE_IDS", JobText(job), StringComparison.Ordinal);
		}

		// ci.yml: every quoted package id of the file is in the Pack step's declaration, once.
		WorkflowFile ci = WorkflowFile.Load(".github/workflows/ci.yml");
		string pack = Assert.Single(ci.Job("build-test").Steps, static step => step.Id == "pack").Run;
		Assert.Equal(packable, QuotedIds(ci.Text, packable).Order(StringComparer.Ordinal));
		Assert.Equal(packable, QuotedIds(pack, packable).Order(StringComparer.Ordinal));
	}

	/// <summary>
	/// verify-publication resolves PackageBaseAddress/3.0.0 from the nuget.org service index instead of a hard-coded
	/// flat container URL, and requires the nuget.org repository signature on every served package.
	/// </summary>
	[Fact]
	public void VerifyPublicationResolvesTheServiceIndexAndRequiresTheRepositorySignature()
	{
		string script = Run(Assert.Single(Steps("verify-publication"), static step => Run(step).Contains("dotnet nuget verify", StringComparison.Ordinal)));
		string[] required =
		[
			"Invoke-RestMethod -Uri 'https://api.nuget.org/v3/index.json'", "'PackageBaseAddress/3.0.0'", "dotnet nuget verify --all",
			"^Signature type: Repository", @"^Service index: https://api\.nuget\.org/v3/index\.json", "'.signature.p7s'", "^Content hash:",
			"DOTNET_CLI_UI_LANGUAGE = 'en'"
		];
		foreach (string value in required)
		{
			Assert.Contains(value, script, StringComparison.Ordinal);
		}

		Assert.DoesNotContain("v3-flatcontainer", script, StringComparison.Ordinal);
	}

	/// <summary>
	/// A re-run deletes every draft already on the tag with gh release delete, which resolves a draft by its tag and
	/// keeps the git tag, before it creates the draft again. The id that gh release view reports is a GraphQL node id the
	/// REST API rejects, so no release id reaches a REST call (PKG-07).
	/// </summary>
	[Fact]
	public void DraftRerunDeletesTheDraftWithGhReleaseDelete()
	{
		string script = Run(Assert.Single(Steps("draft-release"), static step => Run(step).Contains("gh release create", StringComparison.Ordinal)));
		int list = script.IndexOf("gh release list", StringComparison.Ordinal);
		Match delete = Regex.Match(script, @"(?m)^\s*gh release delete \$env:TAG --yes\s*$");
		int create = script.IndexOf("gh release create", StringComparison.Ordinal);

		Assert.True(list >= 0 && delete.Success && list < delete.Index && delete.Index < create,
			"draft-release must list the releases of the tag, delete its drafts with 'gh release delete $env:TAG --yes', then create the draft.");
		Assert.Contains("isDraft", script, StringComparison.Ordinal);
		Assert.Contains("throw", script[..delete.Index], StringComparison.Ordinal);
		Assert.DoesNotContain("--cleanup-tag", script, StringComparison.Ordinal);
		Assert.DoesNotMatch(@"--json\s+(\S+,)?(id|databaseId)(,|\s|$)", script);

		string text = File.ReadAllText(Path.Combine(RepositoryRoot.Path, WorkflowPath));
		Assert.DoesNotMatch(@"\bgh api\b[^\n]*(--method|-X)\s*DELETE", text);
		Assert.DoesNotMatch(@"\bgh api\b[^\n]*releases/", text);
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
		Stack<YamlNode> pending = new([Mapping(Mapping(Workflow.Value, "jobs"), job)]);
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

	/// <summary>The ids of the workflow-level PACKAGE_IDS, in declaration order.</summary>
	private static string[] PackageIds()
	{
		return Scalar(Mapping(Workflow.Value, "env"), "PACKAGE_IDS").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	}

	/// <summary>Every single-quoted occurrence of one of the given package ids, repeats included.</summary>
	private static string[] QuotedIds(string text, string[] ids)
	{
		return QuotedPackageId().Matches(text).Select(static match => match.Groups["id"].Value).Where(id => ids.Contains(id, StringComparer.Ordinal)).ToArray();
	}

	/// <summary>The steps of a job; empty for a reusable-workflow call.</summary>
	private static List<YamlMappingNode> Steps(string job)
	{
		return Mapping(Mapping(Workflow.Value, "jobs"), job).Children.TryGetValue(new YamlScalarNode("steps"), out YamlNode? steps)
			? Assert.IsType<YamlSequenceNode>(steps).Children.Cast<YamlMappingNode>().ToList()
			: [];
	}

	/// <summary>The script of a step; empty for an action step.</summary>
	private static string Run(YamlMappingNode step)
	{
		return step.Children.TryGetValue(new YamlScalarNode("run"), out YamlNode? run) ? Text(run) : string.Empty;
	}

	private static string? StepId(YamlMappingNode step)
	{
		return step.Children.TryGetValue(new YamlScalarNode("id"), out YamlNode? id) ? Text(id) : null;
	}

	/// <summary>The action reference of a step; empty for a script step.</summary>
	private static string Uses(YamlMappingNode step)
	{
		return step.Children.TryGetValue(new YamlScalarNode("uses"), out YamlNode? uses) ? Text(uses) : string.Empty;
	}

	[GeneratedRegex(@"'(?<id>CheatEngine\.Client(?:\.[A-Za-z.]+)?)'", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex QuotedPackageId();

	[GeneratedRegex(@"\b(always|cancelled|failure)\s*\(", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex StatusFunction();

	[GeneratedRegex(@"(?m)^\s*\$view = gh release view \$env:TAG --json isDraft,isImmutable\s*$", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex GhReleaseView();

	[GeneratedRegex(@"(?m)^\s*gh attestation verify\b.*$", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex AttestationVerification();

	[GeneratedRegex(@"\$identity = @\((?<body>[^)]*)\)", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex IdentityDeclaration();

	[GeneratedRegex(@"CheatEngine\.Client(\.[A-Za-z]+)*\.(\$|\{\{|[0-9])", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex PackageFileName();
}
