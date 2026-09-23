using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.Migration;

/// <summary>
///     The SDK 2.0 migration guide (<c>docs/migration/sdk-2.0.md</c>) keeps its required sections, a removal entry for
///     every ADR-01 Lua-global binding of the Client, a well-formed generated break list, and the list of capabilities
///     that no SDK 2.0 work item covers (audit A10-19, A11-06, A11-19, A17-21, A21-20, ADR-01, ADR-11b, PR-SEQ-21).
/// </summary>
/// <remarks>
///     Source scans only: this project has no project reference. <c>eng/migration/Update-SdkMigrationBreakList.ps1</c>
///     writes the generated block; these tests are the gate on its output.
/// </remarks>
public sealed partial class SdkMigrationGuideTests
{
	private const string GuidePath = "docs/migration/sdk-2.0.md";
	private const string ClientLuaGlobalsPath = "libs/CheatEngine.Client.Core/Infrastructure/ClientLuaGlobals.cs";
	private const string RuntimeClientPath = "libs/CheatEngine.Client.Core/Domains/RuntimeClient.cs";
	private const string ConsumedSurfacePath = "tests/CheatEngine.Client.Tests/SdkContract/ConsumedSdkSurface.cs";

	private const string AllowlistPath =
		"source-generators/CheatEngine.Client.SourceGenerators.Lua/ApprovedSdkClientTypes.cs";

	private const string RecreatedHeader = "> Recreated 2026-09 from the audit, not the historical docs/ tree.";
	private const string StartMarker = "<!-- generated:sdk-breaks:start -->";
	private const string EndMarker = "<!-- generated:sdk-breaks:end -->";
	private const string CanaryPlaceholder = "Status: placeholder — content arrives with DOCS-FINAL (V4c)";
	private const int RegexTimeoutMilliseconds = 1000;

	/// <summary>The stable second-level headings of the guide, in order.</summary>
	private static readonly string[] RequiredSections =
	[
		"Scope and status",
		"Why the Client stays on 1.0.0",
		"Same-lot adoption checklist",
		"Induced Client breaks (generated)",
		"Adoption entries",
		"ADR-01 frozen exceptions to remove",
		"Re-qualification list",
		"Still contract-only after 2.0",
		"Mixing SDK 1.x and 2.x plugins in one Cheat Engine process",
		"Traceability"
	];

	/// <summary>The adoption entries every migration must work through.</summary>
	private static readonly string[] RequiredAdoptionEntries =
	[
		"F05 value scanning",
		"F06 AOB outcomes",
		"F07 bounded AOB",
		"F13 owner release status",
		"F08 runtime facts",
		"Pointer primitives",
		"Target identity",
		"Allocations",
		"Assembly and Auto Assembler",
		"Memory-record activation and tables",
		"Symbols",
		"Lua module generator",
		"Timers and hotkeys",
		"Unsafe Lua",
		"Exceptions and enums",
		"AddressResolutionOptions",
		"Diagnostics and package identity"
	];

	/// <summary>The fields every adoption entry states.</summary>
	private static readonly string[] AdoptionFields =
		["Today on 1.0.0", "SDK 2.0 API", "Client change", "Tests to rewrite", "Re-qualification", "Audit"];

	/// <summary>The capabilities no SDK 2.0 work item covers (audit ADR-11, A17-21).</summary>
	private static readonly string[] StillContractOnly =
		["Client.Dbvm", "Client.Debugger", "Client.Hashing", "Client.RemoteExecution", "Client.Speed"];

	[Fact]
	public void MigrationGuideHasEveryRequiredSection()
	{
		string[] lines = ReadLines(GuidePath);

		Assert.Equal(RecreatedHeader, lines.First(static line => line.Trim().Length > 0).Trim());
		string[] sections =
		[
			.. lines.Where(static line => line.StartsWith("## ", StringComparison.Ordinal))
				.Select(static line => line[3..].Trim())
		];
		Assert.Equal(RequiredSections, sections);

		string[] entries = [.. SectionLines(lines, "Adoption entries")
			.Where(static line => line.StartsWith("### ", StringComparison.Ordinal))
			.Select(static line => line[4..].Trim())];
		Assert.Equal(RequiredAdoptionEntries, entries);
		foreach (string entry in RequiredAdoptionEntries)
		{
			string[] body = SubsectionLines(lines, entry);
			foreach (string field in AdoptionFields)
			{
				Assert.True(body.Any(line => line.StartsWith($"- **{field}:**", StringComparison.Ordinal)),
					$"The adoption entry '{entry}' does not state '{field}'.");
			}
		}
	}

