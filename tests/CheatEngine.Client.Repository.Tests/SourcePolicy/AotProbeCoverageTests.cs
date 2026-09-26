using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.SourcePolicy;

/// <summary>
///     The Native AOT probe calls the whole public surface of CheatEngine.Client.Fluent, rather than naming its types
///     with <c>typeof</c> only: the probe is published as a Native AOT executable and run by the delivery gate, so
///     every Fluent member it calls is compiled, reached and executed under Native AOT.
/// </summary>
/// <remarks>
///     <para>
///         The surface is read from the Fluent PublicAPI baselines, so a new public member fails this test until the
///         probe calls it. The check is textual, over a fixed layout of <c>AotProbeFluentCalls.cs</c> with its comments
///         removed: each public Fluent type has a <c>static bool Exercise&lt;Type&gt;(</c> method (generic arity
///         dropped) whose body holds at least as many <c>.Member</c> accesses as each member of that type has public
///         signatures; <c>Run</c> calls every <c>Exercise</c> method; and the probe's entry point returns 1 when
///         <c>AotProbeFluentCalls.Run()</c> returns <see langword="false" />.
///     </para>
///     <para>
///         Counting call sites cannot tell which overload a call binds to: two calls of one overload satisfy a member
///         with two signatures. The probe therefore keeps one call per overload, each with an argument of that
///         overload's type, and a review of <c>AotProbeFluentCalls.cs</c> checks it.
///     </para>
///     <para>
///         Whether each call returns what the probe's in-process fakes hold is checked by the probe itself, whose exit
///         code the delivery gate checks after the Native AOT publish.
///     </para>
/// </remarks>
public sealed partial class AotProbeCoverageTests
{
	private const string FluentCallsSource = "tests/CheatEngine.Client.AotProbe/AotProbeFluentCalls.cs";
	private const string ProbeEntryPoint = "tests/CheatEngine.Client.AotProbe/Program.cs";
	private const string FluentRunCall = "AotProbeFluentCalls.Run()";
	private const string ExercisePrefix = "Exercise";
	private const int RegexTimeoutMilliseconds = 1000;

	private static readonly string[] FluentBaselines =
	[
		"libs/CheatEngine.Client.Fluent/PublicAPI.Shipped.txt",
		"libs/CheatEngine.Client.Fluent/PublicAPI.Unshipped.txt"
	];

	/// <summary>A PublicAPI excerpt in the baseline format, with a short namespace.</summary>
	private static readonly string[] SampleBaseline =
	[
		"#nullable enable",
		"",
		"F.AobScanBuilder",
		"F.AobScanBuilder.AobScanBuilder() -> void",
		"F.AobScanBuilder.InModule(string! moduleName) -> F.AobScanBuilder",
		"F.AobScanBuilder.InModule(CheatEngine.SDK.Engine.Inspection.ModuleName module) -> F.AobScanBuilder",
		"F.AobScanBuilder.Pattern.get -> F.AobPattern",
		"F.AobFirstMatchBuilder",
		"F.AobFirstMatchBuilder.Execute(System.Threading.CancellationToken cancellationToken = default) -> A?",
		"F.AobSingleMatchBuilder",
		"F.AobSingleMatchBuilder.Execute(System.Threading.CancellationToken cancellationToken = default) -> A",
		"F.MemoryPrimitiveBatchBuilder<T>",
		"F.MemoryPrimitiveBatchBuilder<T>.Read(System.ReadOnlySpan<A> addresses) -> System.ImmutableArray<T>",
		"static F.CheatEngineMemoryFluentExtensions.Batch<T>(this F.IMemoryClient! memory) -> F.Batch<T>",
		"[CECLIENT5001]F.AobScanBuilder.Take(int maximumResults) -> F.AobManyMatchBuilder"
	];

	[Fact]
	public void TheProbeCallsEveryPublicFluentMember()
	{
		List<string> lines = [];
		foreach (string baseline in FluentBaselines)
		{
			lines.AddRange(File.ReadAllLines(Path.Combine(RepositoryRoot.Path, baseline)));
		}

		SortedDictionary<string, SortedDictionary<string, int>> surface = ReadSurface(lines);
		string source = File.ReadAllText(Path.Combine(RepositoryRoot.Path, FluentCallsSource));
		List<string> gaps = FindGaps(source, surface);

		Assert.True(surface.Count > 0, "The Fluent baselines declare no public type: the check would pass vacuously.");
		Assert.True(gaps.Count == 0,
			$"{FluentCallsSource} must call every public Fluent member under Native AOT:{Environment.NewLine}" +
			string.Join(Environment.NewLine, gaps));
	}

