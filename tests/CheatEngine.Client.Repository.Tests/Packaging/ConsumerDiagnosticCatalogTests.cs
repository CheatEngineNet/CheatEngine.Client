using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.Packaging;

/// <summary>
///     The CECLIENT build diagnostics that the Hosting package brings to a plugin project (its <c>buildTransitive</c>
///     targets) are catalogued: the codes those targets emit are exactly the rows of the "Build diagnostics" table of
///     the Hosting README, each row has its anchor once and states the severity of its emissions, and every emission
///     carries a help link to that anchor.
/// </summary>
/// <remarks>
///     Source scans only. The targets emit a diagnostic in two ways: an MSBuild <c>Error</c> or <c>Warning</c> element
///     whose <c>HelpLink</c> is <c>$(_CheatEngineClientHelpLink)</c> followed by its code, and a <c>Log.LogError</c>
///     or <c>Log.LogWarning</c> call of an inline task, which receives that property as its <c>HelpLink</c> parameter
///     and appends its code.
/// </remarks>
public sealed partial class ConsumerDiagnosticCatalogTests
{
	private const string TargetsPath =
		"libs/CheatEngine.Client.Hosting/buildTransitive/CheatEngine.Client.Hosting.targets";

	private const string ReadmePath = "libs/CheatEngine.Client.Hosting/README.md";
	private const string TableHeading = "## Build diagnostics";
	private const string HelpLinkProperty = "_CheatEngineClientHelpLink";
	private const string HelpLinkReference = "$(" + HelpLinkProperty + ")";
	private const string ErrorKind = "Error";
	private const string WarningKind = "Warning";

	/// <summary>
	///     The start of the Severity cell of a code that the targets emit both as an error and as a warning: an error,
	///     demoted to a warning by the property the rest of the cell names.
	/// </summary>
	private const string DemotableErrorSeverity = ErrorKind + "; " + WarningKind + " with ";

	private const string ExpectedHelpLink =
		"https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/libs/CheatEngine.Client.Hosting/README.md#";

	private const int RegexTimeoutMilliseconds = 1000;

	[Fact]
	public void TheEmittedCodesAreExactlyTheRowsOfTheReadmeTable()
	{
		string[] emitted =
			[.. EmittedCodes().Select(static emission => emission.Code).Distinct().Order(StringComparer.Ordinal)];
		string[] documented = [.. DocumentedCodes().Order(StringComparer.Ordinal)];
		string[] contiguous = [.. Enumerable.Range(1, emitted.Length).Select(static number => $"CECLIENT{number:000}")];

		Assert.True(emitted.Length > 0, $"{TargetsPath} emits no CECLIENT diagnostic; the test would pass vacuously.");
		Assert.Equal(documented.Distinct(StringComparer.Ordinal), documented);
		Assert.Equal(emitted, documented);
		Assert.Equal(contiguous, emitted);
	}

	[Fact]
	public void EveryRowHasItsAnchorOnce()
	{
		string readme = Read(ReadmePath);
		foreach (string code in DocumentedCodes())
		{
			int anchors = readme.Split($"<a id=\"{code}\"></a>", StringSplitOptions.None).Length - 1;
			Assert.True(anchors == 1, $"{ReadmePath} must contain the anchor of {code} exactly once, found {anchors}.");
		}
	}

