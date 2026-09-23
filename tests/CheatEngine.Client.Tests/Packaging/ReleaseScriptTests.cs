using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

using CheatEngine.Client.Tests.Infrastructure;

namespace CheatEngine.Client.Tests.Packaging;

/// <summary>
/// Runs the release scripts of <c>eng/release</c> in <c>pwsh</c> against <c>FakeGitHubCli.ps1</c>, an in-memory
/// stand-in for the gh CLI, so the release decisions are executed without GitHub: a dispatch is a dry run even from a
/// tag, and a release asset is compared by content, never by name alone (audit A21-04, A21-06). No category: both CI
/// legs run them, in a few seconds.
/// </summary>
public sealed class ReleaseScriptTests
{
	private const string Tag = "v0.1.0";
	private const string Version = "0.1.0";
	private const string Repository = "CheatEngineNet/CheatEngine.Client";
	private const long ExistingReleaseId = 7;

	private static readonly string[] _packageIds =
	[
		"CheatEngine.Client", "CheatEngine.Client.Abstractions", "CheatEngine.Client.Core",
		"CheatEngine.Client.Extensions.DependencyInjection", "CheatEngine.Client.Fluent", "CheatEngine.Client.Hosting",
		"CheatEngine.Client.Templates"
	];

	[Fact]
	public async Task DispatchStartedFromATagIsADryRunWithEmptyOutputs()
	{
		using TemporaryDirectory temporary = new("release-scripts");
		string output = Path.Combine(temporary.Path, "github-output.txt");

		DotNetProcessResult result = await RunScriptAsync(temporary, "Test-ReleaseTag.ps1",
			$"-EventName 'workflow_dispatch' -RefType 'tag' -RefName '{Tag}' -OutputPath '{output}'");

		Assert.True(result.ExitCode == 0, result.ToString());
		Assert.Contains($"::notice::Dry run (workflow_dispatch on tag {Tag})", result.StandardOutput, StringComparison.Ordinal);
		string[] expected = ["version=", "prerelease="];
		Assert.Equal(expected, File.ReadAllLines(output));
	}

	[Fact]
	public async Task PushOfSomethingOtherThanAReleaseTagIsRefused()
	{
		using TemporaryDirectory temporary = new("release-scripts");
		string output = Path.Combine(temporary.Path, "github-output.txt");

		DotNetProcessResult branch = await RunScriptAsync(temporary, "Test-ReleaseTag.ps1",
			$"-EventName 'push' -RefType 'branch' -RefName 'main' -OutputPath '{output}'");
		DotNetProcessResult malformed = await RunScriptAsync(temporary, "Test-ReleaseTag.ps1",
			$"-EventName 'push' -RefType 'tag' -RefName 'v1.0' -OutputPath '{output}'");

		Assert.True(branch.ExitCode != 0, branch.ToString());
		Assert.Contains("must come from a v*.*.* tag", branch.StandardError + branch.StandardOutput, StringComparison.Ordinal);
		Assert.True(malformed.ExitCode != 0, malformed.ToString());
		Assert.Contains("is not a release tag", malformed.StandardError + malformed.StandardOutput, StringComparison.Ordinal);
		Assert.False(File.Exists(output), "A refused run must not write a version.");
	}

