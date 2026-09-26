using System.Text.Json;
using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;
using CheatEngine.Client.Tests.LiveQualification;

namespace CheatEngine.Client.Repository.Tests.Qualification;

/// <summary>
///     Evidence discipline for the live qualification (audit ch.22: an unexecuted test stays unexecuted). Until
///     <c>tests/CheatEngine.Client.Tests/LiveQualification/Evidence/</c> holds a committed run summary, no README,
///     CHANGELOG, RELEASING or capability table may claim a host qualification. The shipping source digest that binds
///     that evidence (<see cref="QualifiedSourceDigest" />, shared with the live runner) covers every shipping source this
///     project's file helpers see.
/// </summary>
public sealed partial class QualificationEvidenceTests
{
	/// <summary>Where the recorded, redacted evidence of a qualification run is committed.</summary>
	private const string EvidenceDirectory = "tests/CheatEngine.Client.Tests/LiveQualification/Evidence";

	private const string SummarySchema = "cheatengine-client-qualification-summary/v1";
	private const string TableStart = "<!-- capability-table:start -->";
	private const string TableEnd = "<!-- capability-table:end -->";
	private const int RegexTimeoutMilliseconds = 1000;

	private static readonly string[] ClaimDocumentNames = ["README.md", "CHANGELOG.md", "RELEASING.md"];

	[Fact]
	public void NoQualificationClaimWithoutCommittedEvidence()
	{
		if (HasCommittedEvidence())
		{
			return;
		}

		List<string> claims = [];
		foreach (string document in ClaimDocuments())
		{
			string text = File.ReadAllText(Path.Combine(RepositoryRoot.Path, document));
			claims.AddRange(FindClaims(document, text).Select(claim => $"{document}:{claim.Line} → {claim.Kind}"));
		}

		Assert.True(claims.Count == 0,
			$"{EvidenceDirectory} holds no committed run summary ({SummarySchema}), yet these lines claim a host qualification. " +
			"Record the evidence of a live run first, or say what is targeted instead:" + Environment.NewLine +
			string.Join(Environment.NewLine, claims));
	}

	[Fact]
	public void TheClaimDetectorRecognizesEveryClaimFormAndTheCurrentWording()
	{
		const string claimsText = """
			## [1.0.0]
			### Qualification
			- Q05 (C3, run 20260930T101530Z-a1b2, 2026-09-30): Passed
			- Q30.b (C4): Waived until 2026-12-31
			The Client is qualified on Cheat Engine 7.7.0.10621 x64.
			<!-- capability-table:start -->
			| Capability id | Implementation | Qualification |
			|---|---|---|
			| `Client.TypedMemory` | Operational adapter | Satisfied (run 20260930T101530Z-a1b2) |
			| `Client.Tables` | Operational adapter | Unknown until a Client receipt exists |
			<!-- capability-table:end -->
			""";
		const string currentText = """
			## Qualification gate
			No Client capability is host-qualified yet: the qualification gate stays `Unknown` until a Client receipt exists.
			These are package-level results (fixture level C2); a Cheat Engine host run of Q40 is a separate qualification.
			A result on this profile authorizes no x86 or ARM64 plugin claim; S0 receipts are never committed.
			<!-- capability-table:start -->
			| Capability id | Implementation | Package | Host | Qualification | Status reported at runtime |
			|---|---|---|---|---|---|
			| `Client.TypedMemory` | Operational adapter | Evidence | Not probed | Unknown until a Client receipt exists | `Unknown` |
			| `Client.UnsafeLuaExecution` | Operational, policy opt-in | Evidence | Not probed | Never qualified: no scenario | `Unknown` |
			<!-- capability-table:end -->
			""";

		Assert.Equal(
		[
			(2, "a Qualification section"),
			(3, "a scenario verdict"),
			(3, "a live run id"),
			(4, "a scenario verdict"),
			(5, "a host qualification statement"),
			(9, "a live run id"),
			(9, "a qualified capability")
		], FindClaims("CHANGELOG.md", claimsText));
		Assert.Empty(FindClaims("CHANGELOG.md", currentText));
		Assert.DoesNotContain((2, "a Qualification section"), FindClaims("README.md", claimsText));
	}

	[Fact]
	public void EveryShippingSourceIsBoundByTheDigest()
	{
		IReadOnlyList<string> inputs = QualifiedSourceDigest.EnumerateInputs(RepositoryRoot.Path);
		HashSet<string> visible = new(RepositoryRoot.EnumerateSourceFiles("*").Where(static path => !path.StartsWith(".claude/", StringComparison.Ordinal)),
			StringComparer.Ordinal);
		string[] shipping =
		[
			.. visible.Where(static path => QualifiedSourceDigest.IncludedDirectories.Any(directory => path.StartsWith(directory, StringComparison.Ordinal)))
				.Where(static path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".csproj", StringComparison.Ordinal) ||
									  path.EndsWith(".props", StringComparison.Ordinal) || path.EndsWith(".targets", StringComparison.Ordinal) ||
									  path.EndsWith("packages.lock.json", StringComparison.Ordinal))
				.Where(static path => !path.EndsWith("/HostQualificationEvidence.cs", StringComparison.Ordinal))
		];

