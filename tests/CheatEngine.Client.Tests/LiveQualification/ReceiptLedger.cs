using System.Runtime.Versioning;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>The verdict of one qualification check.</summary>
internal enum ReceiptStatus
{
	/// <summary>The observation met the expectation.</summary>
	Passed,

	/// <summary>The observation contradicts the expectation.</summary>
	Failed,

	/// <summary>The check did not run, or ran without a conclusive observation. It never counts as a pass.</summary>
	NotExecuted
}

/// <summary>One redacted receipt line.</summary>
/// <param name="RunId">The run id.</param>
/// <param name="Session">The session id (S0 to S6).</param>
/// <param name="Scenario">The scenario id, for example <c>Q05</c> or <c>S0</c>.</param>
/// <param name="Check">The check name within the scenario.</param>
/// <param name="Level">The qualification level: <c>C3</c> (exact host) or <c>C4</c> (host plus a second plugin).</param>
/// <param name="Status">The verdict.</param>
/// <param name="Expectation">What the check requires.</param>
/// <param name="Observation">What the run observed, redacted.</param>
internal sealed record QualificationReceipt(
	string RunId,
	string Session,
	string Scenario,
	string Check,
	string Level,
	ReceiptStatus Status,
	string Expectation,
	string Observation);

/// <summary>
///     Redaction of everything a run writes as evidence: the run directory becomes <c>&lt;run&gt;</c>, and any remaining
///     local path, user name or machine name is refused rather than silently rewritten.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class QualificationRedaction
{
	/// <summary>What the run directory is replaced with.</summary>
	internal const string RunPlaceholder = "<run>";

	private readonly string? _runDirectory;
	private readonly Regex[] _names;

	/// <summary>Creates a redaction for <paramref name="runDirectory" /> that refuses <paramref name="identifyingNames" />.</summary>
	internal QualificationRedaction(string? runDirectory, IEnumerable<string> identifyingNames)
	{
		ArgumentNullException.ThrowIfNull(identifyingNames);
		_runDirectory = string.IsNullOrWhiteSpace(runDirectory) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(runDirectory));
		_names = identifyingNames
			.Where(static name => name.Trim().Length >= 3)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Select(static name => new Regex($"(?<![A-Za-z0-9]){Regex.Escape(name.Trim())}(?![A-Za-z0-9])",
				RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
			.ToArray();
	}

	/// <summary>The redaction of this workstation: the current user and machine names are refused.</summary>
	internal static QualificationRedaction ForWorkstation(string? runDirectory)
	{
		return new QualificationRedaction(runDirectory, [Environment.UserName, Environment.MachineName]);
	}

	/// <summary>Replaces the run directory (with either slash) by <see cref="RunPlaceholder" />.</summary>
	internal string Redact(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		if (_runDirectory is null)
		{
			return text;
		}

		string forward = _runDirectory.Replace('\\', '/');
		string escapedJson = _runDirectory.Replace("\\", "\\\\", StringComparison.Ordinal);
		return text
			.Replace(escapedJson, RunPlaceholder, StringComparison.OrdinalIgnoreCase)
			.Replace(_runDirectory, RunPlaceholder, StringComparison.OrdinalIgnoreCase)
			.Replace(forward, RunPlaceholder, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>What <paramref name="text" /> still discloses; empty when it may be recorded. Never echoes the value.</summary>
	internal IReadOnlyList<string> FindDisclosures(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		List<string> disclosures = [];
		if (LocalPath().IsMatch(text))
		{
			disclosures.Add("a local path");
		}

		if (_names.Any(name => name.IsMatch(text)))
		{
			disclosures.Add("a user or machine name");
		}

		return disclosures;
	}

	/// <summary>
	///     A drive-rooted path, a UNC path (also JSON-escaped) that starts a word, a <c>file:</c> URI or a home-relative
	///     path. A path below <c>&lt;run&gt;</c> is not one: the placeholder stands for the redacted run directory.
	/// </summary>
	[GeneratedRegex(@"(?<![A-Za-z0-9])[A-Za-z]:[\\/]|(?:^|[\s""'=(])(?:\\\\){1,2}[A-Za-z0-9._$-]+\\|file:/|(?<![A-Za-z0-9])~[\\/]|/(?:home|Users)/",
		RegexOptions.CultureInvariant, 1000)]
	private static partial Regex LocalPath();
}

/// <summary>
///     The append-only receipt ledger of a run (<c>receipts.jsonl</c>, one <c>cheatengine-client-qualification-receipt/v1</c>
///     object per line). Every text field is redacted first; a receipt that still discloses a local path or a user name
///     is refused, so the ledger can be committed as evidence once the run is recorded.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class ReceiptLedger
{
	/// <summary>The receipt schema.</summary>
	internal const string Schema = "cheatengine-client-qualification-receipt/v1";

	/// <summary>The qualification levels a receipt may state.</summary>
	internal static readonly string[] Levels = ["C3", "C4"];

	/// <summary>Keeps <c>&lt;run&gt;</c> and non-ASCII text readable; quotes and control characters are still escaped.</summary>
	internal static readonly JsonWriterOptions WriterOptions = new()
	{
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	private readonly string _path;
	private readonly QualificationRedaction _redaction;

	/// <summary>Creates a ledger that appends to <paramref name="path" />.</summary>
	internal ReceiptLedger(string path, QualificationRedaction redaction)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentNullException.ThrowIfNull(redaction);
		_path = path;
		_redaction = redaction;
	}

	/// <summary>Redacts, validates and appends one receipt; returns the receipt as recorded.</summary>
	internal QualificationReceipt Append(QualificationReceipt receipt)
	{
		ArgumentNullException.ThrowIfNull(receipt);
		QualificationReceipt redacted = receipt with
		{
			Expectation = _redaction.Redact(receipt.Expectation),
			Observation = _redaction.Redact(receipt.Observation)
		};
		Validate(redacted);
		File.AppendAllText(_path, Serialize(redacted) + "\n", new UTF8Encoding(false));
		return redacted;
	}

	/// <summary>Reads every receipt of a ledger.</summary>
	internal static IReadOnlyList<QualificationReceipt> Read(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		List<QualificationReceipt> receipts = [];
		if (!File.Exists(path))
		{
			return receipts;
		}

		foreach (string line in File.ReadLines(path).Where(static line => line.Length > 0))
		{
			using JsonDocument document = JsonDocument.Parse(line);
			JsonElement root = document.RootElement;
			if (!string.Equals(root.GetProperty("schema").GetString(), Schema, StringComparison.Ordinal))
			{
				throw new InvalidDataException($"A receipt of '{Path.GetFileName(path)}' does not declare {Schema}.");
			}

			receipts.Add(new QualificationReceipt(Text(root, "runId"), Text(root, "session"), Text(root, "scenario"),
				Text(root, "check"), Text(root, "level"), Enum.Parse<ReceiptStatus>(Text(root, "status")),
				Text(root, "expectation"), Text(root, "observation")));
		}

		return receipts;
	}

	/// <summary>One receipt as a single JSON line, with a fixed property order.</summary>
	internal static string Serialize(QualificationReceipt receipt)
	{
		ArgumentNullException.ThrowIfNull(receipt);
		using MemoryStream buffer = new();
		using (Utf8JsonWriter json = new(buffer, WriterOptions))
		{
			json.WriteStartObject();
			json.WriteString("schema", Schema);
			json.WriteString("runId", receipt.RunId);
			json.WriteString("session", receipt.Session);
			json.WriteString("scenario", receipt.Scenario);
			json.WriteString("check", receipt.Check);
			json.WriteString("level", receipt.Level);
			json.WriteString("status", receipt.Status.ToString());
			json.WriteString("expectation", receipt.Expectation);
			json.WriteString("observation", receipt.Observation);
			json.WriteEndObject();
		}

		return Encoding.UTF8.GetString(buffer.ToArray());
	}

	private void Validate(QualificationReceipt receipt)
	{
		string where = $"Receipt {receipt.Scenario}/{receipt.Check}";
		foreach ((string name, string value) in (ReadOnlySpan<(string, string)>)
				 [("runId", receipt.RunId), ("session", receipt.Session), ("scenario", receipt.Scenario), ("check", receipt.Check)])
		{
			if (!Identifier().IsMatch(value))
			{
				throw new ArgumentException($"{where}: '{name}' must be a short identifier.", nameof(receipt));
			}
		}

		if (!Levels.Contains(receipt.Level, StringComparer.Ordinal))
		{
			throw new ArgumentException($"{where}: the level must be one of {string.Join(", ", Levels)}.", nameof(receipt));
		}

		if (!Enum.IsDefined(receipt.Status))
		{
			throw new ArgumentException($"{where}: the status is not a {nameof(ReceiptStatus)}.", nameof(receipt));
		}

		foreach ((string name, string value) in (ReadOnlySpan<(string, string)>)
				 [("expectation", receipt.Expectation), ("observation", receipt.Observation)])
		{
			IReadOnlyList<string> disclosures = _redaction.FindDisclosures(value);
			if (disclosures.Count > 0)
			{
				throw new InvalidOperationException($"{where}: '{name}' still discloses {string.Join(" and ", disclosures)}; " +
													"redact it before recording.");
			}
		}
	}

	private static string Text(JsonElement root, string name)
	{
		return root.GetProperty(name).GetString() ?? string.Empty;
	}

	[GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex Identifier();
}
