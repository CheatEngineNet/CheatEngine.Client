using System.Globalization;
using System.Text.RegularExpressions;

using CheatEngine.Client.Tests.Infrastructure;

using ReflectionAssembly = System.Reflection.Assembly;

namespace CheatEngine.Client.Tests.Architecture;

/// <summary>
///     The enum charter of the 1.x public surface: every public enum of a shipped Client assembly is backed by
///     <see cref="int" />, declares every value explicitly and defines zero, and is exactly one of two kinds. An outcome
///     enum reports what happened or was observed: its name ends in <c>Kind</c>, <c>Status</c>, <c>State</c>,
///     <c>Effect</c> or <c>Scope</c> (or it is listed in <see cref="OutcomeEnumsWithoutSuffix" />) and it has
///     <c>Unknown = 0</c>, so a value that was never assigned never reads as an outcome. An option enum is the caller's
///     choice: it is listed in <see cref="OptionEnums" />, its zero is a valid default choice and its name never ends in
///     an outcome suffix. No enum is named <c>*Outcome</c>: that suffix names a result object.
/// </summary>
/// <remarks>
///     <para>
///         The enum types are read from the compiled assemblies; whether a value is explicit is read from its
///         declaration under <c>libs/</c> or <c>src/</c>. The classification is total: a new enum that is neither
///         fails until it is named or listed.
///     </para>
///     <para>
///         <see cref="PendingCharterEnums" /> lists the enums that break the charter today, each with the rule it breaks
///         and the lot that fixes it. It is empty and may only stay so: fix the enum instead of adding it.
///     </para>
/// </remarks>
public sealed partial class OutcomeEnumConventionTests
{
	private const int RegexTimeoutMilliseconds = 1000;

	private static readonly string[] OutcomeSuffixes = ["Kind", "Status", "State", "Effect", "Scope"];

	/// <summary>Outcome enums whose name does not end in an outcome suffix, with why the name stays.</summary>
	private static readonly Dictionary<string, string> OutcomeEnumsWithoutSuffix = new(StringComparer.Ordinal)
	{
		["CheatEngine.Client.Runtime.ClientCapabilityEvidenceReasonCode"] =
			"EffectiveReasonCode pairs with EffectiveReason: the code and the text of the same reason.",
		["CheatEngine.Client.Scanning.PatternScanRouteReason"] =
			"RouteReason says why a pattern scan took its route; a reason is not a kind of route."
	};

	/// <summary>Option enums: zero is a valid, documented choice, never <c>Unknown</c>.</summary>
	private static readonly Dictionary<string, string> OptionEnums = new(StringComparer.Ordinal)
	{
		["CheatEngine.Client.Allocations.AllocationProtection"] =
			"ReadWrite (0) is the default protection of an allocation request.",
		["CheatEngine.Client.Assembly.InstructionEncodingPreference"] =
			"None (0) lets Cheat Engine choose the encoding of an assembled instruction.",
		["CheatEngine.Client.Inspection.AddressResolutionMode"] =
			"Default (0) is Cheat Engine's ordinary address resolution.",
		["CheatEngine.Client.Memory.MemoryStringEncoding"] = "Utf8 (0) is the default encoding of a string request.",
		["CheatEngine.Client.Scanning.ScanAlignmentMode"] =
			"None (0) is the default alignment rule: every address is checked.",
		["CheatEngine.Client.Scanning.ScanProtectionRequirement"] =
			"Unspecified (0) leaves the flag out of the protection filter, the default of every flag.",
		["CheatEngine.Client.Scanning.ValueScanComparison"] =
			"Exact (0) is a valid comparison; a request names its comparison through its factory.",
		["CheatEngine.Client.Scanning.ValueScanValueType"] =
			"Integer8 (0) is a valid value type; a request takes it from its value or its factory."
	};

	/// <summary>Enums that break the charter today, with the rule they break and the lot that fixes them.</summary>
	private static readonly Dictionary<string, string> PendingCharterEnums = new(StringComparer.Ordinal);

	[Fact]
	public void PublicEnumsFollowTheCharterOrAreStillPending()
	{
		Dictionary<string, List<string>> violations = FindViolations();

		string[] added =
		[
			.. violations.Keys.Except(PendingCharterEnums.Keys, StringComparer.Ordinal)
				.Order(StringComparer.Ordinal)
				.Select(name => $"{name}: {string.Join("; ", violations[name])}")
		];
		string[] resolved =
			[.. PendingCharterEnums.Keys.Except(violations.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal)];
		Assert.True(added.Length == 0,
			"Follow the enum charter (int, explicit values, a zero value, Unknown = 0 on an outcome enum) instead of " +
			"adding to PendingCharterEnums:" + Environment.NewLine + string.Join(Environment.NewLine, added));
		Assert.True(resolved.Length == 0,
			"These enums follow the charter now; remove them from PendingCharterEnums: " + string.Join(", ", resolved));
	}

	[Fact]
	public void TheCharterInspectsEveryShippedPublicEnum()
	{
		string[] names = [.. PublicEnums().Select(static type => type.FullName!)];

		// The results vocabulary must be among the inspected enums, or the charter would pass vacuously.
		Assert.Contains("CheatEngine.Client.Results.CheatEngineFailureKind", names);
		Assert.Contains("CheatEngine.Client.Results.CheatEngineHostEffect", names);
		Assert.Contains("CheatEngine.Client.Results.LeaseReleaseKind", names);
		Assert.All(PendingCharterEnums.Keys, name => Assert.Contains(name, names));
	}

