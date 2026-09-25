using System.Runtime.Versioning;
using System.Text;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>
///     The evaluators on canned evidence: each check passes on the observation it requires and fails on its opposite,
///     a missing step is NotExecuted, an operator step that did not run keeps its check NotExecuted, and nothing that
///     depends on a fact the spike records is guessed.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EvaluatorTests
{
	[Fact]
	public void AnIndeterminateGlobalZeroPassesAndAFactualZeroDoesNot()
	{
		QualificationCheck check = Check("Q27", "S1", "absent-pattern-indeterminate");

		Assert.Equal(ReceiptStatus.Passed, check.Evaluate(Evidence(("aob-absent",
			"""{"ok":false,"route":{"hostOutcome":"NoResult"},"failure":{"kind":"IndeterminateHostResult"},"checks":{"globalZeroIsIndeterminate":true}}"""))).Status);
		Assert.Equal(ReceiptStatus.Failed, check.Evaluate(Evidence(("aob-absent",
			"""{"ok":true,"route":{"hostOutcome":"NoMatches"},"checks":{"globalZeroIsIndeterminate":false}}"""))).Status);
	}

	[Fact]
	public void AModuleScanMustBeBoundedVerifiedAndExact()
	{
		QualificationCheck check = Check("Q28", "S1", "module-scan-exact");
		const string Exact = """{"route":{"scope":"HostBoundedRange","reason":"ScopedRequestOnQualifiedTarget","targetIdentityVerified":true},"moduleCheck":{"containsBase":true,"filterExact":true,"globalComplete":true}}""";

		Assert.Equal(ReceiptStatus.Passed, check.Evaluate(Evidence(("aob-module", Exact))).Status);
		Assert.Equal(ReceiptStatus.Failed, check.Evaluate(Evidence(("aob-module",
			Exact.Replace("\"filterExact\":true", "\"filterExact\":false", StringComparison.Ordinal)))).Status);
		Assert.Equal(ReceiptStatus.Failed, check.Evaluate(Evidence(("aob-module",
			Exact.Replace("HostBoundedRange", "GlobalHostScanWithManagedFilter", StringComparison.Ordinal)))).Status);
	}

	[Fact]
	public void TheDecimalToleranceNeedsEveryCaseAsExpected()
	{
		QualificationCheck check = Check("Q25", "S1", "decimal-tolerance");
		static string Cases(bool lastFound)
		{
			StringBuilder cases = new("""{"ok":true,"cases":[""");
			for (int index = 0; index < 12; index++)
			{
				bool expected = index % 6 < 4;
				bool found = index == 11 ? lastFound : expected;
				string item = "{\"scanned\":true,\"expectedFound\":" + (expected ? "true" : "false") + ",\"found\":" +
							  (found ? "true" : "false") + "}";
				cases.Append(index == 0 ? string.Empty : ",").Append(item);
			}

			return cases.Append("]}").ToString();
		}

		Assert.Equal(ReceiptStatus.Passed, check.Evaluate(Evidence(("value-scan-decimals", Cases(false)))).Status);
		Assert.Equal(ReceiptStatus.Failed, check.Evaluate(Evidence(("value-scan-decimals", Cases(true)))).Status);
	}

	[Fact]
	public void TheAutoAssemblerPolicyRefusalNeedsNoClientAndAMissingPolicyGate()
	{
		QualificationCheck check = Check("Q44", "S2", "auto-assembler-policy-refused");
		const string Refused = """{"ok":true,"processUnchanged":true,"families":[{"capability":"Client.AutoAssemblerPatches","state":"Unavailable","gates":{"policy":"Missing"},"policyRefusal":{"serviceRegistered":false,"refusedBeforeAnyHostCall":true}}]}""";

		Assert.Equal(ReceiptStatus.Passed, check.Evaluate(Evidence(("capabilities-policy", Refused))).Status);
		Assert.Equal(ReceiptStatus.Failed, check.Evaluate(Evidence(("capabilities-policy",
			Refused.Replace("\"serviceRegistered\":false", "\"serviceRegistered\":true", StringComparison.Ordinal)))).Status);
	}

	[Fact]
	public void ARefusedReleaseAfterATargetChangeEndsTheLease()
	{
		QualificationCheck check = Check("Q30.a", "S3", "refused-after-target-change");

		Assert.Equal(ReceiptStatus.Passed, check.Evaluate(Evidence(("allocation-state-on-b",
			"""{"lease":{"released":true,"requiresManualRecovery":true},"lastRelease":{"kind":"RefusedTargetChanged"}}"""))).Status);
		Assert.Equal(ReceiptStatus.Failed, check.Evaluate(Evidence(("allocation-state-on-b",
			"""{"lease":{"released":true,"requiresManualRecovery":false},"lastRelease":{"kind":"Released"}}"""))).Status);
	}

	[Fact]
	public void AMarshallingRefusalIsARaisedError()
	{
		QualificationCheck check = Check("Q21", "S1", "integer-float-2p53-refused");

		Assert.Equal(ReceiptStatus.Passed, check.Evaluate(Evidence(("integer-float-2p53", "number has no integer representation"),
			errors: ["integer-float-2p53"])).Status);
		Assert.Equal(ReceiptStatus.Failed, check.Evaluate(Evidence(("integer-float-2p53", """{"value":"9007199254740992"}"""))).Status);
	}

	[Fact]
	public void MissingStepsAreNotExecutedAndErrorsFail()
	{
		QualificationCheck check = Check("Q33", "S1", "partial-effect");

		CheckResult missing = check.Evaluate(Evidence());
		CheckResult error = check.Evaluate(Evidence(("batch-partial", "attempt to call a nil value"), errors: ["batch-partial"]));
		CheckResult notJson = check.Evaluate(Evidence(("batch-partial", "not json")));

		Assert.Equal(ReceiptStatus.NotExecuted, missing.Status);
		Assert.Contains("not reached", missing.Observation, StringComparison.Ordinal);
		Assert.Equal(ReceiptStatus.Failed, error.Status);
		Assert.Equal(ReceiptStatus.Failed, notJson.Status);
	}

	[Fact]
	public void AnOperatorStepThatDidNotRunKeepsItsCheckNotExecuted()
	{
		QualificationCheck check = Check("Q16", "S2", "kept-function-dies");

		CheckResult skipped = check.Evaluate(Evidence(("toggle-disable", "Operator: untick it"),
			("kept-function-after-disable", """{"ok":true}"""), notExecuted: ["toggle-disable"]));
		CheckResult ran = check.Evaluate(Evidence(("toggle-disable", "done"), ("kept-function-after-disable", "dead activation"),
			errors: ["kept-function-after-disable"]));

		Assert.Equal(ReceiptStatus.NotExecuted, skipped.Status);
		Assert.Contains("S0 spike", skipped.Observation, StringComparison.Ordinal);
		Assert.Equal(ReceiptStatus.Passed, ran.Status);
	}

	[Fact]
	public void TheReuseOfAProcessIdIsNeverPassed()
	{
		Assert.Equal(ReceiptStatus.NotExecuted, Check("Q30.b", "S3", "process-id-reuse").Evaluate(Evidence()).Status);
	}

	[Fact]
	public void CleanupOrderIsReadFromTheLifecycleSinkAndNeverGuessed()
	{
		QualificationCheck order = Check("Q43", "S2", "cleanup-continues-past-fault");
		QualificationCheck aggregated = Check("Q43", "S2", "failures-aggregated");
		string[] lifecycle =
		[
			"ledger\t#1 plugin.disabling", "ledger\t#1 last.disabling", "ledger\t#1 scenario.disabling",
			"ledger\t#1 fault.disabling.threw InvalidOperationException", "ledger\t#1 first.disabling", "ledger\t#1 resource.disposed",
			"log\tWarning\tCheatEngine.Client.Hosting.CheatEngineClientPlugin\t5\tCheat Engine Client activation {Epoch} completed cleanup with {FailureCount} failure(s)."
		];

		Assert.Equal(ReceiptStatus.Passed, order.Evaluate(Evidence(lifecycle: lifecycle)).Status);
		Assert.Equal(ReceiptStatus.Passed, aggregated.Evaluate(Evidence(lifecycle: lifecycle)).Status);
		Assert.Equal(ReceiptStatus.Failed, order.Evaluate(Evidence(lifecycle: [.. lifecycle.Where(static line => !line.Contains("first.", StringComparison.Ordinal))])).Status);
		Assert.Equal(ReceiptStatus.NotExecuted, order.Evaluate(Evidence(lifecycle: ["ledger\t#1 first.enabled"])).Status);
		Assert.Equal(ReceiptStatus.NotExecuted, aggregated.Evaluate(Evidence()).Status);
	}

	[Fact]
	public void RunnerFactsDecideTheBundleAndHygieneChecks()
	{
		Dictionary<string, string> facts = new(StringComparer.Ordinal)
		{
			[SessionFacts.BundleClientAssembliesMatch] = "true",
			[SessionFacts.ExpectedBridgeSha256] = "b008",
			[SessionFacts.BundleBridgeSha256] = "b008",
			[SessionFacts.DebugOutputSensitiveHits] = "1"
		};

		Assert.Equal(ReceiptStatus.Passed, Check("Q40", "S1", "client-assemblies-packed").Evaluate(Evidence(facts: facts)).Status);
		Assert.Equal(ReceiptStatus.Passed, Check("Q40", "S1", "bridge-reviewed").Evaluate(Evidence(facts: facts)).Status);
		Assert.Equal(ReceiptStatus.Failed, Check("Q46", "S1", "debug-output-clean").Evaluate(Evidence(facts: facts)).Status);
		Assert.Equal(ReceiptStatus.NotExecuted, Check("Q45", "S1", "no-module-injected").Evaluate(Evidence(facts: facts)).Status);
	}

	[Fact]
	public void TheNeighbourIdentityMustHoldEveryBoolean()
	{
		QualificationCheck check = Check("Q10", "S5a", "neighbour-identity");
		const string Answer = "Sdk1Neighbour=Answering; SdkHostingMajorIs1=True; BridgeMatchesPackage=True; " +
							  "OwnLoadContextIsNotDefault=True; SdkHostingMajor2LoadedElsewhere=True";

		Assert.Equal(ReceiptStatus.Passed, check.Evaluate(Evidence(("neighbour-identity", Answer))).Status);
		Assert.Equal(ReceiptStatus.Failed, check.Evaluate(Evidence(("neighbour-identity",
			Answer.Replace("BridgeMatchesPackage=True", "BridgeMatchesPackage=False", StringComparison.Ordinal)))).Status);
	}

	[Fact]
	public void AReceiptCarriesTheLevelOfItsScenario()
	{
		QualificationReceipt receipt = Check("Q09", "S5b", "both-enabled").ToReceipt("20260925T101530Z-a1b2", Evidence(
			("a-identity", "Plugin=A; PluginAssembly=A"), ("b-identity", "Plugin=B; PluginAssembly=B")));

		Assert.Equal(("S5b", "Q09", "both-enabled", "C4", ReceiptStatus.Passed),
			(receipt.Session, receipt.Scenario, receipt.Check, receipt.Level, receipt.Status));
	}

	private static QualificationCheck Check(string scenario, string session, string name)
	{
		return Assert.Single(ScenarioEvaluators.Checks,
			check => check.Scenario == scenario && check.Session == session && check.Name == name);
	}

	private static SessionEvidence Evidence(params (string Step, string Value)[] records)
	{
		return Evidence(records, [], [], [], null);
	}

	private static SessionEvidence Evidence((string Step, string Value) first, string[] errors)
	{
		return Evidence([first], errors, [], [], null);
	}

	private static SessionEvidence Evidence((string Step, string Value) first, (string Step, string Value) second,
		string[]? errors = null, string[]? notExecuted = null)
	{
		return Evidence([first, second], errors ?? [], notExecuted ?? [], [], null);
	}

	private static SessionEvidence Evidence(string[] lifecycle)
	{
		return Evidence([], [], [], lifecycle, null);
	}

	private static SessionEvidence Evidence(Dictionary<string, string> facts)
	{
		return Evidence([], [], [], [], facts);
	}

	private static SessionEvidence Evidence((string Step, string Value)[] records, string[] errors, string[] notExecuted,
		string[] lifecycle, Dictionary<string, string>? facts)
	{
		StringBuilder transcript = new();
		foreach ((string step, string value) in records)
		{
			string status = errors.Contains(step) ? "error" : notExecuted.Contains(step) ? "notexecuted" : "ok";
			transcript.Append("R\t").Append(step).Append('\t').Append(status).Append('\t')
				.Append('"').Append(value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal))
				.Append("\"\n");
		}

		return new SessionEvidence(TranscriptParser.Parse(Encoding.UTF8.GetBytes(transcript.Append("DONE\n").ToString())),
			string.Empty, lifecycle, facts ?? new Dictionary<string, string>(StringComparer.Ordinal));
	}
}
