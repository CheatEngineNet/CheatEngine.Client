using System.Globalization;
using System.Reflection;
using System.Text.Json;

using CheatEngine.Client.Tests.Architecture;
using CheatEngine.Client.Tests.Infrastructure;

namespace CheatEngine.Client.Tests.SdkContract;

/// <summary>
///     The consumed CheatEngine.SDK identity that Client.Core embeds for its runtime package gate (audit ADR-09, ADR-10,
///     A21-35) is the locked and restored package: the version and content hash of the resolved lock-file entry, the pin
///     of <c>eng/CheatEngineSdk.props</c>, the source commit of the CheatEngine.SDK.Engine assembly actually loaded, and
///     the supported major of <c>eng/CheatEngineSdk.props</c>. A build that cannot embed it fails with
///     <c>CHEATENGINECLIENT9050</c>.
/// </summary>
/// <remarks>
///     The guard cases run only the evaluation and the guard target of <c>CheatEngine.Client.Core.csproj</c> (nothing is
///     restored, built or packed), like <c>BuildGuardTests</c>; they need the restored checkout the solution build leaves.
/// </remarks>
public sealed class ConsumedSdkIdentityEmbeddingTests
{
	private const string MetadataPrefix = "CheatEngine.Client.ConsumedSdk.";
	private const string CoreProject = "libs/CheatEngine.Client.Core/CheatEngine.Client.Core.csproj";
	private const string CoreLockFile = "libs/CheatEngine.Client.Core/packages.lock.json";
	private const string IdentityGuard = "CheatEngineClientRequireConsumedSdkIdentity";

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EmbeddedSdkIdentityEqualsTheLockedAndLoadedSdkPackage()
	{
		using JsonDocument lockFile = JsonDocument.Parse(File.ReadAllText(RepositoryLayout.Combine(CoreLockFile)));
		JsonElement lockedSdk = lockFile.RootElement.GetProperty("dependencies").GetProperty("net10.0")
			.GetProperty("CheatEngine.SDK");
		string? loaded = ClientAssemblyCatalog.Load("CheatEngine.SDK.Engine")
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
		Dictionary<string, string> embedded = ReadEmbeddedMetadata();

		Assert.NotNull(loaded);
		Assert.Equal(
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				[MetadataPrefix + "Version"] = lockedSdk.GetProperty("resolved").GetString()!,
				[MetadataPrefix + "SourceCommit"] = loaded[(loaded.IndexOf('+', StringComparison.Ordinal) + 1)..],
				[MetadataPrefix + "ContentHashSha512"] = lockedSdk.GetProperty("contentHash").GetString()!,
				[MetadataPrefix + "SupportedMajor"] = SdkPin.SupportedMajor.ToString(CultureInfo.InvariantCulture)
			},
			embedded);
		Assert.Equal(SdkPin.Version, embedded[MetadataPrefix + "Version"]);
		Assert.Equal($"{embedded[MetadataPrefix + "Version"]}+{embedded[MetadataPrefix + "SourceCommit"]}", loaded);
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public async Task CommittedLockAndRestoredPackagePassTheConsumedSdkIdentityGuardAsync()
	{
		DotNetProcessResult result = await RunGuardAsync();

		Assert.True(result.ExitCode == 0, result.ToString());
		Assert.DoesNotContain("CHEATENGINECLIENT9050", result.StandardOutput, StringComparison.Ordinal);
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public async Task BuildThatCannotEmbedTheConsumedSdkIdentityFailsWithCHEATENGINECLIENT9050Async()
	{
		using TemporaryDirectory emptyPackageRoot = new("empty-package-root");
		string driftedPin = $"{SdkPin.Major}.{SdkPin.Minor}.{SdkPin.Patch + 1}";

		DotNetProcessResult packageMissing = await RunGuardAsync($"-p:NuGetPackageRoot={emptyPackageRoot.Path}");
		DotNetProcessResult pinDrift = await RunGuardAsync($"-p:CheatEngineSdkVersion={driftedPin}");

		Assert.True(packageMissing.ExitCode != 0, packageMissing.ToString());
		Assert.Contains("error CHEATENGINECLIENT9050", packageMissing.StandardOutput, StringComparison.Ordinal);
		Assert.Contains("package was not found under the NuGet package root", packageMissing.StandardOutput,
			StringComparison.Ordinal);
		Assert.True(pinDrift.ExitCode != 0, pinDrift.ToString());
		Assert.Contains("error CHEATENGINECLIENT9050", pinDrift.StandardOutput, StringComparison.Ordinal);
		Assert.Contains($"not the pin {driftedPin} of eng/CheatEngineSdk.props", pinDrift.StandardOutput,
			StringComparison.Ordinal);
	}

	private static Task<DotNetProcessResult> RunGuardAsync(params string[] properties)
	{
		List<string> arguments =
		[
			"msbuild", RepositoryLayout.Combine(CoreProject), $"-t:{IdentityGuard}", "-nologo", "-nodeReuse:false",
			"-verbosity:minimal"
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
}
