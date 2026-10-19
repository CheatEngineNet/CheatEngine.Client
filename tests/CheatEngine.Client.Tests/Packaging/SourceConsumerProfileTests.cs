using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

using CheatEngine.Client.Tests.Infrastructure;

namespace CheatEngine.Client.Tests.Packaging;

/// <summary>Runs the build-transitive source-reference profile guard without restoring or compiling a consumer.</summary>
public sealed class SourceConsumerProfileTests
{
	private const string ValidationTarget = "CheatEngineClientValidatePluginProjectProfile";
	private const string HostingTargets = "libs/CheatEngine.Client.Hosting/buildTransitive/CheatEngine.Client.Hosting.targets";
	private const string ClientFacade = "src/CheatEngine.Client/CheatEngine.Client.csproj";

	[Fact]
	public async Task CanonicalClientFacadeAndExactSdkSourceProjectsPassAsync()
	{
		using TemporaryDirectory directory = new("source-consumer-profile-valid");
		string sourceRoot = await CreateSdkSourceRootAsync(directory);
		string project = await CreateConsumerProjectAsync(directory, sourceRoot,
			[RepositoryLayout.Combine(ClientFacade), EngineProject(sourceRoot), HostingProject(sourceRoot)]);

		DotNetProcessResult result = await RunValidationAsync(project);

		Assert.True(result.ExitCode == 0, result.ToString());
		Assert.DoesNotContain("CECLIENT001", result.StandardOutput, StringComparison.Ordinal);
		Assert.DoesNotContain("CECLIENT002", result.StandardOutput, StringComparison.Ordinal);
	}

