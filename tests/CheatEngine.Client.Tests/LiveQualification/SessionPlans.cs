using System.Runtime.Versioning;

using LivePlugin.Qualification.Harness;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>A plugin bundle a session loads, built from the packed packages (<see cref="PluginBundleBuilder" />).</summary>
internal enum SessionBundle
{
	/// <summary>The qualification harness.</summary>
	Harness,

	/// <summary>Coexistence Plugin A.</summary>
	PluginA,

	/// <summary>Coexistence Plugin B.</summary>
	PluginB,

	/// <summary>The coexistence contender that exports Plugin A's collision global.</summary>
	PluginCollision,

	/// <summary>The plain CheatEngine.SDK 1.x neighbour, generated at run time (<see cref="NeighbourPluginSource" />).</summary>
	SdkNeighbour,

	/// <summary>The packed Templates package, instantiated as <c>QualTemplatePlugin</c>.</summary>
	Template
}

/// <summary>A disposable target a session starts from the sandbox.</summary>
/// <param name="Role">The target's role in the session's steps (<c>A</c>, <c>B</c>).</param>
/// <param name="Executable">The target image of the profile, for example <see cref="CheatEngineProfile.Target64" />.</param>
internal sealed record SessionTarget(string Role, string Executable);

/// <summary>What a session needs before its driver runs.</summary>
/// <param name="Targets">The targets to start.</param>
/// <param name="AuthorizedRole">The target the authorization manifest names, or <see langword="null" /> for none.</param>
/// <param name="Bundles">The bundles to build.</param>
/// <param name="EnableAutoAssembler">Whether the harness composes the Auto Assembler opt-in (Q35).</param>
/// <param name="TableRoot">Whether the harness gets a table root below the session directory (Q34).</param>
/// <param name="LifecycleSink">Whether the harness writes the lifecycle receipt sink (Q43).</param>
/// <param name="Fault">The harness fault switch of the session's first enable.</param>
/// <param name="FileAsProcessCopy">Whether the session opens a copy of the x64 target as a file (S3).</param>
internal sealed record SessionSetup(
	IReadOnlyList<SessionTarget> Targets,
	string? AuthorizedRole,
	IReadOnlyList<SessionBundle> Bundles,
	bool EnableAutoAssembler = false,
	bool TableRoot = false,
	bool LifecycleSink = false,
	FaultStage Fault = FaultStage.None,
	bool FileAsProcessCopy = false);

/// <summary>The run-time values a session's driver needs.</summary>
/// <param name="TranscriptPath">Where the driver writes its transcript.</param>
/// <param name="ProcessIds">The started targets, by role.</param>
/// <param name="PluginPaths">The plugin assemblies, by bundle.</param>
/// <param name="TargetModule">The file name of the module the module-scoped scans use.</param>
/// <param name="ModuleHeaderPattern">The first 8 bytes of that module's file, as an AOB pattern (Q27, Q28).</param>
/// <param name="AbsentPattern">A random 16-byte pattern (Q27, Q28).</param>
/// <param name="FirstMarker">The value-scan marker of the first scan (Q25).</param>
/// <param name="NextMarker">The value-scan marker of the next scan (Q26).</param>
/// <param name="FileAsProcessPath">The target copy S3 opens as a file, or <see langword="null" />.</param>
/// <param name="TemplateStatusGlobal">The Lua global the instantiated template exports (S6).</param>
internal sealed record SessionContext(
	string TranscriptPath,
	IReadOnlyDictionary<string, int> ProcessIds,
	IReadOnlyDictionary<SessionBundle, string> PluginPaths,
	string TargetModule,
	string ModuleHeaderPattern,
	string AbsentPattern,
	int FirstMarker,
	int NextMarker,
	string? FileAsProcessPath,
	string TemplateStatusGlobal)
{
	/// <summary>The process id of the target in <paramref name="role" />.</summary>
	internal int ProcessId(string role)
	{
		return ProcessIds.TryGetValue(role, out int processId)
			? processId
			: throw new InvalidOperationException($"The session started no target '{role}'.");
	}

	/// <summary>The plugin assembly of <paramref name="bundle" />.</summary>
	internal string PluginPath(SessionBundle bundle)
	{
		return PluginPaths.TryGetValue(bundle, out string? path)
			? path
			: throw new InvalidOperationException($"The session built no {bundle} bundle.");
	}
}

