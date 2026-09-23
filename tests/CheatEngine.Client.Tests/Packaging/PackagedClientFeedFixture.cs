using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

using CheatEngine.Client.Tests.Infrastructure;

namespace CheatEngine.Client.Tests.Packaging;

/// <summary>The serial collection of the package consumption tests, sharing one packed feed and one set of consumers.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PackageConsumptionSmokeSerialGroup : ICollectionFixture<PackagedClientFeedFixture>
{
	/// <summary>The collection name.</summary>
	public const string Name = "Package consumption smoke";
}

/// <summary>One recorded <c>dotnet</c> step of the fixture.</summary>
internal sealed record SmokeStep(string Name, DotNetProcessResult Result)
{
	/// <summary>Whether the step exited with 0.</summary>
	internal bool Succeeded => Result.ExitCode == 0;
}

/// <summary>
/// Resolves (or, locally, packs) the Client package directory once, reads every archive, and restores and builds an
/// isolated package consumer and an instantiated template once, in directories outside any repository, with an
/// isolated NuGet global packages folder and package source mapping. Each fact then asserts on the recorded results, so
/// the collection stays well inside the CI hang-dump inactivity window. A failed step is recorded, not thrown, so the
/// archive facts still report on the packages.
/// </summary>
public sealed class PackagedClientFeedFixture : IAsyncLifetime
{
	/// <summary>The umbrella package.</summary>
	internal const string ClientPackageId = "CheatEngine.Client";

	/// <summary>The Hosting package, which carries the generator and the consumer build targets.</summary>
	internal const string HostingPackageId = "CheatEngine.Client.Hosting";

	/// <summary>The template package.</summary>
	internal const string TemplatePackageId = "CheatEngine.Client.Templates";

	/// <summary>The SDK package.</summary>
	internal const string SdkPackageId = "CheatEngine.SDK";

	/// <summary>The native bridge inside the SDK package and next to a built plugin.</summary>
	internal const string BridgeFileName = "cheatengine-sdk-lua-bridge.dll";

	/// <summary>The consumer project name.</summary>
	internal const string ConsumerName = "Smoke.Plugin";

	/// <summary>Optional: a directory holding exactly one CheatEngine.SDK package to consume instead of nuget.org's pin.</summary>
	internal const string SdkPackageSourceVariable = "CHEATENGINE_SDK_PACKAGE_SOURCE";

	/// <summary>The seven packages, one lockstep version.</summary>
	internal static readonly string[] PackageIds =
	[
		"CheatEngine.Client", "CheatEngine.Client.Abstractions", "CheatEngine.Client.Core",
		"CheatEngine.Client.Extensions.DependencyInjection", "CheatEngine.Client.Fluent", "CheatEngine.Client.Hosting",
		"CheatEngine.Client.Templates"
	];

	/// <summary>The packages with build output, hence with a symbol package (not the facade, not the template package).</summary>
	internal static readonly string[] SymbolPackageIds =
	[
		"CheatEngine.Client.Abstractions", "CheatEngine.Client.Core", "CheatEngine.Client.Extensions.DependencyInjection",
		"CheatEngine.Client.Fluent", "CheatEngine.Client.Hosting"
	];

	/// <summary>The managed closure every plugin output and deployment folder must hold.</summary>
	internal static readonly string[] ClientAssemblies =
	[
		"CheatEngine.SDK.dll", "CheatEngine.Client.Abstractions.dll", "CheatEngine.Client.Core.dll",
		"CheatEngine.Client.Fluent.dll", "CheatEngine.Client.Extensions.DependencyInjection.dll", "CheatEngine.Client.Hosting.dll"
	];

