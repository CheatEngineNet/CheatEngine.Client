using System.Text.Json;
using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.Capabilities;

/// <summary>
///     The documentation states what the Client build reports (audit CLI-DOC-1, A00-04, A10-05, A21-13, A21-21, A21-30):
///     every capability table lists each <c>ClientCapabilityId</c> once as the operational adapter that
///     <c>ClientCapabilityCatalog</c> declares and <c>RuntimeClient</c> composes, with its experimental id and the live
///     scenarios its qualification gate requires, and a table with a "1.0 status" column calls exactly the experimental
///     APIs Experimental; the install guides state the supported host profile with its identities.
/// </summary>
/// <remarks>
///     Source scans only: this project has no project reference. A capability table is the Markdown table between
///     <c>&lt;!-- capability-table:start --&gt;</c> and <c>&lt;!-- capability-table:end --&gt;</c> in any Markdown file, so
///     a table copied into another document is checked as soon as it carries the markers.
/// </remarks>
public sealed partial class CapabilityDocumentationTests
{
	private const string StartMarker = "<!-- capability-table:start -->";
	private const string EndMarker = "<!-- capability-table:end -->";
	private const string AbstractionsReadme = "libs/CheatEngine.Client.Abstractions/README.md";
	private const string CapabilityIdSource = "libs/CheatEngine.Client.Abstractions/Runtime/ClientCapabilityId.cs";
	private const string CapabilityIdDeclarationPrefix = "public static ClientCapabilityId ";
	private const string CatalogSource = "libs/CheatEngine.Client.Core/Domains/ClientCapabilityCatalog.cs";
	private const string CoreLockFile = "libs/CheatEngine.Client.Core/packages.lock.json";
	private const string QualificationColumn = "Qualification";

	/// <summary>The optional column of a plugin-author table that says what 1.0 offers for each capability.</summary>
	private const string StatusColumn = "1.0 status";

	private const string AvailableStatus = "Available";
	private const string ExperimentalStatus = "Experimental";
	private const string ProfileId = "ce-7.7.0.10621-x64-managed-hostfxr";
	private const string HostExecutableSha256 = "9727076da50924e4a097b49a02155e4b34759269c3017ff31375364b8826eb4d";
	private const string RuntimeConfigurationSha256 = "68f5d81c0a17cc5bdac40bb3d5d88a624f4d31b414f7195ad847d57b0126ac2b";
	private const int RegexTimeoutMilliseconds = 1000;

	private static readonly string[] OperationalImplementations = ["Operational adapter", "Operational, policy opt-in"];

	private static readonly string[] InstallGuides =
	[
		"src/CheatEngine.Client/README.md",
		"templates/CheatEngine.Client.Templates/README.md",
		"templates/CheatEngine.Client.Templates/content/CheatEngine.Plugin/README.md"
	];

	[Fact]
	public void CapabilityTablesListEveryClientCapabilityExactlyOnce()
	{
		string[] expected = [.. ReadCapabilityIds().Values.Order(StringComparer.Ordinal)];
		IReadOnlyList<CapabilityTable> tables = ReadCapabilityTables();
		// Every static declaration in the source must be parsed, so a declaration the pattern misses cannot drop a row.
		int declared = Read(CapabilityIdSource).Split(CapabilityIdDeclarationPrefix, StringSplitOptions.None).Length - 1;

		Assert.True(declared > 0, $"{CapabilityIdSource} declares no Client capability.");
		Assert.Equal(declared, expected.Length);
		Assert.Contains(tables, static table => table.Path == AbstractionsReadme);
		foreach (CapabilityTable table in tables)
		{
			string[] listed = [.. table.Rows.Select(static row => row.Id)];
			Assert.True(listed.Length == listed.Distinct(StringComparer.Ordinal).Count(),
				$"{table.Path} lists a capability id more than once: {string.Join(", ", listed)}.");
			Assert.Equal(expected, listed.Order(StringComparer.Ordinal));
		}
	}