/// <summary>One session of the live qualification: its setup, its driver and its checks.</summary>
/// <param name="Session">The session id (S1 to S6; S5a and S5b are the two load orders of S5).</param>
/// <param name="Title">What the session establishes.</param>
/// <param name="Setup">What the runner prepares.</param>
/// <param name="Driver">The driver steps, from the run-time values.</param>
[SupportedOSPlatform("windows")]
internal sealed record QualificationSessionPlan(
	string Session,
	string Title,
	SessionSetup Setup,
	Func<SessionContext, IReadOnlyList<LuaDriverStep>> Driver)
{
	/// <summary>The checks the evaluators run on this session's evidence.</summary>
	internal IReadOnlyList<QualificationCheck> Checks => [.. ScenarioEvaluators.Checks.Where(check => check.Session == Session)];

	/// <summary>The value of the <c>Session</c> trait of the live fact that runs this session.</summary>
	internal string Trait => Session[..2];
}

/// <summary>
///     The sessions S1 to S6 of the live qualification (runner specification). Every Cheat Engine-level setup (opening
///     a target, allocating the scratch region, changing the pointer size, opening a file as a process, loading a
///     plugin) is a reviewed driver step; the Client work runs in the plugins. A plugin toggle through Settings &gt;
///     Plugins is an operator step until the S0 spike proves that the driver can perform it (plan A12): the driver
///     prompts the operator and waits for the plugin's functions to disappear or come back, and a toggle the operator
///     skips is recorded <c>notexecuted</c>, so every check that needs it stays NotExecuted instead of guessing.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SessionPlans
{
	/// <summary>The scratch symbol the driver allocates and registers in the authorized target.</summary>
	internal const string ScratchSymbol = LuaDriverScript.HarnessFunctionPrefix + "scratch";

	/// <summary>The Lua global the driver keeps a harness function in, to call it after a disable (Q16, CRIT-07).</summary>
	internal const string KeptFunctionGlobal = LuaDriverScript.HarnessFunctionPrefix + "driver_kept";

	/// <summary>The allocation names of S1 and S3.</summary>
	internal const string AllocationName = LuaDriverScript.HarnessFunctionPrefix + "allocation";

	internal const string SecondAllocationName = AllocationName + "_second";

	/// <summary>The symbol the harness registers for Q16.b.</summary>
	internal const string LeasedSymbol = LuaDriverScript.HarnessFunctionPrefix + "leased_symbol";

	/// <summary>The table files of Q34, below the session's table root.</summary>
	internal const string TableFile = LuaDriverScript.HarnessFunctionPrefix + "table.CT";

	internal const string OutsideTableFile = LuaDriverScript.HarnessFunctionPrefix + "table_outside.CT";

	/// <summary>The harness's Plugin display name.</summary>
	internal const string HarnessDisplayName = LiveSandboxSession.HarnessDisplayName;

	/// <summary>The harness function whose presence tells the driver that the harness is enabled.</summary>
	internal const string HarnessStatus = LuaDriverScript.HarnessFunctionPrefix + "status";

	internal const string PluginADisplayName = "CheatEngine.Client Coexistence Plugin A";
	internal const string PluginBDisplayName = "CheatEngine.Client Coexistence Plugin B";

	/// <summary>The coexistence globals the S5 steps call.</summary>
	internal const string AIdentity = "cheatengine_client_coexistence_a_identity";

	internal const string APing = "cheatengine_client_coexistence_a_ping";
	internal const string ACollision = "cheatengine_client_coexistence_a_collision";
	internal const string BIdentity = "cheatengine_client_coexistence_b_identity";
	internal const string BPing = "cheatengine_client_coexistence_b_ping";

	/// <summary>S1: the x64 core on gtutorial-x86_64, with the Auto Assembler opt-in, a table root and the sink.</summary>
	internal static QualificationSessionPlan S1
	{
		get;
	} = new("S1", "x64 core on gtutorial-x86_64",
		new SessionSetup([new SessionTarget("A", CheatEngineProfile.Target64)], "A", [SessionBundle.Harness],
			EnableAutoAssembler: true, TableRoot: true, LifecycleSink: true),
		static context =>
		[
			LuaDriverSteps.MainForm(),
			.. LuaDriverSteps.OpenProcess("open-process", context.ProcessId("A")),
			ScratchAllocation(),
			.. LoadHarness(context),
			LuaDriverSteps.Call("status", "status"),
			LuaDriverSteps.Call("runtime", "runtime"),
			ScratchDigest("scratch-digest-before"),
			LuaDriverSteps.Call("capabilities-probe", "capabilities", LuaLiteral.Integer(1)),
			ScratchDigest("scratch-digest-after"),
			LuaDriverSteps.Call("target-declare", "target_declare", LuaLiteral.String(ScratchSymbol)),
			.. RoundTrips("bytes-with-nul", "utf8-multibyte", "utf16-with-nul", "int32-minus-one", "uint32-max",
				"int64-limits", "address-above-4gib"),
			LuaDriverSteps.Call("batch-partial", "memory_batch_partial", LuaLiteral.String(ScratchSymbol),
				LuaLiteral.Integer(0x10)),
			LuaDriverSteps.Lua("pointer-size-4", "setPointerSize(4)\nreturn \"4\""),
			LuaDriverSteps.Call("runtime-pointer-4", "runtime"),
			LuaDriverSteps.Call("pointer-width-refusal", "memory_roundtrip", LuaLiteral.String("address-above-4gib"),
				LuaLiteral.String(ScratchSymbol)),
			LuaDriverSteps.Lua("pointer-size-8", "setPointerSize(8)\nreturn \"8\""),
			LuaDriverSteps.Call("runtime-pointer-8", "runtime"),
			LuaDriverSteps.Call("instructions", "instructions", LuaLiteral.String(ScratchSymbol)),
			.. AobSteps(context),
			LuaDriverSteps.Call("value-scan-first", "value_scan", LuaLiteral.String("first"),
				LuaLiteral.String(ScratchSymbol), LuaLiteral.Integer(context.FirstMarker)),
			LuaDriverSteps.Call("value-scan-next", "value_scan", LuaLiteral.String("next"),
				LuaLiteral.String(ScratchSymbol), LuaLiteral.Integer(context.NextMarker)),
			LuaDriverSteps.Call("value-scan-reset", "value_scan", LuaLiteral.String("reset"),
				LuaLiteral.String(ScratchSymbol), LuaLiteral.Integer(0)),
			LuaDriverSteps.Call("value-scan-release", "value_scan", LuaLiteral.String("release"), LuaLiteral.String(""),
				LuaLiteral.Integer(0)),
			LuaDriverSteps.Call("value-scan-decimals", "value_scan", LuaLiteral.String("decimals"),
				LuaLiteral.String(ScratchSymbol), LuaLiteral.Integer(0)),
			Allocation("allocation-allocate", "allocate", AllocationName, 64),
			Allocation("allocation-state", "state", AllocationName, 0),
			Allocation("allocation-release", "release", AllocationName, 0),
			Patch("aa-check", "check", "benign"),
			Patch("aa-apply", "apply", "benign"),
			Patch("aa-release", "release", ""),
			Patch("aa-apply-failing", "apply", "failing"),
			LuaDriverSteps.Call("table-create", "table_create", LuaLiteral.String(ScratchSymbol)),
			LuaDriverSteps.Lua("table-destroy-record", """
				local record = getAddressList().getMemoryRecordByDescription("cheatengine_client_qualification_record")
				if record == nil then error("no harness record") end
				record.destroy()
				return "destroyed"
				"""),
			LuaDriverSteps.Call("table-probe-destroyed", "table_probe"),
			LuaDriverSteps.Call("table-create-again", "table_create", LuaLiteral.String(ScratchSymbol)),
			LuaDriverSteps.Call("table-save", "table_save", LuaLiteral.String(TableFile), LuaLiteral.Integer(0)),
			LuaDriverSteps.Call("table-load", "table_load", LuaLiteral.String(TableFile)),
			LuaDriverSteps.Call("table-probe-loaded", "table_probe"),
			LuaDriverSteps.Call("table-save-outside", "table_save", LuaLiteral.String(OutsideTableFile),
				LuaLiteral.Integer(1)),
			LuaDriverSteps.Call("symbol-register", "symbol_register", LuaLiteral.String(LeasedSymbol),
				LuaLiteral.String(ScratchSymbol)),
			LuaDriverSteps.Call("symbol-state-registered", "symbol_state", LuaLiteral.String(LeasedSymbol)),
			LuaDriverSteps.Call("symbol-release", "symbol_release", LuaLiteral.String(LeasedSymbol)),
			LuaDriverSteps.Call("symbol-state-released", "symbol_state", LuaLiteral.String(LeasedSymbol)),
			LuaDriverSteps.Call("worker-start", "worker_admission", LuaLiteral.String("start")),
			LuaDriverSteps.Poll("worker-result", "worker_admission", LuaDriverScript.ShortAttempts,
				LuaLiteral.String("result")),
			LuaDriverSteps.Lua("worker-probe-global",
				"return tostring(type(_G[\"cheatengine_client_qualification_worker_probe\"]))"),
			LuaDriverSteps.Call("integer-exact", "integer_echo", "9007199254740993"),
			LuaDriverSteps.Call("integer-float-below", "integer_echo", "9007199254740991.0"),
			LuaDriverSteps.Call("integer-float-2p53", "integer_echo", "2^53"),
			LuaDriverSteps.Call("address-exact", "address_echo", "0x7FFFFFFFFFFF"),
			LuaDriverSteps.Call("address-float-2p53", "address_echo", "2^53"),
			LuaDriverSteps.Call("logs", "logs"),
			LuaDriverSteps.SettingsProbe(),
			LuaDriverSteps.ClearAddressList()
		]);

	/// <summary>
	///     S2: lifecycle and faults, without the Auto Assembler opt-in and with the <c>ModuleOnDisabling</c> fault; the
	///     disable at <c>closeCE</c> reaches the lifecycle sink.
	/// </summary>
	internal static QualificationSessionPlan S2
	{
		get;
	} = new("S2", "lifecycle and faults",
		new SessionSetup([new SessionTarget("A", CheatEngineProfile.Target64)], "A", [SessionBundle.Harness],
			LifecycleSink: true, Fault: FaultStage.ModuleOnDisabling),
		static context =>
		[
			LuaDriverSteps.MainForm(),
			.. LuaDriverSteps.OpenProcess("open-process", context.ProcessId("A")),
			.. LoadHarness(context),
			LuaDriverSteps.Call("status", "status"),
			LuaDriverSteps.Call("capabilities-policy", "capabilities", LuaLiteral.Integer(0)),
			Patch("aa-apply-refused", "apply", "benign"),
			LuaDriverSteps.Lua("keep-function",
				$"{KeptFunctionGlobal} = _G[\"cheatengine_client_qualification_status\"]\nreturn \"kept\""),
			LuaDriverSteps.Toggle("toggle-disable", false, HarnessDisplayName, HarnessStatus),
			LuaDriverSteps.Lua("kept-function-after-disable", $"return {KeptFunctionGlobal}()"),
			LuaDriverSteps.Toggle("toggle-enable", true, HarnessDisplayName, HarnessStatus),
			LuaDriverSteps.Call("status-after-reenable", "status"),
			LuaDriverSteps.Lua("write-configure-fault", ConfigureFault(context, write: true)),
			LuaDriverSteps.Toggle("toggle-disable-for-fault", false, HarnessDisplayName, HarnessStatus),
			LuaDriverSteps.ConfirmedToggle("toggle-enable-faulted", true, HarnessDisplayName,
				"This enable must fail, because the harness's Configure throws: close the error Cheat Engine shows."),
			LuaDriverSteps.Lua("remove-configure-fault", ConfigureFault(context, write: false)),
			LuaDriverSteps.Toggle("toggle-enable-after-fault", true, HarnessDisplayName, HarnessStatus,
				"If it is still ticked after the failed enable, untick it and press OK first."),
			LuaDriverSteps.Call("status-after-fault", "status"),
			LuaDriverSteps.Call("logs", "logs"),
			LuaDriverSteps.ClearAddressList()
		]);

	/// <summary>
	///     S3: target identity on two gtutorial-x86_64 instances: the leases made on A after Cheat Engine selects B, back
	///     on A, and on a copy opened as a file.
	/// </summary>
	internal static QualificationSessionPlan S3
	{
		get;
	} = new("S3", "target identity",
		new SessionSetup(
			[new SessionTarget("A", CheatEngineProfile.Target64), new SessionTarget("B", CheatEngineProfile.Target64)], "A",
			[SessionBundle.Harness], EnableAutoAssembler: true, FileAsProcessCopy: true),
		static context =>
		[
			LuaDriverSteps.MainForm(),
			.. LuaDriverSteps.OpenProcess("open-process-a", context.ProcessId("A")),
			ScratchAllocation(),
			.. LoadHarness(context),
			LuaDriverSteps.Call("target-declare", "target_declare", LuaLiteral.String(ScratchSymbol)),
			LuaDriverSteps.Call("runtime-on-a", "runtime"),
			Allocation("allocation-allocate", "allocate", AllocationName, 64),
			LuaDriverSteps.Call("value-scan-first", "value_scan", LuaLiteral.String("first"),
				LuaLiteral.String(ScratchSymbol), LuaLiteral.Integer(context.FirstMarker)),
			Patch("aa-apply", "apply", "benign"),
			.. LuaDriverSteps.OpenProcess("open-process-b", context.ProcessId("B")),
			LuaDriverSteps.Call("runtime-on-b", "runtime"),
			Allocation("allocation-state-on-b", "state", AllocationName, 0),
			Allocation("allocation-release-on-b", "release", AllocationName, 0),
			LuaDriverSteps.Call("value-scan-state-on-b", "value_scan", LuaLiteral.String("state"), LuaLiteral.String(""),
				LuaLiteral.Integer(0)),
			LuaDriverSteps.Call("value-scan-release-on-b", "value_scan", LuaLiteral.String("release"),
				LuaLiteral.String(""), LuaLiteral.Integer(0)),
			Patch("aa-state-on-b", "state", ""),
			Patch("aa-release-on-b", "release", ""),
			.. LuaDriverSteps.OpenProcess("open-process-a-again", context.ProcessId("A")),
			LuaDriverSteps.Call("runtime-back-on-a", "runtime"),
			Allocation("allocation-state-back-on-a", "state", AllocationName, 0),
			Allocation("allocation-allocate-new", "allocate", SecondAllocationName, 64),
			Allocation("allocation-release-new", "release", SecondAllocationName, 0),
			LuaDriverSteps.Lua("open-file-as-process",
				$"return openFileAsProcess({LuaLiteral.String(context.FileAsProcessPath ?? string.Empty)}, true)"),
			LuaDriverSteps.Call("runtime-file-as-process", "runtime"),
			Allocation("allocation-unidentified", "allocate-unidentified", AllocationName + "_file", 16),
			LuaDriverSteps.Call("value-scan-unidentified", "value_scan", LuaLiteral.String("create-unidentified"),
				LuaLiteral.String(""), LuaLiteral.Integer(0)),
			LuaDriverSteps.Call("aob-module-unidentified", "aob", LuaLiteral.String(context.ModuleHeaderPattern),
				LuaLiteral.String(context.TargetModule), LuaLiteral.Integer(100_000), LuaLiteral.Integer(0)),
			LuaDriverSteps.ClearAddressList()
		]);

	/// <summary>S4: the x86 target gtutorial-i386.</summary>
	internal static QualificationSessionPlan S4
	{
		get;
	} = new("S4", "x86 target on gtutorial-i386",
		new SessionSetup([new SessionTarget("A", CheatEngineProfile.Target32)], "A", [SessionBundle.Harness]),
		static context =>
		[
			LuaDriverSteps.MainForm(),
			.. LuaDriverSteps.OpenProcess("open-process", context.ProcessId("A")),
			ScratchAllocation(),
			.. LoadHarness(context),
			LuaDriverSteps.Call("target-declare", "target_declare", LuaLiteral.String(ScratchSymbol)),
			LuaDriverSteps.Call("runtime", "runtime"),
			.. RoundTrips("int64-limits", "address-above-4gib"),
			LuaDriverSteps.Call("aob-module", "aob", LuaLiteral.String(context.ModuleHeaderPattern),
				LuaLiteral.String(context.TargetModule), LuaLiteral.Integer(100_000), LuaLiteral.Integer(0)),
			LuaDriverSteps.Call("aob-module-absent", "aob", LuaLiteral.String(context.AbsentPattern),
				LuaLiteral.String(context.TargetModule), LuaLiteral.Integer(100_000), LuaLiteral.Integer(0)),
			LuaDriverSteps.Call("instructions", "instructions", LuaLiteral.String(ScratchSymbol)),
			LuaDriverSteps.ClearAddressList()
		]);

	/// <summary>S5a: coexistence in the order Plugin A, the SDK 1.x neighbour, Plugin B.</summary>
	internal static QualificationSessionPlan S5a
	{
		get;
	} = new("S5a", "coexistence: A, SDK 1.x neighbour, B", CoexistenceSetup(),
		static context => Coexistence(context, neighbourFirst: false));

	/// <summary>S5b: coexistence in the order the SDK 1.x neighbour, Plugin A, Plugin B.</summary>
	internal static QualificationSessionPlan S5b
	{
		get;
	} = new("S5b", "coexistence: SDK 1.x neighbour, A, B", CoexistenceSetup(),
		static context => Coexistence(context, neighbourFirst: true));

	/// <summary>S6: the template, instantiated from the packed Templates package and loaded (Q40).</summary>
	internal static QualificationSessionPlan S6
	{
		get;
	} = new("S6", "template clean install",
		new SessionSetup([], null, [SessionBundle.Template]),
		static context =>
		[
			LuaDriverSteps.MainForm(),
			LuaDriverSteps.LoadPlugin("load-template", context.PluginPath(SessionBundle.Template)),
			LuaDriverSteps.GlobalReady("template-ready", context.TemplateStatusGlobal),
			LuaDriverSteps.CallGlobal("template-status", context.TemplateStatusGlobal),
			LuaDriverSteps.ClearAddressList()
		]);

	/// <summary>Every session, in run order.</summary>
	internal static IReadOnlyList<QualificationSessionPlan> All => [S1, S2, S3, S4, S5a, S5b, S6];

	/// <summary>The plan of one session.</summary>
	internal static QualificationSessionPlan Get(string session)
	{
		return All.SingleOrDefault(plan => plan.Session == session) ??
			   throw new ArgumentException($"No session '{session}'.", nameof(session));
	}

	private static LuaDriverStep ScratchAllocation()
	{
		string script = $"alloc({ScratchSymbol},4096)\nregistersymbol({ScratchSymbol})";
		return LuaDriverSteps.Lua("scratch-allocation", $"return tostring(autoAssemble({LuaLiteral.String(script)}))");
	}

	private static IEnumerable<LuaDriverStep> LoadHarness(SessionContext context)
	{
		yield return LuaDriverSteps.LoadPlugin("load-plugin", context.PluginPath(SessionBundle.Harness));
		yield return LuaDriverSteps.GlobalReady("harness-ready", LuaDriverScript.HarnessFunctionPrefix + "status");
	}

	/// <summary>
	///     Reads the whole scratch region through Cheat Engine (not the Client) and folds it into one number, with the
	///     opened process id, so Q45 can compare it around the capability probe.
	/// </summary>
	private static LuaDriverStep ScratchDigest(string step)
	{
		return LuaDriverSteps.Lua(step, $$"""
			local bytes = readBytes({{LuaLiteral.String(ScratchSymbol)}}, 4096, true)
			if bytes == nil then error("the scratch region is not readable") end
			local sum = 0
			for index = 1, #bytes do sum = (sum * 31 + bytes[index]) % 2147483647 end
			return getOpenedProcessID() .. ":" .. #bytes .. ":" .. sum
			""");
	}

	private static IEnumerable<LuaDriverStep> RoundTrips(params string[] kinds)
	{
		foreach (string kind in kinds)
		{
			yield return LuaDriverSteps.Call("roundtrip-" + kind, "memory_roundtrip", LuaLiteral.String(kind),
				LuaLiteral.String(ScratchSymbol));
		}
	}

	private static IEnumerable<LuaDriverStep> AobSteps(SessionContext context)
	{
		string header = LuaLiteral.String(context.ModuleHeaderPattern);
		string absent = LuaLiteral.String(context.AbsentPattern);
		string module = LuaLiteral.String(context.TargetModule);
		string none = LuaLiteral.String(string.Empty);
		yield return LuaDriverSteps.Call("aob-known", "aob", header, none, LuaLiteral.Integer(100), LuaLiteral.Integer(0));
		yield return LuaDriverSteps.Call("aob-absent", "aob", absent, none, LuaLiteral.Integer(100), LuaLiteral.Integer(0));
		yield return LuaDriverSteps.Call("aob-module", "aob", header, module, LuaLiteral.Integer(100_000),
			LuaLiteral.Integer(0));
		yield return LuaDriverSteps.Call("aob-module-absent", "aob", absent, module, LuaLiteral.Integer(100_000),
			LuaLiteral.Integer(0));
		yield return LuaDriverSteps.Call("aob-limit", "aob", header, none, LuaLiteral.Integer(1), LuaLiteral.Integer(0));
		yield return LuaDriverSteps.Call("aob-cancel", "aob", header, none, LuaLiteral.Integer(100), LuaLiteral.Integer(1));
	}

	private static LuaDriverStep Allocation(string step, string action, string name, long size)
	{
		return LuaDriverSteps.Call(step, "allocation", LuaLiteral.String(action), LuaLiteral.String(name),
			LuaLiteral.Integer(size));
	}

	private static LuaDriverStep Patch(string step, string action, string variant)
	{
		return LuaDriverSteps.Call(step, "aa_patch", LuaLiteral.String(action), LuaLiteral.String(variant));
	}

	/// <summary>Writes (or removes) the harness fault switch that selects a <c>Configure</c> fault for the next enable (Q06).</summary>
	private static string ConfigureFault(SessionContext context, bool write)
	{
		string directory = Path.GetDirectoryName(context.PluginPath(SessionBundle.Harness)) ?? string.Empty;
		string path = LuaLiteral.String(Path.Combine(directory, QualificationFaultSwitch.FileName));
		if (!write)
		{
			return $"return tostring(os.remove({path}))";
		}

		string content = LuaLiteral.String(
			$$"""{ "schema": "{{QualificationFaultSwitch.Schema}}", "throwIn": "{{FaultStage.Configure}}" }""");
		return $"""
			local file = assert(io.open({path}, "wb"))
			file:write({content})
			file:close()
			return "written"
			""";
	}

	private static SessionSetup CoexistenceSetup()
	{
		return new SessionSetup([], null,
			[SessionBundle.PluginA, SessionBundle.PluginB, SessionBundle.PluginCollision, SessionBundle.SdkNeighbour]);
	}

	private static IReadOnlyList<LuaDriverStep> Coexistence(SessionContext context, bool neighbourFirst)
	{
		LuaDriverStep[] neighbour =
		[
			LuaDriverSteps.LoadPlugin("load-neighbour", context.PluginPath(SessionBundle.SdkNeighbour)),
			LuaDriverSteps.GlobalReady("neighbour-ready", NeighbourPluginSource.IdentityGlobal)
		];
		LuaDriverStep[] pluginA =
		[
			LuaDriverSteps.LoadPlugin("load-plugin-a", context.PluginPath(SessionBundle.PluginA)),
			LuaDriverSteps.GlobalReady("a-ready", AIdentity)
		];
		return
		[
			LuaDriverSteps.MainForm(),
			.. neighbourFirst ? neighbour : pluginA,
			.. neighbourFirst ? pluginA : neighbour,
			LuaDriverSteps.LoadPlugin("load-plugin-b", context.PluginPath(SessionBundle.PluginB)),
			LuaDriverSteps.GlobalReady("b-ready", BIdentity),
			LuaDriverSteps.CallGlobal("a-identity", AIdentity),
			LuaDriverSteps.CallGlobal("b-identity", BIdentity),
			LuaDriverSteps.CallGlobal("neighbour-identity", NeighbourPluginSource.IdentityGlobal),
			LuaDriverSteps.CallGlobal("a-ping", APing),
			LuaDriverSteps.CallGlobal("b-ping", BPing),
			LuaDriverSteps.CallGlobal("a-collision-before", ACollision),
			LuaDriverSteps.LoadPlugin("load-collision", context.PluginPath(SessionBundle.PluginCollision)),
			LuaDriverSteps.CallGlobal("a-collision-after", ACollision),
			LuaDriverSteps.Lua("third-party-replace", $$"""
				cheatengine_client_coexistence_q16_third_party = function() return "ThirdParty=Q16" end
				{{APing}} = cheatengine_client_coexistence_q16_third_party
				return "replaced"
				"""),
			LuaDriverSteps.Toggle("toggle-disable-a", false, PluginADisplayName, AIdentity),
			LuaDriverSteps.Lua("third-party-survives", $$"""
				return tostring({{APing}} == cheatengine_client_coexistence_q16_third_party and {{AIdentity}} == nil and type({{BIdentity}}) == "function")
				"""),
			LuaDriverSteps.CallGlobal("b-ping-after-a-disabled", BPing),
			LuaDriverSteps.Toggle("toggle-disable-b", false, PluginBDisplayName, BIdentity),
			LuaDriverSteps.ClearAddressList()
		];
	}
}
