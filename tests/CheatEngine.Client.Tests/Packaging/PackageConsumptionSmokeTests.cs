using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using CheatEngine.Client.Tests.Infrastructure;

namespace CheatEngine.Client.Tests.Packaging;

/// <summary>
/// Proves the packages plugin authors consume, not the workspace (audit Q40 at C0-C2, A21-10, A04-09, A04-10): the exact
/// package directory of the CI Release leg, consumed from a clean folder with an isolated NuGet cache and package source
/// mapping, restored, built, deployed, and instantiated as a template. These are fixture-level (C2) results; a Cheat
/// Engine host run of Q40 is a separate qualification.
/// </summary>
[Collection(PackageConsumptionSmokeSerialGroup.Name)]
[Trait("Category", "PackageConsumption")]
[Trait("Qualification", "Q40")]
public sealed partial class PackageConsumptionSmokeTests(PackagedClientFeedFixture fixture)
{
	private const string RepositoryUrl = "https://github.com/CheatEngineNet/CheatEngine.Client";
	private const string SbomEntry = "_manifest/spdx_2.2/manifest.spdx.json";
	private const string TemplateProjectEntry = "content/CheatEngine.Plugin/CheatEngine.Plugin.csproj";
	private const string GeneratorEntry = "analyzers/dotnet/cs/CheatEngine.Client.SourceGenerators.Lua.dll";
	private const int RegexTimeoutMilliseconds = 1000;
	private static readonly Guid _sourceLinkKind = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");

	/// <summary>The observed <c>exclude</c> attribute of each direct CheatEngine.SDK dependency, frozen.</summary>
	private static readonly Dictionary<string, string> _sdkDependencyExclude = new(StringComparer.Ordinal)
	{
		["CheatEngine.Client.Abstractions"] = "Build,Native,Analyzers,BuildTransitive",
		["CheatEngine.Client.Core"] = "Build,Analyzers",
		["CheatEngine.Client.Hosting"] = "Build,Native,Analyzers,BuildTransitive"
	};

	[Fact]
	public void SevenPackagesAndFiveSymbolPackagesAreProduced()
	{
		fixture.RequirePackages();
		string[] packages = fixture.Archives.Where(static archive => !archive.IsSymbolPackage).Select(static archive => archive.Id).Order(StringComparer.Ordinal).ToArray();
		string[] symbols = fixture.Archives.Where(static archive => archive.IsSymbolPackage).Select(static archive => archive.Id).Order(StringComparer.Ordinal).ToArray();
		string[] others = Directory.GetFiles(fixture.PackageSource)
			.Where(static file => !file.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
			.ToArray();

		Assert.Equal(PackagedClientFeedFixture.PackageIds.Order(StringComparer.Ordinal), packages);
		Assert.Equal(PackagedClientFeedFixture.SymbolPackageIds.Order(StringComparer.Ordinal), symbols);
		Assert.Empty(others);
		foreach (PackageArchive symbol in fixture.Archives.Where(static archive => archive.IsSymbolPackage))
		{
			Assert.Contains(symbol.EntryNames, static entry => entry.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase));
		}

		foreach (PackageArchive archive in fixture.Archives)
		{
			PackagedClientFeedFixture.Evidence(nameof(SevenPackagesAndFiveSymbolPackagesAreProduced), $"file={archive.FileName} sha256={archive.Sha256}");
		}
	}

	[Fact]
	public void EveryClientPackageSharesOneVersion()
	{
		fixture.RequirePackages();
		foreach (PackageArchive archive in fixture.Archives)
		{
			Assert.Equal(fixture.ClientVersion, archive.Version);
			string extension = archive.IsSymbolPackage ? "snupkg" : "nupkg";
			Assert.Equal($"{archive.Id}.{archive.Version}.{extension}", archive.FileName, ignoreCase: true);
		}

		PackagedClientFeedFixture.Evidence(nameof(EveryClientPackageSharesOneVersion), $"version={fixture.ClientVersion}");
	}

