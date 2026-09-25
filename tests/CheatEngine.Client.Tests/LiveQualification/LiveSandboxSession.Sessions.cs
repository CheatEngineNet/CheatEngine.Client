using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using CheatEngine.Client.Tests.Infrastructure;
using CheatEngine.Client.Tests.Packaging;

using LivePlugin.Qualification.Harness;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>
///     One live qualification run: the run directory every session of the collection shares, its receipt ledger and its
///     summary, rewritten after each session so that an interrupted run still leaves what it recorded.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class LiveQualificationRun
{
	private readonly List<QualificationReceipt> _receipts = [];
	private readonly List<SessionSummary> _sessions = [];
	private readonly List<TargetIdentity> _targets = [];
	private bool _registryRestored = true;
	private QualificationTuple? _tuple;

	private LiveQualificationRun(SandboxLayout layout)
	{
		Layout = layout;
		Redaction = QualificationRedaction.ForWorkstation(layout.RunDirectory);
		Ledger = new ReceiptLedger(layout.ReceiptsPath, Redaction);
	}

	/// <summary>The run directory.</summary>
	internal SandboxLayout Layout
	{
		get;
	}

	/// <summary>The redaction of everything the run records.</summary>
	internal QualificationRedaction Redaction
	{
		get;
	}

	/// <summary>The receipt ledger of the run.</summary>
	internal ReceiptLedger Ledger
	{
		get;
	}

	/// <summary>Creates the run directory below <paramref name="runRoot" />.</summary>
	internal static LiveQualificationRun Create(string runRoot)
	{
		return new LiveQualificationRun(SandboxLayout.Create(runRoot, DateTimeOffset.UtcNow));
	}

	/// <summary>Records one session: its receipts go to the ledger, and the summary is rewritten with every session so far.</summary>
	internal IReadOnlyList<QualificationReceipt> Record(SessionSummary session, IEnumerable<QualificationReceipt> receipts,
		bool registryRestored, QualificationTuple tuple)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(receipts);
		ArgumentNullException.ThrowIfNull(tuple);
		List<QualificationReceipt> recorded = [.. receipts.Select(Ledger.Append)];
		_receipts.AddRange(recorded);
		_sessions.Add(session);
		_registryRestored &= registryRestored;
		_targets.AddRange(tuple.Targets.Where(target => !_targets.Contains(target)));
		_tuple = (_tuple ?? tuple) with
		{
			Targets = [.. _targets]
		};
		QualificationSummaryWriter.Write(Layout.SummaryPath,
			new QualificationSummary(Layout.RunId, _tuple, [.. _sessions], [.. _receipts], ScenarioCatalog.CapabilityScenarios,
				_registryRestored), Redaction);
		return recorded;
	}
}

/// <summary>The sessions S1 to S6 of the live runner (runner specification, plan L24).</summary>
internal static partial class LiveSandboxSession
{
	/// <summary>The sensitive-value marker every harness Auto Assembler script contains (the harness log sink's marker).</summary>
	internal const string ScriptMarker = "cheatengine_client_qualification_script";

