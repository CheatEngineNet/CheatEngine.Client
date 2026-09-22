using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CheatEngine.Client.Repository.Tests.Documentation;

/// <summary>The Markdown construct a link target was read from.</summary>
internal enum MarkdownLinkKind
{
	/// <summary>An inline link, <c>[text](target)</c>.</summary>
	Inline,

	/// <summary>An inline image, <c>![alt](target)</c>.</summary>
	Image,

	/// <summary>A reference definition, <c>[label]: target</c>.</summary>
	ReferenceDefinition,

	/// <summary>An HTML <c>href</c> or <c>src</c> attribute.</summary>
	HtmlAttribute
}

/// <summary>One link target, with the 1-based line where its target starts.</summary>
internal sealed record MarkdownLink(int Line, MarkdownLinkKind Kind, string Text, string Target);

/// <summary>A text match, with its 1-based line.</summary>
internal sealed record MarkdownMatch(int Line, string Text);

/// <summary>
/// A dependency-free subset of CommonMark and GitHub Flavored Markdown that is enough to check links: fenced code
/// blocks, code spans and HTML comments are opaque; inline links and images (including multi-line link text and one
/// nested image, as in badges), reference definitions, HTML <c>href</c>/<c>src</c> attributes, ATX headings and
/// explicit <c>&lt;a id&gt;</c> anchors are extracted. Indented code blocks and setext headings are not recognized:
/// list continuations use indentation, and the repository writes ATX headings only.
/// </summary>
internal sealed partial class MarkdownDocument
{
	private const int RegexTimeoutMilliseconds = 1000;

	private readonly bool[] _fenced;
	private readonly int[] _lineStarts;
	private readonly string _text;

	private MarkdownDocument(string path, string text)
	{
		Path = path;
		_text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
		Lines = _text.Split('\n');
		_lineStarts = ComputeLineStarts(_text);
		_fenced = FindFencedLines(Lines);

		char[] masked = _text.ToCharArray();
		MaskFencedLines(masked);
		MaskHtmlComments(masked);
		List<(int Start, int End)> paragraphs = FindParagraphs(masked);
		foreach ((int start, int end) in paragraphs)
		{
			MaskCodeSpans(masked, start, end);
		}

		string maskedText = new(masked);
		List<MarkdownLink> links = [];
		foreach ((int start, int end) in paragraphs)
		{
			ExtractInlineLinks(maskedText, start, end, links);
		}

		ExtractReferenceDefinitions(maskedText, links);
		ExtractHtmlAttributes(maskedText, links);
		// A stable order: inline links of one line keep their source order (a badge lists its link before its image).
		Links = [.. links.OrderBy(static link => link.Line)];

		(List<string> headings, HashSet<string> anchors) = ExtractHeadingsAndAnchors(maskedText);
		Headings = headings;
		Anchors = anchors;
	}

	/// <summary>The repository-relative path of the document, with forward slashes.</summary>
	internal string Path
	{
		get;
	}

	/// <summary>The raw lines, without line terminators.</summary>
	internal IReadOnlyList<string> Lines
	{
		get;
	}

	/// <summary>Every link target outside opaque regions, in line order.</summary>
	internal IReadOnlyList<MarkdownLink> Links
	{
		get;
	}

	/// <summary>The text of every ATX heading, in document order.</summary>
	internal IReadOnlyList<string> Headings
	{
		get;
	}

	/// <summary>The GitHub anchors of the headings, including <c>-1</c>/<c>-2</c> duplicates, and explicit anchors.</summary>
	internal IReadOnlySet<string> Anchors
	{
		get;
	}

	/// <summary>Parses a document held in memory.</summary>
	internal static MarkdownDocument Parse(string path, string text)
	{
		ArgumentNullException.ThrowIfNull(path);
		ArgumentNullException.ThrowIfNull(text);
		return new MarkdownDocument(path, text);
	}

