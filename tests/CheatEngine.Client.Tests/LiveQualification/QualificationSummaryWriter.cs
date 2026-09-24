using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>How a session ended.</summary>
internal enum SessionOutcome
{
	/// <summary>The driver wrote <c>DONE</c> and Cheat Engine exited.</summary>
	Completed,

	/// <summary>The session exceeded its timeout; the runner closed, then killed Cheat Engine.</summary>
	TimedOut,

	/// <summary>The session could not run to its end (a preflight check, the build, or the host failed).</summary>
	Failed
}

/// <summary>A package the run consumed.</summary>
/// <param name="Id">The package id.</param>
/// <param name="Version">The package version.</param>
/// <param name="Sha256">The lower-case SHA-256 of the package file.</param>
internal sealed record PackageIdentity(string Id, string Version, string Sha256);

/// <summary>A disposable target the run used.</summary>
/// <param name="Name">The target file name.</param>
/// <param name="Sha256">The upper-case SHA-256 of the target image.</param>
internal sealed record TargetIdentity(string Name, string Sha256);

/// <summary>Everything a qualification result is bound to.</summary>
/// <param name="ClientPackages">The packed Client packages the plugins were built from.</param>
/// <param name="ClientCommit">The repository commit the packages name.</param>
/// <param name="SdkVersion">The CheatEngine.SDK version the plugins referenced.</param>
/// <param name="SdkCommit">The source commit the CheatEngine.SDK package names.</param>
/// <param name="SdkContentHash">The NuGet content hash of the CheatEngine.SDK package.</param>
/// <param name="SdkBridgeSha256">The SHA-256 of the native bridge deployed with the plugins.</param>
/// <param name="Profile">The host profile id.</param>
/// <param name="CheatEngineVersion">The host file version.</param>
/// <param name="CheatEngineSha256">The host executable SHA-256.</param>
/// <param name="Targets">The targets.</param>
/// <param name="DotNetRuntime">The .NET runtime the host is configured for.</param>
/// <param name="OperatingSystemBuild">The Windows build.</param>
internal sealed record QualificationTuple(
	IReadOnlyList<PackageIdentity> ClientPackages,
	string ClientCommit,
	string SdkVersion,
	string SdkCommit,
	string SdkContentHash,
	string SdkBridgeSha256,
	string Profile,
	string CheatEngineVersion,
	string CheatEngineSha256,
	IReadOnlyList<TargetIdentity> Targets,
	string DotNetRuntime,
	string OperatingSystemBuild);

/// <summary>One session of the run.</summary>
/// <param name="Session">The session id.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="Reason">Why, for any outcome other than <see cref="SessionOutcome.Completed" />.</param>
internal sealed record SessionSummary(string Session, SessionOutcome Outcome, string Reason);

/// <summary>The aggregated verdict of one scenario or capability.</summary>
/// <param name="Id">The scenario or capability id.</param>
/// <param name="Status">Passed only when every receipt passed; Failed when one failed; NotExecuted otherwise.</param>
/// <param name="Reason">The first failing or unexecuted check, or empty.</param>
internal sealed record QualificationVerdict(string Id, ReceiptStatus Status, string Reason);

/// <summary>The inputs of <see cref="QualificationSummaryWriter.Serialize" />.</summary>
/// <param name="RunId">The run id.</param>
/// <param name="Tuple">What the result is bound to.</param>
/// <param name="Sessions">The sessions.</param>
/// <param name="Receipts">Every receipt of the run.</param>
/// <param name="CapabilityScenarios">The scenarios each capability requires.</param>
/// <param name="RegistryRestored">Whether the Cheat Engine user state was restored and verified after every session.</param>
internal sealed record QualificationSummary(
	string RunId,
	QualificationTuple Tuple,
	IReadOnlyList<SessionSummary> Sessions,
	IReadOnlyList<QualificationReceipt> Receipts,
	IReadOnlyDictionary<string, IReadOnlyList<string>> CapabilityScenarios,
	bool RegistryRestored);