	[Fact]
	public void SdkFacingPackagesDeclareThePinnedSdkRange()
	{
		fixture.RequirePackages();
		XDocument pin = XDocument.Load(RepositoryLayout.Combine("eng/CheatEngineSdk.props"));
		string range = $"[{Assert.Single(pin.Descendants("CheatEngineSdkVersion")).Value},{Assert.Single(pin.Descendants("CheatEngineSdkUpperBound")).Value})";
		foreach (string id in PackagedClientFeedFixture.PackageIds)
		{
			PackageDependency[] sdk = fixture.Package(id).Dependencies.Where(static dependency => dependency.Id == PackagedClientFeedFixture.SdkPackageId).ToArray();
			if (!_sdkDependencyExclude.TryGetValue(id, out string? exclude))
			{
				Assert.True(sdk.Length == 0, $"{id} must not depend on {PackagedClientFeedFixture.SdkPackageId} directly.");
				continue;
			}

			PackageDependency dependency = Assert.Single(sdk);
			Assert.Equal(range, dependency.Version.Replace(" ", string.Empty, StringComparison.Ordinal));
			Assert.Equal(exclude, dependency.Exclude);
			PackagedClientFeedFixture.Evidence(nameof(SdkFacingPackagesDeclareThePinnedSdkRange), $"package={id} sdk={dependency.Version} exclude={dependency.Exclude}");
		}
	}

	[Fact]
	public void InterClientDependenciesRequireTheCoPackedVersion()
	{
		fixture.RequirePackages();
		int count = 0;
		foreach (PackageArchive archive in fixture.Archives.Where(static archive => !archive.IsSymbolPackage))
		{
			foreach (PackageDependency dependency in archive.Dependencies.Where(static dependency => dependency.Id.StartsWith(PackagedClientFeedFixture.ClientPackageId, StringComparison.Ordinal)))
			{
				count++;
				Assert.True(dependency.Version == fixture.ClientVersion,
					$"{archive.Id} depends on {dependency.Id} '{dependency.Version}', expected the co-packed '{fixture.ClientVersion}'.");
			}
		}

		Assert.True(count >= 6, $"Expected the inter-Client dependencies of the package graph, found {count}.");
	}

