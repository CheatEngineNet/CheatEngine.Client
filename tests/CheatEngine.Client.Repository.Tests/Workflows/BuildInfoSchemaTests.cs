using System.Text.Json;
using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.Workflows;

/// <summary>
/// The Release CI leg writes <c>build-info.json</c> (eng/ci/New-BuildInfo.ps1), the identity of a package build that the
/// release tuple is built from: source commit and tree, run, toolchain, runner, the seven packages with their hashes and
/// the consumed CheatEngine.SDK. The schema, the contract fields and the writer must not drift apart.
/// </summary>
public sealed class BuildInfoSchemaTests
{
	private const string SchemaPath = "eng/ci/build-info.v0.schema.json";
	private const string WriterPath = "eng/ci/New-BuildInfo.ps1";

	/// <summary>The contract fields of build-info v0 (Client variant: no toolchain or native bridge, SDK-only fields).</summary>
	private static readonly Dictionary<string, string[]> _requiredFields = new(StringComparer.Ordinal)
	{
		["$"] =
		[
			"schema", "repository", "commit", "treeHash", "ref", "event", "runId", "runAttempt", "runUrl", "pullRequest",
			"dotnetSdk", "globalJsonSha256", "runner", "packages", "consumedSdk", "createdUtc"
		],
		["$.pullRequest"] = ["number", "headSha"],
		["$.runner"] = ["label", "imageOs", "imageVersion"],
		["$.packages[]"] = ["id", "version", "file", "sha256", "sha512", "sbomEntry"],
		["$.consumedSdk"] = ["id", "version", "contentHash", "lockFile"]
	};

	[Fact]
	public void BuildInfoSchemaRequiresExactlyTheContractFields()
	{
		using JsonDocument schema = ReadSchema();
		JsonElement root = schema.RootElement;
		JsonElement properties = root.GetProperty("properties");

		Dictionary<string, JsonElement> objects = new(StringComparer.Ordinal)
		{
			["$"] = root,
			["$.pullRequest"] = properties.GetProperty("pullRequest").GetProperty("oneOf").EnumerateArray()
				.Single(static option => option.TryGetProperty("properties", out _)),
			["$.runner"] = properties.GetProperty("runner"),
			["$.packages[]"] = properties.GetProperty("packages").GetProperty("items"),
			["$.consumedSdk"] = properties.GetProperty("consumedSdk")
		};

		foreach ((string path, string[] expected) in _requiredFields)
		{
			JsonElement node = objects[path];
			string[] required = node.GetProperty("required").EnumerateArray().Select(static item => item.GetString()!).ToArray();
			string[] declared = node.GetProperty("properties").EnumerateObject().Select(static item => item.Name).ToArray();
			Assert.True(expected.SequenceEqual(required),
				$"{path} required = [{string.Join(", ", required)}], contract = [{string.Join(", ", expected)}].");
			Assert.True(expected.Order(StringComparer.Ordinal).SequenceEqual(declared.Order(StringComparer.Ordinal)),
				$"{path} declares [{string.Join(", ", declared)}]; every property must be a required contract field.");
		}

		Assert.Equal("cheatengine-build-info/v0", properties.GetProperty("schema").GetProperty("const").GetString());
		Assert.Equal("https://json-schema.org/draft/2020-12/schema", root.GetProperty("$schema").GetString());
		Assert.Equal("https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/" + SchemaPath,
			root.GetProperty("$id").GetString());
	}

	[Fact]
	public void BuildInfoWriterEmitsEveryRequiredField()
	{
		string writer = File.ReadAllText(Path.Combine(RepositoryRoot.Path, WriterPath));
		foreach ((string path, string[] fields) in _requiredFields)
		{
			foreach (string field in fields)
			{
				Assert.True(Regex.IsMatch(writer, $@"(?m)(^|[{{;]\s*)\s*{Regex.Escape(field)}\s*=\s"),
					$"{WriterPath} never assigns '{field}' ({path}).");
			}
		}

		Assert.Contains("-SchemaFile", writer, StringComparison.Ordinal);
		Assert.Contains("libs/CheatEngine.Client.Core/packages.lock.json", writer, StringComparison.Ordinal);
	}

	[Fact]
	public void BuildInfoSchemaRejectsAdditionalProperties()
	{
		using JsonDocument schema = ReadSchema();
		List<string> open = [];
		Visit(schema.RootElement, "$", open);

		Assert.True(open.Count == 0,
			$"Every object schema must set additionalProperties: false; open: {string.Join(", ", open)}.");
	}

	private static void Visit(JsonElement node, string path, List<string> open)
	{
		if (node.ValueKind == JsonValueKind.Object)
		{
			if (node.TryGetProperty("properties", out _) &&
				(!node.TryGetProperty("additionalProperties", out JsonElement additional) ||
				 additional.ValueKind != JsonValueKind.False))
			{
				open.Add(path);
			}

			foreach (JsonProperty property in node.EnumerateObject())
			{
				Visit(property.Value, $"{path}.{property.Name}", open);
			}
		}
		else if (node.ValueKind == JsonValueKind.Array)
		{
			int index = 0;
			foreach (JsonElement item in node.EnumerateArray())
			{
				Visit(item, $"{path}[{index++}]", open);
			}
		}
	}

	private static JsonDocument ReadSchema()
	{
		return JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, SchemaPath)));
	}
}
