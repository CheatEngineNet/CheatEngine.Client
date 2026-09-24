using System.Runtime.Versioning;
using System.Text;

using CheatEngine.Client.Tests.Infrastructure;
using CheatEngine.Client.Tests.Packaging;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>A plugin deployment folder, built from the packed packages, that Cheat Engine loads with <c>loadPlugin</c>.</summary>
/// <param name="Name">The bundle name, also its folder name below <c>&lt;run&gt;/plugins</c>.</param>
/// <param name="Directory">The deployment folder (the complete closure the Hosting targets produce).</param>
/// <param name="EntryAssemblyPath">The plugin assembly to load.</param>
/// <param name="BridgeSha256">The lower-case SHA-256 of the deployed native bridge.</param>
internal sealed record PluginBundle(string Name, string Directory, string EntryAssemblyPath, string BridgeSha256);

/// <summary>
///     Builds plugin bundles the way a plugin author does (Q40): the sources are copied into an isolated consumer outside
///     any repository, which references the packed <c>CheatEngine.Client</c> from the tested package directory and
///     CheatEngine.SDK from nuget.org (<see cref="PackagedClientFeedFixture" />), and the Hosting deployment target writes
///     the complete closure to <c>&lt;run&gt;/plugins/&lt;name&gt;</c>. The workspace build is never loaded.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class PluginBundleBuilder
{
	/// <summary>The bundle name of the qualification harness.</summary>
	internal const string HarnessName = "harness";

	/// <summary>The assembly name of the qualification harness, as its repository project declares it.</summary>
	internal const string HarnessAssemblyName = "CheatEngine.Client.LivePlugin.Qualification";

	/// <summary>The repository folder of the harness sources.</summary>
	internal const string HarnessSourceFolder = "tests/CheatEngine.Client.LivePlugin.Qualification";

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

	/// <summary>Builds the qualification harness from the packed packages.</summary>
	internal async Task<PluginBundle> BuildHarnessAsync()
	{
		_feed.RequirePackages();
		string consumer = _feed.CreateDirectory("bundle-" + HarnessName);
		PackagedClientFeedFixture.AssertOutsideAnyRepository(consumer);
		foreach (string source in HarnessSources(RepositoryLayout.Root))
		{
			string relative = Path.GetRelativePath(RepositoryLayout.Combine(HarnessSourceFolder), source);
			string copy = Path.Combine(consumer, relative);
			Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
			File.Copy(source, copy);
		}

		string project = Path.Combine(consumer, HarnessAssemblyName + ".csproj");
		string properties = $"""
		                         <AssemblyName>{HarnessAssemblyName}</AssemblyName>
		                         <RootNamespace>LivePlugin.Qualification</RootNamespace>
		                         <EnableDynamicLoading>true</EnableDynamicLoading>
		                         <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
		                     """;
		await File.WriteAllTextAsync(project,
			PackagedClientFeedFixture.CreateConsumerProject(_feed.ClientVersion, _feed.SdkVersion, properties),
			new UTF8Encoding(false));

		string output = Path.Combine(_pluginsDirectory, HarnessName);
		await RunAsync(consumer, "restore", project, "--configfile", _feed.NuGetConfiguration, "--packages", _feed.PackageCache);
		await RunAsync(consumer, "build", project, "--configuration", "Release", "--no-restore", "-p:UseSharedCompilation=false",
			$"-p:CheatEnginePluginOutputPath={output}");

		string entry = Path.Combine(output, HarnessAssemblyName + ".dll");
		string bridge = Path.Combine(output, PackagedClientFeedFixture.BridgeFileName);
		Assert.True(File.Exists(entry), $"The harness bundle has no '{Path.GetFileName(entry)}'.");
		Assert.True(File.Exists(bridge), $"The harness bundle has no '{PackagedClientFeedFixture.BridgeFileName}'.");
		return new PluginBundle(HarnessName, output, entry, CheatEngineInstallation.Sha256(bridge).ToLowerInvariant());
	}

	private async Task RunAsync(string workingDirectory, params string[] arguments)
	{
		DotNetProcessResult result = await _feed.RunAsync(workingDirectory, arguments);
		Assert.True(result.ExitCode == 0, result.ToString());
	}
}