	[Fact]
	public void EveryRowStatesTheSeverityOfItsEmissions()
	{
		ILookup<string, string> kinds = EmittedCodes().ToLookup(static emission => emission.Code,
			static emission => emission.Kind, StringComparer.Ordinal);
		List<DocumentedRow> rows = DocumentedRows();
		List<string> offenders = [];
		foreach (DocumentedRow row in rows)
		{
			string[] emitted = [.. kinds[row.Code].Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
			bool matches = emitted switch
			{
				[ErrorKind, WarningKind] => row.Severity.StartsWith(DemotableErrorSeverity, StringComparison.Ordinal),
				[string kind] => string.Equals(row.Severity, kind, StringComparison.Ordinal),
				_ => false
			};
			if (!matches)
			{
				offenders.Add($"{row.Code}: the table says '{row.Severity}', the targets emit it as " +
							  $"[{string.Join(", ", emitted)}]");
			}
		}

		Assert.True(rows.Count > 0, $"{ReadmePath} lists no diagnostic; the test would pass vacuously.");
		Assert.True(offenders.Count == 0,
			$"The Severity column must be '{ErrorKind}' or '{WarningKind}' as the targets emit the code, or " +
			$"start with '{DemotableErrorSeverity}' for a code emitted as both:{Environment.NewLine}" +
			string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void EveryEmissionLinksToItsRow()
	{
		List<string> offenders = [];
		foreach (Emission emission in EmittedCodes())
		{
			string expected = emission.FromTask ? $"HelpLink + \"{emission.Code}\"" : HelpLinkReference + emission.Code;
			if (!string.Equals(emission.HelpLink, expected, StringComparison.Ordinal))
			{
				offenders.Add($"{emission.Code}: HelpLink is '{emission.HelpLink ?? "<none>"}', expected '{expected}'");
			}
		}

		Assert.True(offenders.Count == 0,
			$"Every CECLIENT diagnostic of {TargetsPath} links to its README row:{Environment.NewLine}" +
			string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void TheHelpLinkPropertyNamesTheHostingReadme()
	{
		XDocument targets = XDocument.Parse(Read(TargetsPath));
		string value = string.Empty;
		foreach (XElement assignment in targets.Descendants(HelpLinkProperty))
		{
			Assert.Null(assignment.Attribute("Condition"));
			value = assignment.Value.Replace(HelpLinkReference, value, StringComparison.Ordinal);
		}

		Assert.Equal(ExpectedHelpLink, value);
	}

	[Fact]
	public void EveryInlineTaskThatLogsADiagnosticReceivesTheHelpLink()
	{
		XDocument targets = XDocument.Parse(Read(TargetsPath));
		int tasks = 0;
		foreach (XElement usingTask in targets.Descendants("UsingTask"))
		{
			string code = usingTask.Descendants("Code").Single().Value;
			if (!LoggedDiagnostic().IsMatch(code))
			{
				continue;
			}

			tasks++;
			string name = (string) usingTask.Attribute("TaskName")!;
			XElement parameter = Assert.Single(usingTask.Descendants("ParameterGroup").Elements("HelpLink"));
			Assert.Equal("true", (string?) parameter.Attribute("Required"));
			XElement invocation = Assert.Single(targets.Descendants(name));
			Assert.Equal(HelpLinkReference, (string?) invocation.Attribute("HelpLink"));
		}

		Assert.True(tasks > 0, $"{TargetsPath} has no inline task that logs a CECLIENT diagnostic.");
	}

	/// <summary>Every CECLIENT emission of the targets, with its kind and the help link it passes.</summary>
	private static List<Emission> EmittedCodes()
	{
		XDocument targets = XDocument.Parse(Read(TargetsPath));
		List<Emission> emissions = [];
		foreach (XElement element in targets.Descendants()
					 .Where(static element => element.Name.LocalName is ErrorKind or WarningKind))
		{
			string? code = (string?) element.Attribute("Code");
			if (code is not null && code.StartsWith("CECLIENT", StringComparison.Ordinal))
			{
				emissions.Add(new Emission(code, element.Name.LocalName, (string?) element.Attribute("HelpLink"),
					FromTask: false));
			}
		}

		foreach (XElement code in targets.Descendants("Code"))
		{
			MatchCollection calls = LoggedDiagnostic().Matches(code.Value);
			// A recognized call quotes its code twice, as the code and in the link: any other quoted code is an
			// emission that this scan would miss.
			Assert.Equal(2 * calls.Count, QuotedCode().Count(code.Value));
			foreach (Match call in calls)
			{
				emissions.Add(new Emission(call.Groups["code"].Value, call.Groups["kind"].Value,
					call.Groups["link"].Value.Trim(), FromTask: true));
			}
		}

		return emissions;
	}

	/// <summary>The codes of the rows of the Hosting README's "Build diagnostics" table.</summary>
	private static IEnumerable<string> DocumentedCodes()
	{
		return DocumentedRows().Select(static row => row.Code);
	}

	/// <summary>The rows of the Hosting README's "Build diagnostics" table: code and Severity cell.</summary>
	private static List<DocumentedRow> DocumentedRows()
	{
		string[] lines = Read(ReadmePath).ReplaceLineEndings("\n").Split('\n');
		int start = Array.IndexOf(lines, TableHeading);
		Assert.True(start >= 0, $"{ReadmePath} has no '{TableHeading}' section.");
		List<DocumentedRow> rows = [];
		for (int index = start + 1;
			 index < lines.Length && !lines[index].StartsWith("## ", StringComparison.Ordinal);
			 index++)
		{
			Match row = DiagnosticRow().Match(lines[index]);
			if (row.Success)
			{
				Assert.Equal(row.Groups["anchor"].Value, row.Groups["code"].Value);
				rows.Add(new DocumentedRow(row.Groups["code"].Value, row.Groups["severity"].Value.Trim()));
			}
		}

		return rows;
	}

	private static string Read(string relativePath)
	{
		return File.ReadAllText(Path.Combine(RepositoryRoot.Path, relativePath));
	}

	/// <summary>A <c>Log.LogError</c> or <c>Log.LogWarning</c> call that reports a CECLIENT code.</summary>
	[GeneratedRegex(
		@"Log\.Log(?<kind>Error|Warning)\(\s*null,\s*""(?<code>CECLIENT\d{3})"",\s*null,\s*(?<link>[^,]+),",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex LoggedDiagnostic();

	[GeneratedRegex(@"""CECLIENT\d{3}""", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex QuotedCode();

	[GeneratedRegex(
		@"^\|\s*<a id=""(?<anchor>CECLIENT\d{3})""></a>`(?<code>CECLIENT\d{3})`\s*\|(?<severity>[^|]*)\|",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex DiagnosticRow();

	/// <summary>One CECLIENT emission: its code, its kind (Error or Warning) and the help link it passes.</summary>
	private sealed record Emission(string Code, string Kind, string? HelpLink, bool FromTask);

	private sealed record DocumentedRow(string Code, string Severity);
}
