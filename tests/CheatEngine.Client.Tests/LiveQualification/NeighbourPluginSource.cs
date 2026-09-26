using System.Globalization;
using System.Text.RegularExpressions;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>
///     The plain CheatEngine.SDK 1.x plugin that S5 loads next to the Client plugins (Q10). It is generated in a temporary
///     consumer at run time, never committed, and references CheatEngine.SDK 1.x from nuget.org and nothing of the Client.
///     Its only export, <see cref="IdentityGlobal" />, reports its identity as booleans (plan A12): the SDK Hosting major it
///     loaded, whether its native bridge equals the one its package ships, whether it runs outside the default load
///     context, and whether a CheatEngine.SDK.Hosting of major 2 is loaded in another context. No version literal, hash or
///     path of the retired package appears in its output, so no receipt can carry one.
/// </summary>
/// <remarks>
///     Its shape follows the quick start of the CheatEngine.SDK 1.x README: a <c>CheatEnginePlugin</c> with
///     <c>[CheatEnginePlugin]</c>, whose <c>OnEnable</c> and <c>OnDisable</c> register and unregister the generated
///     <c>[LuaFunction]</c> set through <c>LuaRuntime.AcquireState()</c>. That shape was compared by hand, once, with the
///     SDK's published 1.x sample; <c>NeighbourPluginSourceTests</c> checks the generated text without reading the SDK
///     repository.
/// </remarks>
internal static partial class NeighbourPluginSource
{
	/// <summary>The CheatEngine.SDK version the neighbour references: the previous major.</summary>
	internal const string SdkVersion = "1.0.0";

	/// <summary>The assembly name of the neighbour.</summary>
	internal const string AssemblyName = "CheatEngine.Client.Qualification.SdkNeighbour";

	/// <summary>The one Lua global the neighbour exports.</summary>
	internal const string IdentityGlobal = "cheatengine_client_qualification_sdk1_identity";

	/// <summary>The name Cheat Engine shows for the neighbour.</summary>
	internal const string DisplayName = "CheatEngine.Client Qualification SDK 1.x Neighbour";

	/// <summary>The project of the neighbour: the SDK 1.x package, x64, no lock file, no Client reference.</summary>
	internal static string Project()
	{
		return $"""
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net10.0</TargetFramework>
			    <LangVersion>14.0</LangVersion>
			    <Nullable>enable</Nullable>
			    <ImplicitUsings>enable</ImplicitUsings>
			    <AssemblyName>{AssemblyName}</AssemblyName>
			    <RootNamespace>QualificationSdkNeighbour</RootNamespace>
			    <PlatformTarget>x64</PlatformTarget>
			    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
			    <EnableDynamicLoading>true</EnableDynamicLoading>
			    <RestorePackagesWithLockFile>false</RestorePackagesWithLockFile>
			  </PropertyGroup>
			  <ItemGroup>
			    <PackageReference Include="CheatEngine.SDK" Version="{SdkVersion}" />
			  </ItemGroup>
			</Project>
			""";
	}

	/// <summary>
	///     The plugin source, with the SHA-256 of the bridge the neighbour's package ships, measured by the runner, as the
	///     value its bridge is compared with.
	/// </summary>
	internal static string Source(string packagedBridgeSha256)
	{
		if (!Sha256().IsMatch(packagedBridgeSha256))
		{
			throw new ArgumentException("The packaged bridge hash must be 64 lower-case hex digits.", nameof(packagedBridgeSha256));
		}

		return string.Create(CultureInfo.InvariantCulture, $$""""
			using System.Reflection;
			using System.Runtime.Loader;
			using System.Security.Cryptography;

			using CheatEngine.SDK.Annotations.Lua;
			using CheatEngine.SDK.Annotations.Plugin;
			using CheatEngine.SDK.Hosting.Plugin;
			using CheatEngine.SDK.Lua.Runtime;

			namespace QualificationSdkNeighbour;

			[CheatEnginePlugin("{{DisplayName}}")]
			public sealed class NeighbourPlugin : CheatEnginePlugin
			{
			    protected override void OnEnable() => NeighbourCommands.RegisterLuaFunctions(LuaRuntime.AcquireState());

			    protected override void OnDisable() => NeighbourCommands.UnregisterLuaFunctions(LuaRuntime.AcquireState());
			}

			internal static partial class NeighbourCommands
			{
			    private const string PackagedBridgeSha256 = "{{packagedBridgeSha256}}";

			    [LuaFunction("{{IdentityGlobal}}")]
			    public static string Identity()
			    {
			        Assembly hosting = typeof(CheatEnginePlugin).Assembly;
			        AssemblyLoadContext? own = AssemblyLoadContext.GetLoadContext(typeof(NeighbourPlugin).Assembly);
			        bool otherMajorLoaded = AssemblyLoadContext.All
			            .Where(context => !ReferenceEquals(context, own))
			            .SelectMany(static context => context.Assemblies)
			            .Any(static assembly => assembly.GetName() is { Name: "CheatEngine.SDK.Hosting", Version.Major: 2 });
			        return "Sdk1Neighbour=Answering" +
			               "; SdkHostingMajorIs1=" + (hosting.GetName().Version?.Major == 1) +
			               "; BridgeMatchesPackage=" + BridgeMatchesPackage() +
			               "; OwnLoadContextIsNotDefault=" + (own is not null && !ReferenceEquals(own, AssemblyLoadContext.Default)) +
			               "; SdkHostingMajor2LoadedElsewhere=" + otherMajorLoaded;
			    }

			    private static bool BridgeMatchesPackage()
			    {
			        string? folder = Path.GetDirectoryName(typeof(NeighbourPlugin).Assembly.Location);
			        string bridge = folder is null ? string.Empty : Path.Combine(folder, "cheatengine-sdk-lua-bridge.dll");
			        if (!File.Exists(bridge))
			        {
			            return false;
			        }

			        using FileStream stream = File.OpenRead(bridge);
			        return string.Equals(Convert.ToHexStringLower(SHA256.HashData(stream)), PackagedBridgeSha256, StringComparison.Ordinal);
			    }
			}

			"""");
	}

	/// <summary>The identity booleans the neighbour's answer carries, by name; empty when the answer has another shape.</summary>
	internal static IReadOnlyDictionary<string, bool> ParseIdentity(string answer)
	{
		ArgumentNullException.ThrowIfNull(answer);
		Dictionary<string, bool> facts = new(StringComparer.Ordinal);
		if (!answer.StartsWith("Sdk1Neighbour=Answering", StringComparison.Ordinal))
		{
			return facts;
		}

		foreach (Match match in IdentityFact().Matches(answer))
		{
			facts[match.Groups["name"].Value] = string.Equals(match.Groups["value"].Value, "True", StringComparison.Ordinal);
		}

		return facts;
	}

	[GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex Sha256();

	[GeneratedRegex(@"; (?<name>[A-Za-z0-9]+)=(?<value>True|False)\b", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex IdentityFact();
}