	[Fact]
	public void PendingCharterEnumsIsEmpty()
	{
		Assert.Empty(PendingCharterEnums);
	}

	[Fact]
	public void OutcomeEnumsWithoutSuffixExistAndDefineUnknownZero()
	{
		Dictionary<string, Type> enums = PublicEnums().ToDictionary(static type => type.FullName!,
			StringComparer.Ordinal);

		foreach (string name in OutcomeEnumsWithoutSuffix.Keys)
		{
			Assert.True(enums.TryGetValue(name, out Type? type), $"The outcome enum {name} is not a shipped public enum.");
			Assert.Equal("Unknown", Enum.GetName(type, Enum.ToObject(type, 0)));
		}
	}

	[Fact]
	public void OptionEnumsExistAndDefineAValidZero()
	{
		Dictionary<string, Type> enums = PublicEnums().ToDictionary(static type => type.FullName!,
			StringComparer.Ordinal);

		foreach (string name in OptionEnums.Keys)
		{
			Assert.True(enums.TryGetValue(name, out Type? type), $"The option enum {name} is not a shipped public enum.");
			string? zero = Enum.GetName(type, Enum.ToObject(type, 0));
			Assert.False(zero is null, $"The option enum {name} defines no zero value.");
			Assert.NotEqual("Unknown", zero);
		}
	}

	/// <summary>Finds every public enum that breaks the charter, with the rules it breaks.</summary>
	private static Dictionary<string, List<string>> FindViolations()
	{
		Dictionary<string, string> sources = ReadEnumDeclarations();
		Dictionary<string, List<string>> violations = new(StringComparer.Ordinal);
		foreach (Type type in PublicEnums())
		{
			List<string> rules = [];
			Type underlying = Enum.GetUnderlyingType(type);
			if (underlying != typeof(int))
			{
				rules.Add($"backed by {underlying.Name}, not Int32");
			}

			if (!sources.TryGetValue(type.Name, out string? body))
			{
				rules.Add("declaration not found under libs/ or src/");
			}
			else
			{
				string[] implicitValues =
				[
					.. Enum.GetNames(type).Where(name =>
						!Regex.IsMatch(body, $@"\b{Regex.Escape(name)}\s*=", RegexOptions.CultureInvariant,
							TimeSpan.FromMilliseconds(RegexTimeoutMilliseconds)))
				];
				if (implicitValues.Length != 0)
				{
					rules.Add($"implicit value for {string.Join(", ", implicitValues)}");
				}
			}

			string? zero = Enum.GetName(type, Enum.ToObject(type, 0));
			if (zero is null)
			{
				rules.Add("no zero value");
			}

			bool hasOutcomeSuffix =
				OutcomeSuffixes.Any(suffix => type.Name.EndsWith(suffix, StringComparison.Ordinal));
			bool isOption = OptionEnums.ContainsKey(type.FullName!);
			bool isOutcome = hasOutcomeSuffix || OutcomeEnumsWithoutSuffix.ContainsKey(type.FullName!);
			if (type.Name.EndsWith("Outcome", StringComparison.Ordinal))
			{
				rules.Add("named *Outcome, which names a result object, never an enum");
			}

			if (isOption && isOutcome)
			{
				rules.Add("listed as an option enum, but its name or listing makes it an outcome enum");
			}
			else if (!isOption && !isOutcome)
			{
				rules.Add("neither an outcome enum (outcome suffix, Unknown = 0) nor a listed option enum");
			}
			else if (isOutcome && zero != "Unknown")
			{
				rules.Add(Enum.GetNames(type).Contains("Unknown", StringComparer.Ordinal)
					? $"Unknown is {Convert.ToInt64(Enum.Parse(type, "Unknown"), CultureInfo.InvariantCulture)}, not 0"
					: "no Unknown = 0 on an outcome enum");
			}

			if (rules.Count != 0)
			{
				violations.Add(type.FullName!, rules);
			}
		}

		return violations;
	}

	private static IEnumerable<Type> PublicEnums()
	{
		foreach (ReflectionAssembly assembly in ClientAssemblyCatalog.LoadAll())
		{
			foreach (Type type in assembly.GetExportedTypes())
			{
				if (type.IsEnum)
				{
					yield return type;
				}
			}
		}
	}

	/// <summary>Reads the body of every enum declared under <c>libs/</c> and <c>src/</c>, keyed by simple name.</summary>
	private static Dictionary<string, string> ReadEnumDeclarations()
	{
		Dictionary<string, string> bodies = new(StringComparer.Ordinal);
		foreach (string root in (string[]) ["libs", "src"])
		{
			string directory = RepositoryLayout.Combine(root);
			foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
			{
				string relative = Path.GetRelativePath(directory, file);
				if (relative.Split(Path.DirectorySeparatorChar).Any(static part => part is "bin" or "obj"))
				{
					continue;
				}

				string text = LineComment().Replace(File.ReadAllText(file), string.Empty);
				foreach (Match declaration in EnumDeclaration().Matches(text))
				{
					bodies[declaration.Groups["name"].Value] = declaration.Groups["body"].Value;
				}
			}
		}

		return bodies;
	}

	[GeneratedRegex(@"\benum\s+(?<name>\w+)(?:\s*:\s*\w+)?\s*\{(?<body>[^}]*)\}", RegexOptions.CultureInvariant,
		RegexTimeoutMilliseconds)]
	private static partial Regex EnumDeclaration();

	[GeneratedRegex(@"//[^\r\n]*", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex LineComment();
}
