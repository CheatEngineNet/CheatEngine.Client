using System.Runtime.Versioning;
using System.Text.Json;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>
///     The run summary (<c>cheatengine-client-qualification-summary/v1</c>): verdicts are derived from the receipts and
///     never invented, a capability passes only when all its scenarios passed, and the text is redacted like a receipt.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class QualificationSummaryWriterTests
{
	private const string RunDirectory = @"C:\runs\20260924T101530Z-a1b2";

	private static readonly QualificationReceipt[] Receipts =
	[
		Receipt("Q05", "identity", ReceiptStatus.Passed),
		Receipt("Q05", "epoch", ReceiptStatus.Passed),
		Receipt("Q06", "rollback", ReceiptStatus.Passed),
		Receipt("Q06", "reenable", ReceiptStatus.Failed),
		Receipt("Q43", "cleanup", ReceiptStatus.Passed),
		Receipt("Q43", "operator-toggle", ReceiptStatus.NotExecuted)
	];

	[Fact]
	public void AScenarioPassesOnlyWhenEveryCheckPassed()
	{
		Assert.Equal(
		[
			new QualificationVerdict("Q05", ReceiptStatus.Passed, string.Empty),
			new QualificationVerdict("Q06", ReceiptStatus.Failed, "reenable"),
			new QualificationVerdict("Q43", ReceiptStatus.NotExecuted, "operator-toggle")
		], QualificationSummaryWriter.Scenarios([.. Enumerable.Reverse(Receipts)]));
	}

	[Fact]
	public void ACapabilityPassesOnlyWhenEveryScenarioPassed()
	{
		Dictionary<string, IReadOnlyList<string>> mapping = new(StringComparer.Ordinal)
		{
			["Client.Unmapped"] = [],
			["Client.Lifecycle"] = ["Q05", "Q06"],
			["Client.Identity"] = ["Q05"],
			["Client.Cleanup"] = ["Q05", "Q43"],
			["Client.Missing"] = ["Q05", "Q99"]
		};

		IReadOnlyList<QualificationVerdict> verdicts = QualificationSummaryWriter.Capabilities(mapping,
			QualificationSummaryWriter.Scenarios(Receipts));

		Assert.Equal(
		[
			new QualificationVerdict("Client.Cleanup", ReceiptStatus.NotExecuted, "Q43: operator-toggle"),
			new QualificationVerdict("Client.Identity", ReceiptStatus.Passed, string.Empty),
			new QualificationVerdict("Client.Lifecycle", ReceiptStatus.Failed, "Q06: reenable"),
			new QualificationVerdict("Client.Missing", ReceiptStatus.NotExecuted, "Q99: Q99 has no receipt"),
			new QualificationVerdict("Client.Unmapped", ReceiptStatus.NotExecuted, "no scenario is mapped")
		], verdicts);
	}

	[Fact]
	public void TheSummaryCarriesTheTupleVerdictsSessionsAndRegistryRestoration()
	{
		string text = QualificationSummaryWriter.Serialize(Summary(Tuple(RunDirectory + @"\ce\ce.runtimeconfig.json")), Redaction());

		using JsonDocument document = JsonDocument.Parse(text);
		JsonElement root = document.RootElement;
		Assert.Equal(QualificationSummaryWriter.Schema, root.GetProperty("schema").GetString());
		Assert.Equal("20260924T101530Z-a1b2", root.GetProperty("runId").GetString());
		JsonElement tuple = root.GetProperty("tuple");
		Assert.Equal(["CheatEngine.Client", "CheatEngine.Client.Core"],
			tuple.GetProperty("clientPackages").EnumerateArray().Select(static package => package.GetProperty("id").GetString()));
		Assert.Equal("b008c8d8", tuple.GetProperty("sdk").GetProperty("bridgeSha256").GetString());
		Assert.Equal("ce-7.7.0.10621-x64-managed-hostfxr", tuple.GetProperty("profile").GetString());
		Assert.Equal("<run>\\ce\\ce.runtimeconfig.json", tuple.GetProperty("dotnetRuntime").GetString());
		Assert.Equal("TimedOut", root.GetProperty("sessions")[0].GetProperty("outcome").GetString());
		Assert.Equal(["Q05", "Q06", "Q43"], root.GetProperty("scenarios").EnumerateArray().Select(static verdict => verdict.GetProperty("id").GetString()));
		Assert.Equal("Passed", root.GetProperty("capabilities")[0].GetProperty("status").GetString());
		Assert.True(root.GetProperty("registryRestored").GetBoolean());
	}

	[Fact]
	public void ASummaryThatStillDisclosesAPathIsRefused()
	{
		InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
			QualificationSummaryWriter.Serialize(Summary(Tuple(@"D:\elsewhere\ce.runtimeconfig.json")), Redaction()));

		Assert.Contains("a local path", refused.Message, StringComparison.Ordinal);
		Assert.DoesNotContain("elsewhere", refused.Message, StringComparison.Ordinal);
	}

	private static QualificationRedaction Redaction()
	{
		return new QualificationRedaction(RunDirectory, ["jdoe"]);
	}

	private static QualificationSummary Summary(QualificationTuple tuple)
	{
		return new QualificationSummary("20260924T101530Z-a1b2", tuple, [new SessionSummary("S1", SessionOutcome.TimedOut, "host TimedOut")],
			Receipts, new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["Client.Identity"] = ["Q05"] }, true);
	}

	private static QualificationTuple Tuple(string runtime)
	{
		return new QualificationTuple(
			[new PackageIdentity("CheatEngine.Client.Core", "1.0.0", "cc"), new PackageIdentity("CheatEngine.Client", "1.0.0", "aa")],
			"0123456789abcdef", "2.0.0", "325c47b", "NLEdZ", "b008c8d8", "ce-7.7.0.10621-x64-managed-hostfxr", "7.7.0.10621",
			"9727076D", [new TargetIdentity("gtutorial-x86_64.exe", "2DABEFFD")], runtime, "10.0.26200.0");
	}

	private static QualificationReceipt Receipt(string scenario, string check, ReceiptStatus status)
	{
		return new QualificationReceipt("20260924T101530Z-a1b2", "S1", scenario, check, "C3", status, "expected", "observed");
	}
}
