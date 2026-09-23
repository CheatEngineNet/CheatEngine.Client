using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.Release;

/// <summary>
/// The Client release tuple (audit A21-04) follows its schema and the encoding rules of every evidence document. The
/// release workflow runs this class on the tuple it produces through <c>CHEATENGINE_CLIENT_TUPLE_PATH</c>; without the
/// variable the committed example is validated, so the test is never a silent no-op.
/// </summary>
public sealed partial class ClientTupleSchemaTests
{
	/// <summary>The schema.</summary>
	internal const string SchemaPath = "eng/release/client-tuple.v0.schema.json";

	/// <summary>A schema-valid example, produced by a local rehearsal of New-ClientTuple.ps1.</summary>
	internal const string ExamplePath = "eng/release/client-tuple.example.json";

	/// <summary>The environment variable that names a produced tuple.</summary>
	internal const string TuplePathVariable = "CHEATENGINE_CLIENT_TUPLE_PATH";

	private static readonly string[] _rootFields =
		["schema", "stage", "source", "build", "ceProfile", "qualification", "sbom", "attestations", "assets", "client", "consumedSdk", "createdUtc"];

	private static readonly string[] _sourceFields = ["repository", "tag", "commit", "treeHash", "pullRequest", "releaseRunUrl", "ciRunUrl"];

	private static readonly string[] _packageFields =
		["id", "version", "attestedAssetSha256", "contentHashSha512", "nugetOrgSignedSha256", "snupkgSha256"];

	private static readonly string[] _consumedSdkFields =
		["id", "version", "range", "contentHashSha512", "attestedAssetSha256", "nugetOrgSignedSha256", "sourceCommit", "nativeBridge", "lockFile"];

	private static readonly string[] _stages = ["PrePublish", "Published"];

	private static readonly string[] _packageIds =
	[
		"CheatEngine.Client", "CheatEngine.Client.Abstractions", "CheatEngine.Client.Core",
		"CheatEngine.Client.Extensions.DependencyInjection", "CheatEngine.Client.Fluent", "CheatEngine.Client.Hosting",
		"CheatEngine.Client.Templates"
	];

	/// <summary>The packages without build output, hence without a symbol package.</summary>
	private static readonly string[] _packagesWithoutSymbols = ["CheatEngine.Client", "CheatEngine.Client.Templates"];

	[Fact]
	public void SchemaRequiredAndEnumListsEqualTheValidatorConstants()
	{
		using JsonDocument schema = Packaging.SdkPin.ReadJson(SchemaPath);
		JsonElement root = schema.RootElement;

		Assert.Equal($"https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/{SchemaPath}", root.GetProperty("$id").GetString());
		Assert.Equal(_rootFields, JsonSchemaSubset.RequiredNames(root));
		Assert.Equal(_sourceFields, JsonSchemaSubset.RequiredNames(root, "source"));
		Assert.Equal(_packageFields, JsonSchemaSubset.RequiredNames(root, "client", "packages"));
		Assert.Equal(_consumedSdkFields, JsonSchemaSubset.RequiredNames(root, "consumedSdk"));
		Assert.Equal(_stages, root.GetProperty("properties").GetProperty("stage").GetProperty("enum").EnumerateArray().Select(static value => value.GetString()));
		Assert.Equal(_packageIds, root.GetProperty("$defs").GetProperty("packageId").GetProperty("enum").EnumerateArray().Select(static value => value.GetString()));
	}

