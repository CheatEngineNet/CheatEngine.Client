using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>
///     The generated CheatEngine.SDK 1.x neighbour of S5, as text: a plain SDK plugin with one Lua export, no Client
///     reference, and an answer made of booleans only. The shape was compared once, by hand, with the SDK's published
///     1.x quick start; this test never reads the SDK repository.
/// </summary>
public sealed partial class NeighbourPluginSourceTests
{
	private const string BridgeSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

	[Fact]
	public void TheNeighbourIsAPlainSdkPluginWithOneExport()
	{
		string source = NeighbourPluginSource.Source(BridgeSha256);

		Assert.Contains($"[CheatEnginePlugin(\"{NeighbourPluginSource.DisplayName}\")]", source, StringComparison.Ordinal);
		Assert.Contains("public sealed class NeighbourPlugin : CheatEnginePlugin", source, StringComparison.Ordinal);
		Assert.Contains("NeighbourCommands.RegisterLuaFunctions(LuaRuntime.AcquireState())", source, StringComparison.Ordinal);
		Assert.Contains("NeighbourCommands.UnregisterLuaFunctions(LuaRuntime.AcquireState())", source, StringComparison.Ordinal);
		Assert.Equal([NeighbourPluginSource.IdentityGlobal], LuaFunction().Matches(source).Select(static match => match.Groups["name"].Value));
		Assert.Contains($"PackagedBridgeSha256 = \"{BridgeSha256}\"", source, StringComparison.Ordinal);
	}

	[Fact]
	public void TheNeighbourReferencesNothingOfTheClient()
	{
		string source = NeighbourPluginSource.Source(BridgeSha256);
		XDocument project = XDocument.Parse(NeighbourPluginSource.Project());

		Assert.DoesNotContain("CheatEngine.Client.", source, StringComparison.Ordinal);
		Assert.All(UsingDirective().Matches(source), static match =>
			Assert.Matches(@"^(System(\.[A-Za-z]+)*|CheatEngine\.SDK(\.[A-Za-z]+)*)$", match.Groups["name"].Value));
		XElement reference = Assert.Single(project.Descendants("PackageReference"));
		Assert.Equal(("CheatEngine.SDK", NeighbourPluginSource.SdkVersion),
			((string?) reference.Attribute("Include"), (string?) reference.Attribute("Version")));
		Assert.Equal("false", project.Descendants("RestorePackagesWithLockFile").Single().Value);
		Assert.Equal("x64", project.Descendants("PlatformTarget").Single().Value);
		Assert.Empty(project.Descendants("ProjectReference"));
	}

	[Fact]
	public void TheAnswerCarriesBooleansOnly()
	{
		string source = NeighbourPluginSource.Source(BridgeSha256);
		const string Answer = "Sdk1Neighbour=Answering; SdkHostingMajorIs1=True; BridgeMatchesPackage=False; " +
							  "OwnLoadContextIsNotDefault=True; SdkHostingMajor2LoadedElsewhere=True";

		string[] fields = [.. AnswerField().Matches(source).Select(static match => match.Groups["name"].Value)];
		Assert.Equal(["SdkHostingMajorIs1", "BridgeMatchesPackage", "OwnLoadContextIsNotDefault", "SdkHostingMajor2LoadedElsewhere"],
			fields);
		IReadOnlyDictionary<string, bool> parsed = NeighbourPluginSource.ParseIdentity(Answer);
		Assert.Equal(fields.Order(StringComparer.Ordinal), parsed.Keys.Order(StringComparer.Ordinal));
		Assert.False(parsed["BridgeMatchesPackage"]);
		Assert.True(parsed["SdkHostingMajorIs1"]);
		Assert.Empty(NeighbourPluginSource.ParseIdentity("Plugin=A; SdkHostingMajorIs1=True"));
	}

	[Theory]
	[InlineData("")]
	[InlineData("0123")]
	[InlineData("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF")]
	[InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde\"")]
	public void OnlyALowerCaseSha256IsEmbedded(string hash)
	{
		Assert.Throws<ArgumentException>(() => NeighbourPluginSource.Source(hash));
	}

	[GeneratedRegex("""\[LuaFunction\("(?<name>[^"]+)"\)\]""", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex LuaFunction();

	[GeneratedRegex(@"^using (?<name>[A-Za-z.]+);", RegexOptions.CultureInvariant | RegexOptions.Multiline, 1000)]
	private static partial Regex UsingDirective();

	[GeneratedRegex("""; (?<name>[A-Za-z0-9]+)=" \+""", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex AnswerField();
}