	[Fact]
	public void TheProbeEntryPointFailsWhenAFluentCallMisbehaves()
	{
		string entryPoint = File.ReadAllText(Path.Combine(RepositoryRoot.Path, ProbeEntryPoint));
		string guard = $"if (!{FluentRunCall})";
		int call = entryPoint.IndexOf(guard, StringComparison.Ordinal);

		Assert.True(call >= 0, $"{ProbeEntryPoint} must test the result of {FluentRunCall}.");
		string branch = entryPoint[(call + guard.Length)..].TrimStart();
		Assert.StartsWith("{", branch, StringComparison.Ordinal);
		Assert.StartsWith("return 1;", branch[1..].TrimStart(), StringComparison.Ordinal);
	}

	[Fact]
	public void TheSurfaceReaderCountsSignaturesAndSkipsTypeLinesAndConstructors()
	{
		SortedDictionary<string, SortedDictionary<string, int>> surface = ReadSurface(SampleBaseline);

		Assert.Equal(
			[
				"AobFirstMatchBuilder", "AobScanBuilder", "AobSingleMatchBuilder", "CheatEngineMemoryFluentExtensions",
				"MemoryPrimitiveBatchBuilder"
			],
			surface.Keys);
		Assert.Equal(["InModule", "Pattern", "Take"], surface["AobScanBuilder"].Keys);
		Assert.Equal(2, surface["AobScanBuilder"]["InModule"]);
		Assert.Equal(1, surface["AobScanBuilder"]["Pattern"]);
		Assert.Equal(1, surface["MemoryPrimitiveBatchBuilder"]["Read"]);
		Assert.Equal(1, surface["CheatEngineMemoryFluentExtensions"]["Batch"]);
	}

	[Fact]
	public void TheGapFinderReportsAMissingMethodCallSiteAndRunCallAndIgnoresComments()
	{
		SortedDictionary<string, SortedDictionary<string, int>> surface =
			ReadSurface(SampleBaseline.Where(static line => line.Contains("Aob", StringComparison.Ordinal)));
		const string Source = """
							  internal static bool Run()
							  {
							      return ExerciseAobScanBuilder(scanner) /* && ExerciseAobFirstMatchBuilder(scanner) */;
							  }

							  private static bool ExerciseAobScanBuilder(AotProbePatternScanner scanner)
							  {
							      AobScanBuilder scan = scanner.Aob("90").InModule("game.exe").Take(1);
							      // scan = scan.InModule(new ModuleName("game.exe"));
							      return scan.Pattern.IsWildcardOnly && scan.PatternLength == 1;
							  }

							  private static bool ExerciseAobFirstMatchBuilder(AotProbePatternScanner scanner)
							  {
							      return scanner.Aob("90").FirstOrNone().Execute() is null;
							  }
							  """;

		List<string> gaps = FindGaps(Source, surface);

		Assert.Equal(3, gaps.Count);
		Assert.Contains(gaps, static gap => gap.Contains("no 'static bool ExerciseAobSingleMatchBuilder('",
			StringComparison.Ordinal));
		Assert.Contains(gaps, static gap => gap.Contains("AobScanBuilder.InModule", StringComparison.Ordinal));
		Assert.Contains(gaps, static gap => gap == "Run does not call ExerciseAobFirstMatchBuilder.");
	}

	/// <summary>
	///     Reads the public types of a PublicAPI baseline and, per type, how many public signatures each member name
	///     has. Constructors are skipped (a value-type builder always has one); a property counts once.
	/// </summary>
	private static SortedDictionary<string, SortedDictionary<string, int>> ReadSurface(IEnumerable<string> lines)
	{
		SortedDictionary<string, SortedDictionary<string, int>> surface = new(StringComparer.Ordinal);
		HashSet<string> properties = new(StringComparer.Ordinal);
		foreach (string line in lines)
		{
			string entry = StripModifiers(line.Trim());
			if (entry.Length == 0 || entry.StartsWith('#'))
			{
				continue;
			}

			int arrow = entry.IndexOf(" -> ", StringComparison.Ordinal);
			string signature = arrow < 0 ? entry : entry[..arrow];
			int parameters = signature.IndexOf('(', StringComparison.Ordinal);
			bool isProperty = parameters < 0 && (signature.EndsWith(".get", StringComparison.Ordinal)
												 || signature.EndsWith(".set", StringComparison.Ordinal)
												 || signature.EndsWith(".init", StringComparison.Ordinal));
			string path = signature;
			if (parameters >= 0)
			{
				path = signature[..parameters];
			}
			else if (isProperty)
			{
				path = signature[..signature.LastIndexOf('.')];
			}

			string[] segments = SplitOutsideTypeArguments(path);
			if (parameters < 0 && !isProperty)
			{
				_ = GetMembers(surface, WithoutTypeArguments(segments[^1]));
				continue;
			}

			string type = WithoutTypeArguments(segments[^2]);
			string member = WithoutTypeArguments(segments[^1]);
			if (member == type || (isProperty && !properties.Add($"{type}.{member}")))
			{
				continue;
			}

			SortedDictionary<string, int> members = GetMembers(surface, type);
			members[member] = members.GetValueOrDefault(member) + 1;
		}

		return surface;
	}

