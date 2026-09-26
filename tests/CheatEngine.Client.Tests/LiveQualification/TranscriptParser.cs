using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>How a driver step ended.</summary>
internal enum TranscriptStatus
{
	/// <summary>The step returned; <see cref="TranscriptRecord.Value" /> is its result.</summary>
	Ok,

	/// <summary>The step raised a Lua error or never produced a result; the value is the error.</summary>
	Error,

	/// <summary>The driver did not attempt the step; the value is the operator prompt.</summary>
	NotExecuted
}

/// <summary>One decoded transcript record.</summary>
/// <param name="Line">The 1-based line the record starts on.</param>
/// <param name="Step">The step name.</param>
/// <param name="Status">How the step ended.</param>
/// <param name="Value">The decoded value (UTF-8).</param>
internal sealed record TranscriptRecord(int Line, string Step, TranscriptStatus Status, string Value);

/// <summary>A parsed transcript.</summary>
/// <param name="Records">The records, in order.</param>
/// <param name="Completed">Whether the driver wrote <c>DONE</c>.</param>
/// <param name="Problems">Every line that is not a well-formed record.</param>
internal sealed record Transcript(IReadOnlyList<TranscriptRecord> Records, bool Completed, IReadOnlyList<string> Problems)
{
	/// <summary>The last record of <paramref name="step" />, or <see langword="null" />.</summary>
	internal TranscriptRecord? Find(string step)
	{
		return Records.LastOrDefault(record => string.Equals(record.Step, step, StringComparison.Ordinal));
	}
}

