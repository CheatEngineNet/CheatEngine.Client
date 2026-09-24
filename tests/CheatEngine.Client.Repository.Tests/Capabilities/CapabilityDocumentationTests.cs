using System.Text.Json;
using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.Capabilities;

/// <summary>
///     The documentation states what the Client build reports (audit CLI-DOC-1, A00-04, A10-05, A21-13, A21-21, A21-30):
///     every capability table lists each <c>ClientCapabilityId</c> once with the implementation gate that
///     <c>ClientCapabilityCatalog</c> declares and <c>RuntimeClient</c> composes, and with the live scenarios its
///     qualification gate requires; the install guides state the supported host profile with its identities.
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
	private const string ContractOnly = "Contract-only (Unavailable)";
	private const string QualificationColumn = "Qualification";
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
		Dictionary<string, string> ids = ReadCapabilityIds();
		Dictionary<string, bool> implemented = new(StringComparer.Ordinal);
		Dictionary<string, string> experimental = new(StringComparer.Ordinal);
		string catalog = Read(CatalogSource);
		foreach (Match match in ImplementationGate().Matches(catalog))
		{
			string id = ids[match.Groups["name"].Value];
			Assert.True(implemented.TryAdd(id, match.Groups["gate"].Value == "Operational"),
				$"{CatalogSource} describes {id} more than once.");
			if (match.Groups["experimental"].Success)
			{
				experimental.Add(id, match.Groups["experimental"].Value);
			}
		}

		Assert.Equal(ids.Count, implemented.Count);
		List<string> offenders = [];
		foreach (CapabilityTable table in ReadCapabilityTables())
		{
			foreach (CapabilityRow row in table.Rows)
			{
				bool matches = implemented.TryGetValue(row.Id, out bool isImplemented) &&
							   (!isImplemented
								   ? row.Implementation == ContractOnly
								   : experimental.TryGetValue(row.Id, out string? diagnosticId)
									   ? row.Implementation == ExperimentalImplementation(diagnosticId)
									   : OperationalImplementations.Contains(row.Implementation, StringComparer.Ordinal));
				if (!matches)
				{
					offenders.Add($"{table.Path}:{row.Line} → {row.Id} says '{row.Implementation}'");
				}
			}
		}

		Assert.True(offenders.Count == 0,
			"The Implementation column must follow the catalog's implementation gate ('Operational' → " +
			$"{string.Join(" or ", OperationalImplementations)}, or '{ExperimentalImplementation("id")}' for an " +
			$"experimental API; 'ContractOnly' → {ContractOnly}):" +
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

	/// <summary>The Implementation label of an operational capability whose public API is experimental.</summary>
	private static string ExperimentalImplementation(string diagnosticId)
	{
		return $"Operational adapter, experimental ({diagnosticId})";
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
			int qualification = Array.FindIndex(header.Split('|'),
				static column => column.Trim() == QualificationColumn);
			Assert.True(qualification > 0, $"{path} has a capability table without a '{QualificationColumn}' column.");
			List<CapabilityRow> rows = [];
			for (int index = start + 1; index < end; index++)
			{
				Match row = CapabilityRowPattern().Match(lines[index]);
				if (row.Success)
				{
					string[] columns = lines[index].Split('|');
					rows.Add(new CapabilityRow(row.Groups["id"].Value, row.Groups["implementation"].Value.Trim(),
						qualification < columns.Length ? columns[qualification].Trim() : string.Empty, index + 1));
				}
			}

			tables.Add(new CapabilityTable(path, rows));
		}

		return tables;
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
		@"Entry\(ClientCapabilityId\.(?<name>\w+),\s*CapabilityImplementation\.(?<gate>Operational|ContractOnly)\b[^)]*\)(?:\s*with\s*\{\s*ExperimentalDiagnosticId\s*=\s*""(?<experimental>[^""]+)""\s*\})?",
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

	private sealed record CapabilityRow(string Id, string Implementation, string Qualification, int Line);
}
