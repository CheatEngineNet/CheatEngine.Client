using System.Reflection;
using System.Text.Json;

using CheatEngine.Client.Tests.Architecture;
using CheatEngine.Client.Tests.Infrastructure;

namespace CheatEngine.Client.Tests.SdkContract;

/// <summary>
///     The consumed CheatEngine.SDK identity that Client.Core embeds for its runtime package gate (audit ADR-09, A21-35)
///     is exactly the reviewed identity file and the resolved lock-file entry.
/// </summary>
public sealed class ConsumedSdkIdentityEmbeddingTests
{
	private const string MetadataPrefix = "CheatEngine.Client.ConsumedSdk.";

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EmbeddedSdkIdentityEqualsTheConsumedSdkFileAndTheLock()
	{
		using JsonDocument identity = JsonDocument.Parse(File.ReadAllText(RepositoryLayout.Combine("eng/sdk/consumed-sdk.json")));
		using JsonDocument lockFile = JsonDocument.Parse(File.ReadAllText(
			RepositoryLayout.Combine("libs/CheatEngine.Client.Core/packages.lock.json")));
		JsonElement lockedSdk = lockFile.RootElement.GetProperty("dependencies").GetProperty("net10.0")
			.GetProperty("CheatEngine.SDK");
		Dictionary<string, string> embedded = ReadEmbeddedMetadata();

		Assert.Equal(
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				[MetadataPrefix + "Version"] = identity.RootElement.GetProperty("version").GetString()!,
				[MetadataPrefix + "SourceCommit"] = identity.RootElement.GetProperty("sourceCommit").GetString()!,
				[MetadataPrefix + "ContentHashSha512"] = identity.RootElement.GetProperty("contentHashSha512").GetString()!
			},
			embedded);
		Assert.Equal(lockedSdk.GetProperty("resolved").GetString(), embedded[MetadataPrefix + "Version"]);
		Assert.Equal(lockedSdk.GetProperty("contentHash").GetString(), embedded[MetadataPrefix + "ContentHashSha512"]);
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
