using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>The verdict of one check, with what the run observed.</summary>
/// <param name="Status">Passed, Failed or NotExecuted; NotExecuted never counts as a pass.</param>
/// <param name="Observation">The observed facts the verdict rests on.</param>
internal readonly record struct CheckResult(ReceiptStatus Status, string Observation)
{
	internal static CheckResult Passed(string observation)
	{
		return new CheckResult(ReceiptStatus.Passed, observation);
	}

	internal static CheckResult Failed(string observation)
	{
		return new CheckResult(ReceiptStatus.Failed, observation);
	}

	internal static CheckResult NotExecuted(string observation)
	{
		return new CheckResult(ReceiptStatus.NotExecuted, observation);
	}

	internal static CheckResult From(bool passed, string observation)
	{
		return passed ? Passed(observation) : Failed(observation);
	}
}

/// <summary>The facts the runner establishes itself, next to the driver transcript, by name.</summary>
internal static class SessionFacts
{
	/// <summary>Whether every target's module list is the same after the session as at its start (<c>true</c>/<c>false</c>).</summary>
	internal const string ModulesUnchanged = "modules-unchanged";

	/// <summary>The forbidden modules (speedhack, allochook, luaclient, vehdebug, dbk) found in a target, comma separated.</summary>
	internal const string ForbiddenModules = "forbidden-modules";

	/// <summary>How many declared scenario values the Cheat Engine debug output contains.</summary>
	internal const string DebugOutputSensitiveHits = "debug-output-sensitive-hits";

	/// <summary>Whether every <c>CheatEngine.Client*.dll</c> of the harness bundle equals the packed package's (<c>true</c>/<c>false</c>).</summary>
	internal const string BundleClientAssembliesMatch = "bundle-client-assemblies-match";

	/// <summary>The lower-case SHA-256 of the native bridge deployed with the plugin.</summary>
	internal const string BundleBridgeSha256 = "bundle-bridge-sha256";

	/// <summary>The lower-case SHA-256 of the bridge the reviewed CheatEngine.SDK package ships.</summary>
	internal const string ExpectedBridgeSha256 = "expected-bridge-sha256";

	/// <summary>
	///     The CheatEngine.SDK content hash the instantiated template's restore recorded: its lock file, or
	///     <c>project.assets.json</c> when the template writes no lock file.
	/// </summary>
	internal const string TemplateSdkContentHash = "template-sdk-content-hash";

	/// <summary>The content hash of the reviewed CheatEngine.SDK package.</summary>
	internal const string ExpectedSdkContentHash = "expected-sdk-content-hash";

	/// <summary>How many paths of the build workspace the template bundle's <c>deps.json</c> holds.</summary>
	internal const string TemplateDepsWorkspacePaths = "template-deps-workspace-paths";

	/// <summary>The Client package version the plugins were built from.</summary>
	internal const string ClientPackageVersion = "client-package-version";
}

