using System.Text.Json.Nodes;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.LockFiles;

/// <summary>
/// Offline mirror of the structural checks of <c>eng/Update-LockFiles.ps1</c>, so a broken lock file is reported
/// before CI restores anything. Every project restores with a committed lock file; the three Coexistence fixtures stay
/// outside Central Package Management with version 1 lock files (a solution-level <c>--force-evaluate</c> once gave
/// them CentralTransitive entries and broke every locked restore); the whole graph consumes one CheatEngine.SDK
/// identity (ADR-10: the Client follows the consumed package, SDK 1.0.0).
/// </summary>
public sealed class LockFileTests
{
	private const string LockFileName = "packages.lock.json";
	private const string TemplateContentProject =
		"templates/CheatEngine.Client.Templates/content/CheatEngine.Plugin/CheatEngine.Plugin.csproj";
	private const string CoexistenceFolder = "tests/CheatEngine.Client.LivePlugin.Coexistence/";
	private const string CoexistenceProps = CoexistenceFolder + "CoexistencePlugin.props";
	private const string LockScript = "eng/Update-LockFiles.ps1";

	// The published CheatEngine.SDK 1.0.0 as NuGet records it (SHA-512 of the unsigned package, base64).
	private const string ConsumedSdkVersion = "1.0.0";
	private const string ConsumedSdkContentHash =
		"n7nHqZ8vzo7Vf20jF0fkh/jUtR3yo1TwRGpXE7ERxZeJ4C5S/Nsft4lqOg7zGwfsD5Nh9tTVgdw4PrybJRF0gA==";

	/// <summary>
	/// Lock files whose committed text ends with a newline. NuGet writes none; eng/Update-LockFiles.ps1 keeps whatever
	/// was committed so a regeneration of an unchanged graph produces no diff.
	/// </summary>
	private static readonly HashSet<string> _lockFilesEndingWithNewline = new(StringComparer.Ordinal)
	{
		"libs/CheatEngine.Client.Abstractions/packages.lock.json",
		"libs/CheatEngine.Client.Core/packages.lock.json",
		"libs/CheatEngine.Client.Extensions.DependencyInjection/packages.lock.json",
		"libs/CheatEngine.Client.Fluent/packages.lock.json",
		"libs/CheatEngine.Client.Hosting/packages.lock.json",
		"src/CheatEngine.Client/packages.lock.json",
		"templates/CheatEngine.Client.Templates/packages.lock.json",
		"tests/CheatEngine.Client.AotProbe/packages.lock.json"
	};

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
					$"{lockFile} would be packed into every plugin created from the template; delete it.");
				continue;
			}

			if (!exists)
			{
				missing.Add(lockFile);
			}
		}

		Assert.True(missing.Count == 0,
			$"Missing lock files (run ./eng/Update-LockFiles.ps1 and commit them): {string.Join(", ", missing)}");
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
					"Regenerate with ./eng/Update-LockFiles.ps1, which restores the fixtures first, one by one.");
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
	public void EveryLockResolvesCheatEngineSdk100WithTheRecordedContentHash()
	{
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
				Assert.True((string?) entry["resolved"] == ConsumedSdkVersion,
					$"{lockFile} [{section}] resolves CheatEngine.SDK {(string?) entry["resolved"]}; the Client consumes {ConsumedSdkVersion}.");
				Assert.True((string?) entry["contentHash"] == ConsumedSdkContentHash,
					$"{lockFile} [{section}] records CheatEngine.SDK contentHash {(string?) entry["contentHash"]}, not the published 1.0.0 package.");
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
	public void LockFilesKeepTheirCommittedTrailingNewlineState()
	{
		List<string> lockFiles = [.. RepositoryRoot.EnumerateSourceFiles(LockFileName)];
		foreach (string expected in _lockFilesEndingWithNewline)
		{
			Assert.Contains(expected, lockFiles);
		}

		foreach (string lockFile in lockFiles)
		{
			string text = File.ReadAllText(Path.Combine(RepositoryRoot.Path, lockFile));
			bool endsWithNewline = text.EndsWith('\n');
			bool expectedNewline = _lockFilesEndingWithNewline.Contains(lockFile);
			Assert.True(endsWithNewline == expectedNewline,
				expectedNewline
					? $"{lockFile} lost its committed final newline; regenerate with ./eng/Update-LockFiles.ps1 instead of a plain restore."
					: $"{lockFile} gained a final newline NuGet does not write; regenerate with ./eng/Update-LockFiles.ps1.");
		}
	}

	[Fact]
	public void LockScriptRestoresTheCoexistenceFixturesFirstAndNeverTheSolutionWithForceEvaluate()
	{
		string script = File.ReadAllText(Path.Combine(RepositoryRoot.Path, LockScript));

		int previous = -1;
		foreach (string fixture in CoexistenceFixtures())
		{
			int position = script.IndexOf($"'{fixture}'", StringComparison.Ordinal);
			Assert.True(position > previous,
				$"{LockScript} must list {fixture} in its ordered Coexistence fixture list.");
			previous = position;
		}

		Assert.Contains($"'{TemplateContentProject}'", script, StringComparison.Ordinal);
		foreach (string line in script.Split('\n'))
		{
			if (line.Contains("$solution", StringComparison.Ordinal) && line.Contains("restore", StringComparison.Ordinal))
			{
				Assert.DoesNotContain("--force-evaluate", line, StringComparison.Ordinal);
				Assert.Contains("--locked-mode", line, StringComparison.Ordinal);
			}
		}
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
		Assert.True(File.Exists(path), $"{lockFile} does not exist; run ./eng/Update-LockFiles.ps1.");
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
