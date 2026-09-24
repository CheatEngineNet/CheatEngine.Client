using System.Reflection;
using System.Text.RegularExpressions;

using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Runtime;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed partial class ClientCapabilityCatalogTests
{
	/// <summary>The scenarios each capability's qualification gate requires (plan L7, table of commit 4).</summary>
	private static readonly Dictionary<string, string[]> ExpectedScenarios = new(StringComparer.Ordinal)
	{
		[ClientCapabilityId.ProcessSelection.Value] = ["Q30.a", "Q31", "Q32"],
		[ClientCapabilityId.TypedMemory.Value] = ["Q20", "Q21", "Q33"],
		[ClientCapabilityId.PatternScanning.Value] = ["Q27", "Q28", "Q29"],
		[ClientCapabilityId.ValueScanning.Value] = ["Q25", "Q26"],
		[ClientCapabilityId.Inspection.Value] = ["Q16.b", "Q28"],
		[ClientCapabilityId.Tables.Value] = ["Q34"],
		[ClientCapabilityId.ProtectedLua.Value] = ["Q05", "Q16", "Q19"],
		[ClientCapabilityId.UnsafeLuaExecution.Value] = [],
		[ClientCapabilityId.Allocations.Value] = ["Q30.a"],
		[ClientCapabilityId.Assembly.Value] = ["Q32"],
		[ClientCapabilityId.AutoAssemblerPatches.Value] = ["Q35", "Q44"]
	};

	[Fact]
	public void EveryClientCapabilityAppearsExactlyOnce()
	{
		string[] declared =
		[
			.. typeof(ClientCapabilityId).GetProperties(BindingFlags.Public | BindingFlags.Static)
				.Where(static property => property.PropertyType == typeof(ClientCapabilityId))
				.Select(static property => ((ClientCapabilityId) property.GetValue(null)!).Value)
		];
		string[] catalogued = [.. ClientCapabilityCatalog.Entries.Select(static entry => entry.Id.Value)];

		Assert.NotEmpty(declared);
		Assert.Equal(catalogued.Length, catalogued.Distinct(StringComparer.Ordinal).Count());
		Assert.Equal(declared.Order(StringComparer.Ordinal), catalogued.Order(StringComparer.Ordinal));
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void EachCapabilityRequiresItsDocumentedScenarios()
	{
		Assert.Equal(ExpectedScenarios.Count, ClientCapabilityCatalog.Entries.Length);
		foreach (ClientCapabilityDescriptor entry in ClientCapabilityCatalog.Entries)
		{
			Assert.True(ExpectedScenarios.TryGetValue(entry.Id.Value, out string[]? expected), entry.Id.Value);
			Assert.Equal(expected, entry.RequiredScenarios);
		}
	}

	[Fact]
	public void ScenarioIdentifiersAreWellFormedAndDistinctWithinARow()
	{
		foreach (ClientCapabilityDescriptor entry in ClientCapabilityCatalog.Entries)
		{
			Assert.False(entry.RequiredScenarios.IsDefault, entry.Id.Value);
			Assert.All(entry.RequiredScenarios, static scenario => Assert.Matches(ScenarioId(), scenario));
			Assert.Equal(entry.RequiredScenarios.Length,
				entry.RequiredScenarios.Distinct(StringComparer.Ordinal).Count());
		}
	}

	[Fact]
	public void UnsafeLuaExecutionIsNeverQualifiedAndFollowsItsOwnOptIn()
	{
		ClientCapabilityDescriptor unsafeLua = Assert.Single(ClientCapabilityCatalog.Entries,
			static entry => entry.Policy == CapabilityPolicySource.UnsafeLuaExecutionOptIn);

		Assert.Equal(ClientCapabilityId.UnsafeLuaExecution, unsafeLua.Id);
		Assert.Empty(unsafeLua.RequiredScenarios);
		Assert.All(
			ClientCapabilityCatalog.Entries.Where(static entry => entry.Id != ClientCapabilityId.UnsafeLuaExecution),
			static entry => Assert.NotEmpty(entry.RequiredScenarios));
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void AutoAssemblerPatchesAreOperationalBehindTheirOwnOptInAndTheOnlyOtherPolicyGate()
	{
		ClientCapabilityDescriptor patches = Assert.Single(ClientCapabilityCatalog.Entries,
			static entry => entry.Policy == CapabilityPolicySource.AutoAssemblerPatchesOptIn);

		Assert.Equal(ClientCapabilityId.AutoAssemblerPatches, patches.Id);
		Assert.Equal(CapabilityImplementation.Operational, patches.Implementation);
		Assert.Equal(CapabilityHostSource.NotProbed, patches.Host);
		Assert.Equal(["Q35", "Q44"], patches.RequiredScenarios);
		Assert.Equal(
			[ClientCapabilityId.UnsafeLuaExecution, ClientCapabilityId.AutoAssemblerPatches],
			ClientCapabilityCatalog.Entries.Where(static entry => entry.Policy != CapabilityPolicySource.NotRequired)
				.Select(static entry => entry.Id));
	}

	[Fact]
	public void OnlyProcessSelectionTakesItsHostGateFromTheSdkSelectedProcessObservation()
	{
		ClientCapabilityDescriptor probed = Assert.Single(ClientCapabilityCatalog.Entries,
			static entry => entry.Host == CapabilityHostSource.SdkSelectedProcess);

		Assert.Equal(ClientCapabilityId.ProcessSelection, probed.Id);
	}

	[Fact]
	public void TheContractOnlyCapabilityIsAssembly()
	{
		Assert.Equal(
			[ClientCapabilityId.Assembly],
			ClientCapabilityCatalog.Entries
				.Where(static entry => entry.Implementation == CapabilityImplementation.ContractOnly)
				.Select(static entry => entry.Id));
	}

	[Fact]
	public void OnlyOperationalCapabilitiesCanBeExperimentalAndValueScanningIsCeclient5001()
	{
		Assert.Equal(
			[
				(ClientCapabilityId.ValueScanning, "CECLIENT5001"), (ClientCapabilityId.Allocations, "CECLIENT5002"),
				(ClientCapabilityId.AutoAssemblerPatches, "CECLIENT5004")
			],
			ClientCapabilityCatalog.Entries
				.Where(static entry => entry.ExperimentalDiagnosticId is not null)
				.Select(static entry => (entry.Id, entry.ExperimentalDiagnosticId!)));
		Assert.All(ClientCapabilityCatalog.Entries.Where(static entry => entry.ExperimentalDiagnosticId is not null),
			static entry => Assert.Equal(CapabilityImplementation.Operational, entry.Implementation));
	}

	[GeneratedRegex(@"^Q\d{2}(\.[a-z])?$", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex ScenarioId();
}
