using System.Text.Json.Nodes;

using CheatEngine.Client.Repository.Tests.Infrastructure;
using CheatEngine.Client.Repository.Tests.Packaging;

namespace CheatEngine.Client.Repository.Tests.LockFiles;

/// <summary>
/// Structural checks of every committed lock file, so a broken one is reported before CI restores anything. Every
/// project restores with a committed lock file; the three Coexistence fixtures stay outside Central Package
/// Management with version 1 lock files (a solution-level <c>--force-evaluate</c> once gave them CentralTransitive
/// entries and broke every locked restore, which is why regeneration always restores each project on its own); the
/// whole graph consumes one CheatEngine.SDK identity, the pin of <c>eng/CheatEngineSdk.props</c> (ADR-10: the Client
/// follows the consumed package); every lock file ends exactly as NuGet writes it.
/// </summary>
public sealed class LockFileTests
{
	private const string LockFileName = "packages.lock.json";
	private const string TemplateContentProject =
		"templates/CheatEngine.Client.Templates/content/CheatEngine.Plugin/CheatEngine.Plugin.csproj";
	private const string CoexistenceFolder = "tests/CheatEngine.Client.LivePlugin.Coexistence/";
	private const string CoexistenceProps = CoexistenceFolder + "CoexistencePlugin.props";

	// The reviewed NuGet content hash of the pinned CheatEngine.SDK package (SHA-512 of the unsigned package, base64),
	// as every lock file records it. The version it belongs to is the pin itself (SdkPin.Version).
	private const string ConsumedSdkContentHash =
		"NLEdZYJ9LKW3EFNB4X5snKCQf7ZS86GkCQ+El7o+S1XQcxHQGjS45Q1ap8lfjQuIwm004mQ3TPxo+ph1yvRrlQ==";

	[Fact]
	public void EveryProjectHasACommittedLockFileExceptTheTemplateContent()
	{
		List<string> missing = [];
		foreach (string project in RepositoryRoot.EnumerateSourceFiles("*.csproj"))
		{
			string lockFile = LockFileOf(project);
			bool exists = File.Exists(Path.Combine(RepositoryRoot.Path, lockFile));
			if (project == TemplateContentProject)
			{
				Assert.False(exists,
					$"{lockFile} records one machine's restore of the template content, which each plugin restores " +
					"for itself; delete it.");
				continue;
			}

			if (!exists)
			{
				missing.Add(lockFile);
			}
		}

		Assert.True(missing.Count == 0,
			$"Missing lock files (regenerate them with 'dotnet restore <project> --force-evaluate' and commit them): {string.Join(", ", missing)}");
	}

	[Fact]
	public void LockFilesParseAndDeclareASupportedFormatVersion()
	{
		HashSet<string> projectFolders = new(StringComparer.Ordinal);
		foreach (string project in RepositoryRoot.EnumerateSourceFiles("*.csproj"))
		{
			projectFolders.Add(FolderOf(project));
		}

		List<string> lockFiles = [.. RepositoryRoot.EnumerateSourceFiles(LockFileName)];
		Assert.NotEmpty(lockFiles);
		foreach (string lockFile in lockFiles)
		{
			Assert.True(projectFolders.Contains(FolderOf(lockFile)),
				$"{lockFile} has no project next to it; delete the orphan.");
			JsonObject root = ReadLock(lockFile);
			int version = root["version"]?.GetValue<int>() ?? 0;
			Assert.True(version is 1 or 2, $"{lockFile} declares unsupported lock format version {version}.");
			Assert.True(root["dependencies"] is JsonObject, $"{lockFile} has no dependencies object.");
		}
	}

