using System.Runtime.Versioning;
using System.Text.Json;

using CheatEngine.Client.Tests.Infrastructure;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>
///     The receipt ledger: one <c>cheatengine-client-qualification-receipt/v1</c> JSON object per line, the run directory
///     redacted to <c>&lt;run&gt;</c>, and any other local path, user name or machine name refused without being echoed.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ReceiptLedgerTests : IDisposable
{
	private readonly TemporaryDirectory _temporary = new("LiveQualificationReceipts");
	private readonly string _run;
	private readonly string _path;
	private readonly ReceiptLedger _ledger;

	public ReceiptLedgerTests()
	{
		_run = _temporary.CreateDirectory("20260924T101530Z-a1b2");
		_path = Path.Combine(_run, "receipts.jsonl");
		_ledger = new ReceiptLedger(_path, new QualificationRedaction(_run, ["jdoe", "WORKSTATION-7"]));
	}

	public void Dispose()
	{
		_temporary.Dispose();
	}

	[Fact]
	public void EveryReceiptIsOneSchemaLineWithAFixedPropertyOrder()
	{
		_ledger.Append(Receipt("status", ReceiptStatus.Passed, "{\"ok\":true}"));
		_ledger.Append(Receipt("toggle-disable", ReceiptStatus.NotExecuted, "Operator: untick 'Plugin' <now>"));

		string[] lines = File.ReadAllText(_path).Split('\n');
		Assert.Equal(3, lines.Length);
		Assert.Equal(string.Empty, lines[2]);
		using JsonDocument first = JsonDocument.Parse(lines[0]);
		Assert.Equal(["schema", "runId", "session", "scenario", "check", "level", "status", "expectation", "observation"],
			first.RootElement.EnumerateObject().Select(static property => property.Name));
		Assert.Equal(ReceiptLedger.Schema, first.RootElement.GetProperty("schema").GetString());
		Assert.Contains("\"observation\":\"Operator: untick 'Plugin' <now>\"", lines[1], StringComparison.Ordinal);
		Assert.Equal(
		[
			Receipt("status", ReceiptStatus.Passed, "{\"ok\":true}"),
			Receipt("toggle-disable", ReceiptStatus.NotExecuted, "Operator: untick 'Plugin' <now>")
		], ReceiptLedger.Read(_path));
	}

	[Fact]
	public void TheRunDirectoryIsRedactedInEveryForm()
	{
		string forward = _run.Replace('\\', '/');
		string json = _run.Replace("\\", "\\\\", StringComparison.Ordinal);

		QualificationReceipt recorded = _ledger.Append(Receipt("paths", ReceiptStatus.Passed,
			$"{_run}\\plugins; {forward}/ce; {{\"path\":\"{json}\\\\sessions\"}}"));

		Assert.Equal("<run>\\plugins; <run>/ce; {\"path\":\"<run>\\\\sessions\"}", recorded.Observation);
	}

	[Theory]
	[InlineData(@"loaded C:\Windows\System32\kernel32.dll")]
	[InlineData(@"C:/Program Files/Cheat Engine")]
	[InlineData(@"share \\server\folder\file")]
	[InlineData(@"{""share"":""\\\\server\\folder""}")]
	[InlineData("file:///tmp/x")]
	[InlineData(@"under ~\AppData")]
	[InlineData("/home/someone/.nuget")]
	[InlineData("C:\\\\Users\\\\x in JSON")]
	public void ALocalPathIsRefusedAndNotEchoed(string observation)
	{
		InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
			_ledger.Append(Receipt("paths", ReceiptStatus.Passed, observation)));

		Assert.Contains("a local path", refused.Message, StringComparison.Ordinal);
		Assert.DoesNotContain(observation, refused.Message, StringComparison.Ordinal);
		Assert.False(File.Exists(_path));
	}

	[Theory]
	[InlineData("run by jdoe")]
	[InlineData("home of JDOE.")]
	[InlineData("machine workstation-7 answered")]
	public void AUserOrMachineNameIsRefused(string observation)
	{
		InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
			_ledger.Append(Receipt("names", ReceiptStatus.Passed, observation)));

		Assert.Contains("a user or machine name", refused.Message, StringComparison.Ordinal);
		Assert.False(File.Exists(_path));
	}

	[Theory]
	[InlineData("xjdoe")]
	[InlineData("jdoes")]
	[InlineData("https://github.com/CheatEngineNet/CheatEngine.Client")]
	[InlineData("ratio 3:4, time 10:15")]
	[InlineData(@"{""bundle"":""<run>\\plugins\\harness\\bridge.dll""}")]
	public void OrdinaryTextIsRecorded(string observation)
	{
		Assert.Equal(observation, _ledger.Append(Receipt("text", ReceiptStatus.Passed, observation)).Observation);
	}

	[Fact]
	public void InvalidIdentifiersAndLevelsAreRefused()
	{
		Assert.Throws<ArgumentException>(() => _ledger.Append(Receipt("text", ReceiptStatus.Passed, "x") with { Level = "C2" }));
		Assert.Throws<ArgumentException>(() => _ledger.Append(Receipt("two words", ReceiptStatus.Passed, "x")));
		Assert.Throws<ArgumentException>(() => _ledger.Append(Receipt("check\n", ReceiptStatus.Passed, "x")));
		Assert.Throws<ArgumentException>(() => _ledger.Append(Receipt("text", (ReceiptStatus) 7, "x")));
		Assert.False(File.Exists(_path));
	}

	private static QualificationReceipt Receipt(string check, ReceiptStatus status, string observation)
	{
		return new QualificationReceipt("20260924T101530Z-a1b2", "S0", "S0", check, "C3", status, "the harness answers", observation);
	}
}
