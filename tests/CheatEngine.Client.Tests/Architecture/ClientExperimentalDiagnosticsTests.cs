using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using CheatEngine.Client.Tests.Infrastructure;

using ReflectionAssembly = System.Reflection.Assembly;

namespace CheatEngine.Client.Tests.Architecture;

/// <summary>
///     Every experimental Client API is catalogued (plan L15): its <c>[Experimental]</c> diagnostic id is an entry of
///     <see cref="Catalog" />, its <c>UrlFormat</c> points to the Abstractions README, that README has the id's anchor, its
///     PublicAPI entries carry the <c>[id]</c> prefix, and only Core, DI and <c>tests/</c> suppress the diagnostic.
/// </summary>
/// <remarks>
///     A lot that adds an experimental API appends one entry to <see cref="Catalog" /> and the matching README anchor; the
///     lot that lifts an id after its live qualification removes the entry together with its attributes and prefixes.
/// </remarks>
public sealed partial class ClientExperimentalDiagnosticsTests
{
	private const string ExperimentalAttributeType = "System.Diagnostics.CodeAnalysis.ExperimentalAttribute";

	private const string ExpectedUrlFormat =
		"https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/libs/CheatEngine.Client.Abstractions/README.md#{0}";

	private const string AbstractionsReadme = "libs/CheatEngine.Client.Abstractions/README.md";

	private const int RegexTimeoutMilliseconds = 1000;

	/// <summary>The experimental Client diagnostics, one entry per id.</summary>
	private static readonly ExperimentalDiagnostic[] Catalog =
	[
		new("CECLIENT5001", "Value scans: IValueScanner, IValueScanSession and their request and result types"),
		new("CECLIENT5002", "Target allocations: IAllocationClient, ITargetMemoryLease, AllocationRequest and AllocationProtection"),
		new("CECLIENT5004",
			"Auto Assembler patches: IAutoAssemblerClient, IAutoAssemblerPatchLease, AutoAssemblerCheckResult, " +
			"AutoAssemblerScript and CheatEngineClientBuilder.EnableAutoAssemblerPatches")
	];

	/// <summary>The only places that may suppress an experimental Client diagnostic.</summary>
	private static readonly string[] SuppressionRoots =
	[
		"libs/CheatEngine.Client.Core/",
		"libs/CheatEngine.Client.Extensions.DependencyInjection/",
		"tests/"
	];

	private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
	{
		"artifacts", "bin", "obj", ".git", ".vs", ".idea", "TestResults", "node_modules"
	};

