using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

using CheatEngine.Client.Tests.Packaging;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>What one sandboxed session produced.</summary>
/// <param name="Layout">The run directory.</param>
/// <param name="Outcome">How the session ended.</param>
/// <param name="Reason">Why, when it did not complete.</param>
/// <param name="Transcript">The driver transcript.</param>
/// <param name="Receipts">The receipts recorded in the ledger.</param>
/// <param name="UserStateRestored">Whether the Cheat Engine user state was restored and verified.</param>
/// <param name="InstallationChanges">How the source installation changed; empty when it is untouched.</param>
/// <param name="LeftoverProcesses">Cheat Engine or gtutorial processes still running after the session.</param>
internal sealed record LiveSessionResult(
	SandboxLayout Layout,
	SessionOutcome Outcome,
	string Reason,
	Transcript Transcript,
	IReadOnlyList<QualificationReceipt> Receipts,
	bool UserStateRestored,
	IReadOnlyList<string> InstallationChanges,
	IReadOnlyList<string> LeftoverProcesses);

/// <summary>
///     Runs one sandboxed session end to end, in the order of the runner specification: refuse a busy workstation, verify
///     the source installation read-only, copy it into the run's sandbox, back up the user state, build the plugin bundle
///     from the packed packages, start the disposable target, write the authorization manifest and the autorun driver,
///     listen to debug output, start the sandboxed Cheat Engine directly and wait for it. A <c>finally</c> always stops
///     Cheat Engine and the target and restores the user state; the source installation is then fingerprinted again.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class LiveSandboxSession
{
	/// <summary>The qualification harness's display name (<c>QualificationPlugin.DisplayName</c>).</summary>
	internal const string HarnessDisplayName = "CheatEngine.Client Qualification Plugin";

	/// <summary>The S0 checks that must pass; the settings probe and the operator toggles may stay NotExecuted.</summary>
	internal static readonly string[] SpikeRequiredChecks =
	[
		"session-completed", "load-plugin", "harness-ready", "status", "runtime", "capabilities", "user-state-restored",
		"installation-unchanged", "no-process-left", "no-injected-module"
	];

	/// <summary>Runs the S0 spike: load the harness on gtutorial-x86_64, call status, runtime and capabilities(1), close.</summary>
	internal static async Task<LiveSessionResult> RunSpikeAsync(LiveQualificationInputs inputs, PackagedClientFeedFixture feed,
		ICheatEngineUserStateGuard userState, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(inputs);
		ArgumentNullException.ThrowIfNull(feed);
		ArgumentNullException.ThrowIfNull(userState);

		const string session = "S0";
		IReadOnlyList<string> blockers = HostProcessGuard.FindBlockers();
		Assert.True(blockers.Count == 0, $"A live session needs a quiet workstation: {string.Join("; ", blockers)}.");

		CheatEngineProfile profile = CheatEngineProfile.CheatEngine77;
		IReadOnlyList<string> problems = CheatEngineInstallation.Verify(inputs.CheatEngineDirectory, profile, PortableExecutableInspector.Instance);
		Assert.True(problems.Count == 0, $"The Cheat Engine installation is not the qualified profile: {string.Join(" ", problems)}");
		InstallationFingerprint before = CheatEngineInstallation.Fingerprint(inputs.CheatEngineDirectory, profile);

		SandboxLayout layout = SandboxLayout.Create(inputs.RunRoot, DateTimeOffset.UtcNow);
		string sessionDirectory = layout.SessionDirectory(session);
		string transcriptPath = Path.Combine(sessionDirectory, "transcript.txt");
		QualificationRedaction redaction = QualificationRedaction.ForWorkstation(layout.RunDirectory);
		ReceiptLedger ledger = new(layout.ReceiptsPath, redaction);

		HostExit exit = HostExit.Exited;
		IReadOnlyList<string> forbiddenModules = [];
		PluginBundle bundle;
		string targetSha256;
		bool restored;
		using (ICheatEngineUserStateScope scope = userState.Begin(layout))
		{
			CheatEngineInstallation.CopyTo(inputs.CheatEngineDirectory, layout.CheatEngineDirectory, profile, PortableExecutableInspector.Instance);
			bundle = await new PluginBundleBuilder(feed, layout.PluginsDirectory).BuildHarnessAsync();
			LaunchedTarget? target = null;
			Process? host = null;
			DebugOutputCapture? capture = null;
			try
			{
				string targetPath = Path.Combine(layout.CheatEngineDirectory, CheatEngineProfile.Target64);
				target = TargetLauncher.Start(targetPath, profile.TargetSha256[CheatEngineProfile.Target64]);
				targetSha256 = target.ImageSha256;
				string manifest = AuthorizationManifestWriter.Write(Path.Combine(sessionDirectory, "authorization.json"),
					profile.HostSha256, target.ProcessId, target.ImageSha256, DateTimeOffset.UtcNow,
					AuthorizationManifestWriter.MaximumLifetime);
				LuaDriverPlan plan = LuaDriverScript.SpikePlan(transcriptPath, target.ProcessId, bundle.EntryAssemblyPath, HarnessDisplayName);
				await File.WriteAllTextAsync(layout.DriverScriptPath, LuaDriverScript.Render(plan), new UTF8Encoding(false), cancellationToken);

				capture = DebugOutputCapture.Start(DebugOutputBuffer.SystemAnsiEncoding());
				host = HostProcessGuard.StartCheatEngine(Path.Combine(layout.CheatEngineDirectory, profile.HostExecutable),
					AuthorizationManifestWriter.SessionInputs(manifest), false);
				capture.Buffer.Track(host.Id);
				exit = await HostProcessGuard.WaitForExitAsync(host, HostProcessGuard.SessionTimeout, cancellationToken);
				forbiddenModules = target.IsRunning ? TargetLauncher.FindForbiddenModules(target.SnapshotModules()) : [];
			}
			finally
			{
				if (host is not null)
				{
					HostProcessGuard.Stop(host);
					capture?.WriteTo(Path.Combine(sessionDirectory, "debug-output.txt"), host.Id);
					host.Dispose();
				}

				capture?.Dispose();
				target?.Dispose();
				scope.Restore();
			}

			restored = scope.Restored;
		}

		IReadOnlyList<string> changes = CheatEngineInstallation.Compare(before,
			CheatEngineInstallation.Fingerprint(inputs.CheatEngineDirectory, profile));
		IReadOnlyList<string> leftovers = HostProcessGuard.FindBlockers();
		Transcript transcript = TranscriptParser.ParseFile(transcriptPath);
		SessionOutcome outcome = exit switch
		{
			HostExit.TimedOut => SessionOutcome.TimedOut,
			HostExit.Exited when transcript.Completed => SessionOutcome.Completed,
			_ => SessionOutcome.Failed
		};
		string reason = outcome == SessionOutcome.Completed ? string.Empty : $"host {exit}, transcript completed: {transcript.Completed}";

		List<QualificationReceipt> receipts = [];
		foreach (QualificationReceipt receipt in SpikeReceipts(layout.RunId, session, transcript, outcome, restored, changes, leftovers, forbiddenModules))
		{
			receipts.Add(ledger.Append(receipt));
		}

		QualificationTuple tuple = Tuple(feed, profile, bundle, layout, targetSha256);
		QualificationSummaryWriter.Write(layout.SummaryPath,
			new QualificationSummary(layout.RunId, tuple, [new SessionSummary(session, outcome, reason)], receipts,
				new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal), restored),
			redaction);
		return new LiveSessionResult(layout, outcome, reason, transcript, receipts, restored, changes, leftovers);
	}

	/// <summary>The S0 checks: the session ran to its end, the harness answered, and the workstation is left as it was.</summary>
	internal static IEnumerable<QualificationReceipt> SpikeReceipts(string runId, string session, Transcript transcript,
		SessionOutcome outcome, bool restored, IReadOnlyList<string> changes, IReadOnlyList<string> leftovers,
		IReadOnlyList<string> forbiddenModules)
	{
		ArgumentNullException.ThrowIfNull(transcript);
		yield return Receipt("session-completed", "the driver writes DONE and Cheat Engine exits after closeCE()",
			outcome == SessionOutcome.Completed, $"outcome {outcome}; {transcript.Records.Count} records; problems: {string.Join("; ", transcript.Problems)}");
		foreach (string step in (string[]) ["load-plugin", "harness-ready"])
		{
			TranscriptRecord? record = transcript.Find(step);
			yield return Receipt(step, $"the driver step {step} succeeds", record?.Status == TranscriptStatus.Ok,
				record is null ? "not reached" : $"{record.Status}: {record.Value}");
		}

		foreach (string step in (string[]) ["status", "runtime", "capabilities"])
		{
			TranscriptRecord? record = transcript.Find(step);
			yield return Receipt(step, $"the harness answers {step} with a JSON observation",
				record?.Status == TranscriptStatus.Ok && IsJsonObject(record.Value), record is null ? "not reached" : $"{record.Status}: {record.Value}");
		}

		// The probe only informs the spike: a settings form it cannot read is a fact to record, not a failure.
		TranscriptRecord? probe = transcript.Find("settings-probe");
		yield return new QualificationReceipt(runId, session, session, "settings-probe", "C3",
			probe?.Status == TranscriptStatus.Ok ? ReceiptStatus.Passed : ReceiptStatus.NotExecuted,
			"the settings form is inspected read-only", probe is null ? "not reached" : $"{probe.Status}: {probe.Value}");
		foreach (string step in (string[]) ["toggle-disable", "toggle-enable"])
		{
			TranscriptRecord? record = transcript.Find(step);
			yield return new QualificationReceipt(runId, session, session, step, "C3", ReceiptStatus.NotExecuted,
				"the plugin toggle runs through Settings > Plugins", record?.Value ?? "not reached");
		}

		yield return Receipt("user-state-restored", "HKCU\\Software\\Cheat Engine and %APPDATA%\\Cheat Engine equal their backup",
			restored, restored ? "restored and verified" : "not restored");
		yield return Receipt("installation-unchanged", "the source installation's host executable and autorun folder are unchanged",
			changes.Count == 0, changes.Count == 0 ? "unchanged" : string.Join("; ", changes));
		yield return Receipt("no-process-left", "no Cheat Engine or gtutorial process survives the session", leftovers.Count == 0,
			leftovers.Count == 0 ? "none" : string.Join("; ", leftovers));
		yield return Receipt("no-injected-module", "the target loads no speedhack, allochook, luaclient, vehdebug or dbk module",
			forbiddenModules.Count == 0, forbiddenModules.Count == 0 ? "none" : string.Join(", ", forbiddenModules));

		QualificationReceipt Receipt(string check, string expectation, bool passed, string observation)
		{
			return new QualificationReceipt(runId, session, session, check, "C3",
				passed ? ReceiptStatus.Passed : ReceiptStatus.Failed, expectation, observation);
		}
	}

	private static bool IsJsonObject(string text)
	{
		try
		{
			using JsonDocument document = JsonDocument.Parse(text);
			return document.RootElement.ValueKind == JsonValueKind.Object;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static QualificationTuple Tuple(PackagedClientFeedFixture feed, CheatEngineProfile profile, PluginBundle bundle,
		SandboxLayout layout, string targetSha256)
	{
		PackageArchive client = feed.Package(PackagedClientFeedFixture.ClientPackageId);
		string sdkFolder = Path.Combine(feed.PackageCache, "cheatengine.sdk", feed.SdkVersion.ToLowerInvariant());
		string sdkCommit = XDocument.Load(Path.Combine(sdkFolder, "cheatengine.sdk.nuspec")).Descendants()
			.FirstOrDefault(static element => element.Name.LocalName == "repository")?.Attribute("commit")?.Value ?? "unknown";
		string sdkContentHash;
		using (JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(sdkFolder, ".nupkg.metadata"))))
		{
			sdkContentHash = metadata.RootElement.GetProperty("contentHash").GetString() ?? "unknown";
		}

		return new QualificationTuple(
			[.. feed.Archives.Where(static archive => !archive.IsSymbolPackage).Select(static archive => new PackageIdentity(archive.Id, archive.Version, archive.Sha256))],
			client.MetadataElement("repository")?.Attribute("commit")?.Value ?? "unknown",
			feed.SdkVersion, sdkCommit, sdkContentHash, bundle.BridgeSha256, profile.Profile, profile.HostFileVersion,
			profile.HostSha256, [new TargetIdentity(CheatEngineProfile.Target64, targetSha256)],
			ConfiguredRuntime(layout.CheatEngineDirectory),
			Environment.OSVersion.Version.ToString());
	}

	/// <summary>The framework the sandboxed host's <c>ce.runtimeconfig.json</c> requests.</summary>
	private static string ConfiguredRuntime(string cheatEngineDirectory)
	{
		string path = Path.Combine(cheatEngineDirectory, "ce.runtimeconfig.json");
		if (!File.Exists(path))
		{
			return "no ce.runtimeconfig.json";
		}

		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
		JsonElement options = document.RootElement.GetProperty("runtimeOptions");
		JsonElement framework = options.TryGetProperty("framework", out JsonElement single)
			? single
			: options.GetProperty("frameworks")[0];
		return string.Create(CultureInfo.InvariantCulture,
			$"{framework.GetProperty("name").GetString()} {framework.GetProperty("version").GetString()} (ce.runtimeconfig.json)");
	}
}
