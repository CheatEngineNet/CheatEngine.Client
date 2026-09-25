namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>One live qualification scenario of the Client.</summary>
/// <param name="Id">The scenario id, for example <c>Q05</c> or <c>Q30.a</c>.</param>
/// <param name="Level">
///     The qualification level its receipts state: <c>C3</c> (exact host) or <c>C4</c> (exact host plus a second
///     plugin).
/// </param>
/// <param name="Title">What the scenario establishes.</param>
/// <param name="Sessions">The sessions whose receipts carry it (S1 to S6; S5a and S5b are the two load orders of S5).</param>
/// <param name="Waivable">
///     Whether a dated waiver may stand in for it when it proves impossible (plan A12: only Q30.b, the reuse of a
///     process id, which cannot be produced on demand).
/// </param>
internal sealed record QualificationScenario(
	string Id,
	string Level,
	string Title,
	IReadOnlyList<string> Sessions,
	bool Waivable = false);

/// <summary>
///     The catalog of the Client's live qualification scenarios, the release gate, and the scenarios each Client
///     capability requires. It is plain data, compiled into CheatEngine.Client.Tests (the live runner and its evaluators)
///     and linked into CheatEngine.Client.Core.Tests, whose <c>ClientCapabilityCatalogTests</c> prove that every scenario
///     a capability of <c>ClientCapabilityCatalog</c> requires exists here and that <see cref="CapabilityScenarios" />
///     equals that catalog.
/// </summary>
internal static class ScenarioCatalog
{
	/// <summary>
	///     The release-gate scenario proven in CI rather than on the host: the SDK consumer contract of
	///     <c>SdkConsumerContractTests</c>, which both CI legs run.
	/// </summary>
	internal const string ContinuousIntegrationReleaseGate = "Q48";

	/// <summary>Every live scenario, in id order.</summary>
	internal static IReadOnlyList<QualificationScenario> Scenarios
	{
		get;
	} =
	[
		new("Q05", "C3", "The plugin identity, activation and epochs", ["S1", "S2"]),
		new("Q06", "C3", "A failed enable rolls back and the next enable reports it", ["S2"]),
		new("Q09", "C4", "Two Client plugins enable and disable independently", ["S5a", "S5b"]),
		new("Q10", "C4", "A CheatEngine.SDK 1.x neighbour and Client plugins load side by side", ["S5a", "S5b"]),
		new("Q16", "C4", "A colliding export is refused, a third-party replacement survives disable, a kept function dies",
			["S2", "S5a", "S5b"]),
		new("Q16.b", "C3", "A Client symbol lease registers and releases its symbol", ["S1"]),
		new("Q19", "C3", "A worker's Client call is marshalled and its direct registration refused", ["S1"]),
		new("Q20", "C3", "Byte and string round trips", ["S1"]),
		new("Q21", "C3", "Integer and address boundaries, and the 2^53 marshalling rule", ["S1", "S4"]),
		new("Q25", "C3", "A value scan finds its marker and float texts follow the rounded comparison", ["S1"]),
		new("Q26", "C3", "A value scan session scans again, resets and releases", ["S1"]),
		new("Q27", "C3", "A global AOB scan: matches, and an indeterminate zero", ["S1"]),
		new("Q28", "C3", "A module AOB scan equals the global result inside the module", ["S1", "S3", "S4"]),
		new("Q29", "C3", "AOB copy limits and cancellation", ["S1"]),
		new("Q30.a", "C3", "An allocation's lifecycle, across a target change", ["S1", "S3"]),
		new("Q30.b", "C3", "An allocation across a reused process id", ["S3"], Waivable: true),
		new("Q31", "C3", "The configured pointer size", ["S1"]),
		new("Q32", "C3", "Target facts and the instruction profiles of x64 and x86", ["S1", "S3", "S4"]),
		new("Q33", "C3", "A partial memory batch", ["S1"]),
		new("Q34", "C3", "Stale record ids and trusted table files", ["S1"]),
		new("Q35", "C3", "An Auto Assembler patch applies and disables, and a failing one is refused", ["S1", "S3"]),
		new("Q40", "C3", "Plugins built from the packed packages", ["S1", "S6"]),
		new("Q43", "C3", "A disable runs every cleanup stage and aggregates the failures", ["S2"]),
		new("Q44", "C3", "An opt-in capability is refused without its opt-in", ["S2"]),
		new("Q45", "C3", "A probe changes no byte, process or module", ["S1"]),
		new("Q46", "C3", "Logs and debug output carry no scenario data", ["S1", "S2"])
	];

	/// <summary>The release gate on the host (plan L24): these scenarios must pass, or carry a dated waiver.</summary>
	internal static IReadOnlyList<string> ReleaseGate
	{
		get;
	} = ["Q09", "Q10", "Q40", "Q43", "Q44", "Q45", "Q46"];

	/// <summary>
	///     The scenarios each Client capability requires, by capability id; it equals the <c>RequiredScenarios</c> of
	///     <c>ClientCapabilityCatalog</c> (plan L7). <c>Client.UnsafeLuaExecution</c> is never qualified.
	/// </summary>
	internal static IReadOnlyDictionary<string, IReadOnlyList<string>> CapabilityScenarios
	{
		get;
	} = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
	{
		["Client.ProcessSelection"] = ["Q30.a", "Q31", "Q32"],
		["Client.TypedMemory"] = ["Q20", "Q21", "Q33"],
		["Client.PatternScanning"] = ["Q27", "Q28", "Q29"],
		["Client.ValueScanning"] = ["Q25", "Q26"],
		["Client.Inspection"] = ["Q16.b", "Q28"],
		["Client.Tables"] = ["Q34"],
		["Client.ProtectedLua"] = ["Q05", "Q16", "Q19"],
		["Client.UnsafeLuaExecution"] = [],
		["Client.Allocations"] = ["Q30.a"],
		["Client.Assembly"] = ["Q32"],
		["Client.AutoAssemblerPatches"] = ["Q35", "Q44"]
	};

	/// <summary>Whether <paramref name="id" /> names a scenario of the catalog.</summary>
	internal static bool Contains(string id)
	{
		return Scenarios.Any(scenario => string.Equals(scenario.Id, id, StringComparison.Ordinal));
	}
}
