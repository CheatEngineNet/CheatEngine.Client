using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>One check of a scenario, evaluated on one session's evidence.</summary>
/// <param name="Scenario">The scenario id of <see cref="ScenarioCatalog" />.</param>
/// <param name="Session">The session whose evidence it reads.</param>
/// <param name="Name">The check name, unique within its scenario and session.</param>
/// <param name="Expectation">What the check requires, as the receipt states it.</param>
/// <param name="Evaluate">The evaluator.</param>
[SupportedOSPlatform("windows")]
internal sealed record QualificationCheck(
	string Scenario,
	string Session,
	string Name,
	string Expectation,
	Func<SessionEvidence, CheckResult> Evaluate)
{
	/// <summary>The receipt of this check in <paramref name="runId" />.</summary>
	internal QualificationReceipt ToReceipt(string runId, SessionEvidence evidence)
	{
		CheckResult result;
		try
		{
			result = Evaluate(evidence);
		}
		catch (Exception exception) when (exception is InvalidOperationException or FormatException or ArgumentException)
		{
			result = CheckResult.Failed($"the evaluator failed on the evidence ({exception.GetType().Name})");
		}

		string level = ScenarioCatalog.Scenarios.Single(scenario => scenario.Id == Scenario).Level;
		return new QualificationReceipt(runId, Session, Scenario, Name, level, result.Status, Expectation, result.Observation);
	}
}