/// <summary>
///     Everything one session produced that the evaluators read: the driver transcript, the Cheat Engine debug output,
///     the harness's lifecycle sink and the facts the runner established itself.
/// </summary>
/// <param name="Transcript">The parsed driver transcript.</param>
/// <param name="DebugOutput">The Cheat Engine process's debug output.</param>
/// <param name="Lifecycle">The lines of the harness's lifecycle receipt sink (Q43).</param>
/// <param name="Facts">The runner's facts, by <see cref="SessionFacts" /> name.</param>
[SupportedOSPlatform("windows")]
internal sealed record SessionEvidence(
	Transcript Transcript,
	string DebugOutput,
	IReadOnlyList<string> Lifecycle,
	IReadOnlyDictionary<string, string> Facts)
{
	/// <summary>The observation of a driver step whose value is a harness JSON observation.</summary>
	internal CheckResult Observe(string step, Func<Observed, bool> passes, params string[] describe)
	{
		ArgumentNullException.ThrowIfNull(passes);
		if (!TryRecord(step, out TranscriptRecord? record, out CheckResult notUsable))
		{
			return notUsable;
		}

		if (!Observed.TryParse(record.Value, out Observed? observed))
		{
			return CheckResult.Failed($"{step}: not a JSON observation: {Shorten(record.Value)}");
		}

		string description = $"{step}: {observed.Describe(describe)}";
		return CheckResult.From(passes(observed), description);
	}

	/// <summary>The harness JSON observation of a driver step, or the result that says why it cannot be used.</summary>
	internal bool TryObserve(string step, [NotNullWhen(true)] out Observed? observed, out CheckResult notUsable)
	{
		observed = null;
		if (!TryRecord(step, out TranscriptRecord? record, out notUsable))
		{
			return false;
		}

		if (Observed.TryParse(record.Value, out observed))
		{
			return true;
		}

		notUsable = CheckResult.Failed($"{step}: not a JSON observation: {Shorten(record.Value)}");
		return false;
	}

	/// <summary>The plain value of a driver step (a Lua setup or check step).</summary>
	internal CheckResult Value(string step, Func<string, bool> passes)
	{
		ArgumentNullException.ThrowIfNull(passes);
		return TryRecord(step, out TranscriptRecord? record, out CheckResult notUsable)
			? CheckResult.From(passes(record.Value), $"{step}: {Shorten(record.Value)}")
			: notUsable;
	}

	/// <summary>A driver step that must raise a Lua error (a refused marshalling, a call of a dead function).</summary>
	internal CheckResult ExpectError(string step)
	{
		TranscriptRecord? record = Transcript.Find(step);
		return record switch
		{
			null => CheckResult.NotExecuted($"{step}: not reached"),
			{ Status: TranscriptStatus.NotExecuted } => CheckResult.NotExecuted($"{step}: {Shorten(record.Value)}"),
			{ Status: TranscriptStatus.Error } => CheckResult.Passed($"{step}: raised {Shorten(record.Value)}"),
			_ => CheckResult.Failed($"{step}: returned {Shorten(record.Value)}")
		};
	}

	/// <summary>
	///     A check that needs operator steps first (plugin toggles through Settings &gt; Plugins, plan A12):
	///     <paramref name="then" /> only when the driver recorded each of them <c>ok</c>, that is when it observed the
	///     toggle's effect or the operator confirmed a toggle whose effect it cannot observe; otherwise NotExecuted, with
	///     what the driver recorded (skipped, no action in time, or a prompt that failed).
	/// </summary>
	internal CheckResult AfterOperator(IReadOnlyList<string> operatorSteps, Func<CheckResult> then)
	{
		ArgumentNullException.ThrowIfNull(operatorSteps);
		ArgumentNullException.ThrowIfNull(then);
		foreach (string operatorStep in operatorSteps)
		{
			TranscriptRecord? record = Transcript.Find(operatorStep);
			if (record is null)
			{
				return CheckResult.NotExecuted($"{operatorStep}: not reached");
			}

			if (record.Status != TranscriptStatus.Ok)
			{
				return CheckResult.NotExecuted($"{operatorStep} was not performed ({record.Status}: " +
											   $"{Shorten(record.Value)}); the Settings > Plugins toggle is operator work");
			}
		}

		return then();
	}

	/// <summary>A fact the runner established, or <see langword="null" />.</summary>
	internal string? Fact(string name)
	{
		return Facts.TryGetValue(name, out string? value) ? value : null;
	}

	/// <summary>A check of one runner fact.</summary>
	internal CheckResult FromFact(string name, Func<string, bool> passes)
	{
		ArgumentNullException.ThrowIfNull(passes);
		return Fact(name) is { } value
			? CheckResult.From(passes(value), $"{name}={Shorten(value)}")
			: CheckResult.NotExecuted($"{name} was not established");
	}

	/// <summary>Cuts a transcript value to a receipt-sized observation.</summary>
	internal static string Shorten(string value)
	{
		ArgumentNullException.ThrowIfNull(value);
		string flat = value.ReplaceLineEndings(" ");
		return flat.Length <= 240 ? flat : string.Concat(flat.AsSpan(0, 240), "…");
	}

	private bool TryRecord(string step, [NotNullWhen(true)] out TranscriptRecord? record, out CheckResult notUsable)
	{
		record = Transcript.Find(step);
		notUsable = record switch
		{
			null => CheckResult.NotExecuted($"{step}: not reached"),
			{ Status: TranscriptStatus.NotExecuted } => CheckResult.NotExecuted($"{step}: {Shorten(record.Value)}"),
			{ Status: TranscriptStatus.Error } => CheckResult.Failed($"{step}: error {Shorten(record.Value)}"),
			_ => default
		};
		return record is { Status: TranscriptStatus.Ok };
	}
}

