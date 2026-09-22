using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml.Linq;

using CheatEngine.Client.Tests.Infrastructure;

namespace CheatEngine.Client.Tests.Packaging;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PackageConsumptionSmokeSerialGroup
{
	public const string Name = "Package consumption smoke";
}

/// <summary>Exercises the packages that plugin authors consume, outside the repository's project graph.</summary>
[Collection(PackageConsumptionSmokeSerialGroup.Name)]
public sealed class PackageConsumptionSmokeTests
{
	private const string PackageSourceEnvironmentVariable = "CHEATENGINE_CLIENT_PACKAGE_SOURCE";
	private const string ClientPackageId = "CheatEngine.Client";
	private const string HostingPackageId = "CheatEngine.Client.Hosting";
	private const string TemplatePackageId = "CheatEngine.Client.Templates";

	private const string ConsumerSource = """
	                                      using CheatEngine.Client;
	                                      using CheatEngine.Client.Hosting;
	                                      using CheatEngine.Client.Memory;
	                                      using CheatEngine.Client.Scanning;
	                                      using CheatEngine.SDK.Annotations.Plugin;
	                                      using CheatEngine.SDK.Engine.Values;

	                                      [CheatEnginePlugin("Package smoke plugin")]
	                                      public sealed class Plugin : CheatEngineClientPlugin
	                                      {
	                                          protected override void Configure(CheatEnginePluginBuilder builder)
	                                          {
	                                          }

	                                          protected override void OnClientEnabled(ICheatEngineClient client)
	                                          {
	                                              _ = client.Memory.At(default(Address));
	                                              _ = client.Patterns.Aob("00").FirstOrNone();
	                                          }
	                                      }
	                                      """;

	[Fact]
	public async Task PackagedClientAndTemplateCanBeInstalledInstantiatedAndBuiltInIsolatedDirectories()
	{
		using TemporaryDirectory temporary = new("PackageConsumptionSmoke");
		string packageSource = await ResolvePackageSourceAsync(temporary);

		PackageArchive clientPackage = FindPackage(packageSource, ClientPackageId);
		PackageArchive hostingPackage = FindPackage(packageSource, HostingPackageId);
		PackageArchive templatePackage = FindPackage(packageSource, TemplatePackageId);
		AssertArchiveContains(hostingPackage.Path,
			"analyzers/dotnet/cs/CheatEngine.Client.SourceGenerators.Lua.dll",
			"buildTransitive/CheatEngine.Client.Hosting.props",
			"buildTransitive/CheatEngine.Client.Hosting.targets");
		AssertArchiveContains(templatePackage.Path,
			"content/CheatEngine.Plugin/.template.config/template.json",
			"content/CheatEngine.Plugin/CheatEngine.Plugin.csproj",
			"content/CheatEngine.Plugin/Modules/PluginClientModule.cs",
			"content/CheatEngine.Plugin/Modules/PluginLuaModule.cs");

		string nuGetConfiguration = WriteNuGetConfiguration(temporary, packageSource);
		await BuildIsolatedPackageConsumerAsync(temporary, nuGetConfiguration, clientPackage.Version);
		await InstallInstantiateAndBuildTemplateAsync(temporary, nuGetConfiguration, templatePackage.Path);
	}