	[Fact]
	public void HostingPackageShipsOnlyTheGeneratorAssemblyAsAnalyzer()
	{
		fixture.RequirePackages();
		string[] analyzers = fixture.Package(PackagedClientFeedFixture.HostingPackageId).EntryNames
			.Where(static entry => entry.StartsWith("analyzers/", StringComparison.Ordinal)).ToArray();

		string[] expected = [GeneratorEntry];

		Assert.Equal(expected, analyzers);
		foreach (PackageArchive archive in fixture.Archives)
		{
			Assert.DoesNotContain(archive.EntryNames, static entry => Path.GetFileName(entry).StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase));
		}
	}

	[Fact]
	public void PackedAssembliesCarryTheMajorMinorAssemblyVersion()
	{
		fixture.RequirePackages();
		string[] parts = fixture.ClientVersion.Split('-')[0].Split('.');
		Version expected = parts[0] == "0" ? new Version(0, int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), 0, 0) : new Version(int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), 0, 0, 0);
		int assemblies = 0;
		foreach (PackageArchive archive in fixture.Archives.Where(static archive => !archive.IsSymbolPackage))
		{
			foreach (string entry in archive.EntryNames.Where(static entry => entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
			{
				assemblies++;
				using PEReader reader = new(new MemoryStream(archive.Entry(entry)));
				Version version = reader.GetMetadataReader().GetAssemblyDefinition().Version;
				Assert.True(version == expected, $"{archive.Id}/{entry} has AssemblyVersion {version}, expected {expected}.");
			}
		}

		Assert.Equal(6, assemblies);
	}

	[Fact]
	public void PackedReadmesContainNoRelativeLinks()
	{
		fixture.RequirePackages();
		List<string> offenders = [];
		foreach (PackageArchive archive in fixture.Archives.Where(static archive => !archive.IsSymbolPackage))
		{
			Assert.Equal("README.md", archive.MetadataValue("readme"));
			string readme = archive.EntryText("README.md");
			foreach (Match link in LinkTarget().Matches(StripCode(readme)))
			{
				string target = link.Groups["target"].Value;
				if (!target.StartsWith("https://", StringComparison.Ordinal))
				{
					offenders.Add($"{archive.Id}: {target}");
				}
			}
		}

		Assert.True(offenders.Count == 0,
			$"nuget.org cannot resolve relative links in a packed README:{System.Environment.NewLine}{string.Join(System.Environment.NewLine, offenders)}");
	}

	[Fact]
	public void EveryPackageNamesTheRepositoryCommitAndLicense()
	{
		fixture.RequirePackages();
		HashSet<string> commits = new(StringComparer.Ordinal);
		HashSet<string> descriptions = new(StringComparer.Ordinal);
		foreach (PackageArchive archive in fixture.Archives.Where(static archive => !archive.IsSymbolPackage))
		{
			XElement repository = Assert.IsType<XElement>(archive.MetadataElement("repository"));
			XElement license = Assert.IsType<XElement>(archive.MetadataElement("license"));
			string commit = (string?) repository.Attribute("commit") ?? string.Empty;

			Assert.Equal("git", (string?) repository.Attribute("type"));
			Assert.Equal(RepositoryUrl, (string?) repository.Attribute("url"));
			Assert.Matches("^[0-9a-f]{40}$", commit);
			Assert.Equal("expression", (string?) license.Attribute("type"));
			Assert.Equal("MIT", license.Value);
			Assert.Equal(RepositoryUrl, archive.MetadataValue("projectUrl"));
			Assert.Equal($"{RepositoryUrl}/blob/main/CHANGELOG.md", archive.MetadataValue("releaseNotes"));
			Assert.StartsWith("Copyright (c) ", archive.MetadataValue("copyright"), StringComparison.Ordinal);
			Assert.True(descriptions.Add(archive.MetadataValue("description") ?? string.Empty), $"{archive.Id} repeats another package's description.");
			Assert.True(archive.Contains("README.md"), $"{archive.Id} has no README.md at the package root.");
			commits.Add(commit);
		}

		string single = Assert.Single(commits);
		PackagedClientFeedFixture.Evidence(nameof(EveryPackageNamesTheRepositoryCommitAndLicense), $"commit={single}");
	}

	[Fact]
	public void SymbolPackagesCarrySourceLinkToTheRepositoryCommit()
	{
		fixture.RequirePackages();
		string commit = (string) fixture.Package(PackagedClientFeedFixture.ClientPackageId).MetadataElement("repository")!.Attribute("commit")!;
		string expectedPrefix = $"https://raw.githubusercontent.com/CheatEngineNet/CheatEngine.Client/{commit}/";
		foreach (PackageArchive symbol in fixture.Archives.Where(static archive => archive.IsSymbolPackage))
		{
			foreach (string pdb in symbol.EntryNames.Where(static entry => entry.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)))
			{
				using MetadataReaderProvider provider = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(symbol.Entry(pdb)));
				MetadataReader reader = provider.GetMetadataReader();
				string? sourceLink = null;
				foreach (CustomDebugInformationHandle handle in reader.GetCustomDebugInformation(EntityHandle.ModuleDefinition))
				{
					CustomDebugInformation information = reader.GetCustomDebugInformation(handle);
					if (reader.GetGuid(information.Kind) == _sourceLinkKind)
					{
						sourceLink = Encoding.UTF8.GetString(reader.GetBlobBytes(information.Value));
					}
				}

				Assert.True(sourceLink is not null, $"{symbol.Id}/{pdb} has no Source Link information.");
				using JsonDocument documents = JsonDocument.Parse(sourceLink!);
				foreach (JsonProperty mapping in documents.RootElement.GetProperty("documents").EnumerateObject())
				{
					Assert.StartsWith(expectedPrefix, mapping.Value.GetString(), StringComparison.Ordinal);
				}
			}
		}
	}

	[Fact]
	public void EveryPackageEmbedsAnSpdxSbomDescribingItsOwnIdentity()
	{
		fixture.RequirePackages();
		foreach (PackageArchive archive in fixture.Archives.Where(static archive => !archive.IsSymbolPackage))
		{
			using JsonDocument sbom = JsonDocument.Parse(archive.Entry(SbomEntry));
			JsonElement root = sbom.RootElement;
			Assert.Equal("SPDX-2.2", root.GetProperty("spdxVersion").GetString());
			JsonElement described = Assert.Single(root.GetProperty("packages").EnumerateArray(),
				static package => package.GetProperty("SPDXID").GetString() == "SPDXRef-RootPackage");
			Assert.Equal(archive.Id, described.GetProperty("name").GetString());
			Assert.Equal(archive.Version, described.GetProperty("versionInfo").GetString());
			Assert.StartsWith($"{RepositoryUrl}/{archive.Id}/{archive.Version}/", root.GetProperty("documentNamespace").GetString(), StringComparison.Ordinal);

			Dictionary<string, string> files = new(StringComparer.Ordinal);
			foreach (JsonElement file in root.GetProperty("files").EnumerateArray())
			{
				string sha256 = file.GetProperty("checksums").EnumerateArray()
					.Single(static checksum => checksum.GetProperty("algorithm").GetString() == "SHA256").GetProperty("checksumValue").GetString()!;
				files[file.GetProperty("fileName").GetString()!.TrimStart('.', '/')] = sha256;
			}

			foreach (string entry in archive.EntryNames.Where(static entry => entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
			{
				Assert.True(files.TryGetValue(entry, out string? recorded), $"The SBOM of {archive.Id} does not list {entry}.");
				Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(archive.Entry(entry))), recorded, ignoreCase: true);
			}

			PackagedClientFeedFixture.Evidence(nameof(EveryPackageEmbedsAnSpdxSbomDescribingItsOwnIdentity),
				$"package={archive.Id} sbomSha256={Convert.ToHexStringLower(SHA256.HashData(archive.Entry(SbomEntry)))} files={files.Count}");
		}
	}

	[Fact]
	public void PackedTemplateReferencesTheCoPackedClientAndThePinnedSdk()
	{
		fixture.RequirePackages();
		XDocument project = XDocument.Parse(fixture.Package(PackagedClientFeedFixture.TemplatePackageId).EntryText(TemplateProjectEntry));

		Assert.Equal(fixture.ClientVersion, PackageReferenceVersion(project, PackagedClientFeedFixture.ClientPackageId));
		Assert.Equal(PinnedSdkVersion(), PackageReferenceVersion(project, PackagedClientFeedFixture.SdkPackageId));
		Assert.Single(fixture.Package(PackagedClientFeedFixture.TemplatePackageId).EntryNames, static entry => entry.EndsWith(".csproj", StringComparison.Ordinal));
	}

	[Fact]
	public void PackedTemplateProjectDiffersFromTheRepositoryTemplateOnlyByStampedVersions()
	{
		fixture.RequirePackages();
		string packed = fixture.Package(PackagedClientFeedFixture.TemplatePackageId).EntryText(TemplateProjectEntry);
		string repository = File.ReadAllText(RepositoryLayout.Combine($"templates/CheatEngine.Client.Templates/{TemplateProjectEntry}"));

		Assert.Equal(3, PackageReferenceVersionAttribute().Count(packed));
		Assert.Equal(PackageReferenceVersionAttribute().Replace(repository, "${prefix}*${suffix}"),
			PackageReferenceVersionAttribute().Replace(packed, "${prefix}*${suffix}"));
	}

	[Fact]
	public async Task TemplatePackageInstallsListsAndUninstalls()
	{
		fixture.RequirePackages();
		string home = fixture.CreateDirectory("template-lifecycle");
		Dictionary<string, string> isolatedHome = new(StringComparer.Ordinal)
		{
			["DOTNET_CLI_HOME"] = Path.Combine(home, "cli-home"),
			["DOTNET_NEW_HOME"] = Path.Combine(home, "template-engine")
		};
		string package = fixture.Package(PackagedClientFeedFixture.TemplatePackageId).Path;

		DotNetProcessResult install = await fixture.RunAsync(home, isolatedHome, "new", "install", package);
		DotNetProcessResult list = await fixture.RunAsync(home, isolatedHome, "new", "list", "ceplugin");
		DotNetProcessResult installed = await fixture.RunAsync(home, isolatedHome, "new", "uninstall");
		DotNetProcessResult uninstall = await fixture.RunAsync(home, isolatedHome, "new", "uninstall", PackagedClientFeedFixture.TemplatePackageId);
		DotNetProcessResult remaining = await fixture.RunAsync(home, isolatedHome, "new", "uninstall");

		Assert.True(install.ExitCode == 0, install.ToString());
		Assert.True(list.ExitCode == 0 && list.StandardOutput.Contains("ceplugin", StringComparison.Ordinal), list.ToString());
		Assert.Contains(PackagedClientFeedFixture.TemplatePackageId, installed.StandardOutput, StringComparison.Ordinal);
		Assert.Contains(fixture.ClientVersion, installed.StandardOutput, StringComparison.Ordinal);
		Assert.True(uninstall.ExitCode == 0, uninstall.ToString());
		Assert.DoesNotContain(PackagedClientFeedFixture.TemplatePackageId, remaining.StandardOutput, StringComparison.Ordinal);
	}

	[Fact]
	public void IsolatedConsumerResolvesClientPackagesOnlyFromTheLocalFeed()
	{
		fixture.RequireConsumer();
		string feed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fixture.PackageSource));
		string[] clientFolders = Directory.GetDirectories(fixture.PackageCache, "cheatengine.client*");
		Assert.Equal(6, clientFolders.Length);
		foreach (string folder in clientFolders)
		{
			string version = Assert.Single(Directory.GetDirectories(folder));
			Assert.Equal(fixture.ClientVersion, Path.GetFileName(version), ignoreCase: true);
			using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(version, ".nupkg.metadata")));
			string source = Path.TrimEndingDirectorySeparator(metadata.RootElement.GetProperty("source").GetString()!);
			Assert.True(string.Equals(source, feed, StringComparison.OrdinalIgnoreCase),
				$"{Path.GetFileName(folder)} was restored from '{source}', not from the local package directory '{feed}'.");
		}

		string sdkFolder = Path.Combine(fixture.PackageCache, "cheatengine.sdk", fixture.SdkVersion.ToLowerInvariant());
		using JsonDocument sdkMetadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(sdkFolder, ".nupkg.metadata")));
		string contentHash = sdkMetadata.RootElement.GetProperty("contentHash").GetString()!;
		if (fixture.UsesPinnedSdk)
		{
			Assert.Equal("https://api.nuget.org/v3/index.json", sdkMetadata.RootElement.GetProperty("source").GetString());
			Assert.Equal(fixture.ConsumedSdk.GetProperty("contentHashSha512").GetString(), contentHash);
		}

		PackagedClientFeedFixture.Evidence(nameof(IsolatedConsumerResolvesClientPackagesOnlyFromTheLocalFeed),
			$"sdk={fixture.SdkVersion} sdkSource={sdkMetadata.RootElement.GetProperty("source").GetString()} sdkContentHashSha512={contentHash}");
	}

	[Fact]
	public void IsolatedConsumerDeploysTheCompleteClosureWithThePackagedBridge()
	{
		fixture.RequireConsumer();
		string bridge = PackagedBridgeSha256();
		string[] required =
		[
			$"{PackagedClientFeedFixture.ConsumerName}.dll", $"{PackagedClientFeedFixture.ConsumerName}.deps.json",
			$"{PackagedClientFeedFixture.ConsumerName}.runtimeconfig.json", PackagedClientFeedFixture.BridgeFileName,
			.. PackagedClientFeedFixture.ClientAssemblies
		];
		foreach (string directory in (string[]) [fixture.ConsumerOutput, fixture.DeploymentDirectory])
		{
			foreach (string asset in required)
			{
				Assert.True(File.Exists(Path.Combine(directory, asset)), $"'{directory}' is missing '{asset}'.");
			}

			Assert.Equal(bridge, FileSha256(Path.Combine(directory, PackagedClientFeedFixture.BridgeFileName)));
		}

		if (fixture.UsesPinnedSdk)
		{
			Assert.Equal(fixture.ConsumedSdk.GetProperty("nativeBridge").GetProperty("sha256").GetString(), bridge);
		}

		PackagedClientFeedFixture.Evidence(nameof(IsolatedConsumerDeploysTheCompleteClosureWithThePackagedBridge), $"bridgeSha256={bridge}");
	}

	[Fact]
	public void IsolatedConsumerDepsJsonRecordsPackagesWithoutWorkspacePaths()
	{
		fixture.RequireConsumer();
		string depsPath = Path.Combine(fixture.ConsumerOutput, $"{PackagedClientFeedFixture.ConsumerName}.deps.json");
		string runtimeConfigPath = Path.Combine(fixture.ConsumerOutput, $"{PackagedClientFeedFixture.ConsumerName}.runtimeconfig.json");
		string depsText = File.ReadAllText(depsPath);
		using JsonDocument deps = JsonDocument.Parse(depsText);
		JsonElement libraries = deps.RootElement.GetProperty("libraries");

		JsonElement sdk = libraries.GetProperty($"{PackagedClientFeedFixture.SdkPackageId}/{fixture.SdkVersion}");
		Assert.Equal("package", sdk.GetProperty("type").GetString());
		foreach (string id in PackagedClientFeedFixture.PackageIds.Where(static id => id != PackagedClientFeedFixture.TemplatePackageId))
		{
			Assert.Equal("package", libraries.GetProperty($"{id}/{fixture.ClientVersion}").GetProperty("type").GetString());
		}

		// Measured, not assumed (audit A21-02): the deps.json library entry carries the NuGet content hash NuGet recorded
		// in .nupkg.metadata, the value a lock file holds, not the SHA-512 of the repository-signed file
		// (<id>.<version>.nupkg.sha512). A deployed plugin can therefore be tied to the Client tuple's consumedSdk.
		string versionFolder = Path.Combine(fixture.PackageCache, "cheatengine.sdk", fixture.SdkVersion.ToLowerInvariant());
		using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(versionFolder, ".nupkg.metadata")));
		string contentHash = metadata.RootElement.GetProperty("contentHash").GetString()!;
		string signedSha512 = File.ReadAllText(Path.Combine(versionFolder, $"cheatengine.sdk.{fixture.SdkVersion.ToLowerInvariant()}.nupkg.sha512")).Trim();
		Assert.Equal($"sha512-{contentHash}", sdk.GetProperty("sha512").GetString());
		if (fixture.UsesPinnedSdk)
		{
			Assert.Equal(fixture.ConsumedSdk.GetProperty("contentHashSha512").GetString(), contentHash);
			Assert.Equal(fixture.ConsumedSdk.GetProperty("nugetOrgSignedSha512").GetString(), signedSha512);
			Assert.NotEqual(contentHash, signedSha512);
		}

		foreach (string text in (string[]) [depsText, File.ReadAllText(runtimeConfigPath)])
		{
			foreach (string workspace in WorkspaceSpellings())
			{
				Assert.DoesNotContain(workspace, text, StringComparison.OrdinalIgnoreCase);
			}
		}

		PackagedClientFeedFixture.Evidence(nameof(IsolatedConsumerDepsJsonRecordsPackagesWithoutWorkspacePaths),
			$"depsSha256={FileSha256(depsPath)} sdkLibrarySha512=sha512-{contentHash} (the NuGet content hash of the lock, not the signed-file SHA-512 {signedSha512})");
	}

	[Fact]
	public void InstantiatedTemplateReferencesTheSdkDirectly()
	{
		fixture.RequireTemplate();
		XDocument project = XDocument.Load(Path.Combine(fixture.TemplateDirectory, $"{PackagedClientFeedFixture.ConsumerName}.csproj"));

		Assert.Equal(fixture.ClientVersion, PackageReferenceVersion(project, PackagedClientFeedFixture.ClientPackageId));
		Assert.Equal(PinnedSdkVersion(), PackageReferenceVersion(project, PackagedClientFeedFixture.SdkPackageId));
		Assert.Equal("true", project.Descendants("CheatEngineClientPluginProject").Single().Value);
	}

	[Fact]
	public void InstantiatedTemplateBuildsTheCompleteDeploymentClosure()
	{
		fixture.RequireTemplate();
		string[] required =
		[
			$"{PackagedClientFeedFixture.ConsumerName}.dll", $"{PackagedClientFeedFixture.ConsumerName}.deps.json",
			$"{PackagedClientFeedFixture.ConsumerName}.runtimeconfig.json", PackagedClientFeedFixture.BridgeFileName,
			.. PackagedClientFeedFixture.ClientAssemblies
		];
		foreach (string asset in required)
		{
			Assert.True(File.Exists(Path.Combine(fixture.TemplateOutput, asset)), $"The instantiated template output is missing '{asset}'.");
		}

		Assert.Equal(PackagedBridgeSha256(), FileSha256(Path.Combine(fixture.TemplateOutput, PackagedClientFeedFixture.BridgeFileName)));
	}

	[Fact]
	public async Task PackagedClientAndTemplateCanBeInstalledInstantiatedAndBuiltInIsolatedDirectories()
	{
		fixture.RequireConsumer();
		fixture.RequireTemplate();
		PackagedClientFeedFixture.AssertOutsideAnyRepository(fixture.ConsumerDirectory);
		PackagedClientFeedFixture.AssertOutsideAnyRepository(fixture.TemplateDirectory);

		string generatedEntryPoint = Assert.Single(Directory.GetFiles(Path.Combine(fixture.ConsumerDirectory, "obj"),
			"CheatEngine.SDK.EntryPoint.g.cs", SearchOption.AllDirectories));
		string text = await File.ReadAllTextAsync(generatedEntryPoint, TestContext.Current.CancellationToken);
		Assert.Contains("namespace CESDK", text, StringComparison.Ordinal);
		Assert.Contains("CEPluginInitialize", text, StringComparison.Ordinal);
		PackagedClientFeedFixture.Evidence(nameof(PackagedClientAndTemplateCanBeInstalledInstantiatedAndBuiltInIsolatedDirectories),
			$"source={fixture.SourceKind} client={fixture.ClientVersion} sdk={fixture.SdkVersion}");
	}

	[Fact]
	public async Task PackagedClientPluginWithoutDirectSdkReferenceReportsCECLIENT001()
	{
		fixture.RequirePackages();
		string consumer = fixture.CreateDirectory("missing-sdk-package-consumer");
		string project = Path.Combine(consumer, "MissingSdk.Plugin.csproj");
		await File.WriteAllTextAsync(project, PackagedClientFeedFixture.CreateConsumerProject(fixture.ClientVersion, null),
			new UTF8Encoding(false), TestContext.Current.CancellationToken);

		DotNetProcessResult restore = await fixture.RunAsync(consumer, "restore", project, "--configfile", fixture.NuGetConfiguration,
			"--packages", fixture.PackageCache);
		DotNetProcessResult build = await fixture.RunAsync(consumer, "build", project, "--configuration", "Release", "--no-restore",
			"-p:UseSharedCompilation=false");

		Assert.True(restore.ExitCode == 0, restore.ToString());
		Assert.True(build.ExitCode != 0, build.ToString());
		Assert.Contains("CECLIENT001", build.StandardOutput + build.StandardError, StringComparison.Ordinal);
	}

	[Fact]
	public async Task PluginReferencingSdkTwoReportsCECLIENT017()
	{
		fixture.RequireConsumer();
		Assert.True(fixture.UsesPinnedSdk, $"This fact re-versions the pinned SDK; unset {PackagedClientFeedFixture.SdkPackageSourceVariable}.");
		string feed = fixture.CreateDirectory("sdk-two-feed");

		// A 2.0.0 prerelease sorts below 2.0.0, so it satisfies [1.0.0, 2.0.0) and NuGet resolves it without any
		// warning; a stable 2.0.0 only triggers the NU1608 warning. Both must fail the plugin build.
		foreach ((string version, bool nuGetWarns) in (ValueTuple<string, bool>[]) [("2.0.0-cecanary.1", false), ("2.0.0", true)])
		{
			fixture.CreateReversionedSdkPackage(feed, version);
			string consumer = fixture.CreateDirectory($"sdk-two-consumer-{version}");
			string project = Path.Combine(consumer, "SdkTwo.Plugin.csproj");
			await File.WriteAllTextAsync(project, PackagedClientFeedFixture.CreateConsumerProject(fixture.ClientVersion, version),
				new UTF8Encoding(false), TestContext.Current.CancellationToken);
			await File.WriteAllTextAsync(Path.Combine(consumer, "Plugin.cs"), PackagedClientFeedFixture.ConsumerSource,
				new UTF8Encoding(false), TestContext.Current.CancellationToken);
			string configuration = PackagedClientFeedFixture.WriteNuGetConfiguration(Path.Combine(consumer, "NuGet.Config"),
				fixture.PackageCache, fixture.PackageSource, feed);

			DotNetProcessResult restore = await fixture.RunAsync(consumer, "restore", project, "--configfile", configuration,
				"--packages", fixture.PackageCache);
			DotNetProcessResult refused = await fixture.RunAsync(consumer, "build", project, "--configuration", "Release",
				"--no-restore", "-p:UseSharedCompilation=false");
			DotNetProcessResult allowed = await fixture.RunAsync(consumer, "build", project, "--configuration", "Release",
				"--no-restore", "-p:UseSharedCompilation=false", "-p:CheatEngineClientAllowUnsupportedSdk=true");

			Assert.True(restore.ExitCode == 0, restore.ToString());
			Assert.Equal(nuGetWarns, restore.StandardOutput.Contains("NU1608", StringComparison.Ordinal));
			Assert.True(refused.ExitCode != 0, refused.ToString());
			Assert.Contains("error CECLIENT017", refused.StandardOutput, StringComparison.Ordinal);
			Assert.True(allowed.ExitCode == 0, allowed.ToString());
			Assert.Contains("warning CECLIENT017", allowed.StandardOutput, StringComparison.Ordinal);
			PackagedClientFeedFixture.Evidence(nameof(PluginReferencingSdkTwoReportsCECLIENT017),
				$"sdk={version} nu1608={nuGetWarns} build=CECLIENT017 error; opt-out=CECLIENT017 warning");
		}
	}

	private static string PinnedSdkVersion()
	{
		XDocument pin = XDocument.Load(RepositoryLayout.Combine("eng/CheatEngineSdk.props"));
		return Assert.Single(pin.Descendants("CheatEngineSdkVersion")).Value.Trim();
	}

	private string PackagedBridgeSha256()
	{
		string version = fixture.SdkVersion.ToLowerInvariant();
		PackageArchive sdk = PackageArchive.Read(Path.Combine(fixture.PackageCache, "cheatengine.sdk", version, $"cheatengine.sdk.{version}.nupkg"));
		return Convert.ToHexStringLower(SHA256.HashData(sdk.Entry($"build/native/{PackagedClientFeedFixture.BridgeFileName}")));
	}

	private static string? PackageReferenceVersion(XDocument project, string id)
	{
		return (string?) project.Descendants("PackageReference").Single(reference => (string?) reference.Attribute("Include") == id).Attribute("Version");
	}

	private static string FileSha256(string path)
	{
		return Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
	}

	private static IEnumerable<string> WorkspaceSpellings()
	{
		string root = Path.TrimEndingDirectorySeparator(RepositoryLayout.Root);
		yield return root.Replace("\\", "\\\\", StringComparison.Ordinal);
		yield return root.Replace('\\', '/');
	}

	private static string StripCode(string markdown)
	{
		return InlineCode().Replace(FencedCode().Replace(markdown, string.Empty), string.Empty);
	}

	[GeneratedRegex(@"(?:\]\(\s*<?(?<target>[^)\s>]+)|(?:href|src)\s*=\s*[""'](?<target>[^""']+))", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex LinkTarget();

	[GeneratedRegex(@"^[ \t]*(`{3,}|~{3,})[^\n]*\n.*?^[ \t]*\1[ \t]*$", RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Singleline, RegexTimeoutMilliseconds)]
	private static partial Regex FencedCode();

	[GeneratedRegex(@"`[^`\n]*`", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex InlineCode();

	[GeneratedRegex(@"(?<prefix><PackageReference\s+Include=""[^""]+""\s+Version="")[^""]*(?<suffix>"")", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex PackageReferenceVersionAttribute();
}
