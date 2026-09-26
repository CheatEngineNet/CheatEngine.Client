using System.Text;
using System.Text.RegularExpressions;

using CheatEngine.Client.Repository.Tests.Infrastructure;

namespace CheatEngine.Client.Repository.Tests.Packaging;

/// <summary>
/// The shipped sources and the template never describe a CheatEngine.SDK the Client no longer consumes, nor a move to
/// the one it already consumes: no "SDK 1.0.0", no "SDK 2.0" owners still to come, no migration guide, and no promise
/// of what happens "when the Client migrates" or "until the SDK 2.0". The phrases are found across wrapped lines and
/// comment markers, in every text file of <c>libs/</c>, <c>src/</c>, <c>source-generators/</c> and <c>templates/</c>.
/// </summary>
public sealed partial class NoStaleSdkWordingTests
{
	private const int RegexTimeoutMilliseconds = 1000;

	private static readonly (string Phrase, Regex Pattern)[] StalePhrases =
	[
		("SDK 1.0.0", RetiredSdkVersion()),
		("SDK 2.0 ... owners", FutureSdkOwners()),
		("migration guide", MigrationGuide()),
		("when the Client migrates", WhenTheClientMigrates()),
		("until the SDK 2.0", UntilTheSdk())
	];

	[Fact]
	public void ShippedSourcesAndTheTemplateUseNoStaleSdkWording()
	{
		List<string> offenders = [];
		int scanned = 0;
		foreach (string file in RepositoryRoot.EnumerateSourceFiles("*").Order(StringComparer.Ordinal))
		{
			if (!SdkPinTests.IsShippedSource(file) || file.EndsWith("/packages.lock.json", StringComparison.Ordinal) ||
				!SdkPinTests.IsTextFile(file))
			{
				continue;
			}

			scanned++;
			string[] lines = File.ReadAllLines(Path.Combine(RepositoryRoot.Path, file));
			offenders.AddRange(FindStaleWording(lines).Select(finding => $"{file}:{finding.Line} → {finding.Phrase}"));
		}

		Assert.True(scanned >= 100, $"Expected the shipped sources, READMEs and template files, found {scanned}.");
		Assert.True(offenders.Count == 0,
			"State what the consumed CheatEngine.SDK (eng/CheatEngineSdk.props) does today; a wording about another " +
			"SDK version or a pending migration is stale:" + Environment.NewLine +
			string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void TheDetectorFindsEveryStaleFormAcrossWrappedCommentsAndSparesTheCurrentWording()
	{
		string[] stale =
		[
			"/// <summary>Consumes CheatEngine.SDK",
			"/// 1.0.0 through a boolean port.</summary>",
			"\t// Unavailable until the SDK 2.0 hotkey and timer",
			"\t// owners are adopted.",
			"See the Migration Guide.",
			"<!-- Kept when the Client migrates to a new major. -->"
		];
		string[] current =
		[
			"/// CheatEngine.SDK 2.0.0 <c>Owned&lt;T&gt;.ReleaseWithOutcome</c> always consumes the owner.",
			"Neighbours built on CheatEngine.SDK 1.x keep their own bridge; owners stay theirs.",
			"CheatEngine.SDK 2.0.0's `LuaOptional<T>` lets a binding omit a trailing argument.",
			"Moving to another major is a deliberate migration, not a dependency bump."
		];

		Assert.Equal(
		[
			(1, "SDK 1.0.0"),
			(3, "SDK 2.0 ... owners"),
			(3, "until the SDK 2.0"),
			(5, "migration guide"),
			(6, "when the Client migrates")
		], FindStaleWording(stale));
		Assert.Empty(FindStaleWording(current));
	}

	/// <summary>
	/// The 1-based line on which each stale phrase starts. Leading whitespace and comment markers are removed and the
	/// lines are joined with one space, so a phrase that a comment wraps over two lines is still found.
	/// </summary>
	private static List<(int Line, string Phrase)> FindStaleWording(string[] lines)
	{
		StringBuilder text = new();
		List<int> lineStarts = [];
		foreach (string line in lines)
		{
			lineStarts.Add(text.Length);
			text.Append(CommentPrefix().Replace(line, string.Empty).TrimEnd()).Append(' ');
		}

		string flat = text.ToString();
		List<(int Line, string Phrase)> findings = [];
		foreach ((string phrase, Regex pattern) in StalePhrases)
		{
			foreach (Match match in pattern.Matches(flat))
			{
				int line = lineStarts.BinarySearch(match.Index);
				findings.Add((line >= 0 ? line + 1 : ~line, phrase));
			}
		}

		return
		[
			.. findings.OrderBy(static finding => finding.Line)
				.ThenBy(static finding => finding.Phrase, StringComparer.Ordinal)
		];
	}

	/// <summary>Leading whitespace and a C#, XML documentation, block, shell or HTML comment marker.</summary>
	[GeneratedRegex(@"^\s*(?:///|//|/\*+|\*/|\*|\#|<!--)?\s*", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex CommentPrefix();

	[GeneratedRegex(@"\bSDK[`""]?\s+1\.0\.0\b", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex RetiredSdkVersion();

	/// <summary>"SDK 2.0" (not the 2.0.0 package) followed, in the same sentence, by owners it would bring.</summary>
	[GeneratedRegex(@"\bSDK[`""]?\s+2\.0(?![.\d])[^.]{0,100}?\bowners\b", RegexOptions.CultureInvariant,
		RegexTimeoutMilliseconds)]
	private static partial Regex FutureSdkOwners();

	[GeneratedRegex(@"\bmigration\s+guides?\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
		RegexTimeoutMilliseconds)]
	private static partial Regex MigrationGuide();

	[GeneratedRegex(@"\bwhen\s+the\s+Client\s+migrates\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
		RegexTimeoutMilliseconds)]
	private static partial Regex WhenTheClientMigrates();

	[GeneratedRegex(@"\buntil\s+the\s+(?:CheatEngine\.)?SDK[`""]?\s+2\.0\b",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex UntilTheSdk();
}