		Assert.NotEmpty(shipping);
		Assert.Empty(shipping.Except(inputs, StringComparer.Ordinal));
		Assert.Empty(inputs.Except(visible, StringComparer.Ordinal));
		Assert.Contains("global.json", inputs);
		Assert.Contains("eng/CheatEngineSdk.props", inputs);
		Assert.DoesNotContain(inputs, static path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
													   Path.GetFileName(path).StartsWith("PublicAPI.", StringComparison.Ordinal));
		Assert.Equal(QualifiedSourceDigest.Compute(RepositoryRoot.Path), QualifiedSourceDigest.Compute(RepositoryRoot.Path));
	}

	/// <summary>Whether the evidence folder holds a run summary of the qualification schema.</summary>
	private static bool HasCommittedEvidence()
	{
		string summary = Path.Combine(RepositoryRoot.Path, EvidenceDirectory, "summary.json");
		if (!File.Exists(summary))
		{
			return false;
		}

		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(summary));
		return document.RootElement.TryGetProperty("schema", out JsonElement schema) &&
			   string.Equals(schema.GetString(), SummarySchema, StringComparison.Ordinal);
	}

	/// <summary>Every README, the CHANGELOG, RELEASING and every Markdown file that carries a capability table.</summary>
	private static IEnumerable<string> ClaimDocuments()
	{
		return RepositoryRoot.EnumerateSourceFiles("*.md")
			.Where(static path => !path.StartsWith(".claude/", StringComparison.Ordinal))
			.Where(static path => ClaimDocumentNames.Contains(Path.GetFileName(path), StringComparer.Ordinal) ||
								  File.ReadAllText(Path.Combine(RepositoryRoot.Path, path)).Contains(TableStart, StringComparison.Ordinal))
			.Order(StringComparer.Ordinal);
	}

	/// <summary>The 1-based line and kind of every qualification claim in a document.</summary>
	private static List<(int Line, string Kind)> FindClaims(string path, string text)
	{
		List<(int Line, string Kind)> claims = [];
		string[] lines = text.ReplaceLineEndings("\n").Split('\n');
		int qualificationColumn = -1;
		bool inTable = false;
		for (int index = 0; index < lines.Length; index++)
		{
			string line = lines[index];
			int number = index + 1;
			if (string.Equals(path, "CHANGELOG.md", StringComparison.Ordinal) && QualificationSection().IsMatch(line))
			{
				claims.Add((number, "a Qualification section"));
			}

			if (ScenarioVerdict().IsMatch(line))
			{
				claims.Add((number, "a scenario verdict"));
			}

			if (RunId().IsMatch(line))
			{
				claims.Add((number, "a live run id"));
			}

			if (QualifiedOnHost().IsMatch(line))
			{
				claims.Add((number, "a host qualification statement"));
			}

			string trimmed = line.Trim();
			if (trimmed == TableStart)
			{
				inTable = true;
				qualificationColumn = -1;
				continue;
			}

			if (trimmed == TableEnd)
			{
				inTable = false;
				continue;
			}

			if (!inTable || !trimmed.StartsWith('|'))
			{
				continue;
			}

			string[] cells = [.. trimmed.Trim('|').Split('|').Select(static cell => cell.Trim())];
			if (qualificationColumn < 0)
			{
				qualificationColumn = Array.IndexOf(cells, "Qualification");
				continue;
			}

			if (qualificationColumn < cells.Length && QualifiedCell().IsMatch(cells[qualificationColumn]))
			{
				claims.Add((number, "a qualified capability"));
			}
		}

		return claims;
	}

	/// <summary>The CHANGELOG section a recorded run adds.</summary>
	[GeneratedRegex(@"^#{2,4}\s+Qualification\s*$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex QualificationSection();

	/// <summary>A scenario id followed, on the same line, by a verdict.</summary>
	[GeneratedRegex(@"\bQ\d{2}(?:\.[a-z])?\b.*\b(?:Passed|Waived)\b", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex ScenarioVerdict();

	/// <summary>The id of a live run (<c>yyyyMMddTHHmmssZ-xxxx</c>).</summary>
	[GeneratedRegex(@"\b\d{8}T\d{6}Z-[0-9a-f]{4}\b", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex RunId();

	/// <summary>"qualified on/against/with Cheat Engine".</summary>
	[GeneratedRegex(@"\b(?:host-)?qualified\s+(?:on|against|with)\s+(?:Cheat\s*Engine|CE)\b",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex QualifiedOnHost();

	/// <summary>A Qualification cell that reports a pass; a negated word ("never qualified", "not passed") is no claim.</summary>
	[GeneratedRegex(@"(?<!\b(?:never|not)\s+)\b(?:passed|qualified|satisfied)\b",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex QualifiedCell();
}
