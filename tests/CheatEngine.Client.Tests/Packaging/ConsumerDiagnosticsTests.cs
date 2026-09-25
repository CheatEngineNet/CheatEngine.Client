using System.Text;
using System.Text.RegularExpressions;

using CheatEngine.Client.Tests.Infrastructure;

namespace CheatEngine.Client.Tests.Packaging;

/// <summary>
///     A plugin project that consumes the packed packages and breaks one rule of the Hosting plugin profile fails its
///     build with that rule's CECLIENT diagnostic and no other (the "Build diagnostics" table of the Hosting README).
///     Each case restores and builds its own consumer against the fixture's isolated package cache; the deployment
///     cases also prove that a refused deployment writes nothing. <c>CECLIENT001</c> and <c>CECLIENT017</c> are covered
///     by <see cref="PackageConsumptionSmokeTests" />.
/// </summary>
[Collection(PackageConsumptionSmokeSerialGroup.Name)]
[Trait("Category", "PackageConsumption")]
public sealed partial class ConsumerDiagnosticsTests(PackagedClientFeedFixture fixture)
{
	private const int RegexTimeoutMilliseconds = 1000;

	/// <summary>A plugin that needs only the Hosting package, so every case compiles once its check passes.</summary>
	private const string PluginSource =
		"""
		using CheatEngine.Client.Hosting;
		using CheatEngine.SDK.Annotations.Plugin;

		[CheatEnginePlugin("Consumer diagnostics plugin")]
		public sealed class Plugin : CheatEngineClientPlugin
		{
		    protected override void Configure(CheatEnginePluginBuilder builder)
		    {
		    }
		}
		""";

	/// <summary>Drops the SDK's Lua bridge from the files copied to the output, like a broken build asset.</summary>
	private const string DropLuaBridgeTarget =
		"""
		<Target Name="DropCheatEngineSdkLuaBridge" BeforeTargets="AssignTargetPaths">
		  <ItemGroup>
		    <Content Remove="@(Content)"
		             Condition="'%(Filename)%(Extension)' == 'cheatengine-sdk-lua-bridge.dll'" />
		  </ItemGroup>
		</Target>
		""";

	[Theory]
	[InlineData("CECLIENT002", "", false)]
	[InlineData("CECLIENT005", "<TargetFramework>net10.0-windows</TargetFramework>", false)]
	[InlineData("CECLIENT006", "<LangVersion>13.0</LangVersion>", false)]
	[InlineData("CECLIENT007", "<PlatformTarget>x86</PlatformTarget>", false)]
	[InlineData("CECLIENT008", "<CheatEngineSdkGenerateEntryPoint>false</CheatEngineSdkGenerateEntryPoint>", false)]
	[InlineData("CECLIENT011", "<GenerateDependencyFile>false</GenerateDependencyFile>", true)]
	[InlineData("CECLIENT012", "<GenerateRuntimeConfigurationFiles>false</GenerateRuntimeConfigurationFiles>", true)]
	[InlineData("CECLIENT013", "<CopyLocalLockFileAssemblies>false</CopyLocalLockFileAssemblies>", true)]
	[InlineData("CECLIENT015", "", true)]
	public async Task APluginThatBreaksOneProfileRuleFailsWithItsDiagnosticAsync(string code, string property,
		bool deploys)
	{
		fixture.RequirePackages();
		string directory = fixture.CreateDirectory($"consumer-diagnostic-{code}");
		string deployment = Path.Combine(fixture.Root, $"consumer-diagnostic-{code}-deployment");
		string project = Path.Combine(directory, "Diagnostics.Plugin.csproj");
		// CECLIENT002: the plugin references CheatEngine.Client.Hosting, which brings the profile, but not the Client.
		string clientPackage = code == "CECLIENT002"
			? PackagedClientFeedFixture.HostingPackageId
			: PackagedClientFeedFixture.ClientPackageId;
		string extra = code == "CECLIENT015" ? DropLuaBridgeTarget : string.Empty;
		await File.WriteAllTextAsync(project, CreateProject(clientPackage, property, extra), new UTF8Encoding(false),
			TestContext.Current.CancellationToken);
		await File.WriteAllTextAsync(Path.Combine(directory, "Plugin.cs"), PluginSource, new UTF8Encoding(false),
			TestContext.Current.CancellationToken);

		DotNetProcessResult restore = await fixture.RunAsync(directory, "restore", project, "--configfile",
			fixture.NuGetConfiguration, "--packages", fixture.PackageCache);
		List<string> arguments =
			["build", project, "--configuration", "Release", "--no-restore", "-p:UseSharedCompilation=false"];
		if (deploys)
		{
			arguments.Add($"-p:CheatEnginePluginOutputPath={deployment}");
		}

		DotNetProcessResult build = await fixture.RunAsync(directory, arguments.ToArray());
		string[] reported =
		[
			.. ReportedError().Matches(build.StandardOutput).Select(static match => match.Groups["code"].Value)
				.Distinct(StringComparer.Ordinal)
		];

		Assert.True(restore.ExitCode == 0, restore.ToString());
		Assert.True(build.ExitCode != 0, build.ToString());
		Assert.True(reported.Length == 1 && reported[0] == code,
			$"Expected {code} alone, got [{string.Join(", ", reported)}].{Environment.NewLine}{build}");
		// A deployment check runs before anything is staged or written.
		Assert.False(Directory.Exists(deployment) && Directory.EnumerateFileSystemEntries(deployment).Any(),
			$"{code} left files in the deployment folder '{deployment}'.");
		PackagedClientFeedFixture.Evidence(nameof(APluginThatBreaksOneProfileRuleFailsWithItsDiagnosticAsync),
			$"code={code} package={clientPackage} property={property} deployment={deploys} build=error {code}");
	}

	private string CreateProject(string clientPackage, string property, string extra)
	{
		const string sdkPackage = PackagedClientFeedFixture.SdkPackageId;
		return $"""
		        <Project Sdk="Microsoft.NET.Sdk">
		          <PropertyGroup>
		            <TargetFramework>net10.0</TargetFramework>
		            <LangVersion>14.0</LangVersion>
		            <Nullable>enable</Nullable>
		            <ImplicitUsings>enable</ImplicitUsings>
		            <PlatformTarget>x64</PlatformTarget>
		            <CheatEngineClientPluginProject>true</CheatEngineClientPluginProject>
		            <RestorePackagesWithLockFile>false</RestorePackagesWithLockFile>
		            {property}
		          </PropertyGroup>
		          <ItemGroup>
		            <PackageReference Include="{clientPackage}" Version="{fixture.ClientVersion}" />
		            <PackageReference Include="{sdkPackage}" Version="{fixture.SdkVersion}" />
		          </ItemGroup>
		        {extra}
		        </Project>
		        """;
	}

	/// <summary>An MSBuild error line of a CECLIENT build diagnostic, whatever its message.</summary>
	[GeneratedRegex(@"\berror (?<code>CECLIENT\d{3})\s*:", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex ReportedError();
}