	[Fact]
	public void EveryClientLuaGlobalsBindingHasARemovalEntryInTheMigrationGuide()
	{
		string[] bindings =
		[
			.. LuaGlobalBinding().Matches(Read(ClientLuaGlobalsPath)).Select(static match => match.Groups["name"].Value)
				.Order(StringComparer.Ordinal)
		];
		string[] rows = [.. FirstTable(SectionLines(ReadLines(GuidePath), "ADR-01 frozen exceptions to remove"))
			.Select(static row => LuaGlobalRow().Match(row))
			.Where(static match => match.Success)
			.Select(static match => match.Groups["name"].Value)];

		Assert.Equal(14, bindings.Length);
		Assert.True(rows.Length == rows.Distinct(StringComparer.Ordinal).Count(),
			"The ADR-01 removal table lists a Lua global more than once.");
		Assert.Equal(bindings, rows.Order(StringComparer.Ordinal));
	}

	[Fact]
	public void GeneratedBreakListIsWellFormedSortedAndCitesItsProvenance()
	{
		string[] lines = ReadLines(GuidePath);
		int start = Array.IndexOf(lines, StartMarker);
		int end = Array.IndexOf(lines, EndMarker);
		Assert.True(start >= 0 && end > start, "The generated break-list markers are missing or out of order.");
		Assert.Equal(1, lines.Count(static line => line == StartMarker));
		Assert.Equal(1, lines.Count(static line => line == EndMarker));

		string[] block = lines[(start + 1)..end];
		Assert.Matches(Provenance(), block[0]);

		HashSet<string> consumed = new(ReadQuoted(ConsumedSurfacePath, ConsumedLine()), StringComparer.Ordinal);
		HashSet<string> allowlisted = new(ReadQuoted(AllowlistPath, AllowlistedType()), StringComparer.Ordinal);
		List<(string Id, string Target)> rows = [];
		foreach (string line in block.Where(static line => line.StartsWith("| CP", StringComparison.Ordinal)))
		{
			Match row = BreakRow().Match(line);
			Assert.True(row.Success, $"Malformed break-list row: {line}");
			string target = row.Groups["target"].Value;
			if (row.Groups["type"].Success)
			{
				string type = row.Groups["type"].Value;
				Assert.Contains(type, allowlisted);
				Assert.Equal(type, GetDeclaringType(target));
			}
			else
			{
				Assert.Contains(row.Groups["consumed"].Value, consumed);
			}

			rows.Add((row.Groups["id"].Value, target));
		}

		Assert.Equal(rows.Count, rows.Distinct().Count());
		List<(string Id, string Target)> sorted = [.. rows];
		sorted.Sort(static (left, right) =>
		{
			int byTarget = string.CompareOrdinal(left.Target, right.Target);
			return byTarget != 0 ? byTarget : string.CompareOrdinal(left.Id, right.Id);
		});
		Assert.Equal(sorted, rows);

		Match counts = Assert.Single(block.Select(static line => BreakCounts().Match(line)), static match => match.Success);
		Assert.Equal(rows.Count, int.Parse(counts.Groups["listed"].Value, System.Globalization.CultureInfo.InvariantCulture));
		Assert.True(int.Parse(counts.Groups["total"].Value, System.Globalization.CultureInfo.InvariantCulture) >= rows.Count);
		Assert.Contains("### Client canary", block);
		Assert.True(block.Contains(CanaryPlaceholder) || block.Any(static line => line.StartsWith("Outcome `", StringComparison.Ordinal)),
			"The client-canary section is neither the placeholder nor a generated report summary.");
	}

	[Fact]
	public void StillContractOnlyListNamesEveryCapabilityWithoutAnSdkWave()
	{
		string[] listed =
		[
			.. FirstTable(SectionLines(ReadLines(GuidePath), "Still contract-only after 2.0"))
				.Select(static row => CapabilityRow().Match(row))
				.Where(static match => match.Success)
				.Select(static match => match.Groups["id"].Value)
				.Order(StringComparer.Ordinal)
		];
		string runtimeClient = Read(RuntimeClientPath);

		Assert.Equal(StillContractOnly, listed);
		foreach (string capability in StillContractOnly)
		{
			string name = capability["Client.".Length..];
			Assert.Matches(new Regex($@"Describe\(ClientCapabilityId\.{name},\s*contractOnly\b", RegexOptions.CultureInvariant,
				TimeSpan.FromMilliseconds(RegexTimeoutMilliseconds)), runtimeClient);
		}
	}

