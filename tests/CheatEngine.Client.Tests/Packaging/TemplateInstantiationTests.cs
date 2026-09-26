using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using CheatEngine.Client.Tests.Infrastructure;

namespace CheatEngine.Client.Tests.Packaging;

/// <summary>
///     Proves what <c>dotnet new ceplugin</c> generates from the packed template, beyond the smoke build of
///     <see cref="PackageConsumptionSmokeTests" />: names derived from the project name, a lock file restore,
///     <c>--no-restore</c>, the name folder, a packed <c>.gitignore</c>, and a build under this repository's code style
///     with warnings as errors (plan L22). Every instance lives in the fixture's temporary directory, outside any
///     repository, and uses the fixture's template installation, NuGet configuration and package folder.
/// </summary>
[Collection(PackageConsumptionSmokeSerialGroup.Name)]
[Trait("Category", "PackageConsumption")]
[Trait("Qualification", "Q40")]
public sealed partial class TemplateInstantiationTests(PackagedClientFeedFixture fixture)
{
	private const string ContentFolder = "content/CheatEngine.Plugin/";
	private const string LockFileName = "packages.lock.json";
	private const int RegexTimeoutMilliseconds = 1000;

	[Fact]
	public void PackedTemplateCarriesItsGitIgnoreAndNoLockFile()
	{
		PackageArchive template = fixture.Package(PackagedClientFeedFixture.TemplatePackageId);
		string[] rules = template.EntryText(ContentFolder + ".gitignore")
			.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Where(static line => !line.StartsWith('#'))
			.ToArray();
		XDocument project = XDocument.Parse(template.EntryText(ContentFolder + "CheatEngine.Plugin.csproj"));

		Assert.Contains("bin/", rules);
		Assert.Contains("obj/", rules);
		Assert.DoesNotContain(rules, static rule => rule.Contains(".json", StringComparison.OrdinalIgnoreCase));
		// LockFileTests keeps the committed content folder free of a lock file; this checks the package itself, which
		// may be packed from a working tree that holds one restored in place.
		Assert.DoesNotContain(template.EntryNames,
			static entry => entry.EndsWith(LockFileName, StringComparison.Ordinal));
		Assert.Equal("true", Assert.Single(project.Descendants("RestorePackagesWithLockFile")).Value.Trim());
	}

