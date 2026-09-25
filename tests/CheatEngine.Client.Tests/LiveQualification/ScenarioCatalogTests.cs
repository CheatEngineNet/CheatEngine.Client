using System.Reflection;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

using CheatEngine.Client.Runtime;
using CheatEngine.Client.Tests.SdkContract;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>
///     The live scenario catalog is complete and coherent: every scenario has checks in the sessions it names, every
///     check belongs to a catalogued scenario and session, the release gate is covered, and the capability map names
///     every Client capability with catalogued scenarios only (its equality with <c>ClientCapabilityCatalog</c> is proven
///     by <c>ClientCapabilityCatalogTests</c> of Core, which links this catalog).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class ScenarioCatalogTests
{
	[Fact]
	public void ScenarioIdsAreWellFormedDistinctAndOrdered()
	{
		string[] ids = [.. ScenarioCatalog.Scenarios.Select(static scenario => scenario.Id)];

		Assert.All(ids, static id => Assert.Matches(ScenarioId(), id));
		Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
		Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
		Assert.All(ScenarioCatalog.Scenarios, static scenario => Assert.Contains(scenario.Level, ReceiptLedger.Levels));
	}

	[Fact]
	public void EveryScenarioHasChecksInEachSessionItNamesAndOnlyThere()
	{
		HashSet<string> sessions = new(SessionPlans.All.Select(static plan => plan.Session), StringComparer.Ordinal);
		foreach (QualificationScenario scenario in ScenarioCatalog.Scenarios)
		{
			Assert.All(scenario.Sessions, session => Assert.Contains(session, sessions));
			string[] checkedSessions =
			[
				.. ScenarioEvaluators.Checks.Where(check => check.Scenario == scenario.Id).Select(static check => check.Session)
					.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
			];
			Assert.Equal(scenario.Sessions.Order(StringComparer.Ordinal), checkedSessions);
		}
	}

	[Fact]
	public void EveryCheckNamesACatalogScenarioAndIsUniqueInItsSession()
	{
		Assert.All(ScenarioEvaluators.Checks, static check => Assert.True(ScenarioCatalog.Contains(check.Scenario), check.Scenario));
		(string, string, string)[] keys =
			[.. ScenarioEvaluators.Checks.Select(static check => (check.Scenario, check.Session, check.Name))];
		Assert.Equal(keys.Length, keys.Distinct().Count());
		Assert.All(ScenarioEvaluators.Checks, static check => Assert.False(string.IsNullOrWhiteSpace(check.Expectation)));
	}

	[Fact]
	public void TheReleaseGateIsCataloguedAndCoveredByLiveChecks()
	{
		Assert.Equal(["Q09", "Q10", "Q40", "Q43", "Q44", "Q45", "Q46"], ScenarioCatalog.ReleaseGate);
		Assert.All(ScenarioCatalog.ReleaseGate, static id =>
			Assert.Contains(ScenarioEvaluators.Checks, check => check.Scenario == id));
		Assert.Equal("Q48", ScenarioCatalog.ContinuousIntegrationReleaseGate);
		Assert.False(ScenarioCatalog.Contains(ScenarioCatalog.ContinuousIntegrationReleaseGate));
	}

	[Fact]
	public void TheContinuousIntegrationReleaseGateIsProvenByTheSdkConsumerContractTests()
	{
		IEnumerable<CustomAttributeData> traits = typeof(SdkConsumerContractTests)
			.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
			.SelectMany(static method => method.GetCustomAttributesData())
			.Where(static attribute => attribute.AttributeType == typeof(TraitAttribute));

		Assert.Contains(traits, static trait => (string?) trait.ConstructorArguments[0].Value == "Qualification" &&
												(string?) trait.ConstructorArguments[1].Value == ScenarioCatalog.ContinuousIntegrationReleaseGate);
	}

	[Fact]
	public void TheCapabilityMapNamesEveryClientCapabilityWithCatalogScenarios()
	{
		string[] capabilities =
		[
			.. typeof(ClientCapabilityId).GetProperties(BindingFlags.Public | BindingFlags.Static)
				.Where(static property => property.PropertyType == typeof(ClientCapabilityId))
				.Select(static property => ((ClientCapabilityId) property.GetValue(null)!).Value)
		];

		Assert.Equal(capabilities.Order(StringComparer.Ordinal), ScenarioCatalog.CapabilityScenarios.Keys.Order(StringComparer.Ordinal));
		Assert.All(ScenarioCatalog.CapabilityScenarios.Values.SelectMany(static scenarios => scenarios),
			static scenario => Assert.True(ScenarioCatalog.Contains(scenario), scenario));
		Assert.Empty(ScenarioCatalog.CapabilityScenarios[ClientCapabilityId.UnsafeLuaExecution.Value]);
	}

	[Fact]
	public void OnlyTheReuseOfAProcessIdIsWaivableAndOnlyCoexistenceIsC4()
	{
		Assert.Equal(["Q30.b"], ScenarioCatalog.Scenarios.Where(static scenario => scenario.Waivable).Select(static scenario => scenario.Id));
		Assert.All(ScenarioCatalog.Scenarios.Where(static scenario => scenario.Level == "C4"),
			static scenario => Assert.Contains(scenario.Sessions, static session => session.StartsWith("S5", StringComparison.Ordinal)));
	}

	[Fact]
	public void EachLiveFactCarriesTheScenarioTraitsOfItsSessions()
	{
		Dictionary<string, Type> facts = new(StringComparer.Ordinal)
		{
			["S1"] = typeof(LiveSessionS1Tests),
			["S2"] = typeof(LiveSessionS2Tests),
			["S3"] = typeof(LiveSessionS3Tests),
			["S4"] = typeof(LiveSessionS4Tests),
			["S5"] = typeof(LiveSessionS5Tests),
			["S6"] = typeof(LiveSessionS6Tests)
		};

		foreach ((string trait, Type fact) in facts)
		{
			string[] expected =
			[
				.. ScenarioEvaluators.Checks.Where(check => check.Session[..2] == trait).Select(static check => check.Scenario)
					.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
			];
			string[] traits =
			[
				.. fact.GetCustomAttributesData().Where(static attribute => attribute.AttributeType == typeof(TraitAttribute) &&
																			 (string?) attribute.ConstructorArguments[0].Value == "Qualification")
					.Select(static attribute => (string) attribute.ConstructorArguments[1].Value!).Order(StringComparer.Ordinal)
			];
			Assert.Equal(expected, traits);
		}
	}

	[GeneratedRegex(@"^Q\d{2}(\.[a-z])?$", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex ScenarioId();
}
