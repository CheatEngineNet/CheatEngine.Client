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
		[ClientCapabilityId.Assembly.Value] = ["Q32"]
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
	public void UnsafeLuaExecutionIsNeverQualifiedAndIsTheOnlyPolicyOptIn()
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
	public void OnlyProcessSelectionTakesItsHostGateFromTheOpenedProcessProbe()
	{
		ClientCapabilityDescriptor probed = Assert.Single(ClientCapabilityCatalog.Entries,
			static entry => entry.Host == CapabilityHostSource.OpenedProcess);

		Assert.Equal(ClientCapabilityId.ProcessSelection, probed.Id);
	}

	[Fact]
	public void TheContractOnlyCapabilitiesAreValueScanningAllocationsAndAssembly()
	{
		Assert.Equal(
			[ClientCapabilityId.ValueScanning, ClientCapabilityId.Allocations, ClientCapabilityId.Assembly],
			ClientCapabilityCatalog.Entries
				.Where(static entry => entry.Implementation == CapabilityImplementation.ContractOnly)
				.Select(static entry => entry.Id));
	}

	[GeneratedRegex(@"^Q\d{2}(\.[a-z])?$", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex ScenarioId();
}