	/// <summary>Lists each missing <c>Exercise</c> method, call site, or <c>Run</c> call of the probe.</summary>
	/// <remarks>
	///     Comments are removed first, a <c>//</c> or <c>/*</c> inside a string literal included: removing too much can
	///     only report a gap that does not exist, never hide one.
	/// </remarks>
	private static List<string> FindGaps(string source, SortedDictionary<string, SortedDictionary<string, int>> surface)
	{
		List<string> gaps = [];
		source = Comments().Replace(source, string.Empty);
		string? run = MethodBody(source, "Run");
		foreach ((string type, SortedDictionary<string, int> members) in surface)
		{
			string exercise = ExercisePrefix + type;
			if (MethodBody(source, exercise) is not { } body)
			{
				gaps.Add($"There is no 'static bool {exercise}(' method for the public Fluent type {type}.");
				continue;
			}

			if (run is null || !run.Contains(exercise + "(", StringComparison.Ordinal))
			{
				gaps.Add($"Run does not call {exercise}.");
			}

			foreach ((string member, int signatures) in members)
			{
				int calls = CountMemberAccesses(body, member);
				if (calls < signatures)
				{
					gaps.Add($"{exercise} has {calls} '.{member}' call site(s) for {type}.{member}, which has " +
							 $"{signatures} public signature(s): call each one.");
				}
			}
		}

		return gaps;
	}

	private static SortedDictionary<string, int> GetMembers(
		SortedDictionary<string, SortedDictionary<string, int>> surface, string type)
	{
		if (!surface.TryGetValue(type, out SortedDictionary<string, int>? members))
		{
			members = new SortedDictionary<string, int>(StringComparer.Ordinal);
			surface.Add(type, members);
		}

		return members;
	}

	/// <summary>Removes the experimental diagnostic prefix and the modifiers before a PublicAPI signature.</summary>
	private static string StripModifiers(string entry)
	{
		string stripped = entry.StartsWith('[') ? entry[(entry.IndexOf(']', StringComparison.Ordinal) + 1)..] : entry;
		string[] modifiers = ["~", "static ", "abstract ", "virtual ", "override ", "const ", "readonly ", "sealed "];
		bool removed = true;
		while (removed)
		{
			removed = false;
			foreach (string modifier in modifiers)
			{
				if (stripped.StartsWith(modifier, StringComparison.Ordinal))
				{
					stripped = stripped[modifier.Length..];
					removed = true;
				}
			}
		}

		return stripped;
	}

	/// <summary>Splits a dotted name at the dots that are not inside a type argument list.</summary>
	private static string[] SplitOutsideTypeArguments(string path)
	{
		List<string> segments = [];
		int depth = 0;
		int start = 0;
		for (int index = 0; index < path.Length; index++)
		{
			switch (path[index])
			{
				case '<':
					depth++;
					break;
				case '>':
					depth--;
					break;
				case '.' when depth == 0:
					segments.Add(path[start..index]);
					start = index + 1;
					break;
			}
		}

		segments.Add(path[start..]);
		return [.. segments];
	}

	private static string WithoutTypeArguments(string name)
	{
		int typeArguments = name.IndexOf('<', StringComparison.Ordinal);
		return typeArguments < 0 ? name : name[..typeArguments];
	}

	/// <summary>Returns the body of the first <c>static bool</c> method with this name, or null.</summary>
	private static string? MethodBody(string source, string name)
	{
		int declaration = source.IndexOf($"static bool {name}(", StringComparison.Ordinal);
		int open = declaration < 0 ? -1 : source.IndexOf('{', declaration);
		if (open < 0)
		{
			return null;
		}

		int depth = 0;
		for (int index = open; index < source.Length; index++)
		{
			depth += source[index] switch
			{
				'{' => 1,
				'}' => -1,
				_ => 0
			};
			if (depth == 0)
			{
				return source[(open + 1)..index];
			}
		}

		return null;
	}

	/// <summary>Counts the <c>.member</c> accesses whose name is not the prefix of a longer identifier.</summary>
	private static int CountMemberAccesses(string body, string member)
	{
		int count = 0;
		string access = "." + member;
		for (int index = body.IndexOf(access, StringComparison.Ordinal); index >= 0;
			 index = body.IndexOf(access, index + access.Length, StringComparison.Ordinal))
		{
			int next = index + access.Length;
			if (next == body.Length || !(char.IsLetterOrDigit(body[next]) || body[next] == '_'))
			{
				count++;
			}
		}

		return count;
	}

	/// <summary>A C# line comment or delimited comment.</summary>
	[GeneratedRegex(@"//[^\r\n]*|/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant,
		RegexTimeoutMilliseconds)]
	private static partial Regex Comments();
}