/// <summary>
///     Decodes the driver transcript: <c>R&lt;TAB&gt;step&lt;TAB&gt;ok|error|notexecuted&lt;TAB&gt;"value"</c> records,
///     whose value is Lua 5.3 <c>%q</c> output (a backslash before a quote, a backslash or a line feed, <c>\r</c>,
///     <c>\ddd</c> decimal bytes), and a final <c>DONE</c> line. The value is decoded byte by byte, then as UTF-8.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class TranscriptParser
{
	/// <summary>Parses the transcript file; a missing file is an empty, incomplete transcript.</summary>
	internal static Transcript ParseFile(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		return File.Exists(path)
			? Parse(File.ReadAllBytes(path))
			: new Transcript([], false, ["The driver wrote no transcript."]);
	}

	/// <summary>Parses transcript bytes.</summary>
	internal static Transcript Parse(ReadOnlySpan<byte> content)
	{
		List<TranscriptRecord> records = [];
		List<string> problems = [];
		bool completed = false;
		int position = 0;
		int line = 1;
		while (position < content.Length)
		{
			int start = line;
			ReadOnlySpan<byte> rest = content[position..];
			if (completed)
			{
				problems.Add(Problem(start, "content after DONE"));
				break;
			}

			if (TryMatchLine(rest, "DONE"u8, out int doneLength))
			{
				completed = true;
				position += doneLength;
				line++;
				continue;
			}

			if (TryParseRecord(rest, start, out TranscriptRecord? record, out int consumed, out int lines, out string? error))
			{
				records.Add(record);
				position += consumed;
				line += lines;
				continue;
			}

			problems.Add(Problem(start, error));
			int next = rest.IndexOf((byte) '\n');
			position = next < 0 ? content.Length : position + next + 1;
			line++;
		}

		return new Transcript(records, completed, problems);
	}

	private static bool TryMatchLine(ReadOnlySpan<byte> rest, ReadOnlySpan<byte> text, out int length)
	{
		length = 0;
		if (!rest.StartsWith(text))
		{
			return false;
		}

		int end = EndOfLine(rest, text.Length);
		if (end < 0)
		{
			return false;
		}

		length = end;
		return true;
	}

	/// <summary>The length through the line ending at <paramref name="index" /> (LF, CRLF or the end), or -1.</summary>
	private static int EndOfLine(ReadOnlySpan<byte> rest, int index)
	{
		if (index == rest.Length)
		{
			return index;
		}

		if (rest[index] == (byte) '\n')
		{
			return index + 1;
		}

		return rest[index] == (byte) '\r' && index + 1 < rest.Length && rest[index + 1] == (byte) '\n' ? index + 2 : -1;
	}

	private static bool TryParseRecord(ReadOnlySpan<byte> rest, int line, [NotNullWhen(true)] out TranscriptRecord? record,
		out int consumed, out int lines, [NotNullWhen(false)] out string? error)
	{
		record = null;
		consumed = 0;
		lines = 1;
		error = null;
		if (!rest.StartsWith("R\t"u8))
		{
			error = "not a record";
			return false;
		}

		int index = 2;
		if (!TryReadField(rest, ref index, out string step) || step.Length == 0 ||
			!TryReadField(rest, ref index, out string statusText))
		{
			error = "a record needs a step and a status separated by tabs";
			return false;
		}

		TranscriptStatus? status = statusText switch
		{
			"ok" => TranscriptStatus.Ok,
			"error" => TranscriptStatus.Error,
			"notexecuted" => TranscriptStatus.NotExecuted,
			_ => null
		};
		if (status is null)
		{
			error = $"unknown status '{statusText}'";
			return false;
		}

		if (!TryDecodeQuoted(rest, ref index, ref lines, out string? value, out error))
		{
			return false;
		}

		int end = EndOfLine(rest, index);
		if (end < 0)
		{
			error = "the quoted value is followed by more text";
			return false;
		}

		consumed = end;
		record = new TranscriptRecord(line, step, status.Value, value);
		return true;
	}

	private static bool TryReadField(ReadOnlySpan<byte> rest, ref int index, out string field)
	{
		int tab = rest[index..].IndexOfAny((byte) '\t', (byte) '\n');
		if (tab < 0 || rest[index + tab] != (byte) '\t')
		{
			field = string.Empty;
			return false;
		}

		field = Encoding.UTF8.GetString(rest.Slice(index, tab));
		index += tab + 1;
		return true;
	}

	private static bool TryDecodeQuoted(ReadOnlySpan<byte> rest, ref int index, ref int lines,
		[NotNullWhen(true)] out string? value,
		[NotNullWhen(false)] out string? error)
	{
		value = null;
		error = null;
		if (index >= rest.Length || rest[index] != (byte) '"')
		{
			error = "the value is not a quoted string";
			return false;
		}

		List<byte> bytes = [];
		index++;
		while (index < rest.Length)
		{
			byte current = rest[index++];
			if (current == (byte) '"')
			{
				value = Encoding.UTF8.GetString([.. bytes]);
				return true;
			}

			if (current == (byte) '\n')
			{
				error = "the quoted value contains an unescaped line feed";
				return false;
			}

			if (current != (byte) '\\')
			{
				bytes.Add(current);
				continue;
			}

			if (index >= rest.Length)
			{
				break;
			}

			byte escape = rest[index++];
			switch (escape)
			{
				case (byte) '\n':
					bytes.Add((byte) '\n');
					lines++;
					break;
				case (byte) '\r' when index < rest.Length && rest[index] == (byte) '\n':
					index++;
					bytes.Add((byte) '\n');
					lines++;
					break;
				case (byte) '"' or (byte) '\\' or (byte) '\'':
					bytes.Add(escape);
					break;
				case (byte) 'n':
					bytes.Add((byte) '\n');
					break;
				case (byte) 'r':
					bytes.Add((byte) '\r');
					break;
				case (byte) 't':
					bytes.Add((byte) '\t');
					break;
				case (byte) 'a':
					bytes.Add(7);
					break;
				case (byte) 'b':
					bytes.Add(8);
					break;
				case (byte) 'f':
					bytes.Add(12);
					break;
				case (byte) 'v':
					bytes.Add(11);
					break;
				case >= (byte) '0' and <= (byte) '9':
					int decimalValue = escape - '0';
					for (int digit = 0; digit < 2 && index < rest.Length && char.IsAsciiDigit((char) rest[index]); digit++)
					{
						decimalValue = (decimalValue * 10) + (rest[index++] - '0');
					}

					if (decimalValue > byte.MaxValue)
					{
						error = string.Create(CultureInfo.InvariantCulture, $"the decimal escape \\{decimalValue} exceeds 255");
						return false;
					}

					bytes.Add((byte) decimalValue);
					break;
				default:
					error = $"unsupported escape '\\{(char) escape}'";
					return false;
			}
		}

		error = "the quoted value is not terminated";
		return false;
	}

	private static string Problem(int line, string? detail)
	{
		return string.Create(CultureInfo.InvariantCulture, $"line {line}: {detail}");
	}
}