	[Fact]
	public void CoexistenceFixturesKeepVersion1LockFilesWithoutCentralTransitiveEntries()
	{
		XDocument props = XDocument.Load(Path.Combine(RepositoryRoot.Path, CoexistenceProps));
		XElement centralManagement = Assert.Single(props.Descendants("ManagePackageVersionsCentrally"));
		Assert.Equal("false", centralManagement.Value.Trim());

		string[] fixtures = CoexistenceFixtures();
		Assert.Equal(3, fixtures.Length);
		foreach (string fixture in fixtures)
		{
			string projectText = File.ReadAllText(Path.Combine(RepositoryRoot.Path, fixture));
			Assert.Contains("CoexistencePlugin.props", projectText, StringComparison.Ordinal);

			string lockFile = LockFileOf(fixture);
			JsonObject root = ReadLock(lockFile);
			Assert.True(root["version"]?.GetValue<int>() == 1,
				$"{lockFile} must stay a version 1 lock file: the fixture is outside Central Package Management.");
			foreach ((string section, string id, JsonObject entry) in Dependencies(root))
			{
				Assert.False(TypeOf(entry) == "CentralTransitive",
					$"{lockFile} [{section}] {id} is CentralTransitive: a solution-level --force-evaluate rewrote it. " +
					"Regenerate by restoring each Coexistence fixture on its own with --force-evaluate (never the solution, which broke this once).");
			}
		}
	}

	[Fact]
	public void CentralPackageManagementProjectsHaveVersion2LockFiles()
	{
		HashSet<string> fixtures = new(CoexistenceFixtures(), StringComparer.Ordinal);
		foreach (string project in RepositoryRoot.EnumerateSourceFiles("*.csproj"))
		{
			if (project == TemplateContentProject || fixtures.Contains(project))
			{
				continue;
			}

			string lockFile = LockFileOf(project);
			JsonObject root = ReadLock(lockFile);
			Assert.True(root["version"]?.GetValue<int>() == 2,
				$"{lockFile} must be a version 2 lock file: {project} uses Central Package Management.");
		}

		foreach (string file in RepositoryRoot.EnumerateSourceFiles("*.*proj")
					 .Concat(RepositoryRoot.EnumerateSourceFiles("*.props")))
		{
			if (file == TemplateContentProject || file.StartsWith(CoexistenceFolder, StringComparison.Ordinal))
			{
				continue;
			}

			XDocument document = XDocument.Load(Path.Combine(RepositoryRoot.Path, file));
			foreach (XElement element in document.Descendants("ManagePackageVersionsCentrally"))
			{
				Assert.False(string.Equals(element.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase),
					$"{file} turns Central Package Management off; only the Coexistence fixtures may.");
			}
		}
	}

