using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;

using CheatEngine.Client.Tests.Infrastructure;

using LivePlugin.Qualification.Harness;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>
///     The session plans S1 to S6 as reviewed data: each driver renders with valid, unique steps in the reviewed order,
///     calls only functions the harness declares, never attempts an unproven plugin toggle, and names every step its
///     checks read, and each session's setup matches what its scenarios need.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class SessionPlanTests
{
	private const string TranscriptPath = @"C:\runs\20260925T101530Z-a1b2\sessions\S1\transcript.txt";

	/// <summary>The reviewed step order of every session.</summary>
	private static readonly Dictionary<string, string> ReviewedSteps = new(StringComparer.Ordinal)
	{
		["S1"] = "main-form open-process opened-process scratch-allocation load-plugin harness-ready status runtime " +
				 "scratch-digest-before capabilities-probe scratch-digest-after target-declare roundtrip-bytes-with-nul " +
				 "roundtrip-utf8-multibyte roundtrip-utf16-with-nul roundtrip-int32-minus-one roundtrip-uint32-max " +
				 "roundtrip-int64-limits roundtrip-address-above-4gib batch-partial pointer-size-4 runtime-pointer-4 " +
				 "pointer-width-refusal pointer-size-8 runtime-pointer-8 instructions aob-known aob-absent aob-module " +
				 "aob-module-absent aob-limit aob-cancel value-scan-first value-scan-next value-scan-reset value-scan-release " +
				 "value-scan-decimals allocation-allocate allocation-state allocation-release aa-check aa-apply aa-release " +
				 "aa-apply-failing table-create table-destroy-record table-probe-destroyed table-create-again table-save " +
				 "table-load table-probe-loaded table-save-outside symbol-register symbol-state-registered symbol-release " +
				 "symbol-state-released worker-start worker-result worker-probe-global integer-exact integer-float-below " +
				 "integer-float-2p53 address-exact address-float-2p53 logs settings-probe clear-address-list",
		["S2"] = "main-form open-process opened-process load-plugin harness-ready status capabilities-policy " +
				 "aa-apply-refused keep-function toggle-disable kept-function-after-disable toggle-enable " +
				 "status-after-reenable write-configure-fault toggle-disable-for-fault toggle-enable-faulted " +
				 "remove-configure-fault toggle-enable-after-fault status-after-fault logs clear-address-list",
		["S3"] = "main-form open-process-a opened-process-a scratch-allocation load-plugin harness-ready target-declare " +
				 "allocation-allocate value-scan-first aa-apply open-process-b opened-process-b runtime-on-b " +
				 "allocation-state-on-b allocation-release-on-b value-scan-state-on-b value-scan-release-on-b aa-state-on-b " +
				 "aa-release-on-b open-process-a-again opened-process-a-again runtime-back-on-a allocation-state-back-on-a " +
				 "allocation-allocate-new allocation-release-new open-file-as-process runtime-file-as-process " +
				 "allocation-unidentified value-scan-unidentified aob-module-unidentified clear-address-list",
		["S4"] = "main-form open-process opened-process scratch-allocation load-plugin harness-ready target-declare runtime " +
				 "roundtrip-int64-limits roundtrip-address-above-4gib aob-module aob-module-absent instructions " +
				 "clear-address-list",
		["S5a"] = "main-form load-plugin-a a-ready load-neighbour neighbour-ready load-plugin-b b-ready a-identity b-identity " +
				  "neighbour-identity a-ping b-ping a-collision-before load-collision a-collision-after third-party-replace " +
				  "toggle-disable-a third-party-survives b-ping-after-a-disabled toggle-disable-b clear-address-list",
		["S5b"] = "main-form load-neighbour neighbour-ready load-plugin-a a-ready load-plugin-b b-ready a-identity b-identity " +
				  "neighbour-identity a-ping b-ping a-collision-before load-collision a-collision-after third-party-replace " +
				  "toggle-disable-a third-party-survives b-ping-after-a-disabled toggle-disable-b clear-address-list",
		["S6"] = "main-form load-template template-ready template-status clear-address-list"
	};

	[Fact]
	public void EverySessionRendersItsReviewedSteps()
	{
		Assert.Equal(ReviewedSteps.Keys.Order(StringComparer.Ordinal), SessionPlans.All.Select(static plan => plan.Session));
		foreach (QualificationSessionPlan plan in SessionPlans.All)
		{
			IReadOnlyList<LuaDriverStep> steps = plan.Driver(Context(plan));
			string rendered = LuaDriverScript.RenderSteps(plan.Session, TranscriptPath, steps);

			Assert.Equal(ReviewedSteps[plan.Session], string.Join(' ', steps.Select(static step => step.Name)));
			Assert.EndsWith("driver.Enabled = true\n", rendered, StringComparison.Ordinal);
			Assert.DoesNotContain("\r", rendered, StringComparison.Ordinal);
		}
	}

	[Fact]
	public void DriversCallOnlyFunctionsTheHarnessDeclares()
	{
		string functions = File.ReadAllText(RepositoryLayout.Combine(
			"tests/CheatEngine.Client.LivePlugin.Qualification/QualificationLuaFunctions.cs"));
		foreach (QualificationSessionPlan plan in SessionPlans.All)
		{
			foreach (LuaDriverStep step in plan.Driver(Context(plan)))
			{
				foreach (Match call in HarnessCall().Matches(step.Body))
				{
					Assert.Contains($"[LuaFunction(\"{call.Groups["name"].Value}\")]", functions, StringComparison.Ordinal);
				}
			}
		}
	}

	[Fact]
	public void PluginTogglesAreOperatorStepsNeverAttempted()
	{
		foreach (QualificationSessionPlan plan in SessionPlans.All)
		{
			IReadOnlyList<LuaDriverStep> steps = plan.Driver(Context(plan));
			Assert.All(steps.Where(static step => step.Name.StartsWith("toggle-", StringComparison.Ordinal)), static step =>
			{
				Assert.Equal(LuaDriverStepKind.Operator, step.Kind);
				Assert.StartsWith("Operator: in Edit > Settings > Plugins, ", step.Prompt, StringComparison.Ordinal);
				Assert.Equal(LuaDriverScript.OperatorAttempts, step.Attempts);
			});
			Assert.All(steps.Where(static step => step.Kind == LuaDriverStepKind.Operator),
				static step => Assert.StartsWith("toggle-", step.Name, StringComparison.Ordinal));
			string rendered = LuaDriverScript.RenderSteps(plan.Session, TranscriptPath, steps);
			Assert.DoesNotContain("Checked", rendered, StringComparison.Ordinal);
			Assert.DoesNotContain("ModalResult", rendered, StringComparison.Ordinal);
			Assert.DoesNotContain("getSettingsForm", string.Concat(steps.Where(static step => step.Kind == LuaDriverStepKind.Operator)
				.Select(static step => step.Body)), StringComparison.Ordinal);
		}
	}

	[Fact]
	public void AToggleEndsWhenThePluginsFunctionIsGoneOrBackAndAFailingEnableOnConfirmation()
	{
		LuaDriverStep disable = LuaDriverSteps.Toggle("toggle-disable", false, "Plugin", "plugin_status");
		LuaDriverStep enable = LuaDriverSteps.Toggle("toggle-enable", true, "Plugin", "plugin_status", "Note.");
		LuaDriverStep failing = LuaDriverSteps.ConfirmedToggle("toggle-enable-faulted", true, "Plugin", "It must fail.");

		Assert.Equal("""
			if type(_G["plugin_status"]) == "function" then return nil end
			return "disabled"
			""".ReplaceLineEndings("\n"), disable.Body.ReplaceLineEndings("\n"));
		Assert.Equal("""
			if type(_G["plugin_status"]) ~= "function" then return nil end
			return "enabled"
			""".ReplaceLineEndings("\n"), enable.Body.ReplaceLineEndings("\n"));
		Assert.Equal("Operator: in Edit > Settings > Plugins, tick 'Plugin' and press OK. Note. The driver continues once " +
					 "the plugin's functions are back.", enable.Prompt);
		Assert.Empty(failing.Body);
		Assert.Equal("Operator: in Edit > Settings > Plugins, tick 'Plugin' and press OK. It must fail. Then press Done.",
			failing.Prompt);
		string rendered = LuaDriverScript.RenderSteps("S2", TranscriptPath, [disable, failing]);
		Assert.Contains("""  { name = "toggle-enable-faulted", kind = "operator", attempts = 360, prompt = "Operator: in Edit > Settings > Plugins, tick 'Plugin' and press OK. It must fail. Then press Done." },""",
			rendered, StringComparison.Ordinal);
		Assert.Contains("""  { name = "toggle-disable", kind = "operator", attempts = 360, prompt = "Operator: in Edit > Settings > Plugins, untick 'Plugin' and press OK. The driver continues once the plugin's functions are gone.", run = function()""",
			rendered, StringComparison.Ordinal);
	}

	[Fact]
	public void EveryStepACheckReadsIsInItsSessionsDriver()
	{
		foreach (QualificationSessionPlan plan in SessionPlans.All)
		{
			IReadOnlyList<LuaDriverStep> steps = plan.Driver(Context(plan));
			StringBuilder transcript = new();
			foreach (LuaDriverStep step in steps)
			{
				transcript.Append("R\t").Append(step.Name).Append("\tok\t\"{}\"\n");
			}

			SessionEvidence evidence = new(TranscriptParser.Parse(Encoding.UTF8.GetBytes(transcript.Append("DONE\n").ToString())),
				string.Empty, [], new Dictionary<string, string>(StringComparer.Ordinal));
			foreach (QualificationCheck check in plan.Checks)
			{
				CheckResult result = check.Evaluate(evidence);
				Assert.False(result.Observation.Contains("not reached", StringComparison.Ordinal),
					$"{plan.Session} {check.Scenario}/{check.Name} reads a step the driver does not have: {result.Observation}");
			}
		}
	}

	[Fact]
	public void SessionSetupsMatchWhatTheirScenariosNeed()
	{
		Assert.True(SessionPlans.S1.Setup is { EnableAutoAssembler: true, TableRoot: true, LifecycleSink: true, AuthorizedRole: "A" });
		Assert.True(SessionPlans.S2.Setup is { EnableAutoAssembler: false, LifecycleSink: true, Fault: FaultStage.ModuleOnDisabling });
		Assert.True(SessionPlans.S3.Setup is { EnableAutoAssembler: true, FileAsProcessCopy: true, AuthorizedRole: "A" });
		Assert.Equal(["A", "B"], SessionPlans.S3.Setup.Targets.Select(static target => target.Role));
		Assert.All(SessionPlans.S3.Setup.Targets, static target => Assert.Equal(CheatEngineProfile.Target64, target.Executable));
		Assert.Equal(CheatEngineProfile.Target32, Assert.Single(SessionPlans.S4.Setup.Targets).Executable);
		Assert.Equal(SessionPlans.S5a.Setup.Bundles, SessionPlans.S5b.Setup.Bundles);
		Assert.Empty(SessionPlans.S5a.Setup.Targets);
		Assert.Empty(SessionPlans.S5b.Setup.Targets);
		Assert.DoesNotContain(SessionBundle.Harness, SessionPlans.S5a.Setup.Bundles);
		Assert.Null(SessionPlans.S5a.Setup.AuthorizedRole);
		Assert.Equal([SessionBundle.Template], SessionPlans.S6.Setup.Bundles);
		Assert.All(SessionPlans.All.Where(static plan => plan.Setup.Bundles.Contains(SessionBundle.Harness)),
			static plan => Assert.Equal("A", plan.Setup.AuthorizedRole));
	}

	[Fact]
	public void TheTwoCoexistenceOrdersDifferOnlyInTheirLoadOrder()
	{
		string[] first = [.. ReviewedSteps["S5a"].Split(' ').Order(StringComparer.Ordinal)];
		string[] second = [.. ReviewedSteps["S5b"].Split(' ').Order(StringComparer.Ordinal)];

		Assert.Equal(first, second);
		Assert.NotEqual(ReviewedSteps["S5a"], ReviewedSteps["S5b"]);
	}

	[Fact]
	public void DriverStepsRefuseDuplicateNames()
	{
		LuaDriverStep step = LuaDriverSteps.Lua("same", "return 1");

		Assert.Throws<ArgumentException>(() => LuaDriverScript.RenderSteps("S1", TranscriptPath, [step, step]));
	}

	[Fact]
	public void TheWorkerPollStopsOnlyWhenTheObservationIsNoLongerPending()
	{
		LuaDriverStep poll = LuaDriverSteps.Poll("worker-result", "worker_admission", 40, LuaLiteral.String("result"));

		Assert.Equal(LuaDriverStepKind.Poll, poll.Kind);
		Assert.Equal("""
			local harness = _G["cheatengine_client_qualification_worker_admission"]
			if type(harness) ~= "function" then error("the harness does not define " .. "cheatengine_client_qualification_worker_admission") end
			local value = harness("result")
			if string.find(value, "\"pending\":true", 1, true) ~= nil then return nil end
			return value
			""".ReplaceLineEndings("\n"), poll.Body.ReplaceLineEndings("\n"));
	}

	[Fact]
	public void PatternsAreTheFileHeaderAndSixteenRandomBytes()
	{
		using TemporaryDirectory temporary = new("SessionPatterns");
		string image = Path.Combine(temporary.CreateDirectory("image"), "target.exe");
		File.WriteAllBytes(image, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04]);

		Assert.Equal("4D 5A 90 00 03 00 00 00", LiveSandboxSession.HeaderPattern(image));
		Assert.Matches("^([0-9A-F]{2} ){15}[0-9A-F]{2}$", LiveSandboxSession.RandomPattern());
		Assert.Equal(4, LiveSandboxSession.CountValues("a MARKER, marker and 0x1E2880 1e2880", ["marker", "1E2880", "ab"]));
	}

	private static SessionContext Context(QualificationSessionPlan plan)
	{
		Dictionary<string, int> processIds = new(StringComparer.Ordinal);
		int next = 4242;
		foreach (SessionTarget target in plan.Setup.Targets)
		{
			processIds[target.Role] = next++;
		}

		return new SessionContext(TranscriptPath, processIds,
			plan.Setup.Bundles.ToDictionary(static bundle => bundle, static bundle => $@"C:\runs\plugins\{bundle}\{bundle}.dll"),
			plan.Setup.Targets.Count > 0 ? plan.Setup.Targets[0].Executable : string.Empty, "4D 5A 90 00 03 00 00 00",
			"01 23 45 67 89 AB CD EF 01 23 45 67 89 AB CD EF", 0x1234_5678, 0x2345_6789,
			plan.Setup.FileAsProcessCopy ? @"C:\runs\file-as-process\gtutorial-x86_64.exe" : null, "cheatengine_client_plugin_status");
	}

	[GeneratedRegex("""_G\["(?<name>cheatengine_client_qualification_[a-z0-9_]+)"\] *$""", RegexOptions.CultureInvariant | RegexOptions.Multiline, 1000)]
	private static partial Regex HarnessCall();
}
