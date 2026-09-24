using System.Globalization;
using System.Text.RegularExpressions;

using CheatEngine.Client.Tests.Infrastructure;

using ReflectionAssembly = System.Reflection.Assembly;

namespace CheatEngine.Client.Tests.Architecture;

/// <summary>
///     The enum charter of the 1.x public surface: every public enum of a shipped Client assembly is backed by
///     <see cref="int" />, declares every value explicitly and defines zero; an outcome enum (a name ending in
///     <c>Kind</c>, <c>Status</c>, <c>State</c>, <c>Effect</c> or <c>Scope</c>) has <c>Unknown = 0</c>, so a value that
///     was never assigned never reads as an outcome.
/// </summary>
/// <remarks>
///     <para>
///         The enum types are read from the compiled assemblies; whether a value is explicit is read from its
///         declaration under <c>libs/</c> or <c>src/</c>.
///     </para>
///     <para>
///         <see cref="OptionEnums" /> documents the enums whose zero is a valid, documented choice instead of
///         <c>Unknown</c>; an outcome suffix on such an enum does not require <c>Unknown = 0</c>.
///         <see cref="PendingCharterEnums" /> lists the enums that break the charter today, each with the rule it breaks
///         and the lot that fixes it. The list may only shrink: fix the enum instead of adding it.
///     </para>
/// </remarks>
public sealed partial class OutcomeEnumConventionTests
{
	private const int RegexTimeoutMilliseconds = 1000;

	private static readonly string[] OutcomeSuffixes = ["Kind", "Status", "State", "Effect", "Scope"];

	/// <summary>Option enums: zero is a valid, documented choice, never <c>Unknown</c>.</summary>
	private static readonly Dictionary<string, string> OptionEnums = new(StringComparer.Ordinal)
	{
		["CheatEngine.Client.Allocations.TargetAllocationAccess"] =
			"ReadWrite (0) is the default protection of an allocation request.",
		["CheatEngine.Client.Memory.MemoryStringEncoding"] = "Utf8 (0) is the default encoding of a string request.",
		["CheatEngine.Client.Scanning.ScanAlignmentKind"] = "None (0) is the default alignment rule: every address is checked.",
		["CheatEngine.Client.Scanning.ScanProtectionRequirement"] =
			"Unspecified (0) leaves the flag out of the protection filter, the default of every flag."
	};

	/// <summary>Enums that break the charter today, with the rule they break and the lot that fixes them.</summary>
	private static readonly Dictionary<string, string> PendingCharterEnums = new(StringComparer.Ordinal)
	{
		["CheatEngine.Client.Allocations.TargetAllocationAccess"] =
			"values are implicit; replaced by AllocationProtection (L16)",
		["CheatEngine.Client.Scanning.ValueScanSessionState"] = "no Unknown = 0 (L15)"
	};

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

			bool isOutcome = OutcomeSuffixes.Any(suffix => type.Name.EndsWith(suffix, StringComparison.Ordinal));
			if (isOutcome && !OptionEnums.ContainsKey(type.FullName!) && zero != "Unknown")
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
