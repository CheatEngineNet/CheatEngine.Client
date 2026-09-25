using System.Text;
using System.Xml.Linq;

using CheatEngine.Client.Tests.Infrastructure;

namespace CheatEngine.Client.Tests.Packaging;

/// <summary>
///     Every <c>csharp</c> block of the README that each packed package publishes on nuget.org, and of the repository
///     README, compiles against the packed Client. The blocks of one README form one plugin project, as a plugin author
///     would copy them: the documented references (CheatEngine.Client, CheatEngine.SDK and
///     Microsoft.Extensions.Configuration.Json), the template's <c>Nullable</c> and <c>ImplicitUsings</c> settings, and
///     warnings as errors. A README that declares a <c>[CheatEnginePlugin]</c> type also runs the Hosting plugin profile
///     checks. A <c>csharp nocompile</c> block is skipped only with its written reason (<see cref="ReadmeCodeBlocks" />).
/// </summary>
[Collection(PackageConsumptionSmokeSerialGroup.Name)]
[Trait("Category", "PackageConsumption")]
public sealed class ReadmeSnippetCompilationTests(PackagedClientFeedFixture fixture)
{
	/// <summary>The theory value that names the repository README instead of a package.</summary>
	private const string RepositoryReadme = "README.md";

	private const string TemplateProjectEntry = "content/CheatEngine.Plugin/CheatEngine.Plugin.csproj";
	private const string ConfigurationJsonPackageId = "Microsoft.Extensions.Configuration.Json";
	private const string PluginAttribute = "[CheatEnginePlugin(";

	/// <summary>Every packed package and the repository README.</summary>
	public static TheoryData<string> Readmes => [.. PackagedClientFeedFixture.PackageIds, RepositoryReadme];

	[Theory]
	[MemberData(nameof(Readmes))]
	public async Task EveryCSharpBlockCompilesAgainstThePackedClientAsync(string readme)
	{
		fixture.RequirePackages();
		ReadmeCodeBlockSet parsed = ReadmeCodeBlocks.Parse(ReadReadme(readme));
		Assert.True(parsed.Problems.Count == 0,
			$"The {readme} README breaks the code block rules:{Environment.NewLine}" +
			string.Join(Environment.NewLine, parsed.Problems));

		ReadmeCodeBlock[] compiled = [.. parsed.Blocks.Where(static block => !block.NoCompile)];
		int[] skipped = [.. parsed.Blocks.Where(static block => block.NoCompile).Select(static block => block.Line)];
		PackagedClientFeedFixture.Evidence(nameof(EveryCSharpBlockCompilesAgainstThePackedClientAsync),
			$"readme={readme} compiled={string.Join(',', compiled.Select(static block => block.Line))} " +
			$"nocompile={string.Join(',', skipped)}");
		if (compiled.Length == 0)
		{
			return;
		}

		string directory = fixture.CreateDirectory($"readme-snippets-{readme.Replace('.', '-')}");
		string project = Path.Combine(directory, "Readme.Snippets.csproj");
		bool declaresPlugin = compiled.Any(static block => block.Code.Contains(PluginAttribute, StringComparison.Ordinal));
		await File.WriteAllTextAsync(project, CreateProject(declaresPlugin), new UTF8Encoding(false),
			TestContext.Current.CancellationToken);
		foreach (ReadmeCodeBlock block in compiled)
		{
			// The file name carries the README line of the block, so a compiler error points to the snippet.
			string file = Path.Combine(directory, $"README.L{block.Line:0000}.cs");
			await File.WriteAllTextAsync(file, block.Code, new UTF8Encoding(false),
				TestContext.Current.CancellationToken);
		}

		DotNetProcessResult restore = await fixture.RunAsync(directory, "restore", project, "--configfile",
			fixture.NuGetConfiguration, "--packages", fixture.PackageCache);
		DotNetProcessResult build = await fixture.RunAsync(directory, "build", project, "--configuration", "Release",
			"--no-restore", "-p:UseSharedCompilation=false");

		Assert.True(restore.ExitCode == 0, restore.ToString());
		Assert.True(build.ExitCode == 0,
			$"A C# block of the {readme} README does not compile:{Environment.NewLine}{build}");
	}

	[Fact]
	public void TheUmbrellaReadmeShowsACompiledPlugin()
	{
		fixture.RequirePackages();
		ReadmeCodeBlockSet parsed = ReadmeCodeBlocks.Parse(ReadReadme(PackagedClientFeedFixture.ClientPackageId));

		Assert.Contains(parsed.Blocks,
			static block => !block.NoCompile && block.Code.Contains(PluginAttribute, StringComparison.Ordinal));
	}

	private string ReadReadme(string readme)
	{
		return readme == RepositoryReadme
			? File.ReadAllText(RepositoryLayout.Combine(RepositoryReadme))
			: fixture.Package(readme).EntryText("README.md");
	}

	/// <summary>The plugin project the READMEs document, with the packed versions.</summary>
	private string CreateProject(bool declaresPlugin)
	{
		// The template package stamps the Microsoft.Extensions.Configuration.Json version the Client documents.
		XDocument template = XDocument.Parse(
			fixture.Package(PackagedClientFeedFixture.TemplatePackageId).EntryText(TemplateProjectEntry));
		string configurationJson = (string?) template.Descendants("PackageReference")
									   .Single(static reference =>
										   (string?) reference.Attribute("Include") == ConfigurationJsonPackageId)
									   .Attribute("Version")
								   ?? throw new InvalidOperationException(
									   $"The packed template does not version {ConfigurationJsonPackageId}.");
		string pluginProject = declaresPlugin ? "true" : "false";
		const string clientId = PackagedClientFeedFixture.ClientPackageId;
		const string sdkId = PackagedClientFeedFixture.SdkPackageId;
		return $"""
		        <Project Sdk="Microsoft.NET.Sdk">
		          <PropertyGroup>
		            <TargetFramework>net10.0</TargetFramework>
		            <LangVersion>14.0</LangVersion>
		            <Nullable>enable</Nullable>
		            <ImplicitUsings>enable</ImplicitUsings>
		            <PlatformTarget>x64</PlatformTarget>
		            <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
		            <CheatEngineClientPluginProject>{pluginProject}</CheatEngineClientPluginProject>
		            <RestorePackagesWithLockFile>false</RestorePackagesWithLockFile>
		          </PropertyGroup>
		          <ItemGroup>
		            <PackageReference Include="{clientId}" Version="{fixture.ClientVersion}" />
		            <PackageReference Include="{sdkId}" Version="{fixture.SdkVersion}" />
		            <PackageReference Include="{ConfigurationJsonPackageId}" Version="{configurationJson}" />
		          </ItemGroup>
		        </Project>
		        """;
	}
}
