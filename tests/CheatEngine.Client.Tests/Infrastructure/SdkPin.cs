using System.Globalization;
using System.Xml.Linq;

namespace CheatEngine.Client.Tests.Infrastructure;

/// <summary>
/// Reads the committed CheatEngine.SDK pin of <c>eng/CheatEngineSdk.props</c>. Guard cases derive the versions they
/// probe (the next major, a prerelease, a drifted patch, a version below the declared range) from it, so they keep
/// testing the same boundaries after the pin moves.
/// </summary>
internal static class SdkPin
{
	/// <summary>The single source of the pin.</summary>
	internal const string PropsPath = "eng/CheatEngineSdk.props";

	private static readonly Lazy<XDocument> Props =
		new(static () => XDocument.Load(RepositoryLayout.Combine(PropsPath)), LazyThreadSafetyMode.ExecutionAndPublication);

	/// <summary><c>CheatEngineSdkVersion</c>, the inclusive lower bound of the declared range.</summary>
	internal static string Version => Property("CheatEngineSdkVersion");

	/// <summary><c>CheatEngineSdkUpperBound</c>, the exclusive upper bound of the declared range.</summary>
	internal static string UpperBound => Property("CheatEngineSdkUpperBound");

	/// <summary>The major version of the pin.</summary>
	internal static int Major => Component(Version, 0);

	/// <summary>The minor version of the pin.</summary>
	internal static int Minor => Component(Version, 1);

	/// <summary>The patch version of the pin.</summary>
	internal static int Patch => Component(Version, 2);

	/// <summary>The major version of the upper bound: the first CheatEngine.SDK major this Client does not support.</summary>
	internal static int UpperMajor => Component(UpperBound, 0);

	private static string Property(string name)
	{
		XElement[] elements = Props.Value.Descendants(name).ToArray();
		Assert.True(elements.Length == 1, $"{PropsPath} must define {name} exactly once, but defines it {elements.Length} times.");
		return elements[0].Value.Trim();
	}

	private static int Component(string version, int index)
	{
		string[] components = version.Split('.');
		Assert.True(components.Length == 3, $"'{version}' in {PropsPath} is not a stable major.minor.patch version.");
		return int.Parse(components[index], NumberStyles.None, CultureInfo.InvariantCulture);
	}
}