	[Fact]
	public async Task NewDraftCarriesExactlyTheFilesOfTheRun()
	{
		using TemporaryDirectory temporary = new("release-scripts");
		ReleaseFolder folder = ReleaseFolder.Create(temporary);
		FakeGitHub github = FakeGitHub.Create(temporary);

		DotNetProcessResult result = await RunDraftAsync(temporary, folder, github);

		Assert.True(result.ExitCode == 0, result.ToString());
		JsonObject draft = Assert.Single(github.Releases());
		Assert.True(draft["draft"]!.GetValue<bool>());
		Assert.Equal(folder.Digests(), FakeGitHub.Digests(draft));
		Assert.Contains($"release create {Tag}", github.Log(), StringComparison.Ordinal);
		Assert.DoesNotContain("--method DELETE", github.Log(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task DraftLeftByAnotherRunOfTheTagIsReplacedByTheFilesOfThisRun()
	{
		using TemporaryDirectory temporary = new("release-scripts");
		ReleaseFolder folder = ReleaseFolder.Create(temporary);
		Dictionary<string, byte[]> otherRun = folder.Contents();
		otherRun[$"CheatEngine.Client.Core.{Version}.nupkg"] = Encoding.UTF8.GetBytes("a package of another build of the tag");
		otherRun["stale.txt"] = Encoding.UTF8.GetBytes("an asset this run does not produce");
		FakeGitHub github = FakeGitHub.Create(temporary,
			new FakeRelease(ExistingReleaseId, true, otherRun, OpenAsset: $"CheatEngine.Client.Hosting.{Version}.nupkg"));

		DotNetProcessResult result = await RunDraftAsync(temporary, folder, github);

		Assert.True(result.ExitCode == 0, result.ToString());
		Assert.Contains("::warning::", result.StandardOutput, StringComparison.Ordinal);
		Assert.Contains($"CheatEngine.Client.Core.{Version}.nupkg is sha256:", result.StandardOutput, StringComparison.Ordinal);
		Assert.Contains("stale.txt is an asset of the release but not a file of this run", result.StandardOutput, StringComparison.Ordinal);
		Assert.Contains("is not completely uploaded (state 'open')", result.StandardOutput, StringComparison.Ordinal);
		Assert.Contains($"api --method DELETE repos/{Repository}/releases/{ExistingReleaseId}", github.Log(), StringComparison.Ordinal);
		JsonObject draft = Assert.Single(github.Releases());
		Assert.NotEqual(ExistingReleaseId, draft["id"]!.GetValue<long>());
		Assert.True(draft["draft"]!.GetValue<bool>());
		Assert.Equal(folder.Digests(), FakeGitHub.Digests(draft));
	}

	[Fact]
	public async Task DraftThatAlreadyCarriesTheFilesOfTheRunIsKept()
	{
		using TemporaryDirectory temporary = new("release-scripts");
		ReleaseFolder folder = ReleaseFolder.Create(temporary);
		FakeGitHub github = FakeGitHub.Create(temporary, new FakeRelease(ExistingReleaseId, true, folder.Contents()));

		DotNetProcessResult result = await RunDraftAsync(temporary, folder, github);

		Assert.True(result.ExitCode == 0, result.ToString());
		Assert.Contains("already carries exactly the assets of this run", result.StandardOutput, StringComparison.Ordinal);
		Assert.DoesNotContain("release create", github.Log(), StringComparison.Ordinal);
		Assert.DoesNotContain("--method DELETE", github.Log(), StringComparison.Ordinal);
		Assert.Equal(ExistingReleaseId, Assert.Single(github.Releases())["id"]!.GetValue<long>());
	}

	[Fact]
	public async Task PublishedReleaseWithOtherAssetsFailsTheDraftJobBeforeAnyPush()
	{
		using TemporaryDirectory temporary = new("release-scripts");
		ReleaseFolder folder = ReleaseFolder.Create(temporary);
		Dictionary<string, byte[]> otherRun = folder.Contents();
		otherRun[$"CheatEngine.Client.{Version}.nupkg"] = Encoding.UTF8.GetBytes("a package of another build of the tag");
		FakeGitHub github = FakeGitHub.Create(temporary, new FakeRelease(ExistingReleaseId, false, otherRun));

		DotNetProcessResult result = await RunDraftAsync(temporary, folder, github);

		Assert.True(result.ExitCode != 0, result.ToString());
		Assert.Contains("is already published", result.StandardError + result.StandardOutput, StringComparison.Ordinal);
		Assert.DoesNotContain("release create", github.Log(), StringComparison.Ordinal);
		Assert.DoesNotContain("--method DELETE", github.Log(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task FinalizeRefusesToPublishADraftWhoseAssetsDifferFromTheRun()
	{
		using TemporaryDirectory temporary = new("release-scripts");
		ReleaseFolder folder = ReleaseFolder.Create(temporary);
		Dictionary<string, byte[]> otherRun = folder.Contents();
		otherRun[$"CheatEngine.Client.Fluent.{Version}.snupkg"] = Encoding.UTF8.GetBytes("symbols of another build of the tag");
		FakeGitHub github = FakeGitHub.Create(temporary, new FakeRelease(ExistingReleaseId, true, otherRun));

		DotNetProcessResult result = await RunFinalizeAsync(temporary, folder, github);

		Assert.True(result.ExitCode != 0, result.ToString());
		Assert.Contains($"CheatEngine.Client.Fluent.{Version}.snupkg is sha256:", result.StandardError + result.StandardOutput, StringComparison.Ordinal);
		Assert.Contains("The release stays a draft.", result.StandardError + result.StandardOutput, StringComparison.Ordinal);
		Assert.DoesNotContain("--method PATCH", github.Log(), StringComparison.Ordinal);
		Assert.DoesNotContain("attestation verify", github.Log(), StringComparison.Ordinal);
		Assert.True(Assert.Single(github.Releases())["draft"]!.GetValue<bool>());
	}

	[Fact]
	public async Task FinalizePublishesTheCheckedDraftById()
	{
		using TemporaryDirectory temporary = new("release-scripts");
		ReleaseFolder folder = ReleaseFolder.Create(temporary);
		Dictionary<string, byte[]> prePublish = folder.Contents();
		prePublish[$"CheatEngine.Client.{Version}.tuple.json"] = Encoding.UTF8.GetBytes("{\"stage\":\"PrePublish\"}\n");
		prePublish["SHA256SUMS"] = Encoding.UTF8.GetBytes("the PrePublish checksums\n");
		FakeGitHub github = FakeGitHub.Create(temporary, new FakeRelease(ExistingReleaseId, true, prePublish));

		DotNetProcessResult result = await RunFinalizeAsync(temporary, folder, github);

		Assert.True(result.ExitCode == 0, result.ToString());
		string log = github.Log();
		Assert.Contains($"release upload {Tag} ", log, StringComparison.Ordinal);
		Assert.Contains($"api --method PATCH repos/{Repository}/releases/{ExistingReleaseId} -F draft=false", log, StringComparison.Ordinal);
		Assert.Equal(2 * _packageIds.Length, log.Split('\n').Count(static line => line.StartsWith("attestation verify ", StringComparison.Ordinal)));
		Assert.True(log.IndexOf("attestation verify", StringComparison.Ordinal) < log.IndexOf("--method PATCH", StringComparison.Ordinal),
			"The attestations must be verified before the release is published.");
		JsonObject release = Assert.Single(github.Releases());
		Assert.False(release["draft"]!.GetValue<bool>());
		Assert.Equal(folder.Digests(), FakeGitHub.Digests(release));
		Assert.Contains("is not immutable", result.StandardOutput, StringComparison.Ordinal);
	}

	private static Task<DotNetProcessResult> RunDraftAsync(TemporaryDirectory temporary, ReleaseFolder folder, FakeGitHub github)
	{
		string notes = Path.Combine(temporary.Path, "release-notes.md");
		File.WriteAllText(notes, "Release notes.\n");
		return RunScriptAsync(temporary, "New-ReleaseDraft.ps1",
			$"-Tag '{Tag}' -AssetDirectory '{folder.Path}' -NotesPath '{notes}' -SettleSeconds 0", github);
	}

	private static Task<DotNetProcessResult> RunFinalizeAsync(TemporaryDirectory temporary, ReleaseFolder folder, FakeGitHub github)
	{
		return RunScriptAsync(temporary, "Complete-GitHubRelease.ps1",
			$"-Tag '{Tag}' -AssetDirectory '{folder.Path}' -PackageDirectory '{folder.Path}' -SettleSeconds 0", github);
	}

	/// <summary>Runs one release script from a driver that first loads the gh stand-in, when one is given.</summary>
	private static Task<DotNetProcessResult> RunScriptAsync(TemporaryDirectory temporary, string script, string arguments,
		FakeGitHub? github = null)
	{
		string driver = Path.Combine(temporary.Path, $"driver-{Guid.NewGuid():N}.ps1");
		string standIn = github is null
			? string.Empty
			: $"\t. '{RepositoryLayout.Combine("tests/CheatEngine.Client.Tests/Packaging/FakeGitHubCli.ps1")}'\n";
		string run = $"\t& '{RepositoryLayout.Combine($"eng/release/{script}")}' {arguments}\n";

		// The message alone, unwrapped, so assertions do not depend on the console error view.
		string text = "$ErrorActionPreference = 'Stop'\ntry {\n" + standIn + run +
					  "}\ncatch {\n\t[Console]::Error.WriteLine($_.Exception.Message)\n\texit 1\n}\nexit 0\n";
		File.WriteAllText(driver, text, new UTF8Encoding(false));

		// An invalid token: if the stand-in were not loaded, a real gh call fails instead of acting on a logged-in account.
		Dictionary<string, string> environment = new(StringComparer.Ordinal)
		{
			["NO_COLOR"] = "1",
			["GH_TOKEN"] = "not-a-token"
		};
		if (github is not null)
		{
			environment["FAKE_GH_STATE"] = github.StatePath;
			environment["FAKE_GH_STORE"] = github.StorePath;
			environment["FAKE_GH_LOG"] = github.LogPath;
		}

		return DotNetProcess.RunToolAsync("pwsh", temporary.Path, environment,
			"-NoLogo", "-NoProfile", "-NonInteractive", "-File", driver);
	}

	private static string Sha256(byte[] content)
	{
		return Convert.ToHexStringLower(SHA256.HashData(content));
	}

	/// <summary>A release asset folder as the release jobs build it, with a SHA256SUMS in the format of New-ReleaseAssets.ps1.</summary>
	private sealed class ReleaseFolder
	{
		private ReleaseFolder(string path)
		{
			Path = path;
		}

		internal string Path
		{
			get;
		}

		internal static ReleaseFolder Create(TemporaryDirectory temporary)
		{
			ReleaseFolder folder = new(temporary.CreateDirectory("release"));
			foreach (string id in _packageIds)
			{
				folder.Write($"{id}.{Version}.nupkg", $"package {id} {Version} of this run");
				folder.Write($"{id}.{Version}.spdx.json", $"{{\"name\":\"{id}\"}}");
				folder.Write($"{id}.{Version}.sbom.sigstore.json", $"{{\"bundle\":\"sbom {id}\"}}");
				if (id is not "CheatEngine.Client" and not "CheatEngine.Client.Templates")
				{
					folder.Write($"{id}.{Version}.snupkg", $"symbols {id} {Version} of this run");
				}
			}

			folder.Write($"CheatEngine.Client.{Version}.provenance.sigstore.json", "{\"bundle\":\"provenance\"}");
			folder.Write($"CheatEngine.Client.{Version}.tuple.json", "{\"stage\":\"Published\"}\n");

			StringBuilder sums = new();
			foreach ((string name, string digest) in folder.Digests().Where(static entry => entry.Key != "SHA256SUMS"))
			{
				sums.Append(digest["sha256:".Length..]).Append("  ").Append(name).Append('\n');
			}

			folder.Write("SHA256SUMS", sums.ToString());
			return folder;
		}

		internal Dictionary<string, byte[]> Contents()
		{
			return Directory.EnumerateFiles(Path).ToDictionary(static file => System.IO.Path.GetFileName(file),
				static file => File.ReadAllBytes(file), StringComparer.Ordinal);
		}

		internal SortedDictionary<string, string> Digests()
		{
			SortedDictionary<string, string> digests = new(StringComparer.Ordinal);
			foreach ((string name, byte[] content) in Contents())
			{
				digests[name] = "sha256:" + Sha256(content);
			}

			return digests;
		}

		private void Write(string name, string content)
		{
			File.WriteAllText(System.IO.Path.Combine(Path, name), content, new UTF8Encoding(false));
		}
	}

	/// <param name="OpenAsset">An asset whose upload never completed (state 'open', no digest).</param>
	private sealed record FakeRelease(long Id, bool Draft, Dictionary<string, byte[]> Assets, string? OpenAsset = null);

	/// <summary>The state of the gh stand-in: a JSON file of releases, a folder of asset contents and a call log.</summary>
	private sealed class FakeGitHub
	{
		private FakeGitHub(TemporaryDirectory temporary)
		{
			StatePath = System.IO.Path.Combine(temporary.Path, "fake-gh-state.json");
			StorePath = temporary.CreateDirectory("fake-gh-store");
			LogPath = System.IO.Path.Combine(temporary.Path, "fake-gh.log");
		}

		internal string StatePath
		{
			get;
		}

		internal string StorePath
		{
			get;
		}

		internal string LogPath
		{
			get;
		}

		internal static FakeGitHub Create(TemporaryDirectory temporary, params FakeRelease[] releases)
		{
			FakeGitHub github = new(temporary);
			JsonArray releaseNodes = [];
			foreach (FakeRelease release in releases)
			{
				string folder = Directory.CreateDirectory(System.IO.Path.Combine(github.StorePath, release.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))).FullName;
				JsonArray assets = [];
				foreach ((string name, byte[] content) in release.Assets)
				{
					File.WriteAllBytes(System.IO.Path.Combine(folder, name), content);
					bool open = name == release.OpenAsset;
					assets.Add(new JsonObject
					{
						["name"] = name,
						["state"] = open ? "open" : "uploaded",
						["size"] = content.Length,
						["digest"] = open ? null : "sha256:" + Sha256(content)
					});
				}

				releaseNodes.Add(new JsonObject
				{
					["id"] = release.Id,
					["tag_name"] = Tag,
					["draft"] = release.Draft,
					["assets"] = assets
				});
			}

			JsonObject state = new()
			{
				["nextId"] = 100,
				["immutable"] = false,
				["releases"] = releaseNodes
			};
			File.WriteAllText(github.StatePath, state.ToJsonString(), new UTF8Encoding(false));
			File.WriteAllText(github.LogPath, string.Empty);
			return github;
		}

		internal static SortedDictionary<string, string> Digests(JsonObject release)
		{
			SortedDictionary<string, string> digests = new(StringComparer.Ordinal);
			foreach (JsonNode? asset in release["assets"]!.AsArray())
			{
				digests[asset!["name"]!.GetValue<string>()] = asset["digest"]?.GetValue<string>() ?? "(none)";
			}

			return digests;
		}

		internal JsonObject[] Releases()
		{
			JsonObject state = JsonNode.Parse(File.ReadAllText(StatePath))!.AsObject();
			return state["releases"] is JsonArray releases ? [.. releases.Select(static node => node!.AsObject())] : [];
		}

		internal string Log()
		{
			return File.ReadAllText(LogPath).Replace("\r\n", "\n", StringComparison.Ordinal);
		}
	}
}