	/// <summary>
	///     Runs one session of <see cref="SessionPlans" />: the same guarded order as the spike, with the session's targets,
	///     bundles, harness inputs and driver, then the evaluators of its checks. The receipts are recorded in
	///     <paramref name="run" />.
	/// </summary>
	[SupportedOSPlatform("windows")]
	internal static async Task<LiveSessionResult> RunSessionAsync(QualificationSessionPlan plan, LiveQualificationInputs inputs,
		PackagedClientFeedFixture feed, ICheatEngineUserStateGuard userState, LiveQualificationRun run,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentNullException.ThrowIfNull(inputs);
		ArgumentNullException.ThrowIfNull(feed);
		ArgumentNullException.ThrowIfNull(userState);
		ArgumentNullException.ThrowIfNull(run);

		IReadOnlyList<string> blockers = HostProcessGuard.FindBlockers();
		Assert.True(blockers.Count == 0, $"A live session needs a quiet workstation: {string.Join("; ", blockers)}.");
		CheatEngineProfile profile = CheatEngineProfile.CheatEngine77;
		IReadOnlyList<string> problems = CheatEngineInstallation.Verify(inputs.CheatEngineDirectory, profile, PortableExecutableInspector.Instance);
		Assert.True(problems.Count == 0, $"The Cheat Engine installation is not the qualified profile: {string.Join(" ", problems)}");
		InstallationFingerprint before = CheatEngineInstallation.Fingerprint(inputs.CheatEngineDirectory, profile);

		SandboxLayout layout = run.Layout;
		string sessionDirectory = layout.SessionDirectory(plan.Session);
		string transcriptPath = Path.Combine(sessionDirectory, "transcript.txt");
		string debugOutputPath = Path.Combine(sessionDirectory, "debug-output.txt");
		string lifecyclePath = Path.Combine(sessionDirectory, "lifecycle.txt");
		Dictionary<string, string> facts = new(StringComparer.Ordinal)
		{
			[SessionFacts.ClientPackageVersion] = feed.ClientVersion
		};
		if (feed.UsesPinnedSdk)
		{
			facts[SessionFacts.ExpectedBridgeSha256] = feed.ConsumedSdk.GetProperty("nativeBridge").GetProperty("sha256").GetString() ?? string.Empty;
			facts[SessionFacts.ExpectedSdkContentHash] = feed.ConsumedSdk.GetProperty("contentHashSha512").GetString() ?? string.Empty;
		}

		HostExit exit = HostExit.Exited;
		List<string> sensitive = [ScriptMarker];
		List<TargetIdentity> targetIdentities = [];
		Dictionary<SessionBundle, PluginBundle> bundles = [];
		bool restored;
		using (ICheatEngineUserStateScope scope = userState.Begin(layout))
		{
			if (Directory.Exists(layout.CheatEngineDirectory))
			{
				Directory.Delete(layout.CheatEngineDirectory, true);
			}

			CheatEngineInstallation.CopyTo(inputs.CheatEngineDirectory, layout.CheatEngineDirectory, profile, PortableExecutableInspector.Instance);
			TemplateBundle? template = await BuildBundlesAsync(plan.Setup, feed, Path.Combine(sessionDirectory, "plugins"), bundles);
			RecordBundleFacts(feed, bundles, template, facts);

			Dictionary<string, LaunchedTarget> targets = new(StringComparer.Ordinal);
			Process? host = null;
			DebugOutputCapture? capture = null;
			try
			{
				foreach (SessionTarget target in plan.Setup.Targets)
				{
					targets[target.Role] = TargetLauncher.Start(Path.Combine(layout.CheatEngineDirectory, target.Executable),
						profile.TargetSha256[target.Executable]);
					targetIdentities.Add(new TargetIdentity(target.Executable, targets[target.Role].ImageSha256));
				}

				SessionContext context = CreateContext(plan, layout, sessionDirectory, transcriptPath, targets, bundles, template);
				sensitive.AddRange([context.ModuleHeaderPattern, context.AbsentPattern,
					context.FirstMarker.ToString(CultureInfo.InvariantCulture), context.NextMarker.ToString(CultureInfo.InvariantCulture)]);
				Dictionary<string, string> sessionInputs = SessionInputs(plan, sessionDirectory, lifecyclePath, profile, targets, bundles);
				await File.WriteAllTextAsync(layout.DriverScriptPath, LuaDriverScript.RenderSteps(plan.Session, transcriptPath,
					plan.Driver(context)), new UTF8Encoding(false), cancellationToken);

				capture = DebugOutputCapture.Start(DebugOutputBuffer.SystemAnsiEncoding());
				host = HostProcessGuard.StartCheatEngine(Path.Combine(layout.CheatEngineDirectory, profile.HostExecutable),
					sessionInputs, false);
				capture.Buffer.Track(host.Id);
				exit = await HostProcessGuard.WaitForExitAsync(host, HostProcessGuard.SessionTimeout, cancellationToken);
				RecordModuleFacts(targets.Values, facts);
			}
			finally
			{
				if (host is not null)
				{
					HostProcessGuard.Stop(host);
					capture?.WriteTo(debugOutputPath, host.Id);
					host.Dispose();
				}

				capture?.Dispose();
				foreach (LaunchedTarget target in targets.Values)
				{
					target.Dispose();
				}

				scope.Restore();
			}

			restored = scope.Restored;
		}

		IReadOnlyList<string> changes = CheatEngineInstallation.Compare(before, CheatEngineInstallation.Fingerprint(inputs.CheatEngineDirectory, profile));
		IReadOnlyList<string> leftovers = HostProcessGuard.FindBlockers();
		Transcript transcript = TranscriptParser.ParseFile(transcriptPath);
		string debugOutput = File.Exists(debugOutputPath) ? await File.ReadAllTextAsync(debugOutputPath, cancellationToken) : string.Empty;
		if (Observed.TryParse(transcript.Find("target-declare")?.Value ?? string.Empty, out Observed? declared) &&
			declared.Text("scratch") is { Length: > 2 } scratch)
		{
			sensitive.Add(scratch[2..]);
		}

		facts[SessionFacts.DebugOutputSensitiveHits] = CountValues(debugOutput, sensitive).ToString(CultureInfo.InvariantCulture);
		IReadOnlyList<string> lifecycle = File.Exists(lifecyclePath) ? await File.ReadAllLinesAsync(lifecyclePath, cancellationToken) : [];
		SessionEvidence evidence = new(transcript, debugOutput, lifecycle, facts);

		SessionOutcome outcome = exit switch
		{
			HostExit.TimedOut => SessionOutcome.TimedOut,
			HostExit.Exited when transcript.Completed => SessionOutcome.Completed,
			_ => SessionOutcome.Failed
		};
		string reason = outcome == SessionOutcome.Completed ? string.Empty : $"host {exit}, transcript completed: {transcript.Completed}";
		List<QualificationReceipt> receipts =
		[
			.. SessionHygieneReceipts(run.Layout.RunId, plan.Session, transcript, outcome, restored, changes, leftovers),
			.. plan.Checks.Select(check => check.ToReceipt(run.Layout.RunId, evidence))
		];
		PluginBundle tupleBundle = bundles.GetValueOrDefault(SessionBundle.Harness) ?? bundles.Values.First();
		QualificationTuple tuple = Tuple(feed, profile, tupleBundle, layout, targetIdentities);
		IReadOnlyList<QualificationReceipt> recorded = run.Record(new SessionSummary(plan.Session, outcome, reason), receipts, restored, tuple);
		return new LiveSessionResult(layout, outcome, reason, transcript, recorded, restored, changes, leftovers);
	}