/// <summary>
///     Writes <c>summary.json</c> (<c>cheatengine-client-qualification-summary/v1</c>): the tuple the result is bound to,
///     the verdict of every scenario and capability derived from the receipts, the sessions and whether the Cheat Engine
///     user state was restored. No pass is ever inferred: a scenario without receipts, or with an unexecuted check, is
///     NotExecuted. The text is redacted like a receipt and refused if it still discloses a path or a name.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class QualificationSummaryWriter
{
	/// <summary>The summary schema.</summary>
	internal const string Schema = "cheatengine-client-qualification-summary/v1";

	private static readonly JsonWriterOptions WriterOptions = ReceiptLedger.WriterOptions with
	{
		Indented = true
	};

	/// <summary>The verdict of every scenario that has receipts, ordinally sorted.</summary>
	internal static IReadOnlyList<QualificationVerdict> Scenarios(IReadOnlyList<QualificationReceipt> receipts)
	{
		ArgumentNullException.ThrowIfNull(receipts);
		return receipts
			.GroupBy(static receipt => receipt.Scenario, StringComparer.Ordinal)
			.OrderBy(static group => group.Key, StringComparer.Ordinal)
			.Select(static group => Verdict(group.Key, [.. group]))
			.ToArray();
	}

	/// <summary>The verdict of every capability: Passed only when each of its scenarios passed.</summary>
	internal static IReadOnlyList<QualificationVerdict> Capabilities(IReadOnlyDictionary<string, IReadOnlyList<string>> capabilityScenarios,
		IReadOnlyList<QualificationVerdict> scenarios)
	{
		ArgumentNullException.ThrowIfNull(capabilityScenarios);
		ArgumentNullException.ThrowIfNull(scenarios);
		Dictionary<string, QualificationVerdict> byId = scenarios.ToDictionary(static verdict => verdict.Id, StringComparer.Ordinal);
		List<QualificationVerdict> verdicts = [];
		foreach ((string capability, IReadOnlyList<string> required) in capabilityScenarios.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
		{
			QualificationVerdict[] parts = required
				.Select(scenario => byId.TryGetValue(scenario, out QualificationVerdict? verdict)
					? verdict
					: new QualificationVerdict(scenario, ReceiptStatus.NotExecuted, $"{scenario} has no receipt"))
				.ToArray();
			QualificationVerdict? failed = parts.FirstOrDefault(static part => part.Status == ReceiptStatus.Failed);
			QualificationVerdict? open = parts.FirstOrDefault(static part => part.Status == ReceiptStatus.NotExecuted);
			if (failed is not null)
			{
				verdicts.Add(new QualificationVerdict(capability, ReceiptStatus.Failed, $"{failed.Id}: {failed.Reason}"));
			}
			else if (parts.Length == 0)
			{
				verdicts.Add(new QualificationVerdict(capability, ReceiptStatus.NotExecuted, "no scenario is mapped"));
			}
			else if (open is not null)
			{
				verdicts.Add(new QualificationVerdict(capability, ReceiptStatus.NotExecuted, $"{open.Id}: {open.Reason}"));
			}
			else
			{
				verdicts.Add(new QualificationVerdict(capability, ReceiptStatus.Passed, string.Empty));
			}
		}

		return verdicts;
	}

	/// <summary>The summary as indented JSON, redacted and checked.</summary>
	internal static string Serialize(QualificationSummary summary, QualificationRedaction redaction)
	{
		ArgumentNullException.ThrowIfNull(summary);
		ArgumentNullException.ThrowIfNull(redaction);
		IReadOnlyList<QualificationVerdict> scenarios = Scenarios(summary.Receipts);
		using MemoryStream buffer = new();
		using (Utf8JsonWriter json = new(buffer, WriterOptions))
		{
			json.WriteStartObject();
			json.WriteString("schema", Schema);
			json.WriteString("runId", summary.RunId);
			WriteTuple(json, summary.Tuple);
			json.WriteStartArray("sessions");
			foreach (SessionSummary session in summary.Sessions)
			{
				json.WriteStartObject();
				json.WriteString("session", session.Session);
				json.WriteString("outcome", session.Outcome.ToString());
				json.WriteString("reason", session.Reason);
				json.WriteEndObject();
			}

			json.WriteEndArray();
			WriteVerdicts(json, "scenarios", scenarios);
			WriteVerdicts(json, "capabilities", Capabilities(summary.CapabilityScenarios, scenarios));
			json.WriteBoolean("registryRestored", summary.RegistryRestored);
			json.WriteEndObject();
		}

		string text = redaction.Redact(Encoding.UTF8.GetString(buffer.ToArray()));
		IReadOnlyList<string> disclosures = redaction.FindDisclosures(text);
		if (disclosures.Count > 0)
		{
			throw new InvalidOperationException($"The summary still discloses {string.Join(" and ", disclosures)}; redact it before writing.");
		}

		return text;
	}

	/// <summary>Writes the summary to <paramref name="path" />.</summary>
	internal static void Write(string path, QualificationSummary summary, QualificationRedaction redaction)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		File.WriteAllText(path, Serialize(summary, redaction) + "\n", new UTF8Encoding(false));
	}

	private static QualificationVerdict Verdict(string scenario, QualificationReceipt[] receipts)
	{
		QualificationReceipt? failed = receipts.FirstOrDefault(static receipt => receipt.Status == ReceiptStatus.Failed);
		if (failed is not null)
		{
			return new QualificationVerdict(scenario, ReceiptStatus.Failed, failed.Check);
		}

		QualificationReceipt? open = receipts.FirstOrDefault(static receipt => receipt.Status == ReceiptStatus.NotExecuted);
		return open is not null
			? new QualificationVerdict(scenario, ReceiptStatus.NotExecuted, open.Check)
			: new QualificationVerdict(scenario, ReceiptStatus.Passed, string.Empty);
	}

	private static void WriteTuple(Utf8JsonWriter json, QualificationTuple tuple)
	{
		json.WriteStartObject("tuple");
		json.WriteStartArray("clientPackages");
		foreach (PackageIdentity package in tuple.ClientPackages.OrderBy(static package => package.Id, StringComparer.Ordinal))
		{
			json.WriteStartObject();
			json.WriteString("id", package.Id);
			json.WriteString("version", package.Version);
			json.WriteString("sha256", package.Sha256);
			json.WriteEndObject();
		}

		json.WriteEndArray();
		json.WriteString("clientCommit", tuple.ClientCommit);
		json.WriteStartObject("sdk");
		json.WriteString("version", tuple.SdkVersion);
		json.WriteString("commit", tuple.SdkCommit);
		json.WriteString("contentHash", tuple.SdkContentHash);
		json.WriteString("bridgeSha256", tuple.SdkBridgeSha256);
		json.WriteEndObject();
		json.WriteString("profile", tuple.Profile);
		json.WriteStartObject("cheatEngine");
		json.WriteString("version", tuple.CheatEngineVersion);
		json.WriteString("sha256", tuple.CheatEngineSha256);
		json.WriteEndObject();
		json.WriteStartArray("targets");
		foreach (TargetIdentity target in tuple.Targets.OrderBy(static target => target.Name, StringComparer.Ordinal))
		{
			json.WriteStartObject();
			json.WriteString("name", target.Name);
			json.WriteString("sha256", target.Sha256);
			json.WriteEndObject();
		}

		json.WriteEndArray();
		json.WriteString("dotnetRuntime", tuple.DotNetRuntime);
		json.WriteString("osBuild", tuple.OperatingSystemBuild);
		json.WriteEndObject();
	}

	private static void WriteVerdicts(Utf8JsonWriter json, string name, IReadOnlyList<QualificationVerdict> verdicts)
	{
		json.WriteStartArray(name);
		foreach (QualificationVerdict verdict in verdicts)
		{
			json.WriteStartObject();
			json.WriteString("id", verdict.Id);
			json.WriteString("status", verdict.Status.ToString());
			json.WriteString("reason", verdict.Reason);
			json.WriteEndObject();
		}

		json.WriteEndArray();
	}
}