	/// <summary>
	/// Computes the anchor GitHub generates for a heading: inline markup is reduced to its text, the result is
	/// lower-cased, every space becomes <c>-</c>, letters, digits, marks, <c>-</c> and <c>_</c> are kept, and every
	/// other punctuation or symbol character is dropped.
	/// </summary>
	internal static string Slug(string headingText)
	{
		ArgumentNullException.ThrowIfNull(headingText);

		string text = InlineImagePattern().Replace(headingText, "${text}");
		text = InlineLinkPattern().Replace(text, "${text}");
		text = HtmlTagPattern().Replace(text, string.Empty);
		text = text.Replace("`", string.Empty, StringComparison.Ordinal).Trim();

		StringBuilder slug = new(text.Length);
		foreach (char character in text.ToLowerInvariant())
		{
			UnicodeCategory category = char.GetUnicodeCategory(character);
			if (character is '-' or '_' || char.IsLetterOrDigit(character)
				|| category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
			{
				slug.Append(character);
			}
			else if (character == ' ')
			{
				slug.Append('-');
			}
		}

		return slug.ToString();
	}

	/// <summary>The lines outside fenced code blocks, with their 1-based numbers.</summary>
	internal IEnumerable<MarkdownMatch> ProseLines()
	{
		for (int index = 0; index < Lines.Count; index++)
		{
			if (!_fenced[index])
			{
				yield return new MarkdownMatch(index + 1, Lines[index]);
			}
		}
	}

	/// <summary>
	/// Finds absolute paths of a developer machine in the raw text, fenced code included: drive-letter paths, <c>file:</c>
	/// URIs and user-profile roots. A drive path that starts with one of <paramref name="illustrativeRoots"/> is allowed,
	/// and so are environment placeholders such as <c>%APPDATA%\x</c>.
	/// </summary>
	internal IReadOnlyList<MarkdownMatch> FindLocalPaths(IReadOnlyList<string> illustrativeRoots)
	{
		ArgumentNullException.ThrowIfNull(illustrativeRoots);

		List<MarkdownMatch> matches = [];
		foreach (Match match in LocalPathPattern().Matches(_text))
		{
			if (match.Groups["drive"].Success && StartsWithAny(_text, match.Index, illustrativeRoots))
			{
				continue;
			}

			matches.Add(new MarkdownMatch(LineOf(match.Index), Lines[LineOf(match.Index) - 1].Trim()));
		}

		return matches;
	}

	private static bool StartsWithAny(string text, int index, IReadOnlyList<string> prefixes)
	{
		foreach (string prefix in prefixes)
		{
			if (string.CompareOrdinal(text, index, prefix, 0, prefix.Length) == 0)
			{
				return true;
			}
		}

		return false;
	}

	private static int[] ComputeLineStarts(string text)
	{
		List<int> starts = [0];
		for (int index = 0; index < text.Length; index++)
		{
			if (text[index] == '\n')
			{
				starts.Add(index + 1);
			}
		}

		return [.. starts];
	}

	private static bool[] FindFencedLines(IReadOnlyList<string> lines)
	{
		bool[] fenced = new bool[lines.Count];
		char fenceCharacter = '\0';
		int fenceLength = 0;
		for (int index = 0; index < lines.Count; index++)
		{
			if (fenceLength == 0)
			{
				Match opening = FenceOpeningPattern().Match(lines[index]);
				if (opening.Success && !(opening.Groups["fence"].Value[0] == '`' && opening.Groups["info"].Value.Contains('`', StringComparison.Ordinal)))
				{
					fenceCharacter = opening.Groups["fence"].Value[0];
					fenceLength = opening.Groups["fence"].Value.Length;
					fenced[index] = true;
				}

				continue;
			}

			fenced[index] = true;
			Match closing = FenceClosingPattern().Match(lines[index]);
			if (closing.Success && closing.Groups["fence"].Value[0] == fenceCharacter
								 && closing.Groups["fence"].Value.Length >= fenceLength)
			{
				fenceLength = 0;
			}
		}

		return fenced;
	}

	private static void Mask(char[] text, int start, int end)
	{
		for (int index = start; index < end; index++)
		{
			if (text[index] != '\n')
			{
				text[index] = ' ';
			}
		}
	}

	private void MaskFencedLines(char[] text)
	{
		for (int line = 0; line < _fenced.Length; line++)
		{
			if (_fenced[line])
			{
				Mask(text, _lineStarts[line], _lineStarts[line] + Lines[line].Length);
			}
		}
	}

	private static void MaskHtmlComments(char[] text)
	{
		string current = new(text);
		int start = current.IndexOf("<!--", StringComparison.Ordinal);
		while (start >= 0)
		{
			int close = current.IndexOf("-->", start + 4, StringComparison.Ordinal);
			int end = close < 0 ? current.Length : close + 3;
			Mask(text, start, end);
			start = end < current.Length ? current.IndexOf("<!--", end, StringComparison.Ordinal) : -1;
		}
	}

	private List<(int Start, int End)> FindParagraphs(char[] text)
	{
		List<(int Start, int End)> paragraphs = [];
		int? start = null;
		for (int line = 0; line < Lines.Count; line++)
		{
			int lineStart = _lineStarts[line];
			int lineEnd = lineStart + Lines[line].Length;
			bool blank = true;
			for (int index = lineStart; index < lineEnd; index++)
			{
				if (!char.IsWhiteSpace(text[index]))
				{
					blank = false;
					break;
				}
			}

			if (!blank && start is null)
			{
				start = lineStart;
			}
			else if (blank && start is not null)
			{
				paragraphs.Add((start.Value, lineStart));
				start = null;
			}
		}

		if (start is not null)
		{
			paragraphs.Add((start.Value, text.Length));
		}

		return paragraphs;
	}

	private static void MaskCodeSpans(char[] text, int start, int end)
	{
		int index = start;
		while (index < end)
		{
			if (text[index] == '\\')
			{
				index += 2;
				continue;
			}

			if (text[index] != '`')
			{
				index++;
				continue;
			}

			int runLength = CountRun(text, index, end, '`');
			int closing = FindBacktickRun(text, index + runLength, end, runLength);
			if (closing < 0)
			{
				index += runLength;
				continue;
			}

			Mask(text, index, closing + runLength);
			index = closing + runLength;
		}
	}

	private static int CountRun(char[] text, int index, int end, char character)
	{
		int length = 0;
		while (index + length < end && text[index + length] == character)
		{
			length++;
		}

		return length;
	}

	private static int FindBacktickRun(char[] text, int index, int end, int length)
	{
		while (index < end)
		{
			if (text[index] != '`')
			{
				index++;
				continue;
			}

			int run = CountRun(text, index, end, '`');
			if (run == length)
			{
				return index;
			}

			index += run;
		}

		return -1;
	}

	private void ExtractInlineLinks(string masked, int start, int end, List<MarkdownLink> links)
	{
		for (int index = start; index < end; index++)
		{
			if (masked[index] == '\\')
			{
				index++;
				continue;
			}

			if (masked[index] != '[')
			{
				continue;
			}

			int close = FindClosingBracket(masked, index, end);
			if (close < 0 || close + 1 >= end || masked[close + 1] != '('
				|| !TryReadDestination(masked, close + 2, end, out int targetStart, out int targetEnd))
			{
				continue;
			}

			bool image = index > start && masked[index - 1] == '!';
			string target = _text[targetStart..targetEnd];
			if (target.StartsWith('<') && target.EndsWith('>'))
			{
				target = target[1..^1];
			}

			links.Add(new MarkdownLink(LineOf(close), image ? MarkdownLinkKind.Image : MarkdownLinkKind.Inline,
				_text[(index + 1)..close], Decode(target)));
		}
	}

	private static int FindClosingBracket(string text, int open, int end)
	{
		int depth = 0;
		for (int index = open; index < end; index++)
		{
			switch (text[index])
			{
				case '\\':
					index++;
					break;
				case '[':
					depth++;
					break;
				case ']':
					depth--;
					if (depth == 0)
					{
						return index;
					}

					break;
			}
		}

		return -1;
	}

	private static bool TryReadDestination(string text, int index, int end, out int targetStart, out int targetEnd)
	{
		index = SkipWhitespace(text, index, end);
		targetStart = index;
		if (index < end && text[index] == '<')
		{
			int close = text.IndexOf('>', index);
			if (close < 0 || close >= end || text.AsSpan(index, close - index).Contains('\n'))
			{
				targetEnd = index;
				return false;
			}

			index = close + 1;
		}
		else
		{
			int depth = 0;
			while (index < end && !char.IsWhiteSpace(text[index]))
			{
				char character = text[index];
				if (character == '\\')
				{
					index += 2;
					continue;
				}

				if (character == '(')
				{
					depth++;
				}
				else if (character == ')')
				{
					if (depth == 0)
					{
						break;
					}

					depth--;
				}

				index++;
			}
		}

		targetEnd = Math.Min(index, end);
		index = SkipWhitespace(text, targetEnd, end);
		if (index < end && text[index] is '"' or '\'' or '(')
		{
			char closing = text[index] == '(' ? ')' : text[index];
			int close = text.IndexOf(closing, index + 1);
			if (close < 0 || close >= end)
			{
				return false;
			}

			index = SkipWhitespace(text, close + 1, end);
		}

		return index < end && text[index] == ')';
	}

	private static int SkipWhitespace(string text, int index, int end)
	{
		while (index < end && char.IsWhiteSpace(text[index]))
		{
			index++;
		}

		return index;
	}

	private void ExtractReferenceDefinitions(string masked, List<MarkdownLink> links)
	{
		foreach (Match match in ReferenceDefinitionPattern().Matches(masked))
		{
			string target = match.Groups["target"].Value;
			if (target.StartsWith('<') && target.EndsWith('>'))
			{
				target = target[1..^1];
			}

			links.Add(new MarkdownLink(LineOf(match.Index), MarkdownLinkKind.ReferenceDefinition,
				match.Groups["label"].Value, Decode(target)));
		}
	}

	private void ExtractHtmlAttributes(string masked, List<MarkdownLink> links)
	{
		foreach (Match match in HtmlAttributePattern().Matches(masked))
		{
			links.Add(new MarkdownLink(LineOf(match.Index), MarkdownLinkKind.HtmlAttribute,
				match.Groups["name"].Value, Decode(match.Groups["target"].Value)));
		}
	}

	private (List<string> Headings, HashSet<string> Anchors) ExtractHeadingsAndAnchors(string masked)
	{
		List<string> headings = [];
		HashSet<string> anchors = new(StringComparer.Ordinal);
		Dictionary<string, int> occurrences = new(StringComparer.Ordinal);
		for (int line = 0; line < Lines.Count; line++)
		{
			string maskedLine = masked.Substring(_lineStarts[line], Lines[line].Length);
			if (!HeadingPattern().IsMatch(maskedLine))
			{
				continue;
			}

			Match heading = HeadingPattern().Match(Lines[line]);
			string text = heading.Groups["text"].Value;
			headings.Add(text);

			string slug = Slug(text);
			string anchor = slug;
			if (occurrences.TryGetValue(slug, out int count))
			{
				do
				{
					count++;
					anchor = string.Create(CultureInfo.InvariantCulture, $"{slug}-{count}");
				}
				while (anchors.Contains(anchor));
			}

			occurrences[slug] = count;
			anchors.Add(anchor);
		}

		foreach (Match match in ExplicitAnchorPattern().Matches(masked))
		{
			anchors.Add(match.Groups["id"].Value);
		}

		return (headings, anchors);
	}

	private int LineOf(int offset)
	{
		int index = Array.BinarySearch(_lineStarts, offset);
		return index >= 0 ? index + 1 : ~index;
	}

	private static string Decode(string target)
	{
		try
		{
			return Uri.UnescapeDataString(target);
		}
		catch (UriFormatException)
		{
			return target;
		}
	}

	[GeneratedRegex(@"^[ \t]*(?<fence>`{3,}|~{3,})(?<info>.*)$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex FenceOpeningPattern();

	[GeneratedRegex(@"^[ \t]*(?<fence>`{3,}|~{3,})[ \t]*$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex FenceClosingPattern();

	[GeneratedRegex(@"^ {0,3}\[(?<label>[^\]\^][^\]]*)\]:[ \t]*(?<target><[^>\n]*>|\S+)", RegexOptions.CultureInvariant | RegexOptions.Multiline, RegexTimeoutMilliseconds)]
	private static partial Regex ReferenceDefinitionPattern();

	[GeneratedRegex(@"\b(?<name>href|src)\s*=\s*(?:""(?<target>[^""]*)""|'(?<target>[^']*)')", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex HtmlAttributePattern();

	[GeneratedRegex(@"<a\s[^>]*?\b(?:id|name)\s*=\s*[""'](?<id>[^""']+)[""']", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex ExplicitAnchorPattern();

	[GeneratedRegex(@"^ {0,3}#{1,6}(?:[ \t]+(?<text>.*?))?(?:[ \t]+#+)?[ \t]*$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex HeadingPattern();

	[GeneratedRegex(@"!\[(?<text>[^\]]*)\]\([^)]*\)", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex InlineImagePattern();

	[GeneratedRegex(@"\[(?<text>[^\]]*)\]\([^)]*\)", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex InlineLinkPattern();

	[GeneratedRegex(@"<[^>]+>", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex HtmlTagPattern();

	[GeneratedRegex(@"(?<drive>(?<![A-Za-z0-9_])[A-Za-z]:[\\/])|(?<uri>\bfile:/)|\\Users\\|/Users/|/home/", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex LocalPathPattern();
}
