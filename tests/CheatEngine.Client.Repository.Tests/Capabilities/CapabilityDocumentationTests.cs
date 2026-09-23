using System.Text.Json;
using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.Capabilities;

/// <summary>
///     The documentation states what the Client build reports (audit CLI-DOC-1, A00-04, A10-05, A21-13, A21-21, A21-30):
///     every capability table lists each <c>ClientCapabilityId</c> once with the implementation gate that
///     <c>RuntimeClient</c> composes, and the install guides state the supported host profile with its identities.
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
	private const string RuntimeClientSource = "libs/CheatEngine.Client.Core/Domains/RuntimeClient.cs";
	private const string ConsumedSdkIdentity = "eng/sdk/consumed-sdk.json";
	private const string ContractOnly = "Contract-only (Unavailable)";
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

		Assert.Equal(17, expected.Length);
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
	public void CapabilityTablesMatchTheRuntimeClientImplementationGates()
	{
		Dictionary<string, string> ids = ReadCapabilityIds();
		Dictionary<string, bool> implemented = new(StringComparer.Ordinal);
		string runtimeClient = Read(RuntimeClientSource);
		foreach (Match match in ImplementationGate().Matches(runtimeClient))
		{
			string id = ids[match.Groups["name"].Value];
			Assert.True(implemented.TryAdd(id, match.Groups["gate"].Value == "implemented"),
				$"{RuntimeClientSource} describes {id} more than once.");
		}

		Assert.Equal(ids.Count, implemented.Count);
		List<string> offenders = [];
		foreach (CapabilityTable table in ReadCapabilityTables())
		{
			foreach (CapabilityRow row in table.Rows)
			{
				bool matches = implemented.TryGetValue(row.Id, out bool isImplemented) &&
							   (isImplemented
								   ? OperationalImplementations.Contains(row.Implementation, StringComparer.Ordinal)
								   : row.Implementation == ContractOnly);
				if (!matches)
				{
					offenders.Add($"{table.Path}:{row.Line} → {row.Id} says '{row.Implementation}'");
				}
			}
		}

		Assert.True(offenders.Count == 0,
			"The Implementation column must follow RuntimeClient's implementation gate ('implemented' → " +
			$"{string.Join(" or ", OperationalImplementations)}; 'contractOnly' → {ContractOnly}):" +
			Environment.NewLine + string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void InstallGuidesStateTheQualifiedHostProfile()
	{
		using JsonDocument identity = JsonDocument.Parse(Read(ConsumedSdkIdentity));
		string contentHash = identity.RootElement.GetProperty("contentHashSha512").GetString()
							 ?? throw new InvalidOperationException($"{ConsumedSdkIdentity} has no contentHashSha512.");

		foreach (string guide in InstallGuides)
		{
			string text = Read(guide);
			Assert.True(text.Contains(ProfileId, StringComparison.Ordinal), $"{guide} does not name the profile id.");
			Assert.True(text.Contains(HostExecutableSha256, StringComparison.Ordinal),
				$"{guide} does not state the SHA-256 of cheatengine-x86_64.exe.");
			Assert.True(text.Contains(contentHash, StringComparison.Ordinal),
				$"{guide} does not state the NuGet content hash of {ConsumedSdkIdentity}.");
			Assert.True(
				text.Split('\n').Any(static line =>
					line.Contains(RuntimeConfigurationSha256, StringComparison.Ordinal) &&
					line.Contains("local modification", StringComparison.OrdinalIgnoreCase)),
				$"{guide} does not state the runtime configuration SHA-256 as a local modification.");
		}
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
			List<CapabilityRow> rows = [];
			for (int index = start + 1; index < end; index++)
			{
				Match row = CapabilityRowPattern().Match(lines[index]);
				if (row.Success)
				{
					rows.Add(new CapabilityRow(row.Groups["id"].Value, row.Groups["implementation"].Value.Trim(),
						index + 1));
				}
			}

			tables.Add(new CapabilityTable(path, rows));
		}

		return tables;
	}

	private static string Read(string relativePath)
	{
		return File.ReadAllText(Path.Combine(RepositoryRoot.Path, relativePath));
	}

	[GeneratedRegex("""public static ClientCapabilityId (?<name>\w+) => new\("(?<id>[^"]+)"\);""",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex CapabilityIdDeclaration();

	[GeneratedRegex(@"Describe\(ClientCapabilityId\.(?<name>\w+),\s*(?<gate>implemented|contractOnly)\b",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex ImplementationGate();

	[GeneratedRegex(@"^\|\s*`(?<id>Client\.[A-Za-z]+)`\s*\|(?<implementation>[^|]+)\|",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex CapabilityRowPattern();

	private sealed record CapabilityTable(string Path, IReadOnlyList<CapabilityRow> Rows);

	private sealed record CapabilityRow(string Id, string Implementation, int Line);
}
