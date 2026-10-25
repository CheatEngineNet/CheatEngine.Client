using System.Text.Json.Nodes;
using System.Xml.Linq;

using CheatEngine.Client.Tests.Infrastructure;

namespace CheatEngine.Client.Tests.Packaging;

/// <summary>
/// Verifies the evaluated build graph used by normal Client development. These checks do not restore or compile: they
/// prove the source-submodule and production-package modes remain intentionally separate.
/// </summary>
public sealed class SdkSourceBuildTests
{
	private const string HostingProject = "libs/CheatEngine.Client.Hosting/CheatEngine.Client.Hosting.csproj";

	[Theory]
	[InlineData("Debug")]
	[InlineData("Release")]
	public async Task TransitiveSdkReferencesKeepTheSameBuildConfigurationAsync(string configuration)
	{
		using TemporaryDirectory directory = new("sdk-transitive-configuration");
		string project = Path.Combine(directory.Path, "Probe.proj");
		XDocument document = new(new XElement("Project",
			new XElement("PropertyGroup", new XElement("CheatEngineSdkSourceMode", "true"),
				new XElement("Configuration", configuration)),
			new XElement("Import", new XAttribute("Project", RepositoryLayout.Combine("Directory.Build.targets"))),
			new XElement("Target", new XAttribute("Name", "IncludeTransitiveProjectReferences"),
				new XElement("ItemGroup",
					new XElement("ProjectReference", new XAttribute("Include", "CheatEngine.SDK.Lua.Interop.csproj"),
						new XElement("AdditionalProperties", "Existing=preserved")),
					new XElement("ProjectReference", new XAttribute("Include", "Other.csproj"))))));
		document.Save(project);

		DotNetProcessResult result = await DotNetProcess.RunAsync(RepositoryLayout.Root,
			"msbuild", project, "-target:IncludeTransitiveProjectReferences", "-getItem:ProjectReference",
			"-nologo", "-nodeReuse:false", "-verbosity:quiet");

		Assert.True(result.ExitCode == 0, result.ToString());
		JsonArray references = JsonNode.Parse(result.StandardOutput)!["Items"]!["ProjectReference"]!.AsArray();
		Assert.Equal(2, references.Count);
		JsonNode sdk = references[0]!;
		Assert.Equal("Platform=AnyCPU", sdk["SetPlatform"]!.GetValue<string>());
		Assert.Equal($"Configuration={configuration}", sdk["SetConfiguration"]!.GetValue<string>());
		Assert.Equal("Existing=preserved;ShouldUnsetParentConfigurationAndPlatform=false",
			sdk["AdditionalProperties"]!.GetValue<string>());
		Assert.Null(references[1]!["AdditionalProperties"]);
	}