	/// <summary>The hygiene checks every session records under its own id: it ran to its end and left the workstation as it was.</summary>
	internal static IEnumerable<QualificationReceipt> SessionHygieneReceipts(string runId, string session, Transcript transcript,
		SessionOutcome outcome, bool restored, IReadOnlyList<string> changes, IReadOnlyList<string> leftovers)
	{
		ArgumentNullException.ThrowIfNull(transcript);
		yield return Receipt("session-completed", "the driver writes DONE and Cheat Engine exits after closeCE()",
			outcome == SessionOutcome.Completed, $"outcome {outcome}; {transcript.Records.Count} records; problems: {string.Join("; ", transcript.Problems)}");
		yield return Receipt("user-state-restored", "HKCU\\Software\\Cheat Engine and %APPDATA%\\Cheat Engine equal their backup",
			restored, restored ? "restored and verified" : "not restored");
		yield return Receipt("installation-unchanged", "the source installation's host executable and autorun folder are unchanged",
			changes.Count == 0, changes.Count == 0 ? "unchanged" : string.Join("; ", changes));
		yield return Receipt("no-process-left", "no Cheat Engine or gtutorial process survives the session", leftovers.Count == 0,
			leftovers.Count == 0 ? "none" : string.Join("; ", leftovers));

		QualificationReceipt Receipt(string check, string expectation, bool passed, string observation)
		{
			return new QualificationReceipt(runId, session, session, check, "C3",
				passed ? ReceiptStatus.Passed : ReceiptStatus.Failed, expectation, observation);
		}
	}

	/// <summary>How many times the values occur in <paramref name="text" />, ignoring case.</summary>
	internal static int CountValues(string text, IEnumerable<string> values)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(values);
		int count = 0;
		foreach (string value in values.Where(static value => value.Trim().Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase))
		{
			for (int index = text.IndexOf(value, StringComparison.OrdinalIgnoreCase); index >= 0;
				 index = text.IndexOf(value, index + value.Length, StringComparison.OrdinalIgnoreCase))
			{
				count++;
			}
		}

