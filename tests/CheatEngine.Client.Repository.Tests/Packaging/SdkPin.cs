using System.Text.Json;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.Packaging;

/// <summary>Reads the committed CheatEngine.SDK pin (<c>eng/CheatEngineSdk.props</c>) and its reviewed identity.</summary>
internal static class SdkPin
{
	/// <summary>The single source of the pin.</summary>
	internal const string PropsPath = "eng/CheatEngineSdk.props";

	/// <summary>The reviewed identity of the pinned package.</summary>
	internal const string IdentityPath = "eng/sdk/consumed-sdk.json";

	/// <summary>The schema of <see cref="IdentityPath"/>.</summary>
	internal const string IdentitySchemaPath = "eng/sdk/consumed-sdk.v0.schema.json";

	/// <summary>The SDK package id.</summary>
	internal const string PackageId = "CheatEngine.SDK";

	private static readonly Lazy<XDocument> _props = new(() => XDocument.Load(Path.Combine(RepositoryRoot.Path, PropsPath)));

	/// <summary><c>CheatEngineSdkVersion</c>.</summary>
	internal static string Version => Property("CheatEngineSdkVersion");

	/// <summary><c>CheatEngineSdkUpperBound</c>.</summary>
	internal static string UpperBound => Property("CheatEngineSdkUpperBound");

	/// <summary>The unexpanded <c>CheatEngineSdkVersionRange</c> expression.</summary>
	internal static string RangeExpression => Property("CheatEngineSdkVersionRange");

	/// <summary><c>CheatEngineSdkVersionRange</c> as MSBuild evaluates it, for example <c>[1.0.0,2.0.0)</c>.</summary>
	internal static string Range => RangeExpression
		.Replace("$(CheatEngineSdkVersion)", Version, StringComparison.Ordinal)
		.Replace("$(CheatEngineSdkUpperBound)", UpperBound, StringComparison.Ordinal);

	/// <summary><c>_CheatEngineClientSupportedSdkMajor</c>.</summary>
	internal static string SupportedMajor => Property("_CheatEngineClientSupportedSdkMajor");

	/// <summary>Parses a repository JSON file.</summary>
	internal static JsonDocument ReadJson(string repositoryRelativePath)
	{
		return JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, repositoryRelativePath)));
	}

	/// <summary>Removes the spaces NuGet inserts in a normalized range, so <c>[1.0.0, 2.0.0)</c> equals <c>[1.0.0,2.0.0)</c>.</summary>
	internal static string NormalizeRange(string range)
	{
		return range.Replace(" ", string.Empty, StringComparison.Ordinal);
	}

	private static string Property(string name)
	{
		XElement[] elements = _props.Value.Descendants(name).ToArray();
		Assert.True(elements.Length == 1, $"{PropsPath} must define {name} exactly once, but defines it {elements.Length} times.");
		return elements[0].Value.Trim();
	}
}