	[Fact]
	public async Task StandaloneBuildUsesPinnedSubmoduleAndSeparateSourceLockAsync()
	{
		JsonObject evaluation = await EvaluateHostingAsync("-p:CheatEngineImportSourceSettings=false");

		Assert.Equal("true", Property(evaluation, "CheatEngineSdkSourceMode"));
		Assert.Equal(RepositoryLayout.Combine("externals/CheatEngine.SDK"),
			Path.TrimEndingDirectorySeparator(Path.GetFullPath(Property(evaluation, "CheatEngineSdkSourceRoot"))));
		Assert.Equal(RepositoryLayout.Combine("eng/locks/source/CheatEngine.Client.Hosting.lock.json"),
			Path.GetFullPath(Property(evaluation, "NuGetLockFilePath")));
		AssertHasProjectReference(evaluation, "CheatEngine.SDK.Engine");
		AssertHasProjectReference(evaluation, "CheatEngine.SDK.Hosting");
		Assert.DoesNotContain("CheatEngine.SDK", PackageReferences(evaluation), StringComparer.OrdinalIgnoreCase);
		// Equal effective defaults with different global properties schedule duplicate SDK nodes and race on outputs.
		Assert.All(evaluation["Items"]!["ProjectReference"]!.AsArray(), reference =>
			Assert.Equal("Platform=AnyCPU", reference!["SetPlatform"]!.GetValue<string>()));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task SourcePackagingIsRefusedWhileProductionPackagingRemainsAvailableAsync(bool sourceMode)
	{
		DotNetProcessResult result = await DotNetProcess.RunAsync(RepositoryLayout.Root,
			"msbuild", RepositoryLayout.Combine(HostingProject), "-t:CheatEngineClientRefuseSourceDependencyPackage",
			"-nologo", "-nodeReuse:false", "-verbosity:minimal", "-p:CheatEngineImportSourceSettings=false",
			$"-p:CheatEngineSdkSourceMode={sourceMode.ToString().ToLowerInvariant()}");

		if (sourceMode)
		{
			Assert.NotEqual(0, result.ExitCode);
			Assert.Contains("error CHEATENGINECLIENT9051", result.StandardOutput, StringComparison.Ordinal);
		}
		else
		{
			Assert.True(result.ExitCode == 0, result.ToString());
		}
	}

	[Fact]
	public async Task ExplicitSdkSourceRootOverridesThePinnedSubmoduleAsync()
	{
		using TemporaryDirectory directory = new("sdk-source-root-override");
		JsonObject evaluation = await EvaluateHostingAsync("-p:CheatEngineImportSourceSettings=false",
			$"-p:CheatEngineSdkSourceRoot={directory.Path}");

		Assert.Equal("true", Property(evaluation, "CheatEngineSdkSourceMode"));
		Assert.Equal(directory.Path, Path.TrimEndingDirectorySeparator(Property(evaluation, "CheatEngineSdkSourceRoot")));
		AssertHasProjectReference(evaluation, "CheatEngine.SDK.Engine");
		AssertHasProjectReference(evaluation, "CheatEngine.SDK.Hosting");
	}

	[Fact]
	public async Task ProductionPackageModeUsesSdkPackageAndCommittedLockLocationAsync()
	{
		JsonObject evaluation = await EvaluateHostingAsync("-p:CheatEngineImportSourceSettings=false",
			"-p:CheatEngineSdkSourceMode=false");

		Assert.Equal("false", Property(evaluation, "CheatEngineSdkSourceMode"));
		Assert.Equal(string.Empty, Property(evaluation, "NuGetLockFilePath"));
		Assert.DoesNotContain(ProjectReferences(evaluation), reference =>
			Path.GetFileNameWithoutExtension(reference) is "CheatEngine.SDK.Engine" or "CheatEngine.SDK.Hosting");
		Assert.Contains("CheatEngine.SDK", PackageReferences(evaluation), StringComparer.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task MissingSdkSubmoduleReportsRecoveryCommandAsync()
	{
		using TemporaryDirectory directory = new("sdk-source-root-missing");
		DotNetProcessResult result = await DotNetProcess.RunAsync(RepositoryLayout.Root,
			"msbuild", RepositoryLayout.Combine(HostingProject), "-t:CheatEngineClientValidateSdkSourceRoot", "-nologo",
			"-nodeReuse:false", "-verbosity:minimal", "-p:CheatEngineImportSourceSettings=false", "-p:CheatEngineSdkSourceMode=true",
			$"-p:CheatEngineSdkSourceRoot={directory.Path}");

		Assert.True(result.ExitCode != 0, result.ToString());
		Assert.Contains("error CHEATENGINECLIENT9051", result.StandardOutput, StringComparison.Ordinal);
		Assert.Contains("git submodule update --init --recursive", result.StandardOutput, StringComparison.Ordinal);
	}

	[Fact]
	public void SourceLocksDoNotResolveCheatEngineSdkFromNuGet()
	{
		string sourceLockDirectory = RepositoryLayout.Combine("eng/locks/source");
		string[] lockFiles = Directory.GetFiles(sourceLockDirectory, "*.lock.json");

		Assert.NotEmpty(lockFiles);
		foreach (string lockFile in lockFiles)
		{
			JsonObject lockFileRoot = Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllText(lockFile)));
			JsonObject frameworks = Assert.IsType<JsonObject>(lockFileRoot["dependencies"]);
			foreach (JsonNode? frameworkDependencies in frameworks.Select(entry => entry.Value))
			{
				JsonObject dependencies = Assert.IsType<JsonObject>(frameworkDependencies);
				Assert.DoesNotContain(dependencies, dependency =>
					string.Equals(dependency.Key, "CheatEngine.SDK", StringComparison.OrdinalIgnoreCase));
			}
		}
	}

	private static async Task<JsonObject> EvaluateHostingAsync(params string[] properties)
	{
		List<string> arguments =
		[
			"msbuild", RepositoryLayout.Combine(HostingProject),
			"-getProperty:CheatEngineSdkSourceMode,CheatEngineSdkSourceRoot,NuGetLockFilePath",
			"-getItem:ProjectReference,PackageReference", "-nologo", "-nodeReuse:false", "-verbosity:quiet"
		];
		arguments.AddRange(properties);
		// Exercise defaults independently of the production workflow's environment-level override.
		DotNetProcessResult result = await DotNetProcess.RunAsync(RepositoryLayout.Root,
			new Dictionary<string, string>(StringComparer.Ordinal) { ["CheatEngineSdkSourceMode"] = string.Empty }, [.. arguments]);
		Assert.True(result.ExitCode == 0, result.ToString());

		return Assert.IsType<JsonObject>(JsonNode.Parse(result.StandardOutput));
	}

	private static string Property(JsonObject evaluation, string name)
	{
		return evaluation["Properties"]?[name]?.GetValue<string>() ?? string.Empty;
	}

	private static string[] ProjectReferences(JsonObject evaluation)
	{
		return evaluation["Items"]?["ProjectReference"]?.AsArray()
			.Select(item => item?["FullPath"]?.GetValue<string>() ?? string.Empty)
			.ToArray() ?? [];
	}

	private static string[] PackageReferences(JsonObject evaluation)
	{
		return Items(evaluation, "PackageReference");
	}

	private static string[] Items(JsonObject evaluation, string name)
	{
		return evaluation["Items"]?[name]?.AsArray()
			.Select(item => item?["Identity"]?.GetValue<string>() ?? string.Empty)
			.ToArray() ?? [];
	}

	private static void AssertHasProjectReference(JsonObject evaluation, string expectedProjectName)
	{
		Assert.Contains(ProjectReferences(evaluation), reference =>
			string.Equals(Path.GetFileNameWithoutExtension(reference), expectedProjectName, StringComparison.Ordinal));
	}
}
