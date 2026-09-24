using System.Reflection.Metadata;

using CheatEngine.Client.SourceGenerators.Lua;
using CheatEngine.Client.Tests.Architecture;
using CheatEngine.SDK.Engine.Values;

using ReflectionAssembly = System.Reflection.Assembly;

namespace CheatEngine.Client.Tests.SdkContract;

/// <summary>
///     Consumer contracts against the consumed CheatEngine.SDK package, the pin of <c>eng/CheatEngineSdk.props</c>
///     (audit Q48, ADR-10, A11-17, A11-30): an SDK change that the Client did not adapt to is detected here, before
///     publication, instead of by a plugin at runtime.
/// </summary>
/// <remarks>
///     Q48 is <em>Partial</em> by decision: the Client has no next-SDK build leg, and no job builds it against an
///     unreleased CheatEngine.SDK. Q48 is covered by the consumer-contract tests only: these C1 tests, the compile-only
///     <see cref="SdkApiUsage" /> map, and the CHEATENGINECLIENT9016 version-range guard.
/// </remarks>
public sealed class SdkConsumerContractTests
{
	private const string CompilerServicesNamespace = "CheatEngine.SDK.Lua.CompilerServices.";

	/// <summary>The single documented member-level debt of the Abstractions public surface.</summary>
	private const string RuntimeCapabilitiesDebt = "CheatEngine.SDK.Engine.Runtime.RuntimeCapabilities";

	[Fact]
	[Trait("Qualification", "Q48")]
	public void AllowlistedSdkTypesResolveAndBothAllowlistsAreEqual()
	{
		ReflectionAssembly[] sdkAssemblies = GetReferencedSdkAssemblies();
		List<string> unresolved = [];
		foreach (string name in ApprovedSdkClientTypes.Names)
		{
			Type? type = sdkAssemblies.Select(assembly => assembly.GetType(name, throwOnError: false))
				.FirstOrDefault(static candidate => candidate is not null);
			if (type is null || !type.IsPublic || !type.IsValueType)
			{
				unresolved.Add(name);
			}
		}

		// A stale entry, such as an SDK AOB pattern type the consumed package does not ship, fails here.
		Assert.True(unresolved.Count == 0,
			"These allowlisted SDK types do not resolve to public value types in the consumed CheatEngine.SDK package: " +
			string.Join(", ", unresolved));
		Assert.Equal(ApprovedSdkClientTypes.Names.Order(StringComparer.Ordinal), ApprovedSdkClientTypes.Names);
		Assert.Equal(ApprovedSdkClientTypes.Names.Length,
			ApprovedSdkClientTypes.Names.Distinct(StringComparer.Ordinal).Count());
		AssertTheGeneratorUsesTheSharedAllowlistFile();
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void PublicSdkTypeReferencesOfAbstractionsFluentAndDependencyInjectionAreAllowlisted()
	{
		HashSet<string> allowed = new(ApprovedSdkClientTypes.Names, StringComparer.Ordinal) { RuntimeCapabilitiesDebt };
		List<string> violations = [];
		foreach (string assembly in new[]
				 {
					 "CheatEngine.Client.Abstractions", "CheatEngine.Client.Fluent",
					 "CheatEngine.Client.Extensions.DependencyInjection"
				 })
		{
			ClientAssemblyCatalog.ReadMetadata(assembly, (reader, _) =>
			{
				foreach (TypeReferenceHandle handle in reader.TypeReferences)
				{
					MetadataSurface.TypeIdentity type = MetadataSurface.ResolveType(reader, handle);
					if (type.IsSdk && !allowed.Contains(type.FullName))
					{
						violations.Add($"{assembly} references non-allowlisted SDK type {type.FullName}.");
					}
				}
			});
		}

		Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void ConsumedSdkSurfaceMatchesTheCommittedInventory()
	{
		string[] actual = SdkSurfaceReader.ReadConsumedSurface();
		string[] added = actual.Except(ConsumedSdkSurface.Lines, StringComparer.Ordinal).ToArray();
		string[] removed = ConsumedSdkSurface.Lines.Except(actual, StringComparer.Ordinal).ToArray();

		Assert.True(added.Length == 0 && removed.Length == 0,
			"The CheatEngine.SDK surface consumed by the Client changed. Review the change, then update " +
			"tests/CheatEngine.Client.Tests/SdkContract/ConsumedSdkSurface.cs (and SdkApiUsage.cs for a new member)." +
			Environment.NewLine + "Added:" + Environment.NewLine + string.Join(Environment.NewLine, added) +
			Environment.NewLine + "Removed:" + Environment.NewLine + string.Join(Environment.NewLine, removed));
		Assert.Equal(ConsumedSdkSurface.Lines.Order(StringComparer.Ordinal), ConsumedSdkSurface.Lines);
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void SdkApiUsageCoversEveryConsumedSdkMember()
	{
		HashSet<string> covered = new(
			SdkSurfaceReader.ReadMemberReferencesFrom(typeof(SdkApiUsage).Assembly.Location,
				typeof(SdkApiUsage).FullName!),
			StringComparer.Ordinal);
		string[] consumedMembers = ConsumedSdkSurface.Lines
			.Select(static line => line.Split(' ', 3))
			.Where(static parts => parts[1] == "M" &&
								   !parts[2].StartsWith(CompilerServicesNamespace, StringComparison.Ordinal))
			.Select(static parts => parts[2])
			.Distinct(StringComparer.Ordinal)
			.ToArray();

		string[] missing = consumedMembers.Where(member => !covered.Contains(member)).ToArray();

		Assert.NotEmpty(consumedMembers);
		Assert.True(missing.Length == 0,
			"SdkApiUsage.cs does not reference these consumed SDK members with their exact signature:" +
			Environment.NewLine + string.Join(Environment.NewLine, missing));
	}

	private static ReflectionAssembly[] GetReferencedSdkAssemblies()
	{
		return
		[
			.. ClientAssemblyCatalog.LoadAll()
				.SelectMany(static assembly => assembly.GetReferencedAssemblies())
				.Where(static name => name.Name is not null && MetadataSurface.IsSdkAssembly(name.Name))
				.DistinctBy(static name => name.Name, StringComparer.Ordinal)
				.Select(ReflectionAssembly.Load),
			typeof(Address).Assembly
		];
	}

	/// <summary>Both consumers compile the same file; this guards against a second, drifting copy in the generator.</summary>
	private static void AssertTheGeneratorUsesTheSharedAllowlistFile()
	{
		string root = FindRepositoryRoot();
		string generator = File.ReadAllText(Path.Combine(root, "source-generators",
			"CheatEngine.Client.SourceGenerators.Lua", "CheatEngineLuaGenerator.cs"));
		string project = File.ReadAllText(Path.Combine(root, "tests", "CheatEngine.Client.Tests",
			"CheatEngine.Client.Tests.csproj"));

		Assert.Contains("ApprovedSdkClientTypes.Names", generator, StringComparison.Ordinal);
		Assert.DoesNotContain("\"CheatEngine.SDK.Engine.Values.Address\"", generator, StringComparison.Ordinal);
		Assert.Contains("CheatEngine.Client.SourceGenerators.Lua/ApprovedSdkClientTypes.cs", project,
			StringComparison.Ordinal);
	}

	private static string FindRepositoryRoot()
	{
		for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
			 directory is not null;
			 directory = directory.Parent)
		{
			if (File.Exists(Path.Combine(directory.FullName, "CheatEngine.Client.slnx")))
			{
				return directory.FullName;
			}
		}

		throw new InvalidOperationException("CheatEngine.Client.slnx was not found above the test output directory.");
	}
}
