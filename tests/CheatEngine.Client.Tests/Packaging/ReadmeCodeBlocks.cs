using System.Text.RegularExpressions;

namespace CheatEngine.Client.Tests.Packaging;

/// <summary>One fenced C# block of a README.</summary>
/// <param name="Line">The 1-based line of the opening fence.</param>
/// <param name="NoCompile">
///     Whether the block is marked <c>csharp nocompile</c>, with a written reason next to it.
/// </param>
/// <param name="Code">The code between the fences.</param>
internal sealed record ReadmeCodeBlock(int Line, bool NoCompile, string Code);

/// <summary>The C# blocks of a README and every rule it breaks.</summary>
internal sealed record ReadmeCodeBlockSet(IReadOnlyList<ReadmeCodeBlock> Blocks, IReadOnlyList<string> Problems);

/// <summary>
///     Reads the fenced C# code blocks of a Markdown file, as CommonMark delimits them: a fence of at least three
///     backticks or tildes, indented by at most three spaces, closed by a fence of the same character that is at least
///     as long. A C# block is labelled <c>csharp</c>, so that <see cref="ReadmeSnippetCompilationTests" /> compiles it.
///     A block that deliberately does not compile is labelled <c>csharp nocompile</c>, and the line right above its
///     opening fence gives the reason: <c>&lt;!-- nocompile: the reason --&gt;</c>.
/// </summary>
internal static partial class ReadmeCodeBlocks
{
	/// <summary>The info-string word that exempts a C# block from compilation.</summary>
	internal const string NoCompileWord = "nocompile";

	private const string CSharpLanguage = "csharp";
	private const int RegexTimeoutMilliseconds = 1000;

	/// <summary>The labels of a C# block that the compilation test would not see.</summary>
	private static readonly string[] OtherCSharpLabels = ["cs", "c#", "c-sharp"];

	/// <summary>Parses <paramref name="markdown" />.</summary>
	internal static ReadmeCodeBlockSet Parse(string markdown)
	{
		ArgumentNullException.ThrowIfNull(markdown);
		string[] lines = markdown.ReplaceLineEndings("\n").Split('\n');
		List<ReadmeCodeBlock> blocks = [];
		List<string> problems = [];
		int index = 0;
		while (index < lines.Length)
		{
			Match opening = OpeningFence().Match(lines[index]);
			if (!opening.Success)
			{
				index++;
				continue;
			}

			string fence = opening.Groups["fence"].Value;
			string[] info = opening.Groups["info"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			int closing = FindClosingFence(lines, index + 1, fence);
			int line = index + 1;
			if (closing < 0)
			{
				problems.Add($"line {line}: the code block opened here is never closed.");
				break;
			}

			string language = info.Length > 0 ? info[0] : string.Empty;
			if (OtherCSharpLabels.Contains(language, StringComparer.OrdinalIgnoreCase))
			{
				problems.Add(
					$"line {line}: label a C# block '{CSharpLanguage}', not '{language}', so that it is compiled.");
			}
			else if (string.Equals(language, CSharpLanguage, StringComparison.OrdinalIgnoreCase))
			{
				bool noCompile = false;
				foreach (string word in info.Skip(1))
				{
					if (string.Equals(word, NoCompileWord, StringComparison.Ordinal))
					{
						noCompile = true;
					}
					else
					{
						problems.Add(
							$"line {line}: unknown code block option '{word}' (only '{NoCompileWord}' exists).");
					}
				}

				if (noCompile && !HasNoCompileReason(lines, index))
				{
					problems.Add($"line {line}: a '{CSharpLanguage} {NoCompileWord}' block needs its reason on the " +
								 $"line right above it: <!-- {NoCompileWord}: the reason -->.");
				}

				blocks.Add(new ReadmeCodeBlock(line, noCompile, string.Join('\n', lines[(index + 1)..closing])));
			}

			index = closing + 1;
		}

		return new ReadmeCodeBlockSet(blocks, problems);
	}

	private static int FindClosingFence(string[] lines, int start, string fence)
	{
		for (int index = start; index < lines.Length; index++)
		{
			Match closing = ClosingFence().Match(lines[index]);
			if (closing.Success && closing.Groups["fence"].Value[0] == fence[0] &&
				closing.Groups["fence"].Value.Length >= fence.Length)
			{
				return index;
			}
		}

		return -1;
	}

	private static bool HasNoCompileReason(string[] lines, int fenceIndex)
	{
		return fenceIndex > 0 && NoCompileReason().IsMatch(lines[fenceIndex - 1]);
	}

	[GeneratedRegex(@"^ {0,3}(?<fence>`{3,}|~{3,})[ \t]*(?<info>[^`]*?)[ \t]*$", RegexOptions.CultureInvariant,
		RegexTimeoutMilliseconds)]
	private static partial Regex OpeningFence();

	[GeneratedRegex(@"^ {0,3}(?<fence>`{3,}|~{3,})[ \t]*$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex ClosingFence();

	[GeneratedRegex(@"^\s*<!--\s*nocompile:\s*\S.*?-->\s*$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex NoCompileReason();
}