	[Fact]
	public async Task PackagedClientPluginWithoutDirectSdkReferenceReportsCECLIENT001()
	{
		using TemporaryDirectory temporary = new("PackageConsumptionSmoke");
		string packageSource = await ResolvePackageSourceAsync(temporary);

		PackageArchive clientPackage = FindPackage(packageSource, ClientPackageId);
		string nuGetConfiguration = WriteNuGetConfiguration(temporary, packageSource);
		string consumerDirectory = temporary.CreateDirectory("missing-sdk-package-consumer");
		string projectPath = Path.Combine(consumerDirectory, "MissingSdk.Plugin.csproj");
		await File.WriteAllTextAsync(projectPath,
			CreateConsumerProject(clientPackage.Version, false), new UTF8Encoding(false),
			TestContext.Current.CancellationToken);

		await AssertDotNetSuccessAsync(consumerDirectory, "restore", projectPath, "--configfile", nuGetConfiguration);

		DotNetProcessResult buildResult = await DotNetProcess.RunAsync(consumerDirectory,
			"build", projectPath, "--configuration", "Release", "--no-restore");
		Assert.True(buildResult.ExitCode != 0, buildResult.ToString());
		Assert.Contains("CECLIENT001", buildResult.StandardOutput + buildResult.StandardError,
			StringComparison.Ordinal);
	}

	private static async Task BuildIsolatedPackageConsumerAsync(TemporaryDirectory temporary, string nuGetConfiguration,
		string clientVersion)
	{
		string consumerDirectory = temporary.CreateDirectory("package-consumer");
		string projectPath = Path.Combine(consumerDirectory, "Smoke.Plugin.csproj");
		await File.WriteAllTextAsync(projectPath, CreateConsumerProject(clientVersion), new UTF8Encoding(false));
		await File.WriteAllTextAsync(Path.Combine(consumerDirectory, "Plugin.cs"), ConsumerSource,
			new UTF8Encoding(false));

		await AssertDotNetSuccessAsync(consumerDirectory, "restore", projectPath, "--configfile", nuGetConfiguration);

		string deploymentDirectory = temporary.CreateDirectory("deployment");
		await AssertDotNetSuccessAsync(consumerDirectory, "build", projectPath, "--configuration", "Release",
			"--no-restore",
			$"-p:CheatEnginePluginOutputPath={deploymentDirectory}");
		await AssertDotNetSuccessAsync(consumerDirectory, "build", projectPath, "--configuration", "Release",
			"--no-restore",
			$"-p:CheatEnginePluginOutputPath={deploymentDirectory}");

		string outputDirectory = Path.Combine(consumerDirectory, "bin", "Release", "net10.0");
		string[] requiredAssets =
		[
			"Smoke.Plugin.dll",
			"Smoke.Plugin.deps.json",
			"Smoke.Plugin.runtimeconfig.json",
			"cheatengine-sdk-lua-bridge.dll",
			"CheatEngine.SDK.dll",
			"CheatEngine.Client.Abstractions.dll",
			"CheatEngine.Client.Core.dll",
			"CheatEngine.Client.Fluent.dll",
			"CheatEngine.Client.Extensions.DependencyInjection.dll",
			"CheatEngine.Client.Hosting.dll"
		];
		Assert.All(requiredAssets, asset =>
		{
			Assert.True(File.Exists(Path.Combine(outputDirectory, asset)),
				$"Isolated package-consumer output is missing '{asset}'.");
			Assert.True(File.Exists(Path.Combine(deploymentDirectory, asset)),
				$"Isolated plugin deployment is missing '{asset}'.");
		});

		string[] generatedEntryPoints = Directory.GetFiles(Path.Combine(consumerDirectory, "obj"),
			"CheatEngine.SDK.EntryPoint.g.cs", SearchOption.AllDirectories);
		string generatedEntryPoint = Assert.Single(generatedEntryPoints);
		string generatedEntryPointText = await File.ReadAllTextAsync(generatedEntryPoint);
		Assert.Contains("namespace CESDK", generatedEntryPointText, StringComparison.Ordinal);
		Assert.Contains("CEPluginInitialize", generatedEntryPointText, StringComparison.Ordinal);
	}