	[Fact]
	public void CapabilityTablesMatchTheCatalogImplementationGates()
	{
		Dictionary<string, string?> experimental = ReadExperimentalDiagnosticIds();
		List<string> offenders = [];
		foreach (CapabilityTable table in ReadCapabilityTables())
		{
			foreach (CapabilityRow row in table.Rows)
			{
				bool matches = experimental.TryGetValue(row.Id, out string? diagnosticId) &&
							   (diagnosticId is not null
								   ? OperationalImplementations.Any(operational =>
									   row.Implementation == ExperimentalImplementation(operational, diagnosticId))
								   : OperationalImplementations.Contains(row.Implementation, StringComparer.Ordinal));
				if (!matches)
				{
					offenders.Add($"{table.Path}:{row.Line} → {row.Id} says '{row.Implementation}'");
				}
			}
		}

		Assert.True(offenders.Count == 0,
			"The Implementation column must name the catalog's operational adapter (" +
			$"{string.Join(" or ", OperationalImplementations)}, or that label followed by " +
			$"'{ExperimentalImplementation(string.Empty, "id")}' for an experimental API):" +
			Environment.NewLine + string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void CapabilityTableStatusColumnsMarkExactlyTheExperimentalApis()
	{
		Dictionary<string, string?> experimental = ReadExperimentalDiagnosticIds();
		List<string> offenders = [];
		int checkedRows = 0;
		foreach (CapabilityTable table in ReadCapabilityTables())
		{
			foreach (CapabilityRow row in table.Rows)
			{
				if (row.Status is not { } status)
				{
					continue;
				}

				checkedRows++;
				bool isExperimental = experimental.GetValueOrDefault(row.Id) is not null;
				string expected = isExperimental ? ExperimentalStatus : AvailableStatus;
				if (!status.StartsWith(expected, StringComparison.Ordinal) ||
					(!isExperimental && status.Contains(ExperimentalStatus, StringComparison.OrdinalIgnoreCase)))
				{
					offenders.Add($"{table.Path}:{row.Line} → {row.Id} says '{status}', expected '{expected}...'");
				}
			}
		}

		Assert.True(checkedRows > 0,
			$"No capability table has a '{StatusColumn}' column; the test would pass vacuously.");
		Assert.True(offenders.Count == 0,
			$"The '{StatusColumn}' column must start with '{ExperimentalStatus}' exactly for the capabilities whose " +
			$"catalog row carries an experimental diagnostic id, and with '{AvailableStatus}' otherwise:" +
			Environment.NewLine + string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void CapabilityTablesNameTheScenariosTheCatalogRequires()
	{
		Dictionary<string, string> ids = ReadCapabilityIds();
		Dictionary<string, string[]> required = new(StringComparer.Ordinal);
		foreach (Match match in CatalogEntry().Matches(Read(CatalogSource)))
		{
			required.Add(ids[match.Groups["name"].Value], Scenarios(match.Groups["arguments"].Value));
		}

		Assert.Equal(ids.Count, required.Count);
		List<string> offenders = [];
		foreach (CapabilityTable table in ReadCapabilityTables())
		{
			foreach (CapabilityRow row in table.Rows)
			{
				string[] documented = Scenarios(row.Qualification);
				if (!required.TryGetValue(row.Id, out string[]? expected) || !documented.SequenceEqual(expected))
				{
					offenders.Add($"{table.Path}:{row.Line} → {row.Id} names [{string.Join(", ", documented)}]");
				}
			}
		}

		Assert.True(offenders.Count == 0,
			"The Qualification column must name exactly the scenarios that the catalog requires:" +
			Environment.NewLine + string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void InstallGuidesStateTheQualifiedHostProfile()
	{
		// The consumed package identity is the resolved CheatEngine.SDK entry of Core's lock file: the same source the
		// Core build embeds for its runtime package gate.
		using JsonDocument lockFile = JsonDocument.Parse(Read(CoreLockFile));
		string contentHash = lockFile.RootElement.GetProperty("dependencies").GetProperty("net10.0")
								 .GetProperty("CheatEngine.SDK").GetProperty("contentHash").GetString()
							 ?? throw new InvalidOperationException($"{CoreLockFile} has no CheatEngine.SDK content hash.");

		foreach (string guide in InstallGuides)
		{
			string text = Read(guide);
			Assert.True(text.Contains(ProfileId, StringComparison.Ordinal), $"{guide} does not name the profile id.");
			Assert.True(text.Contains(HostExecutableSha256, StringComparison.Ordinal),
				$"{guide} does not state the SHA-256 of cheatengine-x86_64.exe.");
			Assert.True(text.Contains(contentHash, StringComparison.Ordinal),
				$"{guide} does not state the NuGet content hash that {CoreLockFile} locks for CheatEngine.SDK.");
			Assert.True(
				text.Split('\n').Any(static line =>
					line.Contains(RuntimeConfigurationSha256, StringComparison.Ordinal) &&
					line.Contains("local modification", StringComparison.OrdinalIgnoreCase)),
				$"{guide} does not state the runtime configuration SHA-256 as a local modification.");
		}
	}

	/// <summary>
	///     The Implementation label of an operational capability whose public API is experimental: its operational label
	///     (for example "Operational adapter", or "Operational, policy opt-in" for an opt-in) followed by the diagnostic id.
	/// </summary>
	private static string ExperimentalImplementation(string operational, string diagnosticId)
	{
		return $"{operational}, experimental ({diagnosticId})";
	}

	/// <summary>
	///     Reads every catalog row, each an operational adapter, keyed by capability id: the value is its experimental
	///     diagnostic id, or <see langword="null" /> for a stable API.
	/// </summary>
	private static Dictionary<string, string?> ReadExperimentalDiagnosticIds()
	{
		Dictionary<string, string> ids = ReadCapabilityIds();
		Dictionary<string, string?> experimental = new(StringComparer.Ordinal);
		foreach (Match match in ImplementationGate().Matches(Read(CatalogSource)))
		{
			string id = ids[match.Groups["name"].Value];
			Group diagnosticId = match.Groups["experimental"];
			Assert.True(experimental.TryAdd(id, diagnosticId.Success ? diagnosticId.Value : null),
				$"{CatalogSource} describes {id} more than once.");
		}

		Assert.Equal(ids.Count, experimental.Count);
		return experimental;
	}

	/// <summary>Reads <c>ClientCapabilityId</c> property names and their stable id strings.</summary>
	private static Dictionary<string, string> ReadCapabilityIds()
	{
		Dictionary<string, string> ids = new(StringComparer.Ordinal);
		foreach (Match match in CapabilityIdDeclaration().Matches(Read(CapabilityIdSource)))
		{
			ids.Add(match.Groups["name"].Value, match.Groups["id"].Value);
		}

		return ids;
	}

	private static List<CapabilityTable> ReadCapabilityTables()
	{
		List<CapabilityTable> tables = [];
		foreach (string path in RepositoryRoot.EnumerateSourceFiles("*.md").Order(StringComparer.Ordinal))
		{
			string[] lines = Read(path).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
			int start = Array.FindIndex(lines, static line => line.Trim() == StartMarker);
			if (start < 0)
			{
				continue;
			}

			int end = Array.FindIndex(lines, start + 1, static line => line.Trim() == EndMarker);
			Assert.True(end > start, $"{path} opens a capability table without closing it.");
			string header = Array.Find(lines[(start + 1)..end], static line => line.StartsWith('|')) ?? string.Empty;
			string[] headers = header.Split('|');
			int qualification = Array.FindIndex(headers, static column => column.Trim() == QualificationColumn);
			int status = Array.FindIndex(headers, static column => column.Trim() == StatusColumn);
			Assert.True(qualification > 0, $"{path} has a capability table without a '{QualificationColumn}' column.");
			List<CapabilityRow> rows = [];
			for (int index = start + 1; index < end; index++)
			{
				Match row = CapabilityRowPattern().Match(lines[index]);
				if (row.Success)
				{
					string[] columns = lines[index].Split('|');
					string? statusCell = status > 0 ? Cell(columns, status) ?? string.Empty : null;
					rows.Add(new CapabilityRow(row.Groups["id"].Value, row.Groups["implementation"].Value.Trim(),
						Cell(columns, qualification) ?? string.Empty, statusCell, index + 1));
				}
			}

			tables.Add(new CapabilityTable(path, rows));
		}

		return tables;
	}

	/// <summary>The trimmed cell at <paramref name="index" />, or <see langword="null" /> past the row's end.</summary>
	private static string? Cell(string[] columns, int index)
	{
		return index < columns.Length ? columns[index].Trim() : null;
	}

	private static string[] Scenarios(string text)
	{
		return
		[
			.. ScenarioId().Matches(text).Select(static match => match.Value).Distinct(StringComparer.Ordinal)
				.Order(StringComparer.Ordinal)
		];
	}

	private static string Read(string relativePath)
	{
		return File.ReadAllText(Path.Combine(RepositoryRoot.Path, relativePath));
	}

	[GeneratedRegex("""public static ClientCapabilityId (?<name>\w+) => new\("(?<id>[^"]+)"\);""",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex CapabilityIdDeclaration();

	[GeneratedRegex(
		@"Entry\(ClientCapabilityId\.(?<name>\w+),[^)]*\)(?:\s*with\s*\{\s*ExperimentalDiagnosticId\s*=\s*""(?<experimental>[^""]+)""\s*\})?",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex ImplementationGate();

	[GeneratedRegex(@"Entry\(ClientCapabilityId\.(?<name>\w+),(?<arguments>[^)]*)\)", RegexOptions.CultureInvariant,
		RegexTimeoutMilliseconds)]
	private static partial Regex CatalogEntry();

	[GeneratedRegex(@"Q\d{2}(?:\.[a-z])?", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex ScenarioId();

	[GeneratedRegex(@"^\|\s*`(?<id>Client\.[A-Za-z]+)`\s*\|(?<implementation>[^|]+)\|",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex CapabilityRowPattern();

	private sealed record CapabilityTable(string Path, IReadOnlyList<CapabilityRow> Rows);

	/// <summary>One capability row.</summary>
	/// <param name="Status">The status cell, or <see langword="null" /> when the table has no status column.</param>
	private sealed record CapabilityRow(
		string Id,
		string Implementation,
		string Qualification,
		string? Status,
		int Line);
}