		return count;
	}

	/// <summary>The first 8 bytes of a file, as an AOB pattern: the module header of a mapped image (Q27, Q28).</summary>
	internal static string HeaderPattern(string path)
	{
		byte[] header = new byte[8];
		using FileStream stream = File.OpenRead(path);
		stream.ReadExactly(header);
		return Convert.ToHexString(header).Chunk(2).Aggregate(new StringBuilder(),
			static (pattern, pair) => pattern.Append(pattern.Length > 0 ? " " : string.Empty).Append(pair)).ToString();
	}

	/// <summary>A random 16-byte AOB pattern, which the scanned modules almost surely do not contain (Q27, Q28).</summary>
	internal static string RandomPattern()
	{
		return string.Join(' ', RandomNumberGenerator.GetBytes(16).Select(static value => value.ToString("X2", CultureInfo.InvariantCulture)));
	}

	[SupportedOSPlatform("windows")]
	private static async Task<TemplateBundle?> BuildBundlesAsync(SessionSetup setup, PackagedClientFeedFixture feed,
		string pluginsDirectory, Dictionary<SessionBundle, PluginBundle> bundles)
	{
		PluginBundleBuilder builder = new(feed, pluginsDirectory);
		TemplateBundle? template = null;
		foreach (SessionBundle bundle in setup.Bundles)
		{
			switch (bundle)
			{
				case SessionBundle.Harness:
					bundles[bundle] = await builder.BuildHarnessAsync();
					break;
				case SessionBundle.PluginA or SessionBundle.PluginB or SessionBundle.PluginCollision:
					bundles[bundle] = await builder.BuildCoexistenceAsync(bundle);
					break;
				case SessionBundle.SdkNeighbour:
					bundles[bundle] = await builder.BuildNeighbourAsync();
					break;
				case SessionBundle.Template:
					template = await builder.BuildTemplateAsync();
					bundles[bundle] = template.Bundle;
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(setup), bundle, "Unknown bundle.");
			}
		}

		return template;
	}

	/// <summary>Q40: the Client assemblies of the harness bundle against the packed packages, and the deployed bridge.</summary>
	private static void RecordBundleFacts(PackagedClientFeedFixture feed, Dictionary<SessionBundle, PluginBundle> bundles,
		TemplateBundle? template, Dictionary<string, string> facts)
	{
		if (bundles.TryGetValue(SessionBundle.Harness, out PluginBundle? harness))
		{
			List<string> mismatches = [];
			foreach (string assembly in Directory.EnumerateFiles(harness.Directory, "CheatEngine.Client*.dll"))
			{
				string name = Path.GetFileName(assembly);
				if (string.Equals(name, PluginBundleBuilder.HarnessAssemblyName + ".dll", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				PackageArchive? archive = feed.Archives.FirstOrDefault(archive => !archive.IsSymbolPackage && archive.Contains("lib/net10.0/" + name));
				if (archive is null || !archive.Entry("lib/net10.0/" + name).AsSpan().SequenceEqual(File.ReadAllBytes(assembly)))
				{
					mismatches.Add(name);
				}
			}

			facts[SessionFacts.BundleClientAssembliesMatch] = mismatches.Count == 0 ? "true" : "false: " + string.Join(", ", mismatches);
			facts[SessionFacts.BundleBridgeSha256] = harness.BridgeSha256;
		}

		if (template is not null)
		{
			facts[SessionFacts.TemplateSdkContentHash] = template.SdkContentHash ?? "none";
			facts[SessionFacts.TemplateDepsWorkspacePaths] = template.DepsWorkspacePaths.ToString(CultureInfo.InvariantCulture);
		}
	}

	[SupportedOSPlatform("windows")]
	private static void RecordModuleFacts(IEnumerable<LaunchedTarget> targets, Dictionary<string, string> facts)
	{
		List<string> forbidden = [];
		List<string> added = [];
		foreach (LaunchedTarget target in targets.Where(static target => target.IsRunning))
		{
			IReadOnlyList<string> now = target.SnapshotModules();
			forbidden.AddRange(TargetLauncher.FindForbiddenModules(now));
			added.AddRange(now.Except(target.ModulesAtStart, StringComparer.Ordinal));
		}

		facts[SessionFacts.ForbiddenModules] = string.Join(", ", forbidden.Distinct(StringComparer.Ordinal));
		facts[SessionFacts.ModulesUnchanged] = added.Count == 0 ? "true" : "false: " + string.Join(", ", added.Distinct(StringComparer.Ordinal));
	}

	[SupportedOSPlatform("windows")]
	private static SessionContext CreateContext(QualificationSessionPlan plan, SandboxLayout layout, string sessionDirectory,
		string transcriptPath, Dictionary<string, LaunchedTarget> targets, Dictionary<SessionBundle, PluginBundle> bundles,
		TemplateBundle? template)
	{
		string module = plan.Setup.Targets.Count > 0 ? plan.Setup.Targets[0].Executable : string.Empty;
		string? fileAsProcess = null;
		if (plan.Setup.FileAsProcessCopy)
		{
			string folder = Directory.CreateDirectory(Path.Combine(sessionDirectory, "file-as-process")).FullName;
			fileAsProcess = Path.Combine(folder, CheatEngineProfile.Target64);
			File.Copy(Path.Combine(layout.CheatEngineDirectory, CheatEngineProfile.Target64), fileAsProcess);
		}

		return new SessionContext(transcriptPath,
			targets.ToDictionary(static pair => pair.Key, static pair => pair.Value.ProcessId, StringComparer.Ordinal),
			bundles.ToDictionary(static pair => pair.Key, static pair => pair.Value.EntryAssemblyPath),
			module,
			module.Length == 0 ? string.Empty : HeaderPattern(Path.Combine(layout.CheatEngineDirectory, module)),
			RandomPattern(),
			RandomNumberGenerator.GetInt32(0x1000_0000, int.MaxValue),
			RandomNumberGenerator.GetInt32(0x1000_0000, int.MaxValue),
			fileAsProcess,
			template?.StatusGlobal ?? string.Empty);
	}

	[SupportedOSPlatform("windows")]
	private static Dictionary<string, string> SessionInputs(QualificationSessionPlan plan, string sessionDirectory,
		string lifecyclePath, CheatEngineProfile profile, Dictionary<string, LaunchedTarget> targets,
		Dictionary<SessionBundle, PluginBundle> bundles)
	{
		Dictionary<string, string> inputs = new(StringComparer.Ordinal);
		if (plan.Setup.AuthorizedRole is { } role)
		{
			LaunchedTarget authorized = targets[role];
			string manifest = AuthorizationManifestWriter.Write(Path.Combine(sessionDirectory, "authorization.json"),
				profile.HostSha256, authorized.ProcessId, authorized.ImageSha256, DateTimeOffset.UtcNow,
				AuthorizationManifestWriter.MaximumLifetime);
			foreach ((string name, string value) in AuthorizationManifestWriter.SessionInputs(manifest))
			{
				inputs[name] = value;
			}
		}

		if (bundles.TryGetValue(SessionBundle.Harness, out PluginBundle? harness) && plan.Setup.Fault != FaultStage.None)
		{
			AuthorizationManifestWriter.WriteFaultSwitch(harness.Directory, plan.Setup.Fault);
		}

		if (plan.Setup.EnableAutoAssembler)
		{
			inputs[QualificationInputs.EnableAutoAssemblerVariable] = "1";
		}

		if (plan.Setup.TableRoot)
		{
			inputs[QualificationInputs.TableRootVariable] = Directory.CreateDirectory(Path.Combine(sessionDirectory, "tables")).FullName;
		}

		if (plan.Setup.LifecycleSink)
		{
			inputs[QualificationInputs.LifecycleFileVariable] = lifecyclePath;
		}

		return inputs;
	}

	private static QualificationTuple Tuple(PackagedClientFeedFixture feed, CheatEngineProfile profile, PluginBundle bundle,
		SandboxLayout layout, List<TargetIdentity> targets)
	{
		QualificationTuple single = Tuple(feed, profile, bundle, layout, targets.Count > 0 ? targets[0].Sha256 : "none");
		return single with
		{
			Targets = [.. targets.Distinct()]
		};
	}
}