/// <summary>A parsed harness observation, read by dotted paths (<c>checks.filterExact</c>).</summary>
internal sealed class Observed
{
	private readonly JsonElement _root;

	private Observed(JsonElement root)
	{
		_root = root;
	}

	/// <summary>Parses a JSON object observation.</summary>
	internal static bool TryParse(string text, [NotNullWhen(true)] out Observed? observed)
	{
		observed = null;
		try
		{
			using JsonDocument document = JsonDocument.Parse(text);
			if (document.RootElement.ValueKind != JsonValueKind.Object)
			{
				return false;
			}

			observed = new Observed(document.RootElement.Clone());
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	/// <summary>The element at a dotted path, or <see langword="null" />.</summary>
	internal JsonElement? Element(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		JsonElement current = _root;
		foreach (string segment in path.Split('.'))
		{
			if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
			{
				return null;
			}
		}

		return current;
	}

	/// <summary>The boolean at a path, or <see langword="null" /> when it is absent or not a boolean.</summary>
	internal bool? Bool(string path)
	{
		return Element(path) is { ValueKind: JsonValueKind.True or JsonValueKind.False } element ? element.GetBoolean() : null;
	}

	/// <summary>Whether the boolean at a path is <see langword="true" />.</summary>
	internal bool Is(string path)
	{
		return Bool(path) == true;
	}

	/// <summary>The string at a path, or <see langword="null" />.</summary>
	internal string? Text(string path)
	{
		return Element(path) is { ValueKind: JsonValueKind.String } element ? element.GetString() : null;
	}

	/// <summary>The integer at a path, or <see langword="null" />.</summary>
	internal long? Number(string path)
	{
		return Element(path) is { ValueKind: JsonValueKind.Number } element && element.TryGetInt64(out long value)
			? value
			: null;
	}

	/// <summary>The item of an array whose <paramref name="key" /> equals <paramref name="value" />.</summary>
	internal Observed? Item(string arrayPath, string key, string value)
	{
		if (Element(arrayPath) is not { ValueKind: JsonValueKind.Array } array)
		{
			return null;
		}

		foreach (JsonElement item in array.EnumerateArray())
		{
			if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out JsonElement found) &&
				found.ValueKind == JsonValueKind.String && string.Equals(found.GetString(), value, StringComparison.Ordinal))
			{
				return new Observed(item);
			}
		}

		return null;
	}

	/// <summary>Every object item of an array.</summary>
	internal IReadOnlyList<Observed> Items(string arrayPath)
	{
		return Element(arrayPath) is { ValueKind: JsonValueKind.Array } array
			? [.. array.EnumerateArray().Where(static item => item.ValueKind == JsonValueKind.Object).Select(static item => new Observed(item))]
			: [];
	}

	/// <summary>The values at <paramref name="paths" />, as <c>path=value</c> pairs.</summary>
	internal string Describe(IReadOnlyList<string> paths)
	{
		ArgumentNullException.ThrowIfNull(paths);
		if (paths.Count == 0)
		{
			return "observed";
		}

		StringBuilder text = new();
		foreach (string path in paths)
		{
			if (text.Length > 0)
			{
				text.Append("; ");
			}

			text.Append(path).Append('=').Append(Element(path) is { } element ? Render(element) : "absent");
		}

		return text.ToString();
	}

	private static string Render(JsonElement element)
	{
		return element.ValueKind switch
		{
			JsonValueKind.String => element.GetString() ?? string.Empty,
			JsonValueKind.True => "true",
			JsonValueKind.False => "false",
			JsonValueKind.Null => "null",
			JsonValueKind.Number => element.GetRawText(),
			_ => string.Create(CultureInfo.InvariantCulture, $"<{element.ValueKind}>")
		};
	}
}