	[Fact]
	public async Task MissingClientFacadeFailsWithCECLIENT002Async()
	{
		using TemporaryDirectory directory = new("source-consumer-profile-missing-client");
		string sourceRoot = await CreateSdkSourceRootAsync(directory);
		string project = await CreateConsumerProjectAsync(directory, sourceRoot,
			[EngineProject(sourceRoot), HostingProject(sourceRoot)]);

		DotNetProcessResult result = await RunValidationAsync(project);

		Assert.True(result.ExitCode != 0, result.ToString());
		Assert.Contains("error CECLIENT002", result.StandardOutput, StringComparison.Ordinal);
		Assert.DoesNotContain("error CECLIENT001", result.StandardOutput, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("CheatEngine.SDK.Engine")]
	[InlineData("CheatEngine.SDK.Hosting")]
	public async Task MissingEitherSdkSourceProjectFailsWithCECLIENT001Async(string missingProject)
	{
		using TemporaryDirectory directory = new($"source-consumer-profile-missing-{missingProject}");
		string sourceRoot = await CreateSdkSourceRootAsync(directory);
		List<string> references = [RepositoryLayout.Combine(ClientFacade), EngineProject(sourceRoot), HostingProject(sourceRoot)];
		references.RemoveAll(reference => string.Equals(Path.GetFileNameWithoutExtension(reference), missingProject,
			StringComparison.Ordinal));
		string project = await CreateConsumerProjectAsync(directory, sourceRoot, references);

		DotNetProcessResult result = await RunValidationAsync(project);

		Assert.True(result.ExitCode != 0, result.ToString());
		Assert.Contains("error CECLIENT001", result.StandardOutput, StringComparison.Ordinal);
		Assert.DoesNotContain("error CECLIENT002", result.StandardOutput, StringComparison.Ordinal);
	}

	[Fact]
	public async Task SameNamedSdkProjectOutsideConfiguredSourceRootFailsWithCECLIENT001Async()
	{
		using TemporaryDirectory directory = new("source-consumer-profile-outside-root");
		string sourceRoot = await CreateSdkSourceRootAsync(directory);
		string outside = directory.CreateDirectory("outside-source-root");
		string outsideEngine = await CreateProjectFileAsync(outside, "CheatEngine.SDK.Engine");
		string project = await CreateConsumerProjectAsync(directory, sourceRoot,
			[RepositoryLayout.Combine(ClientFacade), outsideEngine, HostingProject(sourceRoot)]);

		DotNetProcessResult result = await RunValidationAsync(project);

		Assert.True(result.ExitCode != 0, result.ToString());
		Assert.Contains("error CECLIENT001", result.StandardOutput, StringComparison.Ordinal);
		Assert.DoesNotContain("error CECLIENT002", result.StandardOutput, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(2, true)]
	[InlineData(1, false)]
	[InlineData(3, false)]
	[InlineData(0, false)]
	public async Task SourceMajorGuardReadsTheResolvedEngineMetadataAsync(int major, bool accepted)
	{
		using TemporaryDirectory directory = new("source-consumer-major");
		string engine = Path.Combine(directory.Path, "CheatEngine.SDK.Engine.dll");
		if (major != 0)
		{
			MetadataBuilder metadata = new();
			metadata.AddModule(0, metadata.GetOrAddString("CheatEngine.SDK.Engine.dll"),
				metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
			metadata.AddAssembly(metadata.GetOrAddString("CheatEngine.SDK.Engine"), new Version(major, 0, 0, 0),
				default, default, (AssemblyFlags) 0, AssemblyHashAlgorithm.None);
			metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"),
				default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
			ManagedPEBuilder pe = new(new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
				new MetadataRootBuilder(metadata), new BlobBuilder(), flags: CorFlags.ILOnly);
			BlobBuilder image = new();
			pe.Serialize(image);
			await File.WriteAllBytesAsync(engine, image.ToArray(), TestContext.Current.CancellationToken);
		}
		string reference = major == 0 ? "" : $"<ReferencePath Include=\"{Escape(engine)}\" />";
		string project = Path.Combine(directory.Path, "SourceMajor.proj");
		await File.WriteAllTextAsync(project, $"""
			<Project>
			  <PropertyGroup>
			    <CheatEngineClientPluginProject>true</CheatEngineClientPluginProject>
			    <CheatEngineSdkSourceRoot>{Escape(directory.Path)}</CheatEngineSdkSourceRoot>
			  </PropertyGroup>
			  <ItemGroup>{reference}</ItemGroup>
			  <Import Project="{Escape(RepositoryLayout.Combine(HostingTargets))}" />
			</Project>
			""", TestContext.Current.CancellationToken);

		DotNetProcessResult result = await DotNetProcess.RunAsync(directory.Path, "msbuild", project,
			"-t:CheatEngineClientValidateResolvedSdkMajor", "-nologo", "-nodeReuse:false", "-verbosity:minimal");

		if (accepted)
		{
			Assert.True(result.ExitCode == 0, result.ToString());
		}
		else
		{
			Assert.True(result.ExitCode != 0, result.ToString());
			Assert.Contains("error CECLIENT017", result.StandardOutput, StringComparison.Ordinal);
		}
	}

	private static async Task<string> CreateSdkSourceRootAsync(TemporaryDirectory directory)
	{
		string root = directory.CreateDirectory("sdk-source-root");
		await CreateProjectFileAsync(Path.Combine(root, "libs", "CheatEngine.SDK.Engine"), "CheatEngine.SDK.Engine");
		await CreateProjectFileAsync(Path.Combine(root, "libs", "CheatEngine.SDK.Hosting"), "CheatEngine.SDK.Hosting");
		return root;
	}

	private static async Task<string> CreateConsumerProjectAsync(TemporaryDirectory directory, string sourceRoot,
		IReadOnlyList<string> references)
	{
		string project = Path.Combine(directory.Path, "SourceConsumer.proj");
		string projectReferences = string.Join(Environment.NewLine, references.Select(reference =>
			$"    <ProjectReference Include=\"{Escape(reference)}\" />"));
		string targets = RepositoryLayout.Combine(HostingTargets);
		string source = $"""
			<Project>
			  <PropertyGroup>
			    <TargetFramework>net10.0</TargetFramework>
			    <LangVersion>14.0</LangVersion>
			    <PlatformTarget>x64</PlatformTarget>
			    <CheatEngineClientPluginProject>true</CheatEngineClientPluginProject>
			    <CheatEngineSdkSourceRoot>{Escape(sourceRoot)}</CheatEngineSdkSourceRoot>
			  </PropertyGroup>
			  <ItemGroup>
			{projectReferences}
			  </ItemGroup>
			  <Import Project="{Escape(targets)}" />
			</Project>
			""";
		await File.WriteAllTextAsync(project, source, new UTF8Encoding(false), TestContext.Current.CancellationToken);
		return project;
	}

	private static async Task<string> CreateProjectFileAsync(string directory, string name)
	{
		Directory.CreateDirectory(directory);
		string project = Path.Combine(directory, $"{name}.csproj");
		await File.WriteAllTextAsync(project, "<Project />", new UTF8Encoding(false), TestContext.Current.CancellationToken);
		return project;
	}

	private static Task<DotNetProcessResult> RunValidationAsync(string project)
	{
		return DotNetProcess.RunAsync(Path.GetDirectoryName(project)!, "msbuild", project, $"-t:{ValidationTarget}",
			"-nologo", "-nodeReuse:false", "-verbosity:minimal");
	}

	private static string EngineProject(string sourceRoot) =>
		Path.Combine(sourceRoot, "libs", "CheatEngine.SDK.Engine", "CheatEngine.SDK.Engine.csproj");

	private static string HostingProject(string sourceRoot) =>
		Path.Combine(sourceRoot, "libs", "CheatEngine.SDK.Hosting", "CheatEngine.SDK.Hosting.csproj");

	private static string Escape(string value) => value.Replace("&", "&amp;", StringComparison.Ordinal)
		.Replace("\"", "&quot;", StringComparison.Ordinal);
}
