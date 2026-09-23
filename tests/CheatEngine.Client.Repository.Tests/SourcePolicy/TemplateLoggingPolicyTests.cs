using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.SourcePolicy;

/// <summary>
///     Enforces the Q46 redaction policy on the plugin template: the template is packed as source and only compiled after
///     <c>dotnet new</c>, so its log events are checked from the committed text.
/// </summary>
public sealed partial class TemplateLoggingPolicyTests
{
	private const string TemplateContentRoot = "templates/CheatEngine.Client.Templates/content/";

	private static readonly HashSet<string> SensitiveTypeNames = new(StringComparer.Ordinal)
	{
		"Address",
		"SymbolExpression",
		"ModuleName",
		"CheatEngineFailure",
		"LuaScript",
		"FileInfo",
		"FileSystemInfo"
	};

	[Fact]
	[Trait("Qualification", "Q46")]
	public void TemplateLogsNoAddressesValuesOrRawFailures()
	{
		List<string> violations = [];
		int events = 0;
		foreach (string file in RepositoryRoot.EnumerateSourceFiles("*.cs")
					 .Where(static path => path.StartsWith(TemplateContentRoot, StringComparison.Ordinal)))
		{
			string source = File.ReadAllText(Path.Combine(RepositoryRoot.Path, file));
			foreach (LogEvent logEvent in FindLogEvents(source))
			{
				events++;
				violations.AddRange(FindViolations(logEvent).Select(violation => $"{file}: {violation}"));
			}
		}

		Assert.True(events > 0, "No LoggerMessage event was found in the template; the policy would pass vacuously.");
		Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
	}

	[Fact]
	[Trait("Qualification", "Q46")]
	public void TemplateLoggingParserRejectsSensitiveParametersAndAcceptsSafeOnes()
	{
		const string Sample = """
							  [LoggerMessage(Level = LogLevel.Information, Message = "Read {Address} (value).")]
							  private static partial void LeakAddress(ILogger logger, Address address);

							  [LoggerMessage(Level = LogLevel.Debug, Message = "Skipped {Operation}: {Reason}")]
							  private static partial void LeakFailure(ILogger logger, string operation, CheatEngineFailure reason);

							  [LoggerMessage(3, LogLevel.Warning, "Failed.")]
							  internal static partial void LeakException(ILogger logger, InvalidOperationException error);

							  [LoggerMessage(Level = LogLevel.Debug, Message = "Loaded {ScriptSource}.")]
							  private static partial void LeakScript(ILogger logger, string scriptSource);

							  [LoggerMessage(Level = LogLevel.Debug, Message = "Count {Count}; kind {Kind}.")]
							  private static partial void SafeEvent(ILogger logger, int count, CheatEngineFailureKind kind,
							      Dictionary<string, int> totals);
							  """;

		LogEvent[] events = FindLogEvents(Sample).ToArray();

		Assert.Equal(["LeakAddress", "LeakFailure", "LeakException", "LeakScript", "SafeEvent"],
			events.Select(static logEvent => logEvent.Name));
		Assert.All(events.Where(static logEvent => logEvent.Name.StartsWith("Leak", StringComparison.Ordinal)),
			static logEvent => Assert.NotEmpty(FindViolations(logEvent)));
		Assert.Empty(FindViolations(events.Single(static logEvent => logEvent.Name == "SafeEvent")));
	}

	private static IEnumerable<LogEvent> FindLogEvents(string source)
	{
		foreach (Match match in LoggerMessageDeclaration().Matches(source))
		{
			yield return new LogEvent(match.Groups["name"].Value, SplitParameters(match.Groups["parameters"].Value));
		}
	}

	private static IEnumerable<string> FindViolations(LogEvent logEvent)
	{
		foreach ((string type, string name) in logEvent.Parameters)
		{
			string simpleType = SimpleTypeName(type);
			if (SensitiveTypeNames.Contains(simpleType) ||
				simpleType.EndsWith("Exception", StringComparison.Ordinal))
			{
				yield return $"{logEvent.Name} parameter '{name}' has user-data type '{type}'.";
			}
			else if (simpleType == "string" && SensitiveName().IsMatch(name))
			{
				yield return $"{logEvent.Name} string parameter '{name}' is named like user data.";
			}
		}
	}

	/// <summary>Splits a parameter list at top-level commas, ignoring commas inside generic argument lists.</summary>
	private static (string Type, string Name)[] SplitParameters(string parameters)
	{
		List<(string Type, string Name)> result = [];
		int depth = 0;
		int start = 0;
		for (int index = 0; index <= parameters.Length; index++)
		{
			char current = index < parameters.Length ? parameters[index] : ',';
			if (current == '<')
			{
				depth++;
			}
			else if (current == '>')
			{
				depth--;
			}
			else if (current == ',' && depth == 0)
			{
				string parameter = AttributePrefix().Replace(parameters[start..index], string.Empty).Trim();
				start = index + 1;
				if (parameter.Length == 0)
				{
					continue;
				}

				int separator = parameter.LastIndexOf(' ');
				result.Add((parameter[..separator].Trim(), parameter[(separator + 1)..]));
			}
		}

		return [.. result];
	}

	private static string SimpleTypeName(string type)
	{
		string withoutNullable = type.TrimEnd('?');
		int genericStart = withoutNullable.IndexOf('<', StringComparison.Ordinal);
		string withoutGenerics = genericStart < 0 ? withoutNullable : withoutNullable[..genericStart];
		int namespaceEnd = withoutGenerics.LastIndexOf('.');
		return namespaceEnd < 0 ? withoutGenerics : withoutGenerics[(namespaceEnd + 1)..];
	}

	[GeneratedRegex(@"\[LoggerMessage\b.*?partial\s+void\s+(?<name>\w+)\s*\((?<parameters>[^)]*)\)",
		RegexOptions.Singleline)]
	private static partial Regex LoggerMessageDeclaration();

	[GeneratedRegex(@"\[[^\]]*\]")]
	private static partial Regex AttributePrefix();

	[GeneratedRegex("path|file|script|source|message|expression", RegexOptions.IgnoreCase)]
	private static partial Regex SensitiveName();

	private sealed record LogEvent(string Name, (string Type, string Name)[] Parameters);
}