	[Fact]
	public void ConfiguredTupleDocumentSatisfiesTheSchemaAndTheEncodingRules()
	{
		string path = TuplePath();
		string text = File.ReadAllText(path);
		using JsonDocument schema = Packaging.SdkPin.ReadJson(SchemaPath);
		using JsonDocument tuple = JsonDocument.Parse(text);
		JsonElement root = tuple.RootElement;

		IReadOnlyList<string> errors = JsonSchemaSubset.Validate(schema.RootElement, root);
		Assert.True(errors.Count == 0, $"{path} does not satisfy {SchemaPath}:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
		Assert.False(LocalPath().IsMatch(text), $"{path} contains an absolute local path.");
		List<string> unexpanded = [];
		CollectUnexpandedExpressions(root, "$", unexpanded);
		Assert.True(unexpanded.Count == 0,
			$"{path} records MSBuild expression text instead of evaluated values:{Environment.NewLine}{string.Join(Environment.NewLine, unexpanded)}");

		string version = root.GetProperty("client").GetProperty("version").GetString()!;
		bool published = root.GetProperty("stage").GetString() == "Published";
		Assert.Equal($"v{version}", root.GetProperty("source").GetProperty("tag").GetString());

		Dictionary<string, string> assets = root.GetProperty("assets").EnumerateArray()
			.ToDictionary(static asset => asset.GetProperty("name").GetString()!, static asset => asset.GetProperty("sha256").GetString()!, StringComparer.Ordinal);
		Assert.DoesNotContain("SHA256SUMS", assets.Keys);
		Assert.DoesNotContain(assets.Keys, static name => name.EndsWith(".tuple.json", StringComparison.Ordinal));

		List<string> ids = [];
		foreach (JsonElement package in root.GetProperty("client").GetProperty("packages").EnumerateArray())
		{
			string id = package.GetProperty("id").GetString()!;
			ids.Add(id);
			Assert.Equal(version, package.GetProperty("version").GetString());
			Assert.Equal(published, package.GetProperty("nugetOrgSignedSha256").ValueKind == JsonValueKind.String);
			Assert.Equal(Array.IndexOf(_packagesWithoutSymbols, id) < 0, package.GetProperty("snupkgSha256").ValueKind == JsonValueKind.String);
			Assert.True(assets.TryGetValue($"{id}.{version}.nupkg", out string? assetSha256), $"The assets do not list {id}.{version}.nupkg.");
			Assert.Equal(assetSha256, package.GetProperty("attestedAssetSha256").GetString());
		}

		Assert.Equal(_packageIds.Order(StringComparer.Ordinal), ids.Order(StringComparer.Ordinal));
		Assert.Equal(_packageIds.Order(StringComparer.Ordinal),
			root.GetProperty("sbom").GetProperty("documents").EnumerateArray().Select(static document => document.GetProperty("id").GetString()!).Order(StringComparer.Ordinal));
		Assert.Equal(_packageIds.Order(StringComparer.Ordinal),
			root.GetProperty("attestations").GetProperty("sbomBundles").EnumerateArray().Select(static bundle => bundle.GetProperty("id").GetString()!).Order(StringComparer.Ordinal));
	}

	[Fact]
	public void TupleConsumedSdkComesFromTheConsumedSdkIdentity()
	{
		using JsonDocument tuple = JsonDocument.Parse(File.ReadAllText(TuplePath()));
		using JsonDocument identity = Packaging.SdkPin.ReadJson("eng/sdk/consumed-sdk.json");
		JsonElement consumed = tuple.RootElement.GetProperty("consumedSdk");

		foreach (JsonProperty field in consumed.EnumerateObject())
		{
			Assert.True(JsonElement.DeepEquals(field.Value, JsonProjection(identity.RootElement.GetProperty(field.Name), field.Value)),
				$"consumedSdk.{field.Name} is {field.Value.GetRawText()}, but eng/sdk/consumed-sdk.json records {identity.RootElement.GetProperty(field.Name).GetRawText()}.");
		}

		using JsonDocument lockFile = Packaging.SdkPin.ReadJson(consumed.GetProperty("lockFile").GetString()!);
		string lockContentHash = lockFile.RootElement.GetProperty("dependencies").GetProperty("net10.0").GetProperty("CheatEngine.SDK")
			.GetProperty("contentHash").GetString()!;
		Assert.Equal(lockContentHash, consumed.GetProperty("contentHashSha512").GetString());
		Assert.Equal("ce-7.7.0.10621-x64-managed-hostfxr", tuple.RootElement.GetProperty("ceProfile").GetProperty("profileId").GetString());
	}

	[Fact]
	public void UnexpandedMSBuildExpressionsFailTheSchema()
	{
		using JsonDocument schema = Packaging.SdkPin.ReadJson(SchemaPath);
		JsonNode tuple = JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, ExamplePath)))!;
		tuple["build"]!["analysisLevel"] = "$(_CheatEngineClientPinnedAnalysisLevel)";
		tuple["build"]!["roslynFloor"] = "$(CheatEngineClientRoslynComponentFloor)";
		using JsonDocument document = JsonDocument.Parse(tuple.ToJsonString());

		IReadOnlyList<string> errors = JsonSchemaSubset.Validate(schema.RootElement, document.RootElement);

		Assert.Equal(2, errors.Count);
		Assert.Contains(errors, static error => error.StartsWith("$.build.analysisLevel: ", StringComparison.Ordinal));
		Assert.Contains(errors, static error => error.StartsWith("$.build.roslynFloor: ", StringComparison.Ordinal));
	}

	[Fact]
	public void CommittedExampleRecordsTheBuildOptionsOfThisTree()
	{
		using JsonDocument example = Packaging.SdkPin.ReadJson(ExamplePath);
		JsonElement build = example.RootElement.GetProperty("build");
		Dictionary<string, string> expected = new(StringComparer.Ordinal)
		{
			["analysisLevel"] = Packaging.PackageVersioningTests.EvaluatedBuildProperty("AnalysisLevel"),
			["roslynFloor"] = Packaging.PackageVersioningTests.EvaluatedBuildProperty("CheatEngineClientRoslynComponentFloor")
		};

		List<string> offenders = [];
		foreach ((string field, string value) in expected)
		{
			string? recorded = build.GetProperty(field).GetString();
			if (recorded != value)
			{
				offenders.Add($"build.{field} is '{recorded}', but Directory.Build.props evaluates to '{value}'");
			}
		}

		Assert.True(offenders.Count == 0,
			$"{ExamplePath} no longer describes this tree; regenerate it with eng/release/New-ClientTuple.ps1 (RELEASING.md) or correct the fields:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
	}

	/// <summary>Lists every string value that still holds an MSBuild property reference.</summary>
	private static void CollectUnexpandedExpressions(JsonElement element, string path, List<string> offenders)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				foreach (JsonProperty property in element.EnumerateObject())
				{
					CollectUnexpandedExpressions(property.Value, $"{path}.{property.Name}", offenders);
				}

				break;
			case JsonValueKind.Array:
				int index = 0;
				foreach (JsonElement item in element.EnumerateArray())
				{
					CollectUnexpandedExpressions(item, string.Create(CultureInfo.InvariantCulture, $"{path}[{index}]"), offenders);
					index++;
				}

				break;
			case JsonValueKind.String when element.GetString()!.Contains("$(", StringComparison.Ordinal):
				offenders.Add($"{path}: '{element.GetString()}'");
				break;
			default:
				break;
		}
	}

	private static string TuplePath()
	{
		string? configured = Environment.GetEnvironmentVariable(TuplePathVariable);
		return string.IsNullOrWhiteSpace(configured) ? Path.Combine(RepositoryRoot.Path, ExamplePath) : Path.GetFullPath(configured);
	}

	/// <summary>The identity value restricted to the fields the tuple copies (the tuple's nativeBridge is a subset).</summary>
	private static JsonElement JsonProjection(JsonElement identityValue, JsonElement tupleValue)
	{
		if (tupleValue.ValueKind != JsonValueKind.Object)
		{
			return identityValue;
		}

		Dictionary<string, JsonElement> projected = new(StringComparer.Ordinal);
		foreach (JsonProperty property in tupleValue.EnumerateObject())
		{
			projected[property.Name] = identityValue.GetProperty(property.Name);
		}

		return JsonSerializer.SerializeToElement(projected);
	}

	// Over the JSON text, where a backslash is escaped: C:\x appears as C:\\x.
	[GeneratedRegex(@"(?<![A-Za-z0-9_])[A-Za-z]:(?:\\\\|/)|file:/|\\\\Users\\\\|/Users/|/home/", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex LocalPath();
}