	[Fact]
	public void InstantiatedTemplateLockFileRecordsThePinnedSdk()
	{
		fixture.RequireTemplate();
		string lockPath = Path.Combine(fixture.TemplateDirectory, LockFileName);
		using JsonDocument lockFile = JsonDocument.Parse(File.ReadAllText(lockPath));
		JsonElement packages = lockFile.RootElement.GetProperty("dependencies").GetProperty("net10.0");
		JsonElement sdk = packages.GetProperty(PackagedClientFeedFixture.SdkPackageId);
		JsonElement client = packages.GetProperty(PackagedClientFeedFixture.ClientPackageId);
		// The .NET SDK adds this reference for IsAotCompatible at the version it bundles, so the lock changes with the
		// SDK: both template READMEs tell plugin authors to pin the SDK or regenerate the lock after an update.
		JsonElement illink = packages.GetProperty("Microsoft.NET.ILLink.Tasks");
		string sdkFolder = Path.Combine(fixture.PackageCache, "cheatengine.sdk", fixture.SdkVersion.ToLowerInvariant());
		using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(sdkFolder, ".nupkg.metadata")));
		string contentHash = sdk.GetProperty("contentHash").GetString()!;

		Assert.Equal("Direct", sdk.GetProperty("type").GetString());
		Assert.Equal(fixture.SdkVersion, sdk.GetProperty("resolved").GetString());
		Assert.Equal(metadata.RootElement.GetProperty("contentHash").GetString(), contentHash);
		Assert.Equal("Direct", client.GetProperty("type").GetString());
		Assert.Equal(fixture.ClientVersion, client.GetProperty("resolved").GetString());
		Assert.Equal("Direct", illink.GetProperty("type").GetString());
		if (fixture.UsesPinnedSdk)
		{
			Assert.Equal(fixture.ConsumedSdk.GetProperty("contentHashSha512").GetString(), contentHash);
		}

		PackagedClientFeedFixture.Evidence(nameof(InstantiatedTemplateLockFileRecordsThePinnedSdk),
			$"sdk={fixture.SdkVersion} lockContentHash={contentHash} client={fixture.ClientVersion} " +
			$"illinkTasks={illink.GetProperty("resolved").GetString()}");
	}

	[Fact]
	public async Task TemplateDerivesDistinctPluginNamesAndLuaGlobalsFromTheProjectNameAsync()
	{
		fixture.RequireTemplate();
		(string Project, string DisplayName, string LuaGlobal)[] expected =
		[
			(PackagedClientFeedFixture.ConsumerName, "Smoke.Plugin", "smoke_plugin_status"),
			("QualTemplatePlugin", "QualTemplatePlugin", "qual_template_plugin_status"),
			("Contoso.CheatEngine.Plugin", "Contoso.CheatEngine.Plugin", "contoso_cheat_engine_plugin_status"),
			("My-Plugin2", "My-Plugin2", "my_plugin2_status"),
			("1Plugin", "1Plugin", "plugin_1_plugin_status"),
			("Überwachung.Plugin", "_berwachung.Plugin", "berwachung_plugin_status")
		];
		string root = fixture.CreateDirectory("template-names");
		List<string> globals = [];
		foreach ((string project, string displayName, string luaGlobal) in expected)
		{
			string directory = project == PackagedClientFeedFixture.ConsumerName
				? fixture.TemplateDirectory
				: await InstantiateAsync(root, project, "--no-restore");
			(string derivedDisplayName, string global) = await ReadDerivedNamesAsync(directory);

			Assert.Equal(displayName, derivedDisplayName);
			Assert.Equal(luaGlobal, global);
			Assert.Matches(LuaStatusGlobal(), global);
			globals.Add(global);
		}

		Assert.Equal(globals.Count, globals.Distinct(StringComparer.Ordinal).Count());
		PackagedClientFeedFixture.Evidence(
			nameof(TemplateDerivesDistinctPluginNamesAndLuaGlobalsFromTheProjectNameAsync), string.Join(' ', globals));
	}

	/// <summary>
	///     The derivation is lossy, as both template READMEs state: it lowercases the name and turns each camel-case
	///     boundary and each run of other characters, non-ASCII letters included, into <c>_</c>, so different project
	///     names can derive the same Lua global, which the Client then lets only one plugin register.
	/// </summary>
	[Fact]
	public async Task DifferentProjectNamesCanDeriveTheSameLuaGlobalAsync()
	{
		fixture.RequireTemplate();
		(string Project, string DisplayName, string LuaGlobal)[] expected =
		[
			("MyPlugin", "MyPlugin", "my_plugin_status"),
			("My.Plugin", "My.Plugin", "my_plugin_status"),
			("Плагин", "Plugin", "plugin_status")
		];
		string root = fixture.CreateDirectory("template-name-collisions");
		foreach ((string project, string displayName, string luaGlobal) in expected)
		{
			(string derivedDisplayName, string global) =
				await ReadDerivedNamesAsync(await InstantiateAsync(root, project, "--no-restore"));

			Assert.Equal(displayName, derivedDisplayName);
			Assert.Equal(luaGlobal, global);
		}
	}

	[Fact]
	public async Task TemplateRestoresWithALockFileUnlessNoRestoreIsPassedAsync()
	{
		fixture.RequireTemplate();
		string root = fixture.CreateDirectory("template-restore");

		// The restore post action finds the fixture's NuGet.Config above the temporary directory.
		string restored = await InstantiateAsync(root, "Restored.Plugin");
		string unrestored = await InstantiateAsync(root, "Unrestored.Plugin", "--no-restore");

		Assert.True(File.Exists(Path.Combine(restored, LockFileName)), $"{restored} has no {LockFileName}.");
		Assert.True(File.Exists(Path.Combine(restored, "obj", "project.assets.json")), $"{restored} was not restored.");
		Assert.False(File.Exists(Path.Combine(unrestored, LockFileName)), $"--no-restore wrote {LockFileName}.");
		Assert.False(Directory.Exists(Path.Combine(unrestored, "obj")), "--no-restore restored the project.");
	}

	[Fact]
	public async Task TemplateNameWithoutOutputCreatesTheNameFolderAsync()
	{
		fixture.RequireTemplate();
		string root = fixture.CreateDirectory("template-name-folder");

		DotNetProcessResult created =
			await fixture.RunAsync(root, "new", "ceplugin", "--name", "Named.Plugin", "--no-restore");

		Assert.True(created.ExitCode == 0, created.ToString());
		Assert.True(File.Exists(Path.Combine(root, "Named.Plugin", "Named.Plugin.csproj")), created.ToString());
		Assert.True(File.Exists(Path.Combine(root, "Named.Plugin", ".gitignore")), created.ToString());
		Assert.Empty(Directory.GetFiles(root));
	}

	[Fact]
	public async Task InstantiatedTemplateBuildsWithWarningsAsErrorsUnderTheRepositoryCodeStyleAsync()
	{
		fixture.RequireTemplate();
		string directory =
			await InstantiateAsync(fixture.CreateDirectory("template-strict"), "Strict.Plugin", "--no-restore");
		string project = Path.Combine(directory, "Strict.Plugin.csproj");
		File.Copy(RepositoryLayout.Combine(".editorconfig"), Path.Combine(directory, ".editorconfig"));
		string analysisLevel = Assert.Single(XDocument.Load(RepositoryLayout.Combine("Directory.Build.props"))
			.Descendants("_CheatEngineClientPinnedAnalysisLevel")).Value.Trim();
		string[] strictBuild =
		[
			"build", project, "--configuration", "Release", "--no-restore", "-warnaserror",
			"-p:EnforceCodeStyleInBuild=true", $"-p:AnalysisLevel={analysisLevel}", "-p:UseSharedCompilation=false"
		];

		DotNetProcessResult restore = await fixture.RunAsync(directory, "restore", project, "--configfile",
			fixture.NuGetConfiguration, "--packages", fixture.PackageCache);
		DotNetProcessResult strict = await fixture.RunAsync(directory, strictBuild);

		// The same build refuses a file that breaks the repository's namespace and indentation rules, so the style
		// settings above are in effect rather than silently ignored.
		await File.WriteAllTextAsync(Path.Combine(directory, "StyleProbe.cs"),
			"namespace Strict.Plugin.StyleProbe\r\n{\r\n  internal static class Probe\r\n  {\r\n  }\r\n}\r\n",
			new UTF8Encoding(false), TestContext.Current.CancellationToken);
		DotNetProcessResult refused = await fixture.RunAsync(directory, strictBuild);

		Assert.True(restore.ExitCode == 0, restore.ToString());
		Assert.True(strict.ExitCode == 0, strict.ToString());
		Assert.True(refused.ExitCode != 0, refused.ToString());
		Assert.Contains("error IDE0161", refused.StandardOutput, StringComparison.Ordinal);
		PackagedClientFeedFixture.Evidence(
			nameof(InstantiatedTemplateBuildsWithWarningsAsErrorsUnderTheRepositoryCodeStyleAsync),
			$"analysisLevel={analysisLevel} strict=exit {strict.ExitCode} styleViolation=exit {refused.ExitCode}");
	}

	/// <summary>Instantiates the installed template as <paramref name="name" /> in its folder under a root.</summary>
	private async Task<string> InstantiateAsync(string root, string name, params string[] options)
	{
		string directory = Path.Combine(root, name);
		string[] arguments = ["new", "ceplugin", "--name", name, "--output", directory, .. options];
		DotNetProcessResult created = await fixture.RunAsync(root, arguments);
		Assert.True(created.ExitCode == 0, created.ToString());
		Assert.True(File.Exists(Path.Combine(directory, name + ".csproj")), created.ToString());
		return directory;
	}

	/// <summary>Reads the plugin name and the Lua status global that an instance's sources declare.</summary>
	private static async Task<(string DisplayName, string LuaGlobal)> ReadDerivedNamesAsync(string directory)
	{
		string pluginSource = await File.ReadAllTextAsync(Path.Combine(directory, "Plugin.cs"),
			TestContext.Current.CancellationToken);
		string luaSource = await File.ReadAllTextAsync(Path.Combine(directory, "Modules", "PluginLuaFunctions.cs"),
			TestContext.Current.CancellationToken);
		return (Assert.Single(PluginDisplayName().Matches(pluginSource)).Groups["name"].Value,
			Assert.Single(LuaFunctionName().Matches(luaSource)).Groups["name"].Value);
	}

	[GeneratedRegex("""\[CheatEnginePlugin\("(?<name>[^"]*)"\)\]""", RegexOptions.CultureInvariant,
		RegexTimeoutMilliseconds)]
	private static partial Regex PluginDisplayName();

	[GeneratedRegex("""\[LuaFunction\("(?<name>[^"]*)"\)\]""", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex LuaFunctionName();

	/// <summary>An ASCII Lua name (Lua 5.3, section 3.1) in lower_snake_case that ends with <c>_status</c>.</summary>
	[GeneratedRegex("^[a-z_][a-z0-9_]*_status$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex LuaStatusGlobal();
}
