using System.Text.Json;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.Packaging;

/// <summary>Reads the committed CheatEngine.SDK pin (<c>eng/CheatEngineSdk.props</c>).</summary>
internal static class SdkPin
{
	/// <summary>The single source of the pin.</summary>
	internal const string PropsPath = "eng/CheatEngineSdk.props";

	/// <summary>The SDK package id.</summary>
	internal const string PackageId = "CheatEngine.SDK";

	private static readonly Lazy<XDocument> _props = new(() => XDocument.Load(Path.Combine(RepositoryRoot.Path, PropsPath)));

	/// <summary><c>CheatEngineSdkVersion</c>.</summary>
	internal static string Version => Property("CheatEngineSdkVersion");

	/// <summary><c>CheatEngineSdkUpperBound</c>.</summary>
	internal static string UpperBound => Property("CheatEngineSdkUpperBound");

	/// <summary>The unexpanded <c>CheatEngineSdkVersionRange</c> expression.</summary>
	internal static string RangeExpression => Property("CheatEngineSdkVersionRange");

	/// <summary><c>_CheatEngineClientSupportedSdkMajor</c>.</summary>
	internal static string SupportedMajor => Property("_CheatEngineClientSupportedSdkMajor");

	/// <summary>Parses a repository JSON file.</summary>
	internal static JsonDocument ReadJson(string repositoryRelativePath)
	{
		return JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, repositoryRelativePath)));
	}

	private static string Property(string name)
	{
		XElement[] elements = _props.Value.Descendants(name).ToArray();
		Assert.True(elements.Length == 1, $"{PropsPath} must define {name} exactly once, but defines it {elements.Length} times.");
		return elements[0].Value.Trim();
	}
}