	private static string GetDeclaringType(string docId)
	{
		string name = docId[2..];
		if (docId.StartsWith("T:", StringComparison.Ordinal))
		{
			return name;
		}

		int parenthesis = name.IndexOf('(', StringComparison.Ordinal);
		if (parenthesis >= 0)
		{
			name = name[..parenthesis];
		}

		return name[..name.LastIndexOf('.')];
	}

	/// <summary>The lines of a second-level section, without its heading.</summary>
	private static string[] SectionLines(string[] lines, string heading)
	{
		int start = Array.IndexOf(lines, "## " + heading);
		Assert.True(start >= 0, $"The section '{heading}' is missing.");
		int end = Array.FindIndex(lines, start + 1, static line => line.StartsWith("## ", StringComparison.Ordinal));
		return lines[(start + 1)..(end < 0 ? lines.Length : end)];
	}

	/// <summary>The lines of a third-level subsection, without its heading.</summary>
	private static string[] SubsectionLines(string[] lines, string heading)
	{
		int start = Array.IndexOf(lines, "### " + heading);
		Assert.True(start >= 0, $"The subsection '{heading}' is missing.");
		int end = Array.FindIndex(lines, start + 1, static line => line.StartsWith("##", StringComparison.Ordinal));
		return lines[(start + 1)..(end < 0 ? lines.Length : end)];
	}

	/// <summary>The rows of the first Markdown table of a section, header and separator excluded.</summary>
	private static IEnumerable<string> FirstTable(string[] section)
	{
		int start = Array.FindIndex(section, static line => line.StartsWith('|'));
		Assert.True(start >= 0, "The section has no table.");
		for (int index = start + 2; index < section.Length && section[index].StartsWith('|'); index++)
		{
			yield return section[index];
		}
	}

	private static string[] ReadQuoted(string relativePath, Regex pattern)
	{
		return [.. ReadLines(relativePath).Select(line => pattern.Match(line)).Where(static match => match.Success)
			.Select(static match => match.Groups["value"].Value)];
	}

	private static string[] ReadLines(string relativePath)
	{
		return Read(relativePath).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
	}

	private static string Read(string relativePath)
	{
		return File.ReadAllText(Path.Combine(RepositoryRoot.Path, relativePath));
	}

	[GeneratedRegex("""\[LuaGlobal\("(?<name>[A-Za-z0-9_]+)"\)\]""", RegexOptions.CultureInvariant,
		RegexTimeoutMilliseconds)]
	private static partial Regex LuaGlobalBinding();

	[GeneratedRegex(@"^\|\s*`(?<name>[A-Za-z0-9_]+)`\s*\|", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex LuaGlobalRow();

	[GeneratedRegex(@"^\|\s*`(?<id>Client\.[A-Za-z]+)`\s*\|", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex CapabilityRow();

	[GeneratedRegex(
		@"^Provenance: SDK commit `[0-9a-f]{40}`, CompatibilitySuppressions\.xml SHA-256 `[0-9a-f]{64}`, client-canary report: (none|https://\S+|local report SHA-256 [0-9a-f]{64}), generated \d{4}-\d{2}-\d{2}\.$",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex Provenance();

	[GeneratedRegex(
		@"^\| (?<id>CP\d{4}) \| `(?<target>[TMFPE]:CheatEngine\.SDK\.[^`]+)` \| (?:Public API \(`(?<type>CheatEngine\.SDK\.[^`]+)`\)|Consumed \(`(?<consumed>CheatEngine\.Client[^`]+)`\)) \| [^|]+ \|$",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex BreakRow();

	[GeneratedRegex(
		@"^Suppressions listed: (?<listed>\d+)\. Suppressions without Client exposure \(counted, not listed\): (?<unlisted>\d+) of (?<total>\d+)\.$",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex BreakCounts();

	[GeneratedRegex("""^\s*"(?<value>CheatEngine\.Client[^"]*)",?\s*$""", RegexOptions.CultureInvariant,
		RegexTimeoutMilliseconds)]
	private static partial Regex ConsumedLine();

	[GeneratedRegex("""^\s*"(?<value>CheatEngine\.SDK\.[^"]+)",?\s*$""", RegexOptions.CultureInvariant,
		RegexTimeoutMilliseconds)]
	private static partial Regex AllowlistedType();
}