	[Fact]
	public void EveryLockResolvesThePinnedSdkWithTheRecordedContentHash()
	{
		string pin = SdkPin.Version;
		List<string> consumers = [];
		foreach (string lockFile in RepositoryRoot.EnumerateSourceFiles(LockFileName))
		{
			foreach ((string section, string id, JsonObject entry) in Dependencies(ReadLock(lockFile)))
			{
				if (!string.Equals(id, "CheatEngine.SDK", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				consumers.Add(lockFile);
				Assert.True((string?) entry["resolved"] == pin,
					$"{lockFile} [{section}] resolves CheatEngine.SDK {(string?) entry["resolved"]}; " +
					$"the Client consumes the pin {pin} ({SdkPin.PropsPath}).");
				Assert.True((string?) entry["contentHash"] == ConsumedSdkContentHash,
					$"{lockFile} [{section}] records CheatEngine.SDK contentHash {(string?) entry["contentHash"]}, " +
					$"not the reviewed hash of the published {pin} package.");
			}
		}

		Assert.Contains("libs/CheatEngine.Client.Core/packages.lock.json", consumers);
	}

	[Fact]
	public void NoLockFileResolvesAClientPackageFromNuGet()
	{
		foreach (string lockFile in RepositoryRoot.EnumerateSourceFiles(LockFileName))
		{
			foreach ((string section, string id, JsonObject entry) in Dependencies(ReadLock(lockFile)))
			{
				if (id.StartsWith("CheatEngine.Client", StringComparison.OrdinalIgnoreCase))
				{
					Assert.True(TypeOf(entry) == "Project",
						$"{lockFile} [{section}] resolves {id} as '{TypeOf(entry)}' from a feed; Client projects are project references.");
				}
			}
		}
	}

	[Fact]
	public void NativeAotProbeLockRecordsTheWinX64IlCompilerPackages()
	{
		List<string> aotProjects = [];
		foreach (string project in RepositoryRoot.EnumerateSourceFiles("*.csproj"))
		{
			XDocument document = XDocument.Load(Path.Combine(RepositoryRoot.Path, project));
			bool publishAot = document.Descendants("PublishAot")
				.Any(static element => string.Equals(element.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));
			if (!publishAot)
			{
				continue;
			}

			aotProjects.Add(project);
			string? runtimeIdentifier = document.Descendants("RuntimeIdentifier").Select(static element => element.Value.Trim())
				.SingleOrDefault();
			Assert.False(string.IsNullOrEmpty(runtimeIdentifier),
				$"{project} publishes Native AOT without a RuntimeIdentifier, so its lock file cannot record the ILCompiler runtime pack.");

			string lockFile = LockFileOf(project);
			JsonObject dependencies = (JsonObject) ReadLock(lockFile)["dependencies"]!;
			string runtimePack = $"runtime.{runtimeIdentifier}.Microsoft.DotNet.ILCompiler";
			bool recorded = dependencies.Any(section =>
				section.Key.EndsWith("/" + runtimeIdentifier, StringComparison.Ordinal) &&
				section.Value is JsonObject packages &&
				packages.ContainsKey(runtimePack));
			Assert.True(recorded,
				$"{lockFile} has no '<tfm>/{runtimeIdentifier}' section with {runtimePack}; 'dotnet publish --no-restore' would fail.");
		}

		Assert.Contains("tests/CheatEngine.Client.AotProbe/CheatEngine.Client.AotProbe.csproj", aotProjects);
	}

	[Fact]
	public void LockFilesEndExactlyAsNuGetWritesThem()
	{
		List<string> lockFiles = [.. RepositoryRoot.EnumerateSourceFiles(LockFileName)];
		Assert.NotEmpty(lockFiles);
		List<string> offenders = [];
		foreach (string lockFile in lockFiles)
		{
			string text = File.ReadAllText(Path.Combine(RepositoryRoot.Path, lockFile));
			if (text.EndsWith('\n') || text.EndsWith('\r'))
			{
				offenders.Add(lockFile);
			}
		}

		Assert.True(offenders.Count == 0,
			"NuGet writes no final newline, so an editor or a hand edit added one to: " + string.Join(", ", offenders) +
			". Regenerate each with 'dotnet restore <project> --force-evaluate' instead of editing it.");
	}

	private static string[] CoexistenceFixtures()
	{
		return RepositoryRoot.EnumerateSourceFiles("*.csproj")
			.Where(static project => project.StartsWith(CoexistenceFolder, StringComparison.Ordinal))
			.Order(StringComparer.Ordinal)
			.ToArray();
	}

	private static string FolderOf(string relativePath)
	{
		int separator = relativePath.LastIndexOf('/');
		return separator < 0 ? string.Empty : relativePath[..separator];
	}

	private static string LockFileOf(string project)
	{
		string folder = FolderOf(project);
		return folder.Length == 0 ? LockFileName : $"{folder}/{LockFileName}";
	}

	private static JsonObject ReadLock(string lockFile)
	{
		string path = Path.Combine(RepositoryRoot.Path, lockFile);
		Assert.True(File.Exists(path), $"{lockFile} does not exist; run 'dotnet restore <project> --force-evaluate' and commit it.");
		return JsonNode.Parse(File.ReadAllText(path)) as JsonObject
			   ?? throw new InvalidOperationException($"{lockFile} is not a JSON object.");
	}

	private static IEnumerable<(string Section, string Id, JsonObject Entry)> Dependencies(JsonObject root)
	{
		if (root["dependencies"] is not JsonObject sections)
		{
			yield break;
		}

		foreach (KeyValuePair<string, JsonNode?> section in sections)
		{
			if (section.Value is not JsonObject packages)
			{
				continue;
			}

			foreach (KeyValuePair<string, JsonNode?> package in packages)
			{
				if (package.Value is JsonObject entry)
				{
					yield return (section.Key, package.Key, entry);
				}
			}
		}
	}

	private static string? TypeOf(JsonObject entry)
	{
		return (string?) entry["type"];
	}
}
