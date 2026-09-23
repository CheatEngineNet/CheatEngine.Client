using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

using CheatEngine.Client.Tests.Architecture;
using CheatEngine.Client.Tests.Infrastructure;

namespace CheatEngine.Client.Tests.SdkContract;

/// <summary>
///     The consumed CheatEngine.SDK identity that Client.Core embeds for its runtime package gate (audit ADR-09, ADR-10,
///     A21-35) is the locked and restored package: the version and content hash of the resolved lock-file entry, the pin
///     of <c>eng/CheatEngineSdk.props</c>, and the source commit of the CheatEngine.SDK.Engine assembly actually loaded.
///     A build that cannot embed it fails with <c>CHEATENGINECLIENT9050</c> unless it is the SDK-side canary.
/// </summary>
/// <remarks>
///     The guard cases run only the evaluation and the guard target of <c>CheatEngine.Client.Core.csproj</c> (nothing is
///     restored, built or packed), like <c>BuildGuardTests</c>; they need the restored checkout the solution build leaves.
/// </remarks>
public sealed partial class ConsumedSdkIdentityEmbeddingTests
{
	private const string MetadataPrefix = "CheatEngine.Client.ConsumedSdk.";
	private const string CoreProject = "libs/CheatEngine.Client.Core/CheatEngine.Client.Core.csproj";
	private const string CoreLockFile = "libs/CheatEngine.Client.Core/packages.lock.json";
	private const string SdkPinFile = "eng/CheatEngineSdk.props";
	private const string IdentityGuard = "CheatEngineClientRequireConsumedSdkIdentity";
	private const int RegexTimeoutMilliseconds = 1000;

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EmbeddedSdkIdentityEqualsTheLockedAndLoadedSdkPackage()
	{
		using JsonDocument lockFile = JsonDocument.Parse(File.ReadAllText(RepositoryLayout.Combine(CoreLockFile)));
		JsonElement lockedSdk = lockFile.RootElement.GetProperty("dependencies").GetProperty("net10.0")
			.GetProperty("CheatEngine.SDK");
		Match pin = SdkVersionPin().Match(File.ReadAllText(RepositoryLayout.Combine(SdkPinFile)));
		string? loaded = ClientAssemblyCatalog.Load("CheatEngine.SDK.Engine")
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
		Dictionary<string, string> embedded = ReadEmbeddedMetadata();

		Assert.True(pin.Success, $"{SdkPinFile} declares no CheatEngineSdkVersion.");
		Assert.NotNull(loaded);
		Assert.Equal(
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				[MetadataPrefix + "Version"] = lockedSdk.GetProperty("resolved").GetString()!,
				[MetadataPrefix + "SourceCommit"] = loaded[(loaded.IndexOf('+', StringComparison.Ordinal) + 1)..],
				[MetadataPrefix + "ContentHashSha512"] = lockedSdk.GetProperty("contentHash").GetString()!
			},
			embedded);
		Assert.Equal(pin.Groups["version"].Value, embedded[MetadataPrefix + "Version"]);
		Assert.Equal($"{embedded[MetadataPrefix + "Version"]}+{embedded[MetadataPrefix + "SourceCommit"]}", loaded);
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public async Task CommittedLockAndRestoredPackagePassTheConsumedSdkIdentityGuard()
	{
		DotNetProcessResult result = await RunGuardAsync();

		Assert.True(result.ExitCode == 0, result.ToString());
		Assert.DoesNotContain("CHEATENGINECLIENT9050", result.StandardOutput, StringComparison.Ordinal);
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public async Task BuildThatCannotEmbedTheConsumedSdkIdentityFailsWithCHEATENGINECLIENT9050()
	{
		using TemporaryDirectory emptyPackageRoot = new("empty-package-root");

		DotNetProcessResult packageMissing = await RunGuardAsync($"-p:NuGetPackageRoot={emptyPackageRoot.Path}");
		DotNetProcessResult pinDrift = await RunGuardAsync("-p:CheatEngineSdkVersion=1.0.1");

		Assert.True(packageMissing.ExitCode != 0, packageMissing.ToString());
		Assert.Contains("error CHEATENGINECLIENT9050", packageMissing.StandardOutput, StringComparison.Ordinal);
		Assert.Contains("package was not found under the NuGet package root", packageMissing.StandardOutput,
			StringComparison.Ordinal);
		Assert.True(pinDrift.ExitCode != 0, pinDrift.ToString());
		Assert.Contains("error CHEATENGINECLIENT9050", pinDrift.StandardOutput, StringComparison.Ordinal);
		Assert.Contains("not the pin 1.0.1 of eng/CheatEngineSdk.props", pinDrift.StandardOutput,
			StringComparison.Ordinal);
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public async Task CanaryBuildEmbedsNoConsumedSdkIdentityAndIsNotRefused()
	{
		using TemporaryDirectory emptyPackageRoot = new("empty-package-root");

		DotNetProcessResult guard = await RunGuardAsync($"-p:NuGetPackageRoot={emptyPackageRoot.Path}",
			"-p:CheatEngineSdkCanary=true");
		DotNetProcessResult items = await DotNetProcess.RunAsync(RepositoryLayout.Root, "msbuild",
			RepositoryLayout.Combine(CoreProject), "-nologo", "-nodeReuse:false", "-getItem:AssemblyMetadata",
			"-p:CheatEngineSdkCanary=true");

		Assert.True(guard.ExitCode == 0, guard.ToString());
		Assert.DoesNotContain("CHEATENGINECLIENT9050", guard.StandardOutput, StringComparison.Ordinal);
		Assert.True(items.ExitCode == 0, items.ToString());
		Assert.DoesNotContain(MetadataPrefix, items.StandardOutput, StringComparison.Ordinal);
	}

	private static Task<DotNetProcessResult> RunGuardAsync(params string[] properties)
	{
		List<string> arguments =
		[
			"msbuild", RepositoryLayout.Combine(CoreProject), $"-t:{IdentityGuard}", "-nologo", "-nodeReuse:false",
			"-verbosity:minimal", "-p:CheatEngineSdkCanary=false"
		];
		arguments.AddRange(properties);
		return DotNetProcess.RunAsync(RepositoryLayout.Root, [.. arguments]);
	}

	private static Dictionary<string, string> ReadEmbeddedMetadata()
	{
		Dictionary<string, string> metadata = new(StringComparer.Ordinal);
		foreach (CustomAttributeData attribute in ClientAssemblyCatalog.Load("CheatEngine.Client.Core")
					 .GetCustomAttributesData())
		{
			if (attribute.AttributeType == typeof(AssemblyMetadataAttribute) &&
				attribute.ConstructorArguments[0].Value is string key &&
				key.StartsWith(MetadataPrefix, StringComparison.Ordinal))
			{
				metadata.Add(key, (string) attribute.ConstructorArguments[1].Value!);
			}
		}

		return metadata;
	}

	[GeneratedRegex(@"<CheatEngineSdkVersion>(?<version>[^<]+)</CheatEngineSdkVersion>", RegexOptions.CultureInvariant,
		RegexTimeoutMilliseconds)]
	private static partial Regex SdkVersionPin();
}