	private static async Task InstallInstantiateAndBuildTemplateAsync(TemporaryDirectory temporary,
		string nuGetConfiguration,
		string templatePackage)
	{
		string templateHome = temporary.CreateDirectory("template-home");
		// A fresh DOTNET_CLI_HOME triggers the CLI first-run experience, which by default appends that home's
		// .dotnet\tools folder to the user's persistent PATH. Disable every first-run side effect so the smoke test
		// leaves no trace outside its temporary directory.
		IReadOnlyDictionary<string, string> environment = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["DOTNET_CLI_HOME"] = Path.Combine(templateHome, ".dotnet-cli"),
			["DOTNET_NEW_HOME"] = Path.Combine(templateHome, ".template-engine"),
			["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "false",
			["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false",
			["DOTNET_NOLOGO"] = "true",
			["DOTNET_CLI_TELEMETRY_OPTOUT"] = "true"
		};

		await AssertDotNetSuccessAsync(templateHome, environment, "new", "install", templatePackage, "--force");
		await AssertDotNetSuccessAsync(templateHome, environment, "new", "ceplugin", "--dry-run", "--name",
			"Smoke.Plugin",
			"--output", Path.Combine(templateHome, "dry-run"));

		string instantiatedDirectory = Path.Combine(templateHome, "Smoke.Plugin");
		await AssertDotNetSuccessAsync(templateHome, environment, "new", "ceplugin", "--name", "Smoke.Plugin",
			"--output",
			instantiatedDirectory);

		string projectPath = Path.Combine(instantiatedDirectory, "Smoke.Plugin.csproj");
		Assert.True(File.Exists(projectPath), "Template instantiation did not produce the expected plugin project.");
		await AssertDotNetSuccessAsync(instantiatedDirectory, environment, "restore", projectPath, "--configfile",
			nuGetConfiguration);
		await AssertDotNetSuccessAsync(instantiatedDirectory, environment, "build", projectPath, "--configuration",
			"Release",
			"--no-restore");
	}

	private static async Task AssertDotNetSuccessAsync(string workingDirectory, params string[] arguments)
	{
		await AssertDotNetSuccessAsync(workingDirectory, new Dictionary<string, string>(StringComparer.Ordinal),
			arguments);
	}

	private static async Task AssertDotNetSuccessAsync(string workingDirectory,
		IReadOnlyDictionary<string, string> environment,
		params string[] arguments)
	{
		DotNetProcessResult result = await DotNetProcess.RunAsync(workingDirectory, environment, arguments);
		Assert.True(result.ExitCode == 0, result.ToString());
	}

	private static async Task<string> ResolvePackageSourceAsync(TemporaryDirectory temporary)
	{
		string? configuredPackageSource = Environment.GetEnvironmentVariable(PackageSourceEnvironmentVariable);
		if (configuredPackageSource is not null)
		{
			Assert.False(string.IsNullOrWhiteSpace(configuredPackageSource),
				$"{PackageSourceEnvironmentVariable} is set but empty. " +
				"It must be an absolute directory containing prebuilt .nupkg files.");
			Assert.True(Path.IsPathFullyQualified(configuredPackageSource),
				$"{PackageSourceEnvironmentVariable} must be an absolute directory path, but was '{configuredPackageSource}'.");
			Assert.True(Directory.Exists(configuredPackageSource),
				$"{PackageSourceEnvironmentVariable} points to a missing directory: '{configuredPackageSource}'.");
			return configuredPackageSource;
		}

		string repositoryRoot = FindRepositoryRoot();
		string packageSource = temporary.CreateDirectory("packages");
		await AssertDotNetSuccessAsync(repositoryRoot,
			"pack", Path.Combine(repositoryRoot, "CheatEngine.Client.slnx"), "--configuration", "Release",
			"--output", packageSource);
		return packageSource;
	}

	private static PackageArchive FindPackage(string packageSource, string packageId)
	{
		PackageArchive[] packages = Directory.GetFiles(packageSource, "*.nupkg")
			.Select(ReadPackageArchive)
			.ToArray();
		return Assert.Single(packages, package => package.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase));
	}

