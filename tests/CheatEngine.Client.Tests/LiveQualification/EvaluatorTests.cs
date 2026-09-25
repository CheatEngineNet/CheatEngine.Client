using System.Globalization;
using System.Runtime.Versioning;
using System.Text;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>
///     The evaluators on canned evidence: each check passes on the observation it requires and fails on its opposite,
///     a missing step is NotExecuted, an operator step the driver did not see done keeps its check NotExecuted, and
///     nothing that depends on a fact the spike records is guessed.
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

	[Theory]
	[InlineData(true, true, false, 12, true, "rule: ordinary rounding")]
	[InlineData(false, false, false, 12, true, "rule: the documented range")]
	[InlineData(true, false, false, 12, false, "float and double disagree")]
	[InlineData(true, true, true, 12, false, "unmet: DoubleFloat with 2 decimals")]
	[InlineData(true, true, false, 11, false, "11 cases")]
	public void TheDecimalToleranceMeetsEveryExpectationAndNamesTheRoundingRule(bool singleFound, bool doubleFound,
		bool lastFound, int count, bool passes, string observation)
	{
		QualificationCheck check = Check("Q25", "S1", "decimal-tolerance");
		StringBuilder cases = new("""{"ok":true,"cases":[""");
		for (int index = 0; index < count; index++)
		{
			// Per type: 5 decimals, the discriminating 3 decimals, 2, 0, then 3.2 and 3.15, which must not match.
			string type = index < 6 ? "SingleFloat" : "DoubleFloat";
			string decimals = (index % 6) switch
			{
				0 => "5",
				1 => "3",
				2 => "2",
				3 => "0",
				4 => "1",
				_ => "2"
			};
			string item = index % 6 == 1
				? "{\"type\":\"" + type + "\",\"decimals\":3,\"discriminating\":true,\"expectedFound\":null,\"scanned\":true," +
				  "\"found\":" + ((index < 6 ? singleFound : doubleFound) ? "true" : "false") + "}"
				: "{\"type\":\"" + type + "\",\"decimals\":" + decimals + ",\"discriminating\":false,\"expectedFound\":" +
				  (index % 6 < 4 ? "true" : "false") + ",\"scanned\":true,\"found\":" +
				  ((index == 11 ? lastFound : index % 6 < 4) ? "true" : "false") + "}";
			cases.Append(index == 0 ? string.Empty : ",").Append(item);
		}

		CheckResult result = check.Evaluate(Evidence(("value-scan-decimals", cases.Append("]}").ToString())));

		Assert.Equal(passes ? ReceiptStatus.Passed : ReceiptStatus.Failed, result.Status);
		Assert.Contains(observation, result.Observation, StringComparison.Ordinal);
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

	[Theory]
	[InlineData(4243, 2, true)]
	[InlineData(4242, 2, false)]
	[InlineData(4243, 1, false)]
	[InlineData(4243, 3, false)]
	public void ATargetSwitchReportsTheSelectedProcessWithALaterEpoch(int processOnB, int epochOnB, bool passes)
	{
		QualificationCheck check = Check("Q32", "S3", "target-change-observed");
		static string Runtime(int processId, int epoch)
		{
			return "{\"ok\":true,\"process\":{\"processId\":" + processId.ToString(CultureInfo.InvariantCulture) +
				   ",\"selectionEpoch\":" + epoch.ToString(CultureInfo.InvariantCulture) + "}}";
		}

		CheckResult result = check.Evaluate(Evidence(("opened-process-a", "4242"), ("runtime-on-a", Runtime(4242, 1)),
			("opened-process-b", "4243"), ("runtime-on-b", Runtime(processOnB, epochOnB)),
			("opened-process-a-again", "4242"), ("runtime-back-on-a", Runtime(4242, 3))));

		Assert.Equal(passes ? ReceiptStatus.Passed : ReceiptStatus.Failed, result.Status);
		Assert.Contains("opened-process-b=4243: runtime-on-b process " + processOnB.ToString(CultureInfo.InvariantCulture),
			result.Observation, StringComparison.Ordinal);
		Assert.Equal(ReceiptStatus.NotExecuted, check.Evaluate(Evidence(("opened-process-a", "4242"),
			("runtime-on-a", Runtime(4242, 1)))).Status);
	}

	[Fact]
	public void ALeaseEndedByATargetChangeIsNeverReleasedOnTheOtherProcess()
	{
		const string Ended = """{"ok":true,"session":{"state":"Released","released":true},"lastRelease":{"kind":"RefusedTargetChanged","requiresManualRecovery":true}}""";
		const string ReleasedOnB = """{"ok":true,"release":{"kind":"RefusedTargetChanged","hostEffect":"NotStarted"},"lastRelease":{"kind":"RefusedTargetChanged","requiresManualRecovery":true}}""";
		const string FreedOnB = """{"ok":true,"release":{"kind":"Released","hostEffect":"NotStarted"},"lastRelease":{"kind":"RefusedTargetChanged","requiresManualRecovery":true}}""";
		const string AllocationReleasedOnB = """{"ok":true,"releasedBefore":true,"release":{"kind":"RefusedTargetChanged","hostEffect":"NotStarted"},"requiresManualRecovery":true}""";
		const string AllocationFreedOnB = """{"ok":true,"releasedBefore":false,"release":{"kind":"Released","hostEffect":"Completed"},"requiresManualRecovery":false}""";

		Assert.Equal(ReceiptStatus.Passed, Check("Q26", "S3", "refused-after-target-change")
			.Evaluate(Evidence(("value-scan-state-on-b", Ended))).Status);
		Assert.Equal(ReceiptStatus.Failed, Check("Q26", "S3", "refused-after-target-change")
			.Evaluate(Evidence(("value-scan-state-on-b", Ended.Replace("RefusedTargetChanged", "Released", StringComparison.Ordinal)))).Status);
		Assert.Equal(ReceiptStatus.Passed, Check("Q26", "S3", "nothing-released-on-b")
			.Evaluate(Evidence(("value-scan-release-on-b", ReleasedOnB))).Status);
		Assert.Equal(ReceiptStatus.Failed, Check("Q26", "S3", "nothing-released-on-b")
			.Evaluate(Evidence(("value-scan-release-on-b", FreedOnB))).Status);
		Assert.Equal(ReceiptStatus.Passed, Check("Q35", "S3", "nothing-disabled-on-b")
			.Evaluate(Evidence(("aa-release-on-b", ReleasedOnB))).Status);
		Assert.Equal(ReceiptStatus.Failed, Check("Q35", "S3", "nothing-disabled-on-b")
			.Evaluate(Evidence(("aa-release-on-b", ReleasedOnB.Replace("NotStarted", "Completed", StringComparison.Ordinal)))).Status);
		Assert.Equal(ReceiptStatus.Passed, Check("Q30.a", "S3", "nothing-freed-on-b")
			.Evaluate(Evidence(("allocation-release-on-b", AllocationReleasedOnB))).Status);
		Assert.Equal(ReceiptStatus.Failed, Check("Q30.a", "S3", "nothing-freed-on-b")
			.Evaluate(Evidence(("allocation-release-on-b", AllocationFreedOnB))).Status);
		Assert.Equal(ReceiptStatus.Passed, Check("Q26", "S3", "file-as-process-refused").Evaluate(Evidence(("value-scan-unidentified",
			"""{"ok":true,"created":false,"failure":{"kind":"TargetIdentityUnavailable","hostEffect":"NotStarted"}}"""))).Status);
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
	public void AnOperatorStepTheDriverDidNotSeeDoneKeepsItsCheckNotExecuted()
	{
		QualificationCheck check = Check("Q16", "S2", "kept-function-dies");

		CheckResult skipped = check.Evaluate(Evidence(("toggle-disable", "skipped by the operator: Operator: untick it"),
			("kept-function-after-disable", """{"ok":true}"""), notExecuted: ["toggle-disable"]));
		CheckResult failedPrompt = check.Evaluate(Evidence(("toggle-disable", "attempt to call a nil value"),
			("kept-function-after-disable", """{"ok":true}"""), errors: ["toggle-disable"]));
		CheckResult ran = check.Evaluate(Evidence(("toggle-disable", "disabled"), ("kept-function-after-disable", "dead activation"),
			errors: ["kept-function-after-disable"]));

		Assert.Equal(ReceiptStatus.NotExecuted, skipped.Status);
		Assert.Contains("toggle-disable was not performed (NotExecuted: skipped by the operator", skipped.Observation,
			StringComparison.Ordinal);
		Assert.Equal(ReceiptStatus.NotExecuted, failedPrompt.Status);
		Assert.Contains("toggle-disable was not performed (Error:", failedPrompt.Observation, StringComparison.Ordinal);
		Assert.Equal(ReceiptStatus.Passed, ran.Status);
	}

	[Fact]
	public void AFailedEnableIsReadOnlyAfterEveryToggleOfItsProtocol()
	{
		QualificationCheck check = Check("Q06", "S2", "failed-enable-reported");
		const string Reported = """{"ok":true,"plugin":{"active":true},"ledger":["#2 configure fault=Configure reason=Selected","#2 configure.threw InvalidOperationException"]}""";
		(string Step, string Value)[] steps =
		[
			("toggle-disable-for-fault", "disabled"), ("toggle-enable-faulted", "confirmed by the operator"),
			("toggle-enable-after-fault", "enabled"), ("status-after-fault", Reported)
		];

		Assert.Equal(ReceiptStatus.Passed, check.Evaluate(Evidence(steps)).Status);
		Assert.Equal(ReceiptStatus.NotExecuted, check.Evaluate(Evidence([.. steps.Skip(1)])).Status);
		Assert.Equal(ReceiptStatus.NotExecuted,
			check.Evaluate(Evidence(steps, [], ["toggle-enable-after-fault"], [], null)).Status);
		Assert.Equal(ReceiptStatus.Failed, check.Evaluate(Evidence([.. steps[..3], ("status-after-fault",
			Reported.Replace("configure.threw", "configure", StringComparison.Ordinal))])).Status);
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