	private static readonly HashSet<string> MsBuildExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".csproj", ".props", ".targets"
	};

	private static readonly string[] MsBuildSuppressionElements = ["NoWarn", "WarningsNotAsErrors"];

	private static readonly string[] PublicApiModifiers =
		["~", "override ", "static ", "abstract ", "virtual ", "const ", "readonly ", "sealed ", "new "];

	private static readonly Lazy<ExperimentalSymbol[]> Symbols = new(ReadExperimentalSymbols,
		LazyThreadSafetyMode.ExecutionAndPublication);

	[Fact]
	public void EveryExperimentalClientApiUsesACataloguedIdAndTheReadmeUrl()
	{
		HashSet<string> catalogued = [.. Catalog.Select(static diagnostic => diagnostic.Id)];
		string[] uncatalogued =
		[
			.. Symbols.Value.Where(symbol => !catalogued.Contains(symbol.Id))
				.Select(static symbol => $"{symbol.Name} [Experimental(\"{symbol.Id}\")]")
		];
		string[] wrongUrl =
		[
			.. Symbols.Value.Where(static symbol => symbol.UrlFormat != ExpectedUrlFormat)
				.Select(static symbol => $"{symbol.Name}: UrlFormat = {symbol.UrlFormat ?? "<none>"}")
		];
		string[] unused =
		[
			.. catalogued.Where(id => Symbols.Value.All(symbol => symbol.Id != id))
		];

		Assert.True(uncatalogued.Length == 0,
			"Append these experimental ids to ClientExperimentalDiagnosticsTests.Catalog:" + Environment.NewLine +
			string.Join(Environment.NewLine, uncatalogued));
		Assert.True(wrongUrl.Length == 0,
			$"An experimental Client API must use UrlFormat = \"{ExpectedUrlFormat}\":" + Environment.NewLine +
			string.Join(Environment.NewLine, wrongUrl));
		Assert.True(unused.Length == 0,
			"These catalogued ids mark no Client API; remove them from the catalog: " + string.Join(", ", unused));
	}

	[Fact]
	public void EveryCataloguedIdHasItsReadmeAnchor()
	{
		string readme = File.ReadAllText(RepositoryLayout.Combine(AbstractionsReadme));

		foreach (ExperimentalDiagnostic diagnostic in Catalog)
		{
			string anchor = $"<a id=\"{diagnostic.Id}\"></a>";
			int count = readme.Split(anchor).Length - 1;
			Assert.True(count == 1, $"{AbstractionsReadme} must contain '{anchor}' exactly once ({diagnostic.Scope}).");
		}
	}

	[Fact]
	public void PublicApiEntriesOfExperimentalApisCarryTheirIdPrefix()
	{
		List<string> offenders = [];
		Dictionary<string, int> prefixed = Catalog.ToDictionary(static diagnostic => diagnostic.Id, static _ => 0,
			StringComparer.Ordinal);
		foreach (IGrouping<string, ExperimentalSymbol> assembly in Symbols.Value.GroupBy(static symbol => symbol.Assembly))
		{
			foreach (string line in ReadPublicApiEntries(assembly.Key))
			{
				Match prefix = IdPrefix().Match(line);
				string symbolText = StripModifiers(prefix.Success ? line[prefix.Length..] : line);
				ExperimentalSymbol? owner = assembly.FirstOrDefault(symbol => Declares(symbol.Name, symbolText));
				string? expected = owner?.Id;
				string? actual = prefix.Success ? prefix.Groups["id"].Value : null;
				if (!string.Equals(expected, actual, StringComparison.Ordinal))
				{
					offenders.Add($"{assembly.Key}: '{line}' should carry {(expected is null ? "no prefix" : $"[{expected}]")}");
				}
				else if (actual is not null && prefixed.TryGetValue(actual, out int count))
				{
					prefixed[actual] = count + 1;
				}
			}
		}

		Assert.True(offenders.Count == 0,
			"The PublicAPI entry of an experimental API starts with its [id], and only those entries do:" +
			Environment.NewLine + string.Join(Environment.NewLine, offenders));
		Assert.All(prefixed, static entry => Assert.True(entry.Value > 0, $"No PublicAPI entry carries [{entry.Key}]."));
	}

	[Fact]
	public void OnlyCoreDependencyInjectionAndTestsSuppressAnExperimentalClientDiagnostic()
	{
		List<string> suppressing = [];
		foreach (string file in EnumerateScannedFiles())
		{
			string relative = Path.GetRelativePath(RepositoryLayout.Root, file).Replace('\\', '/');
			if (FindSuppressions(relative, File.ReadAllText(file)).Count != 0)
			{
				suppressing.Add(relative);
			}
		}

		string[] offenders =
		[
			.. suppressing.Where(static file =>
				!SuppressionRoots.Any(root => file.StartsWith(root, StringComparison.Ordinal)))
		];

		// Core implements the experimental APIs, so the scan cannot pass vacuously.
		Assert.Contains("libs/CheatEngine.Client.Core/CheatEngine.Client.Core.csproj", suppressing);
		Assert.True(offenders.Length == 0,
			"Only Core, DI and tests/ may suppress an experimental Client diagnostic (CECLIENT5xxx); a consumer-facing " +
			"project, the template or Fluent must not:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
	}

	[Theory]
	[InlineData("Probe.cs", "#pragma warning disable CECLIENT5001")]
	[InlineData("Probe.cs", "\t#pragma warning disable CS0618, CECLIENT5002 // deliberate")]
	[InlineData("Probe.cs", "[SuppressMessage(\"Usage\", \"CECLIENT5001:Experimental API\", Justification = \"x\")]")]
	[InlineData("Probe.props", "<Project><PropertyGroup><NoWarn>$(NoWarn);CECLIENT5001</NoWarn></PropertyGroup></Project>")]
	[InlineData("Probe.csproj",
		"<Project><PropertyGroup><WarningsNotAsErrors>CECLIENT5001</WarningsNotAsErrors></PropertyGroup></Project>")]
	[InlineData("Probe.targets",
		"<Project><ItemGroup><PackageReference Include=\"X\" NoWarn=\"CECLIENT5001\" /></ItemGroup></Project>")]
	[InlineData(".editorconfig", "[*.cs]\ndotnet_diagnostic.CECLIENT5001.severity = none")]
	[InlineData("Probe.globalconfig", "is_global = true\ndotnet_diagnostic.CECLIENT5003.severity = warning")]
	[InlineData("Directory.Build.rsp", "-nowarn:CECLIENT5001")]
	public void TheSuppressionScanRecognizesEverySuppressionForm(string path, string text)
	{
		Assert.NotEmpty(FindSuppressions(path, text));
	}

	[Theory]
	[InlineData("Probe.cs", "// CECLIENT5001 marks the value scans experimental.")]
	[InlineData("Probe.cs", "#pragma warning disable CS0618 // CECLIENT5001 stays an error")]
	[InlineData("Probe.props", "<Project><!-- Never add CECLIENT5001 here. --><PropertyGroup><NoWarn>CS1591</NoWarn></PropertyGroup></Project>")]
	[InlineData(".editorconfig", "# dotnet_diagnostic.CECLIENT5001.severity = none is forbidden")]
	public void CommentsMayCiteExperimentalClientDiagnostics(string path, string text)
	{
		Assert.Empty(FindSuppressions(path, text));
	}

	/// <summary>Whether a PublicAPI symbol, without prefix and modifiers, belongs to the experimental symbol.</summary>
	private static bool Declares(string experimentalName, string symbolText)
	{
		if (!symbolText.StartsWith(experimentalName, StringComparison.Ordinal))
		{
			return false;
		}

		return symbolText.Length == experimentalName.Length ||
			   symbolText[experimentalName.Length] is '.' or ' ' or '(' or '<';
	}

	private static string StripModifiers(string line)
	{
		string stripped = line;
		bool changed = true;
		while (changed)
		{
			changed = false;
			foreach (string modifier in PublicApiModifiers)
			{
				if (stripped.StartsWith(modifier, StringComparison.Ordinal))
				{
					stripped = stripped[modifier.Length..];
					changed = true;
				}
			}
		}

		return stripped;
	}

	private static IEnumerable<string> ReadPublicApiEntries(string assemblyName)
	{
		foreach (string file in (string[]) ["PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt"])
		{
			string path = RepositoryLayout.Combine($"libs/{assemblyName}/{file}");
			Assert.True(File.Exists(path), $"{assemblyName} ships experimental APIs but has no {file}.");
			foreach (string line in File.ReadAllLines(path))
			{
				if (line.Length != 0 && !line.StartsWith('#'))
				{
					yield return line;
				}
			}
		}
	}

	/// <summary>Reads every public type and member of the Client assemblies that carries <c>[Experimental]</c>.</summary>
	private static ExperimentalSymbol[] ReadExperimentalSymbols()
	{
		List<ExperimentalSymbol> symbols = [];
		foreach (ReflectionAssembly assembly in ClientAssemblyCatalog.LoadAll())
		{
			string assemblyName = assembly.GetName().Name!;
			foreach (Type type in assembly.GetExportedTypes())
			{
				string typeName = type.FullName!.Replace('+', '.');
				if (TryGetExperimental(type.GetCustomAttributesData(), out string? id, out string? urlFormat))
				{
					symbols.Add(new ExperimentalSymbol(assemblyName, typeName, id, urlFormat));
				}

				foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance |
															  BindingFlags.Static | BindingFlags.DeclaredOnly))
				{
					if (TryGetExperimental(member.GetCustomAttributesData(), out string? memberId,
							out string? memberUrlFormat))
					{
						string name = $"{typeName}.{member.Name}";
						symbols.Add(new ExperimentalSymbol(assemblyName, name, memberId, memberUrlFormat));
					}
				}
			}
		}

		Assert.NotEmpty(symbols);
		return [.. symbols];
	}

	private static bool TryGetExperimental(IList<CustomAttributeData> attributes,
		[NotNullWhen(true)] out string? id, out string? urlFormat)
	{
		foreach (CustomAttributeData attribute in attributes)
		{
			if (attribute.AttributeType.FullName != ExperimentalAttributeType)
			{
				continue;
			}

			id = (string) attribute.ConstructorArguments[0].Value!;
			urlFormat = attribute.NamedArguments
				.Where(static argument => argument.MemberName == "UrlFormat")
				.Select(static argument => (string?) argument.TypedValue.Value)
				.FirstOrDefault();
			return true;
		}

		id = null;
		urlFormat = null;
		return false;
	}

	/// <summary>Returns every suppression of an experimental Client diagnostic in one file.</summary>
	private static List<string> FindSuppressions(string path, string text)
	{
		string name = Path.GetFileName(path);
		string extension = Path.GetExtension(path);
		List<string> found = [];
		if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
		{
			found.AddRange(PragmaDisable().Matches(text)
				.Where(static match => ExperimentalId().IsMatch(match.Groups["ids"].Value.Split("//", 2)[0]))
				.Select(static match => match.Value.Trim()));
			found.AddRange(SuppressMessageAttribute().Matches(text)
				.Where(static match => ExperimentalId().IsMatch(match.Groups["arguments"].Value))
				.Select(static match => match.Value.Trim()));
		}
		else if (MsBuildExtensions.Contains(extension))
		{
			XDocument document = XDocument.Parse(text);
			found.AddRange(document.Descendants()
				.Where(static element => MsBuildSuppressionElements.Contains(element.Name.LocalName) &&
										 ExperimentalId().IsMatch(element.Value))
				.Select(static element => element.Name.LocalName));
			found.AddRange(document.Descendants().Attributes()
				.Where(static attribute => MsBuildSuppressionElements.Contains(attribute.Name.LocalName) &&
										   ExperimentalId().IsMatch(attribute.Value))
				.Select(static attribute => attribute.ToString()));
		}
		else if (name.Equals(".editorconfig", StringComparison.OrdinalIgnoreCase) ||
				 extension.Equals(".globalconfig", StringComparison.OrdinalIgnoreCase))
		{
			found.AddRange(SeverityConfiguration().Matches(text).Select(static match => match.Value.Trim()));
		}
		else if (extension.Equals(".rsp", StringComparison.OrdinalIgnoreCase))
		{
			found.AddRange(ResponseFileNoWarn().Matches(text).Select(static match => match.Value.Trim()));
		}

		return found;
	}

	private static IEnumerable<string> EnumerateScannedFiles()
	{
		Stack<string> directories = new([RepositoryLayout.Root]);
		while (directories.TryPop(out string? directory))
		{
			foreach (string child in Directory.EnumerateDirectories(directory))
			{
				if (!SkippedDirectories.Contains(Path.GetFileName(child)))
				{
					directories.Push(child);
				}
			}

			foreach (string file in Directory.EnumerateFiles(directory))
			{
				string extension = Path.GetExtension(file);
				if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) || MsBuildExtensions.Contains(extension) ||
					extension.Equals(".globalconfig", StringComparison.OrdinalIgnoreCase) ||
					extension.Equals(".rsp", StringComparison.OrdinalIgnoreCase) ||
					Path.GetFileName(file).Equals(".editorconfig", StringComparison.OrdinalIgnoreCase))
				{
					yield return file;
				}
			}
		}
	}

	[GeneratedRegex(@"^\[(?<id>CECLIENT\d{4})\]", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex IdPrefix();

	[GeneratedRegex(@"CECLIENT5\d{3}", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex ExperimentalId();

	[GeneratedRegex(@"^[ \t]*#[ \t]*pragma[ \t]+warning[ \t]+disable\b(?<ids>[^\r\n]*)",
		RegexOptions.CultureInvariant | RegexOptions.Multiline, RegexTimeoutMilliseconds)]
	private static partial Regex PragmaDisable();

	[GeneratedRegex(
		@"^[ \t]*\[[ \t]*(?:(?:assembly|module)[ \t]*:[ \t]*)?(?:global::)?(?:System\.Diagnostics\.CodeAnalysis\.)?(?:Unconditional)?SuppressMessage(?:Attribute)?[ \t]*\((?<arguments>[^)]*)\)",
		RegexOptions.CultureInvariant | RegexOptions.Multiline, RegexTimeoutMilliseconds)]
	private static partial Regex SuppressMessageAttribute();

	[GeneratedRegex(@"^[ \t]*dotnet_diagnostic\.CECLIENT5\d{3}\.severity[ \t]*=[^\r\n]*",
		RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex SeverityConfiguration();

	[GeneratedRegex(@"(?:^|\s)[-/]nowarn:[^\r\n]*CECLIENT5\d{3}",
		RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex ResponseFileNoWarn();

	/// <summary>One catalogued experimental diagnostic.</summary>
	/// <param name="Id">The diagnostic id, for example <c>CECLIENT5001</c>.</param>
	/// <param name="Scope">The APIs the id marks.</param>
	private sealed record ExperimentalDiagnostic(string Id, string Scope);

	/// <summary>One public type or member marked <c>[Experimental]</c>.</summary>
	/// <param name="Assembly">The simple name of the declaring assembly.</param>
	/// <param name="Name">The PublicAPI name of the type, or of the member with its declaring type.</param>
	/// <param name="Id">The diagnostic id.</param>
	/// <param name="UrlFormat">The documentation address format, if any.</param>
	private sealed record ExperimentalSymbol(string Assembly, string Name, string Id, string? UrlFormat);
}