/// <summary>
///     The C# evaluators of every live check. Each reads the driver transcript (the harness's JSON observations and the
///     driver's own steps), the debug output, the lifecycle sink and the runner's facts, and returns Passed, Failed or
///     NotExecuted with the facts it rests on. A missing step is NotExecuted, never a pass; a check that needs an operator
///     toggle stays NotExecuted unless the driver observed the toggle done; nothing is inferred from a count that varies
///     with the loaded modules (Q28 compares the bounded result with the global result inside the module).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ScenarioEvaluators
{
	/// <summary>Every check, by scenario then session.</summary>
	/// <summary>The start of the identification line in the debug output (SDK <c>DebugOutputLogSink</c> and <c>LoadIdentification</c>).</summary>
	internal const string IdentificationPrefix = "[CheatEngine.SDK.Hosting] Information: CheatEngineSdkIdentification: ";

	private const string NoDisable = "the lifecycle sink records no disabled enable: no operator toggle ran, and what " +
									 "closeCE disables is a fact the S0 spike records";

	internal static IReadOnlyList<QualificationCheck> Checks
	{
		get;
	} = Build();

	private static List<QualificationCheck> Build()
	{
		List<QualificationCheck> checks = [];

		void Add(string scenario, string session, string name, string expectation, Func<SessionEvidence, CheckResult> evaluate)
		{
			checks.Add(new QualificationCheck(scenario, session, name, expectation, evaluate));
		}

		// Q05: the plugin identity and its activation epochs.
		Add("Q05", "S1", "harness-identity", "status() reports an active harness with a plugin id and an activation epoch",
			static evidence => evidence.Observe("status",
				static observed => observed.Is("plugin.active") && observed.Number("plugin.pluginId") > 0 &&
								   observed.Number("plugin.epoch") > 0,
				"plugin.active", "plugin.pluginId", "plugin.epoch"));
		Add("Q05", "S1", "identification-line",
			"the SDK identification line of the enable names the harness assembly as its plugin.assembly",
			IdentificationLine);
		Add("Q05", "S2", "reenable-epochs", "a re-enable reports a new activation with a later epoch",
			static evidence => evidence.AfterOperator(["toggle-disable", "toggle-enable"],
				() => evidence.Observe("status-after-reenable",
					static observed => observed.Number("plugin.activations") >= 2 &&
									   observed.Number("plugin.lastEpoch") > observed.Number("plugin.previousEpoch"),
					"plugin.activations", "plugin.lastEpoch", "plugin.previousEpoch")));

		// Q06: a failed enable rolls back and the next enable reports it.
		Add("Q06", "S2", "failed-enable-reported", "an enable whose Configure throws fails, and the next enable reports it",
			static evidence => evidence.AfterOperator(["toggle-disable-for-fault", "toggle-enable-faulted", "toggle-enable-after-fault"],
				() => evidence.Observe("status-after-fault",
					static observed => observed.Is("plugin.active") &&
									   observed.Element("ledger")?.ToString().Contains("configure.threw", StringComparison.Ordinal) == true,
					"plugin.active", "plugin.enableAttempts", "plugin.activations")));

		foreach (string session in (string[]) ["S5a", "S5b"])
		{
			// Q09: two Client plugins enable and disable independently.
			Add("Q09", session, "both-enabled", "Plugin A and Plugin B both answer after loading",
				static evidence => Both(evidence.Value("a-identity", static value => value.StartsWith("Plugin=A", StringComparison.Ordinal)),
					evidence.Value("b-identity", static value => value.StartsWith("Plugin=B", StringComparison.Ordinal))));
			Add("Q09", session, "independent-disable", "disabling Plugin A leaves Plugin B answering",
				static evidence => evidence.AfterOperator(["toggle-disable-a"],
					() => evidence.Value("b-ping-after-a-disabled", static value => long.TryParse(value, CultureInfo.InvariantCulture, out _))));
			Add("Q09", session, "b-disables-alone", "Plugin B then disables on its own: its functions are gone",
				static evidence => evidence.AfterOperator(["toggle-disable-b"],
					() => evidence.Value("toggle-disable-b", static value => value == "disabled")));

			// Q10: a CheatEngine.SDK 1.x neighbour and the Client plugins side by side.
			Add("Q10", session, "neighbour-identity",
				"the SDK 1.x neighbour answers with its own SDK Hosting major 1, its packaged bridge and another load context",
				static evidence => evidence.Value("neighbour-identity", static value =>
				{
					IReadOnlyDictionary<string, bool> facts = NeighbourPluginSource.ParseIdentity(value);
					return facts.GetValueOrDefault("SdkHostingMajorIs1") && facts.GetValueOrDefault("BridgeMatchesPackage") &&
						   facts.GetValueOrDefault("OwnLoadContextIsNotDefault") &&
						   facts.GetValueOrDefault("SdkHostingMajor2LoadedElsewhere");
				}));
			Add("Q10", session, "client-sdk-hosting-2", "Plugin A and Plugin B run on CheatEngine.SDK.Hosting 2.0.0.0",
				static evidence => Both(
					evidence.Value("a-identity", static value => value.Contains("CheatEngine.SDK.Hosting, Version=2.0.0.0", StringComparison.Ordinal)),
					evidence.Value("b-identity", static value => value.Contains("CheatEngine.SDK.Hosting, Version=2.0.0.0", StringComparison.Ordinal))));
			Add("Q10", session, "both-answer", "both Client plugins answer with the neighbour loaded",
				static evidence => Both(evidence.Value("a-ping", static value => long.TryParse(value, CultureInfo.InvariantCulture, out _)),
					evidence.Value("b-ping", static value => long.TryParse(value, CultureInfo.InvariantCulture, out _))));

			// Q16: collision refusal and a third-party replacement.
			Add("Q16", session, "collision-refused", "the colliding plugin leaves Plugin A's collision marker intact",
				static evidence => Both(evidence.Value("a-collision-before", static value => value == "CollisionOwner=A"),
					evidence.Value("a-collision-after", static value => value == "CollisionOwner=A")));
			Add("Q16", session, "third-party-survives-disable",
				"a third-party replacement of a Plugin A global survives the disable of Plugin A",
				static evidence => evidence.AfterOperator(["toggle-disable-a"],
					() => evidence.Value("third-party-survives", static value => value == "true")));
		}

		Add("Q16", "S2", "kept-function-dies", "a harness function kept by Lua raises a classified error after the disable",
			static evidence => evidence.AfterOperator(["toggle-disable"], () => evidence.ExpectError("kept-function-after-disable")));

		// Q16.b: a Client symbol lease.
		Add("Q16.b", "S1", "registered", "the lease registers the symbol on the scratch address",
			static evidence => Both(evidence.Observe("symbol-register", static observed => observed.Is("ok"), "ok", "name"),
				evidence.Observe("symbol-state-registered", static observed => observed.Is("resolvesToLeasedAddress"),
					"resolves", "resolvesToLeasedAddress")));
		Add("Q16.b", "S1", "released", "disposing the lease unregisters the symbol",
			static evidence => Both(evidence.Observe("symbol-release", static observed => observed.Is("released"), "released"),
				evidence.Observe("symbol-state-released", static observed => observed.Bool("resolves") == false,
					"resolves", "lease.released")));

		// Q19: worker admission.
		Add("Q19", "S1", "marshalled-call", "a Client call from a worker thread is marshalled to the main thread",
			static evidence => evidence.Observe("worker-result",
				static observed => observed.Is("offMainThread") && observed.Is("marshalledCall.succeeded"),
				"offMainThread", "marshalledCall.succeeded"));
		Add("Q19", "S1", "direct-register-refused",
			"a direct Register from a worker is refused with InvalidState and NotStarted, and publishes nothing",
			static evidence => Both(evidence.Observe("worker-result",
					static observed => observed.Bool("directRegister.registered") == false &&
									   observed.Text("directRegister.failure.kind") == "InvalidState" &&
									   observed.Text("directRegister.failure.hostEffect") == "NotStarted",
					"directRegister.registered", "directRegister.failure.kind", "directRegister.failure.hostEffect"),
				evidence.Value("worker-probe-global", static value => value == "nil")));

		// Q20: byte and string round trips.
		foreach (string kind in (string[]) ["bytes-with-nul", "utf8-multibyte", "utf16-with-nul"])
		{
			Add("Q20", "S1", kind, $"the {kind} value is written, read back exactly and the original restored",
				evidence => evidence.Observe("roundtrip-" + kind,
					static observed => observed.Is("ok") && observed.Is("bytesEqual") && observed.Is("originalRestored") &&
									   observed.Bool("textEqual") != false,
					"ok", "bytesEqual", "textEqual", "originalRestored"));
		}

		// Q21: integer and address boundaries.
		Add("Q21", "S1", "int32-minus-one", "-1 reads back as int -1 and uint 0xFFFFFFFF",
			static evidence => evidence.Observe("roundtrip-int32-minus-one",
				static observed => observed.Is("signedEqual") && observed.Is("unsignedEqual") && observed.Is("originalRestored"),
				"signedEqual", "unsignedEqual", "originalRestored"));
		Add("Q21", "S1", "uint32-max", "uint.MaxValue reads back as uint and as int -1",
			static evidence => evidence.Observe("roundtrip-uint32-max",
				static observed => observed.Is("unsignedEqual") && observed.Is("signedEqual") && observed.Is("originalRestored"),
				"unsignedEqual", "signedEqual", "originalRestored"));
		foreach (string session in (string[]) ["S1", "S4"])
		{
			Add("Q21", session, "int64-limits", "the 64-bit limits and 2^53 + 1 read back exactly",
				static evidence => evidence.Observe("roundtrip-int64-limits",
					static observed => observed.Is("allEqual") && observed.Is("originalRestored"), "allEqual", "originalRestored"));
		}

		Add("Q21", "S1", "address-above-4gib", "an address above 4 GiB reads back exactly on the x64 target",
			static evidence => evidence.Observe("roundtrip-address-above-4gib",
				static observed => observed.Is("valueEqual") && observed.Is("rawEqual") && observed.Is("originalRestored"),
				"valueEqual", "rawEqual", "originalRestored"));
		Add("Q21", "S4", "address-above-4gib-refused", "an address above 4 GiB is refused on the x86 target",
			static evidence => evidence.Observe("roundtrip-address-above-4gib",
				static observed => observed.Text("writeFailure.kind") == "OperationRejected" && observed.Is("originalRestored"),
				"writeFailure.kind", "writeFailure.hostEffect", "originalRestored"));
		Add("Q21", "S1", "integer-exact", "an integer above 2^53 is marshalled exactly",
			static evidence => evidence.Observe("integer-exact", static observed => observed.Text("value") == "9007199254740993",
				"value"));
		Add("Q21", "S1", "integer-float-below-2p53", "a float below 2^53 is marshalled exactly",
			static evidence => evidence.Observe("integer-float-below",
				static observed => observed.Text("value") == "9007199254740991", "value"));
		Add("Q21", "S1", "integer-float-2p53-refused", "a float of 2^53 is refused with a Lua error, never rounded",
			static evidence => evidence.ExpectError("integer-float-2p53"));
		Add("Q21", "S1", "address-exact", "an integer address is marshalled exactly",
			static evidence => evidence.Observe("address-exact",
				static observed => observed.Text("value") == "140737488355327", "value"));
		Add("Q21", "S1", "address-float-2p53-refused", "a float address of 2^53 is refused with a Lua error",
			static evidence => evidence.ExpectError("address-float-2p53"));

		// Q25 and Q26: value scans.
		Add("Q25", "S1", "first-scan-finds-marker", "a first scan of the scratch region finds the int32 marker",
			static evidence => evidence.Observe("value-scan-first",
				static observed => observed.Is("scanned") && observed.Is("results.containsMarker"),
				"scanned", "results.resultCount", "results.containsMarker", "session.state"));
		Add("Q25", "S1", "decimal-tolerance",
			"3.14159 is found by its 5, 2 and 0 decimal texts and not by 3.2 or 3.15, as float and as double; its " +
			"3-decimal text 3.142 gives one verdict for both, which names the rounding rule Cheat Engine applies",
			DecimalTolerance);
		Add("Q26", "S1", "next-scan-finds-marker", "a next scan finds the new marker",
			static evidence => evidence.Observe("value-scan-next",
				static observed => observed.Is("scanned") && observed.Is("results.containsMarker"),
				"scanned", "results.resultCount", "results.containsMarker"));
		Add("Q26", "S1", "reset", "the session resets",
			static evidence => evidence.Observe("value-scan-reset", static observed => observed.Is("reset"), "reset", "session.state"));
		Add("Q26", "S1", "released", "the session releases as Released",
			static evidence => evidence.Observe("value-scan-release",
				static observed => observed.Text("release.kind") == "Released" && observed.Is("release.complete"),
				"release.kind", "release.hostEffect", "session.state"));
		Add("Q26", "S3", "refused-after-target-change",
			"after Cheat Engine selects another process, the session has ended with RefusedTargetChanged and manual recovery",
			static evidence => evidence.Observe("value-scan-state-on-b",
				static observed => observed.Is("session.released") && observed.Text("session.state") == "Closed" &&
								   observed.Text("lastRelease.kind") == "RefusedTargetChanged" &&
								   observed.Is("lastRelease.requiresManualRecovery"),
				"session.released", "session.state", "lastRelease.kind", "lastRelease.requiresManualRecovery"));
		Add("Q26", "S3", "nothing-released-on-b",
			"a release attempt on the other process makes no Cheat Engine call: it returns the ending refusal again",
			static evidence => evidence.Observe("value-scan-release-on-b",
				static observed => observed.Text("release.kind") == "RefusedTargetChanged" &&
								   observed.Text("release.hostEffect") == "NotStarted" &&
								   observed.Text("lastRelease.kind") == "RefusedTargetChanged" &&
								   observed.Is("lastRelease.requiresManualRecovery"),
				"release.kind", "release.hostEffect", "lastRelease.kind"));
		Add("Q26", "S3", "file-as-process-refused", "no session is created on a file opened as a process",
			static evidence => evidence.Observe("value-scan-unidentified",
				static observed => observed.Bool("created") == false && observed.Text("failure.kind") == "TargetIdentityUnavailable",
				"created", "failure.kind", "failure.hostEffect"));

		// Q27: global AOB scans.
		Add("Q27", "S1", "known-pattern-matches", "the module header pattern gives Matches on the global route",
			static evidence => evidence.Observe("aob-known",
				static observed => observed.Is("ok") && observed.Text("route.scope") == "GlobalHostScan" &&
								   observed.Text("route.hostOutcome") == "Matches" && observed.Number("matches.count") >= 1,
				"route.scope", "route.hostOutcome", "matches.count"));
		Add("Q27", "S1", "absent-pattern-indeterminate", "an absent random pattern is IndeterminateHostResult with NoResult",
			static evidence => evidence.Observe("aob-absent", static observed => observed.Is("checks.globalZeroIsIndeterminate"),
				"failure.kind", "route.hostOutcome"));

		// Q28: module AOB scans.
		foreach (string session in (string[]) ["S1", "S4"])
		{
			Add("Q28", session, "module-scan-exact",
				"the bounded module scan finds the module base and equals the global result inside the module",
				static evidence => evidence.Observe("aob-module",
					static observed => observed.Text("route.scope") == "HostBoundedRange" && observed.Is("route.targetIdentityVerified") &&
									   observed.Is("moduleCheck.containsBase") && observed.Is("moduleCheck.filterExact"),
					"route.scope", "route.reason", "moduleCheck.containsBase", "moduleCheck.filterExact",
					"moduleCheck.globalComplete"));
			Add("Q28", session, "module-absent-no-matches", "an absent pattern in the module is a factual NoMatches",
				static evidence => evidence.Observe("aob-module-absent", static observed => observed.Is("checks.boundedZeroIsNoMatches"),
					"route.scope", "route.hostOutcome"));
		}

		Add("Q28", "S3", "file-as-process-fallback",
			"on a file opened as a process a module scan takes the global route with the managed filter, unverified",
			static evidence => evidence.Observe("aob-module-unidentified",
				static observed => observed.Text("route.scope") == "GlobalHostScanWithManagedFilter" &&
								   observed.Text("route.reason") == "TargetIdentityNotQualified" &&
								   observed.Bool("route.targetIdentityVerified") == false,
				"route.scope", "route.reason", "route.targetIdentityVerified", "failure.kind"));

		// Q29: copy limits and cancellation.
		Add("Q29", "S1", "limit-truncates", "a limit of 1 truncates explicitly",
			static evidence => evidence.Observe("aob-limit",
				static observed => observed.Is("truncated") && observed.Is("checks.truncationExplicit"),
				"truncated", "matches.count", "checks.truncationExplicit"));
		Add("Q29", "S1", "cancellation-honest", "a cancellation either publishes nothing or completes untruncated",
			static evidence => evidence.Observe("aob-cancel", static observed => observed.Is("checks.cancellationHonest"),
				"failure.kind", "truncated", "checks.cancellationHonest"));

		// Q30.a: allocations.
		Add("Q30.a", "S1", "allocated",
			"an allocation is a committed region that starts at the lease address, held by an active lease",
			static evidence => Both(evidence.Observe("allocation-allocate",
					static observed => observed.Is("allocated") && observed.Text("region.state") == "Committed" &&
									   observed.Is("region.startsAtAllocation"),
					"allocated", "region.state", "region.protection"),
				evidence.Observe("allocation-state",
					static observed => observed.Bool("lease.released") == false &&
									   observed.Bool("lease.requiresManualRecovery") == false &&
									   observed.Element("lastRelease") is { ValueKind: JsonValueKind.Null },
					"lease.released", "lease.requiresManualRecovery")));
		Add("Q30.a", "S1", "released", "its release is Released and frees the region",
			static evidence => evidence.Observe("allocation-release",
				static observed => observed.Text("release.kind") == "Released" && observed.Is("release.complete") &&
								   observed.Text("regionAfterRelease.state") == "Free",
				"release.kind", "regionAfterRelease.state"));
		Add("Q30.a", "S3", "refused-after-target-change",
			"after Cheat Engine selects another process, the lease has ended with RefusedTargetChanged and manual recovery",
			static evidence => evidence.Observe("allocation-state-on-b",
				static observed => observed.Is("lease.released") && observed.Text("lastRelease.kind") == "RefusedTargetChanged" &&
								   observed.Is("lease.requiresManualRecovery"),
				"lease.released", "lastRelease.kind", "lease.requiresManualRecovery"));
		Add("Q30.a", "S3", "nothing-freed-on-b",
			"a release attempt on the other process frees nothing: it returns the ending refusal again",
			static evidence => evidence.Observe("allocation-release-on-b",
				static observed => observed.Is("releasedBefore") &&
								   observed.Text("release.kind") == "RefusedTargetChanged" &&
								   observed.Text("release.hostEffect") == "NotStarted" &&
								   observed.Is("requiresManualRecovery"),
				"releasedBefore", "release.kind", "release.hostEffect", "requiresManualRecovery",
				"onAuthorizedTarget"));
		Add("Q30.a", "S3", "consumed-owner-stays-ended", "back on the first process the refused lease stays ended",
			static evidence => evidence.Observe("allocation-state-back-on-a",
				static observed => observed.Is("lease.released") && observed.Text("lastRelease.kind") == "RefusedTargetChanged",
				"lease.released", "lastRelease.kind"));
		Add("Q30.a", "S3", "new-allocation-released", "a new allocation on the first process releases as Released",
			static evidence => evidence.Observe("allocation-release-new",
				static observed => observed.Text("release.kind") == "Released" && observed.Is("release.complete"),
				"release.kind", "regionAfterRelease.state"));
		Add("Q30.a", "S3", "file-as-process-refused", "no allocation is made on a file opened as a process",
			static evidence => evidence.Observe("allocation-unidentified",
				static observed => observed.Bool("allocated") == false && observed.Text("failure.kind") == "TargetIdentityUnavailable",
				"allocated", "failure.kind", "failure.hostEffect"));
		Add("Q30.b", "S3", "process-id-reuse", "an allocation is refused on a process that reuses the process id",
			static _ => CheckResult.NotExecuted("the reuse of a process id cannot be produced on demand; this scenario is " +
												"waivable (plan A12)"));

		// Q31: the configured pointer size.
		Add("Q31", "S1", "configured-4", "setPointerSize(4) is reported as a configured size of 4 that differs from the bitness",
			static evidence => evidence.Observe("runtime-pointer-4",
				static observed => observed.Is("configuredPointerSize.exposedByClient") &&
								   observed.Number("configuredPointerSize.bytes") == 4 &&
								   observed.Is("configuredPointerSize.differsFromBitness"),
				"configuredPointerSize.bytes", "configuredPointerSize.differsFromBitness", "process.bitnessBytes"));
		Add("Q31", "S1", "width-refusal", "an address write is refused while the configured size differs from the bitness",
			static evidence => evidence.Observe("pointer-width-refusal",
				static observed => observed.Element("writeFailure") is not null && observed.Is("originalRestored"),
				"writeFailure.kind", "writeFailure.hostEffect", "originalRestored"));
		Add("Q31", "S1", "restored-8", "setPointerSize(8) restores a configured size equal to the bitness",
			static evidence => evidence.Observe("runtime-pointer-8",
				static observed => observed.Number("configuredPointerSize.bytes") == 8 &&
								   observed.Bool("configuredPointerSize.differsFromBitness") == false,
				"configuredPointerSize.bytes", "configuredPointerSize.differsFromBitness"));

		// Q32: target facts and instruction profiles.
		Add("Q32", "S1", "x64-facts", "Cheat Engine 7.7.0.10621 x64 on Windows, a local x64 target of bitness 8",
			static evidence => evidence.Observe("runtime",
				static observed => observed.Text("host.cheatEngineVersion") == "7.7.0.10621" &&
								   observed.Number("host.cheatEngineBitnessBytes") == 8 &&
								   observed.Text("host.operatingSystem") == "Windows" &&
								   observed.Text("process.backend") == "LocalProcess" &&
								   observed.Text("process.targetArchitecture") == "X64" && observed.Number("process.bitnessBytes") == 8,
				"host.cheatEngineVersion", "host.cheatEngineBitnessBytes", "host.operatingSystem", "process.backend",
				"process.targetArchitecture", "process.bitnessBytes"));
		Add("Q32", "S4", "x86-facts", "a local x86 target of bitness 4",
			static evidence => evidence.Observe("runtime",
				static observed => observed.Text("process.backend") == "LocalProcess" &&
								   observed.Text("process.targetArchitecture") == "X86" && observed.Number("process.bitnessBytes") == 4,
				"process.backend", "process.targetArchitecture", "process.bitnessBytes"));
		foreach (string session in (string[]) ["S1", "S4"])
		{
			Add("Q32", session, "instruction-profile",
				"nop, ret, int3 and push of the frame pointer assemble to 90, C3, CC and 55; short and long jumps to EB and E9",
				static evidence => evidence.Observe("instructions", static observed =>
					Assembled(observed, "nop", "90") && Assembled(observed, "ret", "C3") && Assembled(observed, "int3", "CC") &&
					Assembled(observed, "push-frame-pointer", "55") && Assembled(observed, "jmp-short", "EB") &&
					Assembled(observed, "jmp-long", "E9"), "bitnessBytes"));
			Add("Q32", session, "instruction-round-trip",
				"mov eax,1; ret written to the scratch window disassembles, measures and finds its previous instruction",
				static evidence => evidence.Observe("instructions",
					static observed => observed.Is("roundTrip.disassembled") && observed.Is("roundTrip.bytesEqual") &&
									   observed.Number("roundTrip.length") == observed.Number("roundTrip.snapshotLength") &&
									   observed.Is("roundTrip.previousIsWindow") && observed.Is("originalRestored"),
					"roundTrip.opcode", "roundTrip.length", "roundTrip.previousIsWindow", "originalRestored"));
		}

		Add("Q32", "S3", "file-as-process-backend", "a file opened as a process is reported with the FileAsProcess backend",
			static evidence => evidence.Observe("runtime-file-as-process",
				static observed => observed.Text("process.backend") == "FileAsProcess", "process.backend", "runtime.backend"));
		Add("Q32", "S3", "target-change-observed",
			"each process Cheat Engine selects (A, B, then A again) is the one the Client reports next, with a later selection epoch",
			TargetSwitches);

		// Q33: a partial batch.
		Add("Q33", "S1", "partial-effect", "a batch whose third address is unmapped reports two writes and a partial effect",
			static evidence => evidence.Observe("batch-partial",
				static observed => observed.Is("checks.partialEffectExposed") && observed.Is("checks.readBackConfirms") &&
								   observed.Is("checks.originalRestored"),
				"outcome.completed", "outcome.failedIndex", "outcome.effectState"));

		// Q34: tables.
		Add("Q34", "S1", "destroyed-record-refused", "the id of a destroyed record is refused, never answered by another",
			static evidence => evidence.Observe("table-probe-destroyed",
				static observed => observed.Is("checks.oldReferenceRefused") && !observed.Is("checks.reusedAsAnotherRecord"),
				"found", "failure.kind"));
		Add("Q34", "S1", "table-file-round-trip", "a table saved below the table root loads again",
			static evidence => Both(evidence.Observe("table-save", static observed => observed.Is("saved") && observed.Is("fileExists"),
					"saved", "fileExists"),
				evidence.Observe("table-load", static observed => observed.Is("loaded"), "loaded", "failure.kind")));
		Add("Q34", "S1", "loaded-table-ends-ids", "a table load ends every record id handed out before",
			static evidence => evidence.Observe("table-probe-loaded", static observed => observed.Is("checks.oldReferenceRefused"),
				"found", "failure.kind"));
		Add("Q34", "S1", "outside-root-refused", "a save outside the table root is refused before any Cheat Engine call",
			static evidence => evidence.Observe("table-save-outside",
				static observed => observed.Bool("saved") == false && observed.Text("failure.hostEffect") == "NotStarted" &&
								   observed.Bool("fileExists") == false,
				"saved", "failure.kind", "failure.hostEffect"));

		// Q35: Auto Assembler patches.
		Add("Q35", "S1", "benign-applied", "the benign patch checks, applies on the target Cheat Engine selected and registers its symbol",
			static evidence => Both(evidence.Observe("aa-check", static observed => observed.Is("accepted"), "accepted"),
				evidence.Observe("aa-apply",
					static observed => observed.Is("applied") && observed.Is("lease.canDisable") &&
									   observed.Is("symbolResolves") &&
									   observed.Number("lease.selectionEpoch") == observed.Number("selectionEpochBeforeApply"),
					"applied", "lease.canDisable", "symbolResolves", "lease.selectionEpoch",
					"selectionEpochBeforeApply")));
		Add("Q35", "S1", "benign-disabled", "releasing the patch runs its [DISABLE] section: Released and the symbol is gone",
			static evidence => evidence.Observe("aa-release",
				static observed => observed.Text("release.kind") == "Released" && observed.Bool("symbolResolves") == false,
				"release.kind", "symbolResolves"));
		Add("Q35", "S1", "failing-refused", "a patch that does not assemble is refused and leaves no lease",
			static evidence => evidence.Observe("aa-apply-failing",
				static observed => observed.Bool("applied") == false && observed.Element("failure") is not null,
				"applied", "failure.kind", "failure.hostEffect"));
		Add("Q35", "S3", "refused-after-target-change",
			"after a target change the patch lease ended with RefusedTargetChanged and manual recovery",
			static evidence => evidence.Observe("aa-state-on-b",
				static observed => observed.Is("lease.released") && observed.Is("lease.requiresManualRecovery") &&
								   observed.Text("lastRelease.kind") == "RefusedTargetChanged",
				"lease.released", "lastRelease.kind", "lease.requiresManualRecovery"));
		Add("Q35", "S3", "nothing-disabled-on-b",
			"a release attempt on the other process makes no Cheat Engine call: it returns the ending refusal again",
			static evidence => evidence.Observe("aa-release-on-b",
				static observed => observed.Text("release.kind") == "RefusedTargetChanged" &&
								   observed.Text("release.hostEffect") == "NotStarted" &&
								   observed.Text("lastRelease.kind") == "RefusedTargetChanged",
				"release.kind", "release.hostEffect", "lastRelease.kind", "symbolResolves"));

		// Q40: plugins built from the packed packages.
		Add("Q40", "S1", "client-assemblies-packed", "every Client assembly of the harness bundle equals the packed package's",
			static evidence => evidence.FromFact(SessionFacts.BundleClientAssembliesMatch, static value => value == "true"));
		Add("Q40", "S1", "bridge-reviewed", "the deployed bridge is the reviewed CheatEngine.SDK bridge",
			static evidence => evidence.Fact(SessionFacts.ExpectedBridgeSha256) is { } expected
				? evidence.FromFact(SessionFacts.BundleBridgeSha256, value => value == expected)
				: CheckResult.NotExecuted("the reviewed bridge hash was not established"));
		Add("Q40", "S1", "loaded-client-version",
			"the loaded Client Hosting and Core assemblies are both reported and carry the packed package version",
			LoadedClientVersion);
		Add("Q40", "S6", "template-sdk-content-hash",
			"the instantiated template restores the reviewed CheatEngine.SDK package (its content hash)",
			static evidence => evidence.Fact(SessionFacts.ExpectedSdkContentHash) is { } expected
				? evidence.FromFact(SessionFacts.TemplateSdkContentHash, value => value == expected)
				: CheckResult.NotExecuted("the reviewed content hash was not established"));
		Add("Q40", "S6", "template-answers", "the template's status global answers after loadPlugin",
			static evidence => evidence.Value("template-status", static value => value.Length > 0));
		Add("Q40", "S6", "template-deps-isolated", "the template bundle's deps.json names no path of the build workspace",
			static evidence => evidence.FromFact(SessionFacts.TemplateDepsWorkspacePaths, static value => value == "0"));

		// Q43: the last disable (at closeCE, or the operator's last toggle) runs every cleanup stage and aggregates the
		// failures; every enable of S2 but the one that must fail carries the ModuleOnDisabling fault.
		Add("Q43", "S2", "cleanup-continues-past-fault",
			"in the last enable that was disabled, the faulty module's disable is followed by the first module's disable " +
			"and the resource cleanup",
			static evidence => LifecycleOrder(evidence, "fault.disabling.threw", "first.disabling", "resource.disposed"));
		Add("Q43", "S2", "failures-aggregated",
			"in the last enable that was disabled, the aggregated cleanup failure is logged with its template only",
			static evidence => Lifecycle(evidence, static line => line.StartsWith("log\tWarning\t", StringComparison.Ordinal) &&
																 line.EndsWith("completed cleanup with {FailureCount} failure(s).", StringComparison.Ordinal)));

		// Q44: the policy refusal of the opt-in capabilities.
		Add("Q44", "S2", "auto-assembler-policy-refused",
			"without the opt-in Client.AutoAssemblerPatches is Unavailable with a Missing policy gate and no client exists",
			static evidence => evidence.Observe("capabilities-policy", static observed =>
				observed.Item("families", "capability", "Client.AutoAssemblerPatches") is { } patches &&
				patches.Text("state") == "Unavailable" && patches.Text("gates.policy") == "Missing" &&
				patches.Bool("policyRefusal.serviceRegistered") == false && observed.Is("processUnchanged"), "processUnchanged"));
		Add("Q44", "S2", "unsafe-lua-policy-refused", "without the opt-in no unsafe Lua client exists",
			static evidence => evidence.Observe("capabilities-policy", static observed =>
				observed.Item("families", "capability", "Client.UnsafeLuaExecution") is { } unsafeLua &&
				unsafeLua.Text("gates.policy") == "Missing" && unsafeLua.Bool("policyRefusal.serviceRegistered") == false,
				"processUnchanged"));
		Add("Q44", "S2", "patch-refused", "the harness cannot apply a patch without the opt-in",
			static evidence => evidence.Observe("aa-apply-refused",
				static observed => observed.Text("refusal") == "AutoAssemblerNotEnabled" && observed.Bool("optInRequested") == false,
				"refusal", "optInRequested"));

		// Q45: the probe changes nothing.
		Add("Q45", "S1", "probe-changes-nothing", "the capability probe leaves the scratch bytes and the opened process unchanged",
			static evidence => Both(evidence.Observe("capabilities-probe", static observed => observed.Is("processUnchanged"),
					"processIdBefore", "processIdAfter"),
				Same(evidence, "scratch-digest-before", "scratch-digest-after")));
		Add("Q45", "S1", "no-module-injected", "no speedhack, allochook, luaclient, vehdebug or dbk module and no new module",
			static evidence => Both(evidence.FromFact(SessionFacts.ForbiddenModules, static value => value.Length == 0),
				evidence.FromFact(SessionFacts.ModulesUnchanged, static value => value == "true")));

		// Q46: no scenario data in logs or debug output.
		foreach (string session in (string[]) ["S1", "S2"])
		{
			Add("Q46", session, "logs-clean", "no captured log event carries a declared value, an address or the script marker",
				static evidence => evidence.Observe("logs", static observed => observed.Number("sensitiveHits") == 0,
					"eventCount", "sensitiveHits", "dropped"));
			Add("Q46", session, "debug-output-clean", "the Cheat Engine debug output carries none of the scenario values",
				static evidence => evidence.FromFact(SessionFacts.DebugOutputSensitiveHits, static value => value == "0"));
		}

		return checks;
	}

	/// <summary>
	///     Q05: with <c>CHEATENGINE_SDK_IDENTIFY_ON_ENABLE=1</c> (the runner sets it), CheatEngine.SDK 2.0.0 writes one
	///     <c>CheatEngineSdkIdentification: </c> line per enable through its default debug output sink, which prefixes
	///     <c>[CheatEngine.SDK.Hosting] Information: </c>; its <c>plugin.assembly</c> field is the plugin assembly's name
	///     and version. Another debug line that merely names the harness, such as a load failure, is no identification.
	/// </summary>
	private static CheckResult IdentificationLine(SessionEvidence evidence)
	{
		if (evidence.DebugOutput.Length == 0)
		{
			return CheckResult.NotExecuted("no debug output was captured");
		}

		string[] lines =
		[
			.. evidence.DebugOutput.ReplaceLineEndings("\n").Split('\n')
				.Where(static line => line.StartsWith(IdentificationPrefix, StringComparison.Ordinal))
		];
		foreach (string line in lines)
		{
			Dictionary<string, string> fields = new(StringComparer.Ordinal);
			foreach (string field in line[IdentificationPrefix.Length..].Split("; "))
			{
				int equals = field.IndexOf('=', StringComparison.Ordinal);
				if (equals > 0)
				{
					fields.TryAdd(field[..equals], field[(equals + 1)..]);
				}
			}

			if (fields.TryGetValue("plugin.assembly", out string? assembly) &&
				assembly.StartsWith(PluginBundleBuilder.HarnessAssemblyName + " ", StringComparison.Ordinal))
			{
				return CheckResult.Passed($"plugin.assembly={assembly}; sdk.version={fields.GetValueOrDefault("sdk.version")}");
			}
		}

		return CheckResult.Failed($"{lines.Length} identification line(s), none whose plugin.assembly is " +
								  PluginBundleBuilder.HarnessAssemblyName);
	}

	/// <summary>
	///     Q40: the status observation reports both loaded Client assemblies (Hosting, and Core, which exists only while
	///     the activation does), each with the packed package version, alone or with build metadata; an observation that
	///     reports neither is no evidence of the version.
	/// </summary>
	private static CheckResult LoadedClientVersion(SessionEvidence evidence)
	{
		if (evidence.Fact(SessionFacts.ClientPackageVersion) is not { } version)
		{
			return CheckResult.NotExecuted("the package version was not established");
		}

		if (!evidence.TryObserve("status", out Observed? observed, out CheckResult notUsable))
		{
			return notUsable;
		}

		Observed[] client =
		[
			.. observed.Items("assemblies").Where(static item => item.Text("role") is "clientHosting" or "clientCore")
		];
		string[] roles = [.. client.Select(static item => item.Text("role") ?? string.Empty).Distinct(StringComparer.Ordinal)];
		bool versioned = client.All(item => item.Text("informationalVersion") is { } loaded &&
											(loaded == version || loaded.StartsWith(version + "+", StringComparison.Ordinal)));
		string reported = string.Join(", ", client.Select(static item => $"{item.Text("role")}={item.Text("informationalVersion")}"));
		return CheckResult.From(roles.Length == 2 && versioned,
			$"package {version}; status reports {(reported.Length == 0 ? "no Client assembly" : reported)}");
	}

	/// <summary>
	///     Q32 on S3: each process the driver selects through Cheat Engine (A, then B, then A again) is the one the
	///     Client's next <c>runtime</c> reports, and each selection moves the Client's selection epoch forward; a Client
	///     that kept a stale selection fails.
	/// </summary>
	private static CheckResult TargetSwitches(SessionEvidence evidence)
	{
		(string Opened, string Runtime)[] switches =
		[
			("opened-process-a", "runtime-on-a"), ("opened-process-b", "runtime-on-b"),
			("opened-process-a-again", "runtime-back-on-a")
		];
		List<string> facts = [];
		bool passed = true;
		string? previousProcess = null;
		long? previousEpoch = null;
		foreach ((string opened, string runtime) in switches)
		{
			if (!evidence.TryValue(opened, out string? selected, out CheckResult notUsable) ||
				!evidence.TryObserve(runtime, out Observed? observed, out notUsable))
			{
				return notUsable;
			}

			long? processId = observed.Number("process.processId");
			long? epoch = observed.Number("process.selectionEpoch");
			passed &= processId?.ToString(CultureInfo.InvariantCulture) == selected &&
					  !string.Equals(selected, previousProcess, StringComparison.Ordinal) &&
					  epoch is not null && (previousEpoch is null || epoch > previousEpoch);
			string reported = processId?.ToString(CultureInfo.InvariantCulture) ?? "absent";
			string moved = epoch?.ToString(CultureInfo.InvariantCulture) ?? "absent";
			facts.Add($"{opened}={selected}: {runtime} process {reported}, epoch {moved}");
			previousProcess = selected;
			previousEpoch = epoch;
		}

		return CheckResult.From(passed, string.Join("; ", facts));
	}

	/// <summary>
	///     Q25: every case with an expectation meets it, and the two discriminating cases (the 3-decimal text 3.142 of
	///     3.14159, as float and as double) agree. Their verdict names the rule: ordinary rounding finds the probe, the
	///     range of the <c>rtRounded</c> documentation (up to half a unit above the text) does not. Either rule passes;
	///     the receipt names it, so the remarks of <c>ValueScanValue</c> can follow the recorded run.
	/// </summary>
	private static CheckResult DecimalTolerance(SessionEvidence evidence)
	{
		if (!evidence.TryObserve("value-scan-decimals", out Observed? observed, out CheckResult notUsable))
		{
			return notUsable;
		}

		IReadOnlyList<Observed> cases = observed.Items("cases");
		Observed[] discriminating = [.. cases.Where(static item => item.Is("discriminating"))];
		if (cases.Count != 12 || discriminating.Length != 2 ||
			!cases.All(static item => item.Is("scanned") && item.Bool("found") is not null))
		{
			return CheckResult.Failed($"value-scan-decimals: {cases.Count} cases, {discriminating.Length} discriminating; " +
									  "every case must be scanned and read back");
		}

		string[] unmet =
		[
			.. cases.Where(static item => !item.Is("discriminating") &&
										  (item.Bool("expectedFound") is not { } expected || item.Bool("found") != expected))
				.Select(static item => $"{item.Text("type")} with {item.Number("decimals")} decimals")
		];
		bool? found = discriminating[0].Bool("found");
		bool agree = discriminating[1].Bool("found") == found;
		string rule = !agree
			? "float and double disagree on 3.142"
			: found == true
				? "ordinary rounding (3.142 finds 3.14159)"
				: "the documented range up to half a unit above the text (3.142 does not find 3.14159)";
		string expectations = unmet.Length == 0 ? "every expectation met" : "unmet: " + string.Join(", ", unmet);
		return CheckResult.From(unmet.Length == 0 && agree, $"value-scan-decimals: {expectations}; rule: {rule}");
	}

	private static bool Assembled(Observed observed, string id, string bytesPrefix)
	{
		return observed.Item("assembled", "id", id) is { } item && item.Is("assembled") &&
			   item.Text("bytes")?.StartsWith(bytesPrefix, StringComparison.Ordinal) == true;
	}

	/// <summary>Both results: Failed when one failed, NotExecuted when one did not run, Passed otherwise.</summary>
	private static CheckResult Both(CheckResult first, CheckResult second)
	{
		string observation = first.Observation + " | " + second.Observation;
		if (first.Status == ReceiptStatus.Failed || second.Status == ReceiptStatus.Failed)
		{
			return CheckResult.Failed(observation);
		}

		return first.Status == ReceiptStatus.NotExecuted || second.Status == ReceiptStatus.NotExecuted
			? CheckResult.NotExecuted(observation)
			: CheckResult.Passed(observation);
	}

	private static CheckResult Same(SessionEvidence evidence, string before, string after)
	{
		TranscriptRecord? first = evidence.Transcript.Find(before);
		TranscriptRecord? second = evidence.Transcript.Find(after);
		if (first is not { Status: TranscriptStatus.Ok } || second is not { Status: TranscriptStatus.Ok })
		{
			return CheckResult.NotExecuted($"{before} or {after} did not run");
		}

		return CheckResult.From(string.Equals(first.Value, second.Value, StringComparison.Ordinal),
			$"{before}={first.Value}; {after}={second.Value}");
	}

	/// <summary>The lines of the last disabled enable hold one matching <paramref name="matches" />.</summary>
	private static CheckResult Lifecycle(SessionEvidence evidence, Func<string, bool> matches)
	{
		if (LastDisabledEnable(evidence.Lifecycle) is not { } enable)
		{
			return CheckResult.NotExecuted(NoDisable);
		}

		return CheckResult.From(enable.Any(matches), $"{enable[0][LedgerPrefixLength(enable[0])..]}: {enable.Count} lifecycle lines");
	}

	/// <summary>The ledger of the last disabled enable holds these stages, each a whole stage, in this order.</summary>
	private static CheckResult LifecycleOrder(SessionEvidence evidence, params string[] stages)
	{
		if (LastDisabledEnable(evidence.Lifecycle) is not { } enable)
		{
			return CheckResult.NotExecuted(NoDisable);
		}

		string configured = enable[0][LedgerPrefixLength(enable[0])..];
		int position = 0;
		foreach (string stage in stages)
		{
			int found = -1;
			for (int index = position; index < enable.Count; index++)
			{
				if (string.Equals(LedgerStage(enable[index]), stage, StringComparison.Ordinal))
				{
					found = index;
					break;
				}
			}

			if (found < 0)
			{
				return CheckResult.Failed($"{configured}: no '{stage}' after the earlier stages ({enable.Count} lines)");
			}

			position = found + 1;
		}

		return CheckResult.Passed($"{configured}: stages in order: {string.Join(", ", stages)}");
	}

	/// <summary>
	///     The lifecycle sink lines of the last enable that recorded a disable (<c>plugin.disabling</c>), from its
	///     <c>configure</c> ledger entry, which every enable writes first, to the next one; a disable of an earlier enable,
	///     or its log lines, never count for a later one.
	/// </summary>
	private static List<string>? LastDisabledEnable(IReadOnlyList<string> lifecycle)
	{
		List<string>? last = null;
		List<string>? current = null;
		foreach (string line in lifecycle)
		{
			if (string.Equals(LedgerStage(line), "configure", StringComparison.Ordinal))
			{
				last = Disabled(current) ?? last;
				current = [line];
			}
			else
			{
				current?.Add(line);
			}
		}

		return Disabled(current) ?? last;

		static List<string>? Disabled(List<string>? enable)
		{
			return enable?.Any(static line => string.Equals(LedgerStage(line), "plugin.disabling", StringComparison.Ordinal)) == true
				? enable
				: null;
		}
	}

	/// <summary>The stage of a ledger line, <c>ledger&lt;TAB&gt;#&lt;enable&gt; &lt;stage&gt;[ &lt;detail&gt;]</c>, or <see langword="null" />.</summary>
	private static string? LedgerStage(string line)
	{
		int start = LedgerPrefixLength(line);
		if (start == 0)
		{
			return null;
		}

		int end = line.IndexOf(' ', start);
		return end < 0 ? line[start..] : line[start..end];
	}

	/// <summary>The length of <c>ledger&lt;TAB&gt;#&lt;enable&gt; </c> at the start of a ledger line, or 0.</summary>
	private static int LedgerPrefixLength(string line)
	{
		if (!line.StartsWith("ledger\t#", StringComparison.Ordinal))
		{
			return 0;
		}

		int space = line.IndexOf(' ', "ledger\t#".Length);
		return space < 0 ? 0 : space + 1;
	}
}