	private static PackageArchive ReadPackageArchive(string packagePath)
	{
		using ZipArchive archive = ZipFile.OpenRead(packagePath);
		ZipArchiveEntry nuspec = Assert.Single(archive.Entries,
			static entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
		using Stream stream = nuspec.Open();
		XDocument document = XDocument.Load(stream);
		XNamespace packageNamespace = document.Root!.Name.Namespace;
		XElement metadata = document.Root.Element(packageNamespace + "metadata")
		                    ?? throw new InvalidOperationException(
			                    $"Package '{packagePath}' does not declare metadata.");
		string id = metadata.Element(packageNamespace + "id")?.Value
		            ?? throw new InvalidOperationException($"Package '{packagePath}' does not declare an id.");
		string version = metadata.Element(packageNamespace + "version")?.Value
		                 ?? throw new InvalidOperationException($"Package '{packagePath}' does not declare a version.");
		return new PackageArchive(id, packagePath, version);
	}

	private static void AssertArchiveContains(string packagePath, params string[] expectedEntries)
	{
		using ZipArchive archive = ZipFile.OpenRead(packagePath);
		HashSet<string> entries = archive.Entries.Select(static entry => entry.FullName)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		Assert.All(expectedEntries, entry => Assert.Contains(entry, entries, StringComparer.OrdinalIgnoreCase));
	}

	private static string WriteNuGetConfiguration(TemporaryDirectory temporary, string packageSource)
	{
		string packageCache = temporary.CreateDirectory("packages-cache");
		string path = Path.Combine(temporary.Path, "NuGet.Config");
		string configuration = $"""
		                        <?xml version="1.0" encoding="utf-8"?>
		                        <configuration>
		                          <config>
		                            <add key="globalPackagesFolder" value="{EscapeXml(packageCache)}" />
		                          </config>
		                          <packageSources>
		                            <clear />
		                            <add key="local-client-packages" value="{EscapeXml(packageSource)}" />
		                            <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
		                          </packageSources>
		                        </configuration>
		                        """;
		File.WriteAllText(path, configuration, new UTF8Encoding(false));
		return path;
	}

	private static string EscapeXml(string value)
	{
		return SecurityElement.Escape(value) ??
		       throw new InvalidOperationException("Could not escape NuGet configuration.");
	}

	private static string FindRepositoryRoot()
	{
		for (DirectoryInfo? candidate = new(AppContext.BaseDirectory);
		     candidate is not null;
		     candidate = candidate.Parent)
		{
			if (File.Exists(Path.Combine(candidate.FullName, "CheatEngine.Client.slnx")))
			{
				return candidate.FullName;
			}
		}

		throw new DirectoryNotFoundException(
			"Could not find the CheatEngine.Client repository root from the test output.");
	}

	private static string CreateConsumerProject(string clientVersion, bool hasDirectSdkPackageReference = true)
	{
		string sdkPackageReference = hasDirectSdkPackageReference
			? "    <PackageReference Include=\"CheatEngine.SDK\" Version=\"1.0.0\" />"
			: string.Empty;

		return $$"""
		         <Project Sdk="Microsoft.NET.Sdk">
		           <PropertyGroup>
		             <TargetFramework>net10.0</TargetFramework>
		             <LangVersion>14.0</LangVersion>
		             <Nullable>enable</Nullable>
		             <ImplicitUsings>enable</ImplicitUsings>
		             <PlatformTarget>x64</PlatformTarget>
		             <CheatEngineClientPluginProject>true</CheatEngineClientPluginProject>
		             <RestorePackagesWithLockFile>false</RestorePackagesWithLockFile>
		             <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
		             <CompilerGeneratedFilesOutputPath>obj/Generated</CompilerGeneratedFilesOutputPath>
		           </PropertyGroup>
		           <ItemGroup>
		             <PackageReference Include="CheatEngine.Client" Version="{{clientVersion}}" />
		         {{sdkPackageReference}}
		           </ItemGroup>
		         </Project>
		         """;
	}

	private sealed record PackageArchive(string Id, string Path, string Version);
}
