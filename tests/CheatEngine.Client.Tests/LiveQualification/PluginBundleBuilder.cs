using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using CheatEngine.Client.Tests.Infrastructure;
using CheatEngine.Client.Tests.Packaging;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>A plugin deployment folder, built from the packed packages, that Cheat Engine loads with <c>loadPlugin</c>.</summary>
/// <param name="Name">The bundle name, also its folder name below <c>&lt;run&gt;/plugins</c>.</param>
/// <param name="Directory">The deployment folder (the complete closure the Hosting targets produce).</param>
/// <param name="EntryAssemblyPath">The plugin assembly to load.</param>
/// <param name="BridgeSha256">The lower-case SHA-256 of the deployed native bridge.</param>
internal sealed record PluginBundle(string Name, string Directory, string EntryAssemblyPath, string BridgeSha256);

/// <summary>The instantiated template's bundle, with the facts S6 checks (Q40).</summary>
/// <param name="Bundle">The deployment folder.</param>
/// <param name="StatusGlobal">The Lua global the instantiated template exports.</param>
/// <param name="SdkContentHash">
///     The CheatEngine.SDK content hash the template's restore recorded (its lock file, or <c>project.assets.json</c>
///     when the template writes no lock file), or <see langword="null" />.
/// </param>
/// <param name="DepsWorkspacePaths">How many paths of the build workspace its <c>deps.json</c> holds.</param>
internal sealed record TemplateBundle(PluginBundle Bundle, string StatusGlobal, string? SdkContentHash, int DepsWorkspacePaths);