	/// <summary>The plugin source of every consumer project.</summary>
	internal const string ConsumerSource = """
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

	/// <summary>
	/// The reviewed identity of the published CheatEngine.SDK 1.0.0 (shared-contracts.md §2.4, verified 2026-09-23):
	/// the NuGet content hash a lock file records, the nuget.org repository-signed file's SHA-512, and the SHA-256 of
	/// the native bridge packaged inside it. Hardcoded, not read from a reviewed-identity file: the sync mechanism
	/// that used to keep such a file current was removed, and the Client's SDK pin (eng/CheatEngineSdk.props) does not
	/// move without updating these literals in the same change.
	/// </summary>
	private static readonly JsonDocument _pinnedSdkIdentity = JsonDocument.Parse("""
		{
		  "version": "1.0.0",
		  "contentHashSha512": "n7nHqZ8vzo7Vf20jF0fkh/jUtR3yo1TwRGpXE7ERxZeJ4C5S/Nsft4lqOg7zGwfsD5Nh9tTVgdw4PrybJRF0gA==",
		  "nugetOrgSignedSha512": "1a2B/E6reX5e636hfdb+Zdj3kT6817DuNES1RWvprhRyuyztE/56Zk2iHOMQIKpGH+O2Va8rYJxXXXTVq5aN9Q==",
		  "nativeBridge": { "sha256": "da08c2ba03019da3a8c432ef061d5d6133fd2169ba3a6a8e9ac903353856d994" }
		}
		""");

	private readonly List<SmokeStep> _steps = [];
	private TemporaryDirectory? _temporary;
	private Dictionary<string, string> _environment = new(StringComparer.Ordinal);
	private bool _hasConsumedSdkIdentity;

	/// <summary>Why the packages are unusable, or <see langword="null"/>.</summary>
	internal string? SetupFailure
	{
		get;
		private set;
	}

	/// <summary>How the package directory was obtained.</summary>
	internal PackageSourceKind SourceKind
	{
		get;
		private set;
	}

	/// <summary>The absolute package directory the tests consume.</summary>
	internal string PackageSource
	{
		get;
		private set;
	} = string.Empty;

	/// <summary>Every archive of <see cref="PackageSource"/>.</summary>
	internal IReadOnlyList<PackageArchive> Archives
	{
		get;
		private set;
	} = [];

	/// <summary>The version every Client package carries.</summary>
	internal string ClientVersion
	{
		get;
		private set;
	} = string.Empty;

	/// <summary>The CheatEngine.SDK version the consumers reference directly.</summary>
	internal string SdkVersion
	{
		get;
		private set;
	} = string.Empty;

	/// <summary>Whether the consumers use the committed pin from nuget.org (so the reviewed SDK identity applies).</summary>
	internal bool UsesPinnedSdk
	{
		get;
		private set;
	} = true;

	/// <summary>The root of this run's temporary directories, outside any repository.</summary>
	internal string Root => _temporary?.Path ?? string.Empty;

	/// <summary>The isolated NuGet global packages folder.</summary>
	internal string PackageCache
	{
		get;
		private set;
	} = string.Empty;

	/// <summary>The NuGet.Config every restore uses (exclusively, through <c>--configfile</c>).</summary>
	internal string NuGetConfiguration
	{
		get;
		private set;
	} = string.Empty;

	/// <summary>The environment every child <c>dotnet</c> command receives.</summary>
	internal IReadOnlyDictionary<string, string> Environment => _environment;

	/// <summary>The isolated consumer project directory.</summary>
	internal string ConsumerDirectory
	{
		get;
		private set;
	} = string.Empty;

	/// <summary>The build output of the isolated consumer.</summary>
	internal string ConsumerOutput => Path.Combine(ConsumerDirectory, "bin", "Release", "net10.0");

	/// <summary>The <c>CheatEnginePluginOutputPath</c> deployment folder of the isolated consumer.</summary>
	internal string DeploymentDirectory
	{
		get;
		private set;
	} = string.Empty;

	/// <summary>The template home (isolated CLI and template engine settings).</summary>
	internal string TemplateHome
	{
		get;
		private set;
	} = string.Empty;

	/// <summary>The directory of the instantiated template.</summary>
	internal string TemplateDirectory
	{
		get;
		private set;
	} = string.Empty;

	/// <summary>The build output of the instantiated template.</summary>
	internal string TemplateOutput => Path.Combine(TemplateDirectory, "bin", "Release", "net10.0");

	/// <summary>Whether every consumer step succeeded.</summary>
	internal bool ConsumerSucceeded
	{
		get;
		private set;
	}

	/// <summary>Whether every template step succeeded.</summary>
	internal bool TemplateSucceeded
	{
		get;
		private set;
	}

	/// <summary>The reviewed identity of the pinned SDK (the hardcoded 1.0.0 reference values above).</summary>
	internal JsonElement ConsumedSdk
	{
		get
		{
			Assert.True(_hasConsumedSdkIdentity, "The reviewed SDK identity applies only when the consumers use the pinned SDK.");
			return _pinnedSdkIdentity.RootElement;
		}
	}

	/// <inheritdoc />
	public async ValueTask InitializeAsync()
	{
		PackageSourceDecision decision = PackageSourceResolution.ResolveFromEnvironment();
		SourceKind = decision.Kind;
		if (decision.Kind == PackageSourceKind.Invalid)
		{
			SetupFailure = decision.Error;
			return;
		}

		_temporary = new TemporaryDirectory("PackageConsumptionSmoke");
		_environment = CreateEnvironment(Root);
		PackageCache = _environment["NUGET_PACKAGES"];
		PackageSource = decision.Kind == PackageSourceKind.ConfiguredDirectory
			? decision.Directory!
			: await PackRepositoryAsync(_temporary.CreateDirectory("packages"));
		if (SetupFailure is not null)
		{
			return;
		}

		Archives = PackageArchive.ReadDirectory(PackageSource);
		PackageArchive? client = Archives.FirstOrDefault(static archive => !archive.IsSymbolPackage && archive.Id == ClientPackageId);
		if (client is null)
		{
			SetupFailure = $"The package directory '{PackageSource}' holds no {ClientPackageId} package.";
			return;
		}

		ClientVersion = client.Version;
		string? sdkSource = ResolveSdkVersion();
		NuGetConfiguration = WriteNuGetConfiguration(Path.Combine(Root, "NuGet.Config"), PackageCache, PackageSource, sdkSource);

		await BuildConsumerAsync();
		await InstantiateTemplateAsync();
	}

	/// <inheritdoc />
	public ValueTask DisposeAsync()
	{
		_temporary?.Dispose();
		return ValueTask.CompletedTask;
	}

	/// <summary>Fails the calling test when the package directory could not be used.</summary>
	internal void RequirePackages()
	{
		Assert.True(SetupFailure is null, SetupFailure);
	}

	/// <summary>Fails the calling test unless the isolated consumer restored and built.</summary>
	internal void RequireConsumer()
	{
		RequirePackages();
		Assert.True(ConsumerSucceeded, $"The isolated package consumer did not build.{System.Environment.NewLine}{DescribeSteps("consumer")}");
	}

	/// <summary>Fails the calling test unless the template installed, instantiated, restored and built.</summary>
	internal void RequireTemplate()
	{
		RequirePackages();
		Assert.True(TemplateSucceeded, $"The template did not install, instantiate or build.{System.Environment.NewLine}{DescribeSteps("template")}");
	}

	/// <summary>The one package (not symbol package) with this id.</summary>
	internal PackageArchive Package(string id)
	{
		RequirePackages();
		return Assert.Single(Archives, archive => !archive.IsSymbolPackage && archive.Id == id);
	}

	/// <summary>Creates a new directory under <see cref="Root"/>.</summary>
	internal string CreateDirectory(string name)
	{
		RequirePackages();
		return _temporary!.CreateDirectory(name);
	}

	/// <summary>Runs <c>dotnet</c> with the isolated environment.</summary>
	internal Task<DotNetProcessResult> RunAsync(string workingDirectory, params string[] arguments)
	{
		return DotNetProcess.RunAsync(workingDirectory, _environment, arguments);
	}

	/// <summary>Runs <c>dotnet</c> with the isolated environment and extra variables.</summary>
	internal Task<DotNetProcessResult> RunAsync(string workingDirectory, IReadOnlyDictionary<string, string> overrides,
		params string[] arguments)
	{
		Dictionary<string, string> environment = new(_environment, StringComparer.Ordinal);
		foreach ((string name, string value) in overrides)
		{
			environment[name] = value;
		}

		return DotNetProcess.RunAsync(workingDirectory, environment, arguments);
	}

	/// <summary>A consumer project file that references the packed Client and, optionally, CheatEngine.SDK directly.</summary>
	internal static string CreateConsumerProject(string clientVersion, string? sdkVersion, string extraProperties = "")
	{
		string sdkPackageReference = sdkVersion is null
			? string.Empty
			: $"    <PackageReference Include=\"{SdkPackageId}\" Version=\"{sdkVersion}\" />";

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
		         {{extraProperties}}
		           </PropertyGroup>
		           <ItemGroup>
		             <PackageReference Include="{{ClientPackageId}}" Version="{{clientVersion}}" />
		         {{sdkPackageReference}}
		           </ItemGroup>
		         </Project>
		         """;
	}

	/// <summary>
	/// Writes a NuGet.Config whose package source mapping sends the Client ids to the local feed only, and CheatEngine.SDK
	/// to <paramref name="sdkSource"/> when one is given, otherwise to nuget.org. The exact-id pattern has the highest
	/// precedence (https://learn.microsoft.com/nuget/consume-packages/package-source-mapping#package-pattern-precedence).
	/// A packageSourceMapping section may only contain packageSource elements, and restores pass this file with
	/// <c>--configfile</c>, so no other configuration applies.
	/// </summary>
	internal static string WriteNuGetConfiguration(string path, string packagesFolder, string clientSource, string? sdkSource)
	{
		string sdkSourceLine = sdkSource is null ? string.Empty : $"""    <add key="local-sdk-packages" value="{EscapeXml(sdkSource)}" />""";
		string sdkMapping = sdkSource is null
			? string.Empty
			: $"""    <packageSource key="local-sdk-packages">{"\r\n"}      <package pattern="{SdkPackageId}" />{"\r\n"}    </packageSource>""";
		string nuGetOrgSdkPattern = sdkSource is null ? $"""      <package pattern="{SdkPackageId}" />""" : string.Empty;
		string configuration = $"""
		                        <?xml version="1.0" encoding="utf-8"?>
		                        <configuration>
		                          <config>
		                            <add key="globalPackagesFolder" value="{EscapeXml(packagesFolder)}" />
		                          </config>
		                          <packageSources>
		                            <clear />
		                            <add key="local-client-packages" value="{EscapeXml(clientSource)}" />
		                        {sdkSourceLine}
		                            <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
		                          </packageSources>
		                          <packageSourceMapping>
		                            <packageSource key="local-client-packages">
		                              <package pattern="{ClientPackageId}" />
		                              <package pattern="{ClientPackageId}.*" />
		                            </packageSource>
		                        {sdkMapping}
		                            <packageSource key="nuget.org">
		                        {nuGetOrgSdkPattern}
		                              <package pattern="*" />
		                            </packageSource>
		                          </packageSourceMapping>
		                        </configuration>
		                        """;
		File.WriteAllText(path, configuration, new UTF8Encoding(false));
		return path;
	}

	/// <summary>Fails when a directory or one of its ancestors holds MSBuild or solution files that a restore would pick up.</summary>
	internal static void AssertOutsideAnyRepository(string directory)
	{
		string[] markers = ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "CheatEngine.Client.slnx"];
		for (DirectoryInfo? current = new DirectoryInfo(directory).Parent; current is not null; current = current.Parent)
		{
			foreach (string marker in markers)
			{
				Assert.False(File.Exists(Path.Combine(current.FullName, marker)),
					$"'{directory}' has '{Path.Combine(current.FullName, marker)}' above it, so its build would not be isolated from a workspace.");
			}
		}
	}

	/// <summary>
	/// Copies the pinned CheatEngine.SDK package of the isolated cache into <paramref name="feed"/> under another version,
	/// for tests that need an SDK the Client was not built for. The repository signature covers the original content, so
	/// the modified copy drops <c>.signature.p7s</c>; a modified package that keeps it fails with NU3008
	/// (https://learn.microsoft.com/nuget/consume-packages/installing-signed-packages).
	/// </summary>
	internal string CreateReversionedSdkPackage(string feed, string version)
	{
		RequireConsumer();
		Assert.True(UsesPinnedSdk, $"Re-versioning needs the pinned SDK from nuget.org; unset {SdkPackageSourceVariable}.");
		string pinned = SdkVersion.ToLowerInvariant();
		string source = Path.Combine(PackageCache, "cheatengine.sdk", pinned, $"cheatengine.sdk.{pinned}.nupkg");
		string destination = Path.Combine(feed, $"{SdkPackageId}.{version}.nupkg");
		File.Copy(source, destination, overwrite: true);
		using ZipArchive archive = ZipFile.Open(destination, ZipArchiveMode.Update);
		archive.GetEntry(".signature.p7s")?.Delete();
		ZipArchiveEntry nuspec = archive.Entries.Single(static entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
		string text;
		using (StreamReader reader = new(nuspec.Open()))
		{
			text = reader.ReadToEnd();
		}

		string name = nuspec.FullName;
		nuspec.Delete();
		ZipArchiveEntry replacement = archive.CreateEntry(name);
		using StreamWriter writer = new(replacement.Open(), new UTF8Encoding(false));
		writer.Write(text.Replace($"<version>{SdkVersion}</version>", $"<version>{version}</version>", StringComparison.Ordinal));
		return destination;
	}

	/// <summary>Writes a structured evidence line into the test output, which the TRX report keeps.</summary>
	internal static void Evidence(string fact, string text)
	{
		TestContext.Current.TestOutputHelper?.WriteLine($"evidence[{fact}] {text}");
	}

	private static Dictionary<string, string> CreateEnvironment(string root)
	{
		// NUGET_PACKAGES overrides a globalPackagesFolder setting and child processes inherit the parent's value, so it is
		// set explicitly for every command; package source mapping is ignored for packages already in the global folder.
		// A fresh DOTNET_CLI_HOME triggers the CLI first-run experience, whose side effects are disabled here.
		return new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["NUGET_PACKAGES"] = Path.Combine(root, "packages-cache"),
			["NUGET_HTTP_CACHE_PATH"] = Path.Combine(root, "http-cache"),
			["DOTNET_CLI_HOME"] = Path.Combine(root, "cli-home"),
			["DOTNET_NEW_HOME"] = Path.Combine(root, "template-engine"),
			["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "false",
			["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false",
			["DOTNET_NOLOGO"] = "true",
			["DOTNET_CLI_TELEMETRY_OPTOUT"] = "true",
			["DOTNET_CLI_USE_MSBUILD_SERVER"] = "false",
			["MSBUILDDISABLENODEREUSE"] = "1"
		};
	}

	private static string EscapeXml(string value)
	{
		return SecurityElement.Escape(value) ?? throw new InvalidOperationException("Could not escape the NuGet configuration value.");
	}

	private string? ResolveSdkVersion()
	{
		string? sdkSource = System.Environment.GetEnvironmentVariable(SdkPackageSourceVariable);
		if (!string.IsNullOrWhiteSpace(sdkSource))
		{
			string[] packages = Directory.GetFiles(sdkSource, "*.nupkg");
			Assert.True(packages.Length == 1, $"{SdkPackageSourceVariable} must name a directory with exactly one {SdkPackageId} package.");
			PackageArchive sdk = PackageArchive.Read(packages[0]);
			Assert.Equal(SdkPackageId, sdk.Id);
			SdkVersion = sdk.Version;
			UsesPinnedSdk = false;
			return Path.GetFullPath(sdkSource);
		}

		XDocument pin = XDocument.Load(RepositoryLayout.Combine("eng/CheatEngineSdk.props"));
		SdkVersion = Assert.Single(pin.Descendants("CheatEngineSdkVersion")).Value.Trim();
		Assert.Equal(_pinnedSdkIdentity.RootElement.GetProperty("version").GetString(), SdkVersion);
		_hasConsumedSdkIdentity = true;
		return null;
	}

	private async Task<string> PackRepositoryAsync(string output)
	{
		DotNetProcessResult pack = await DotNetProcess.RunAsync(RepositoryLayout.Root,
			"pack", RepositoryLayout.Combine("CheatEngine.Client.slnx"), "--configuration", "Release", "--output", output,
			"-nodeReuse:false");
		_steps.Add(new SmokeStep("self-pack", pack));
		if (pack.ExitCode != 0)
		{
			SetupFailure = $"Packing the repository for the local run failed.{System.Environment.NewLine}{pack}";
		}

		return output;
	}

	private async Task<bool> StepAsync(string name, string workingDirectory, params string[] arguments)
	{
		DotNetProcessResult result = await RunAsync(workingDirectory, arguments);
		_steps.Add(new SmokeStep(name, result));
		return result.ExitCode == 0;
	}

	private async Task BuildConsumerAsync()
	{
		ConsumerDirectory = _temporary!.CreateDirectory("package-consumer");
		DeploymentDirectory = _temporary.CreateDirectory("deployment");
		string project = Path.Combine(ConsumerDirectory, $"{ConsumerName}.csproj");
		await File.WriteAllTextAsync(project, CreateConsumerProject(ClientVersion, SdkVersion), new UTF8Encoding(false));
		await File.WriteAllTextAsync(Path.Combine(ConsumerDirectory, "Plugin.cs"), ConsumerSource, new UTF8Encoding(false));

		string[] build =
		[
			"build", project, "--configuration", "Release", "--no-restore", "-p:UseSharedCompilation=false",
			$"-p:CheatEnginePluginOutputPath={DeploymentDirectory}"
		];
		ConsumerSucceeded = await StepAsync("consumer restore", ConsumerDirectory,
								"restore", project, "--configfile", NuGetConfiguration, "--packages", PackageCache)
							&& await StepAsync("consumer build", ConsumerDirectory, build)
							// The second build proves that deployment replaces files already in place.
							&& await StepAsync("consumer rebuild", ConsumerDirectory, build);
	}

	private async Task InstantiateTemplateAsync()
	{
		TemplateHome = _temporary!.CreateDirectory("template-home");
		TemplateDirectory = Path.Combine(TemplateHome, ConsumerName);
		string templatePackage = Package(TemplatePackageId).Path;
		string project = Path.Combine(TemplateDirectory, $"{ConsumerName}.csproj");
		TemplateSucceeded = await StepAsync("template install", TemplateHome, "new", "install", templatePackage, "--force")
							&& await StepAsync("template dry run", TemplateHome,
								"new", "ceplugin", "--dry-run", "--name", ConsumerName, "--output", Path.Combine(TemplateHome, "dry-run"))
							&& await StepAsync("template instantiate", TemplateHome,
								"new", "ceplugin", "--name", ConsumerName, "--output", TemplateDirectory)
							&& await StepAsync("template restore", TemplateDirectory,
								"restore", project, "--configfile", NuGetConfiguration, "--packages", PackageCache)
							&& await StepAsync("template build", TemplateDirectory,
								"build", project, "--configuration", "Release", "--no-restore", "-p:UseSharedCompilation=false");
	}

	private string DescribeSteps(string prefix)
	{
		StringBuilder description = new();
		foreach (SmokeStep step in _steps)
		{
			if (step.Name.StartsWith(prefix, StringComparison.Ordinal) || !step.Succeeded)
			{
				description.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"[{step.Name}] exit {step.Result.ExitCode}");
				if (!step.Succeeded)
				{
					description.AppendLine(step.Result.ToString());
				}
			}
		}

		return description.ToString();
	}
}
