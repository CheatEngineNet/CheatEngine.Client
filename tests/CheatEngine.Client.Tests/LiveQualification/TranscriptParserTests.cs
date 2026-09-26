using System.Runtime.Versioning;
using System.Text;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>
///     The transcript decoder, on the exact bytes Lua 5.3 writes: <c>string.format("%q")</c> escapes a quote, a backslash
///     and a line feed with a backslash, writes <c>\r</c> and decimal <c>\ddd</c> escapes, and passes UTF-8 through.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TranscriptParserTests
{
	[Fact]
	public void RecordsAndTheDoneLineAreDecoded()
	{
		Transcript transcript = Parse(
			"R\tmain-form\tok\t\"ready\"\n" +
			"R\tload-plugin\terror\t\"loadPlugin failed\"\n" +
			"R\ttoggle-disable\tnotexecuted\t\"Operator: untick it\"\n" +
			"DONE\n");

		Assert.True(transcript.Completed);
		Assert.Empty(transcript.Problems);
		Assert.Equal(
		[
			new TranscriptRecord(1, "main-form", TranscriptStatus.Ok, "ready"),
			new TranscriptRecord(2, "load-plugin", TranscriptStatus.Error, "loadPlugin failed"),
			new TranscriptRecord(3, "toggle-disable", TranscriptStatus.NotExecuted, "Operator: untick it")
		], transcript.Records);
	}

	[Fact]
	public void LuaQuotedEscapesAreDecoded()
	{
		// %q of: a"b\c<LF>d<CR>e<NUL>1 é, then a record on the next physical line.
		Transcript transcript = Parse("R\tstatus\tok\t\"a\\\"b\\\\c\\\nd\\re\\0001 \u00e9\"\nR\tnext\tok\t\"\\195\\169\\9\"\nDONE");

		Assert.True(transcript.Completed);
		Assert.Empty(transcript.Problems);
		Assert.Equal("a\"b\\c\nd\re\u00001 \u00e9", transcript.Records[0].Value);
		Assert.Equal(new TranscriptRecord(3, "next", TranscriptStatus.Ok, "\u00e9\t"), transcript.Records[1]);
	}

	[Fact]
	public void JsonObservationsSurviveTheRoundTrip()
	{
		const string observation = """{"ok":true,"message":"line one\nline two","path":"<run>/x"}""";

		Transcript transcript = Parse("R\tstatus\tok\t\"" + LuaQuote(observation) + "\"\r\nDONE\r\n");

		Assert.Equal(observation, Assert.Single(transcript.Records).Value);
		Assert.True(transcript.Completed);
	}

	[Theory]
	[InlineData("R\tstep\tmaybe\t\"x\"\n", "unknown status 'maybe'")]
	[InlineData("R\tstep\tok\n", "a record needs a step and a status separated by tabs")]
	[InlineData("R\t\tok\t\"x\"\n", "a record needs a step and a status separated by tabs")]
	[InlineData("R\tstep\tok\tx\n", "the value is not a quoted string")]
	[InlineData("R\tstep\tok\t\"x\n\"\n", "the quoted value contains an unescaped line feed")]
	[InlineData("R\tstep\tok\t\"x\" tail\n", "the quoted value is followed by more text")]
	[InlineData("R\tstep\tok\t\"\\999\"\n", "the decimal escape \\999 exceeds 255")]
	[InlineData("R\tstep\tok\t\"\\q\"\n", "unsupported escape '\\q'")]
	[InlineData("hello\n", "not a record")]
	public void MalformedLinesAreReportedNotDropped(string text, string problem)
	{
		Transcript transcript = Parse(text + "R\tafter\tok\t\"kept\"\n");

		Assert.Equal("line 1: " + problem, transcript.Problems[0]);
		Assert.Equal("after", transcript.Records[^1].Step);
		Assert.False(transcript.Completed);
	}

	[Fact]
	public void AnUnterminatedValueIsAProblem()
	{
		// The value ends with an escaped quote, so the file ends inside it.
		Transcript transcript = Parse("R\tstep\tok\t\"x\\\"");

		Assert.Equal(["line 1: the quoted value is not terminated"], transcript.Problems);
		Assert.Empty(transcript.Records);
	}

	[Fact]
	public void ContentAfterDoneIsAProblem()
	{
		Transcript transcript = Parse("DONE\nR\tlate\tok\t\"x\"\n");

		Assert.True(transcript.Completed);
		Assert.Equal(["line 2: content after DONE"], transcript.Problems);
		Assert.Empty(transcript.Records);
	}

	[Fact]
	public void AMissingTranscriptIsIncompleteAndFindReturnsTheLastRecord()
	{
		Transcript missing = TranscriptParser.ParseFile(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "transcript.txt"));
		Transcript repeated = Parse("R\tstep\terror\t\"first\"\nR\tstep\tok\t\"second\"\n");

		Assert.False(missing.Completed);
		Assert.Equal(["The driver wrote no transcript."], missing.Problems);
		Assert.Equal("second", repeated.Find("step")?.Value);
		Assert.Null(repeated.Find("other"));
	}

	/// <summary>What Lua 5.3 <c>%q</c> writes between the quotes for a string without control characters.</summary>
	private static string LuaQuote(string value)
	{
		return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
	}

	private static Transcript Parse(string text)
	{
		return TranscriptParser.Parse(Encoding.UTF8.GetBytes(text));
	}
}