/// <summary>
///     Builds plugin bundles the way a plugin author does (Q40): the sources are copied into an isolated consumer outside
///     any repository, which references the packed <c>CheatEngine.Client</c> from the tested package directory and
///     CheatEngine.SDK from nuget.org (<see cref="PackagedClientFeedFixture" />), and the Hosting deployment target writes
///     the complete closure to <c>&lt;run&gt;/plugins/&lt;name&gt;</c>. The workspace build is never loaded. The SDK 1.x
///     neighbour of S5 references nothing of the Client, and the template of S6 is instantiated from the packed Templates
///     package.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class PluginBundleBuilder
{
	/// <summary>The bundle name of the qualification harness.</summary>
	internal const string HarnessName = "harness";

	/// <summary>The assembly name of the qualification harness, as its repository project declares it.</summary>
	internal const string HarnessAssemblyName = "CheatEngine.Client.LivePlugin.Qualification";

	/// <summary>The repository folder of the harness sources.</summary>
	internal const string HarnessSourceFolder = "tests/CheatEngine.Client.LivePlugin.Qualification";

	/// <summary>The repository folder of the coexistence fixtures.</summary>
	internal const string CoexistenceSourceFolder = "tests/CheatEngine.Client.LivePlugin.Coexistence";

	/// <summary>The project name S6 instantiates the template as.</summary>
	internal const string TemplateProjectName = "QualTemplatePlugin";

	private readonly PackagedClientFeedFixture _feed;
	private readonly string _pluginsDirectory;

	internal PluginBundleBuilder(PackagedClientFeedFixture feed, string pluginsDirectory)
	{
		ArgumentNullException.ThrowIfNull(feed);
		ArgumentException.ThrowIfNullOrWhiteSpace(pluginsDirectory);
		_feed = feed;
		_pluginsDirectory = pluginsDirectory;
	}

	/// <summary>The harness sources: the plugin folder's own C# files and its <c>Harness/</c> folder.</summary>
	internal static IReadOnlyList<string> HarnessSources(string repositoryRoot)
	{
		string folder = Path.Combine(repositoryRoot, HarnessSourceFolder);
		return Directory.EnumerateFiles(folder, "*.cs", SearchOption.TopDirectoryOnly)
			.Concat(Directory.EnumerateFiles(Path.Combine(folder, "Harness"), "*.cs", SearchOption.TopDirectoryOnly))
			.Order(StringComparer.Ordinal)
			.ToArray();
	}

	/// <summary>The sources of one coexistence plugin: its folder's C# files and the shared diagnostics file.</summary>
	internal static IReadOnlyList<string> CoexistenceSources(string repositoryRoot, SessionBundle bundle)
	{
		string folder = Path.Combine(repositoryRoot, CoexistenceSourceFolder);
		return Directory.EnumerateFiles(Path.Combine(folder, CoexistenceFolder(bundle)), "*.cs", SearchOption.TopDirectoryOnly)
			.Append(Path.Combine(folder, "CoexistenceDiagnostics.cs"))
			.Order(StringComparer.Ordinal)
			.ToArray();
	}

	/// <summary>The assembly name of a coexistence plugin, as its repository project declares it.</summary>
	internal static string CoexistenceAssemblyName(SessionBundle bundle)
	{
		return "CheatEngine.Client.LivePlugin.Coexistence." + CoexistenceFolder(bundle);
	}

	/// <summary>Builds the qualification harness from the packed packages.</summary>
	internal Task<PluginBundle> BuildHarnessAsync()
	{
		string root = RepositoryLayout.Combine(HarnessSourceFolder);
		return BuildClientConsumerAsync(HarnessName, HarnessAssemblyName, "LivePlugin.Qualification",
			HarnessSources(RepositoryLayout.Root).Select(source => (Path.GetRelativePath(root, source), source)));
	}

	/// <summary>Builds one coexistence plugin (A, B or the collision contender) from the packed packages.</summary>
	internal Task<PluginBundle> BuildCoexistenceAsync(SessionBundle bundle)
	{
		string folder = CoexistenceFolder(bundle);
		return BuildClientConsumerAsync(folder.ToLowerInvariant(), CoexistenceAssemblyName(bundle),
			"LivePlugin.Coexistence." + folder,
			CoexistenceSources(RepositoryLayout.Root, bundle).Select(static source => (Path.GetFileName(source), source)));
	}

	/// <summary>
	///     Builds the plain CheatEngine.SDK 1.x neighbour of S5: restored first, so that the SHA-256 of the bridge its
	///     package ships is measured before the source that compares with it is written.
	/// </summary>
	internal async Task<PluginBundle> BuildNeighbourAsync()
	{
		_feed.RequirePackages();
		Assert.True(_feed.UsesPinnedSdk,
			$"The SDK 1.x neighbour restores CheatEngine.SDK {NeighbourPluginSource.SdkVersion} from nuget.org; unset " +
			$"{PackagedClientFeedFixture.SdkPackageSourceVariable}.");
		const string Name = "sdk1-neighbour";
		string consumer = _feed.CreateDirectory("bundle-" + Name);
		PackagedClientFeedFixture.AssertOutsideAnyRepository(consumer);
		string project = Path.Combine(consumer, NeighbourPluginSource.AssemblyName + ".csproj");
		await File.WriteAllTextAsync(project, NeighbourPluginSource.Project(), new UTF8Encoding(false));
		await RunAsync(consumer, "restore", project, "--configfile", _feed.NuGetConfiguration, "--packages", _feed.PackageCache);

		string package = Path.Combine(_feed.PackageCache, "cheatengine.sdk", NeighbourPluginSource.SdkVersion);
		string packagedBridge = Assert.Single(Directory.EnumerateFiles(package, PackagedClientFeedFixture.BridgeFileName,
			SearchOption.AllDirectories));
		await File.WriteAllTextAsync(Path.Combine(consumer, "NeighbourPlugin.cs"),
			NeighbourPluginSource.Source(CheatEngineInstallation.Sha256(packagedBridge).ToLowerInvariant()), new UTF8Encoding(false));

		string output = Path.Combine(_pluginsDirectory, Name);
		await RunAsync(consumer, "build", project, "--configuration", "Release", "--no-restore", "-p:UseSharedCompilation=false",
			"--output", output);
		return Bundle(Name, output, NeighbourPluginSource.AssemblyName);
	}

	/// <summary>
	///     Instantiates the packed Templates package as <see cref="TemplateProjectName" /> in an isolated template home,
	///     restores and builds it with its deployment path, and reads the facts S6 checks.
	/// </summary>
	internal async Task<TemplateBundle> BuildTemplateAsync()
	{
		_feed.RequirePackages();
		const string Name = "template";
		string home = _feed.CreateDirectory("bundle-" + Name);
		PackagedClientFeedFixture.AssertOutsideAnyRepository(home);
		Dictionary<string, string> isolatedHome = new(StringComparer.Ordinal)
		{
			["DOTNET_CLI_HOME"] = Path.Combine(home, "cli-home"),
			["DOTNET_NEW_HOME"] = Path.Combine(home, "template-engine")
		};
		string consumer = Path.Combine(home, TemplateProjectName);
		string project = Path.Combine(consumer, TemplateProjectName + ".csproj");
		string output = Path.Combine(_pluginsDirectory, Name);
		await RunAsync(home, isolatedHome, "new", "install", _feed.Package(PackagedClientFeedFixture.TemplatePackageId).Path);
		await RunAsync(home, isolatedHome, "new", "ceplugin", "--name", TemplateProjectName, "--output", consumer);
		await RunAsync(consumer, isolatedHome, "restore", project, "--configfile", _feed.NuGetConfiguration, "--packages",
			_feed.PackageCache);
		await RunAsync(consumer, isolatedHome, "build", project, "--configuration", "Release", "--no-restore",
			"-p:UseSharedCompilation=false", $"-p:CheatEnginePluginOutputPath={output}");

		PluginBundle bundle = Bundle(Name, output, TemplateProjectName);
		string sources = string.Concat(Directory.EnumerateFiles(consumer, "*.cs", SearchOption.AllDirectories)
			.Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
			.Select(File.ReadAllText));
		string statusGlobal = LuaFunctionName().Match(sources) is { Success: true } match
			? match.Groups["name"].Value
			: throw new InvalidOperationException("The instantiated template declares no [LuaFunction].");
		string deps = await File.ReadAllTextAsync(Path.Combine(output, TemplateProjectName + ".deps.json"));
		return new TemplateBundle(bundle, statusGlobal, SdkContentHash(consumer),
			CountPaths(deps, [consumer, home, RepositoryLayout.Root]));
	}

	/// <summary>How many times <paramref name="text" /> names one of <paramref name="paths" />, with either slash, JSON-escaped or not.</summary>
	internal static int CountPaths(string text, IEnumerable<string> paths)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(paths);
		int count = 0;
		foreach (string path in paths.Select(static path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))))
		{
			foreach (string form in new HashSet<string>(
						 [path, path.Replace('\\', '/'), path.Replace("\\", "\\\\", StringComparison.Ordinal)],
						 StringComparer.OrdinalIgnoreCase))
			{
				for (int index = text.IndexOf(form, StringComparison.OrdinalIgnoreCase); index >= 0;
					 index = text.IndexOf(form, index + form.Length, StringComparison.OrdinalIgnoreCase))
				{
					count++;
				}
			}
		}

		return count;
	}

	/// <summary>The CheatEngine.SDK content hash a restore recorded: the lock file, else <c>obj/project.assets.json</c>.</summary>
	internal static string? SdkContentHash(string projectDirectory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
		string lockFile = Path.Combine(projectDirectory, "packages.lock.json");
		if (File.Exists(lockFile))
		{
			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(lockFile));
			foreach (JsonProperty framework in document.RootElement.GetProperty("dependencies").EnumerateObject())
			{
				if (framework.Value.TryGetProperty(PackagedClientFeedFixture.SdkPackageId, out JsonElement sdk) &&
					sdk.TryGetProperty("contentHash", out JsonElement hash))
				{
					return hash.GetString();
				}
			}
		}

		string assets = Path.Combine(projectDirectory, "obj", "project.assets.json");
		if (!File.Exists(assets))
		{
			return null;
		}

		using JsonDocument restore = JsonDocument.Parse(File.ReadAllText(assets));
		foreach (JsonProperty library in restore.RootElement.GetProperty("libraries").EnumerateObject())
		{
			if (library.Name.StartsWith(PackagedClientFeedFixture.SdkPackageId + "/", StringComparison.OrdinalIgnoreCase) &&
				library.Value.TryGetProperty("sha512", out JsonElement sha512))
			{
				return sha512.GetString();
			}
		}

		return null;
	}

	private static string CoexistenceFolder(SessionBundle bundle)
	{
		return bundle switch
		{
			SessionBundle.PluginA => "PluginA",
			SessionBundle.PluginB => "PluginB",
			SessionBundle.PluginCollision => "PluginCollision",
			_ => throw new ArgumentOutOfRangeException(nameof(bundle), bundle, "Not a coexistence plugin.")
		};
	}

	private async Task<PluginBundle> BuildClientConsumerAsync(string name, string assemblyName, string rootNamespace,
		IEnumerable<(string Relative, string Source)> sources)
	{
		_feed.RequirePackages();
		string consumer = _feed.CreateDirectory("bundle-" + name);
		PackagedClientFeedFixture.AssertOutsideAnyRepository(consumer);
		foreach ((string relative, string source) in sources)
		{
			string copy = Path.Combine(consumer, relative);
			Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
			File.Copy(source, copy);
		}

		string project = Path.Combine(consumer, assemblyName + ".csproj");
		string properties = $"""
		                         <AssemblyName>{assemblyName}</AssemblyName>
		                         <RootNamespace>{rootNamespace}</RootNamespace>
		                         <EnableDynamicLoading>true</EnableDynamicLoading>
		                         <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
		                     """;
		await File.WriteAllTextAsync(project,
			PackagedClientFeedFixture.CreateConsumerProject(_feed.ClientVersion, _feed.SdkVersion, properties),
			new UTF8Encoding(false));

		string output = Path.Combine(_pluginsDirectory, name);
		await RunAsync(consumer, "restore", project, "--configfile", _feed.NuGetConfiguration, "--packages", _feed.PackageCache);
		await RunAsync(consumer, "build", project, "--configuration", "Release", "--no-restore", "-p:UseSharedCompilation=false",
			$"-p:CheatEnginePluginOutputPath={output}");
		return Bundle(name, output, assemblyName);
	}

	private static PluginBundle Bundle(string name, string output, string assemblyName)
	{
		string entry = Path.Combine(output, assemblyName + ".dll");
		string bridge = Path.Combine(output, PackagedClientFeedFixture.BridgeFileName);
		Assert.True(File.Exists(entry), $"The {name} bundle has no '{Path.GetFileName(entry)}'.");
		Assert.True(File.Exists(bridge), $"The {name} bundle has no '{PackagedClientFeedFixture.BridgeFileName}'.");
		return new PluginBundle(name, output, entry, CheatEngineInstallation.Sha256(bridge).ToLowerInvariant());
	}

	private Task RunAsync(string workingDirectory, params string[] arguments)
	{
		return RunAsync(workingDirectory, new Dictionary<string, string>(StringComparer.Ordinal), arguments);
	}

	private async Task RunAsync(string workingDirectory, IReadOnlyDictionary<string, string> overrides, params string[] arguments)
	{
		DotNetProcessResult result = await _feed.RunAsync(workingDirectory, overrides, arguments);
		Assert.True(result.ExitCode == 0, result.ToString());
	}

	[GeneratedRegex("""\[LuaFunction\("(?<name>[A-Za-z_][A-Za-z0-9_]*)"\)\]""", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex LuaFunctionName();
}
