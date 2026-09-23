using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;

namespace CheatEngine.Client.SourceGenerators.Lua.Tests.Diagnostics;

/// <summary>
///     Diagnostic hygiene (DoD F): every descriptor is tracked in the analyzer release file with its category and severity,
///     listed in the generator README, and inside the CECLUA ranges allocated to this generator.
/// </summary>
public sealed partial class CheatEngineLuaDiagnosticCatalogTests
{
	private const string ReleaseFile = "AnalyzerReleases.Unshipped.md";
	private const string ShippedReleaseFile = "AnalyzerReleases.Shipped.md";
	private const string GeneratorReadme = "Generator.README.md";

	[Fact]
	public void EveryDescriptorIsTrackedInTheReleaseFileWithMatchingCategoryAndSeverity()
	{
		Dictionary<string, (string Category, string Severity)> rows = ReadReleaseRows();

		foreach (DiagnosticDescriptor descriptor in CheatEngineLuaDiagnostics.All)
		{
			Assert.True(rows.TryGetValue(descriptor.Id, out (string Category, string Severity) row),
				$"{descriptor.Id} is not tracked in {ShippedReleaseFile} or {ReleaseFile}.");
			Assert.Equal(descriptor.Category, row.Category);
			Assert.Equal(descriptor.DefaultSeverity.ToString(), row.Severity);
		}

		Assert.Equal(CheatEngineLuaDiagnostics.All.Select(static descriptor => descriptor.Id).Order(StringComparer.Ordinal),
			rows.Keys.Order(StringComparer.Ordinal));
	}

	[Fact]
	public void EveryDescriptorIsListedInTheGeneratorReadme()
	{
		string readme = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Diagnostics", GeneratorReadme));

		Assert.Contains("\n## Diagnostics", readme.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
		foreach (DiagnosticDescriptor descriptor in CheatEngineLuaDiagnostics.All)
		{
			Assert.Contains("| " + descriptor.Id + " | " + descriptor.Title + " |", readme, StringComparison.Ordinal);
			Assert.Equal(CheatEngineLuaDiagnostics.HelpLinkUri, descriptor.HelpLinkUri);
		}

		Assert.EndsWith("/README.md#diagnostics", CheatEngineLuaDiagnostics.HelpLinkUri, StringComparison.Ordinal);
	}

	[Fact]
	public void NewDiagnosticIdsStayInsideTheAllocatedRanges()
	{
		string[] ids = [.. CheatEngineLuaDiagnostics.All.Select(static descriptor => descriptor.Id)];

		Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
		Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
		foreach (string id in ids)
		{
			int number = int.Parse(id["CECLUA".Length..], System.Globalization.CultureInfo.InvariantCulture);
			Assert.StartsWith("CECLUA", id, StringComparison.Ordinal);
			// Generator ranges: 1001-1006 modules, 1101-1106 operations, 1201-1209 module ownership (C-LUAGEN). 1107-1109
			// (C-CORE-A) and 1301-1309 (C-CORE-B) belong to other lots and must never appear here.
			Assert.True(number is >= 1001 and <= 1006 or >= 1101 and <= 1106 or >= 1201 and <= 1209,
				$"{id} is outside the ranges allocated to the Lua generator.");
			Assert.Same(CheatEngineLuaDiagnostics.All.Single(descriptor => descriptor.Id == id),
				CheatEngineLuaDiagnostics.Get(id));
		}

		Assert.Equal(["CECLUA1201", "CECLUA1202", "CECLUA1203", "CECLUA1204"],
			ids.Where(static id => id.StartsWith("CECLUA12", StringComparison.Ordinal)));
		Assert.All(CheatEngineLuaDiagnostics.All, static descriptor =>
		{
			Assert.Equal(DiagnosticSeverity.Error, descriptor.DefaultSeverity);
			Assert.True(descriptor.IsEnabledByDefault);
			Assert.Equal(CheatEngineLuaDiagnostics.Category, descriptor.Category);
		});
	}

	private static Dictionary<string, (string Category, string Severity)> ReadReleaseRows()
	{
		Dictionary<string, (string, string)> rows = new(StringComparer.Ordinal);
		IEnumerable<string> lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Diagnostics", ShippedReleaseFile))
			.Concat(File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Diagnostics", ReleaseFile)));
		foreach (string line in lines)
		{
			Match match = ReleaseRow().Match(line);
			if (match.Success)
			{
				Assert.True(rows.TryAdd(match.Groups["id"].Value,
						(match.Groups["category"].Value.Trim(), match.Groups["severity"].Value.Trim())),
					$"{match.Groups["id"].Value} is tracked twice.");
			}
		}

		return rows;
	}

	[GeneratedRegex(@"^\s*(?<id>CECLUA\d{4})\s*\|(?<category>[^|]+)\|(?<severity>[^|]+)\|", RegexOptions.CultureInvariant)]
	private static partial Regex ReleaseRow();
}
