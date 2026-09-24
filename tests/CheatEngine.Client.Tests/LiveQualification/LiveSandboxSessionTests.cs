using System.Runtime.Versioning;
using System.Text;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>
///     How the S0 session turns a transcript and the workstation checks into receipts, without starting anything: a
///     step that never ran fails, an operator toggle is never counted as passed, and the host hygiene checks are explicit.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LiveSandboxSessionTests
{
	private const string RunId = "20260924T101530Z-a1b2";

	[Fact]
	public void ACompleteSpikeTranscriptPassesEveryRequiredCheck()
	{
		Transcript transcript = TranscriptParser.Parse(Encoding.UTF8.GetBytes(
			"R\tmain-form\tok\t\"ready\"\n" +
			"R\tload-plugin\tok\t\"0\"\n" +
			"R\tharness-ready\tok\t\"ready\"\n" +
			"R\tstatus\tok\t\"{\\\"ok\\\":true}\"\n" +
			"R\truntime\tok\t\"{\\\"ok\\\":true}\"\n" +
			"R\tcapabilities\tok\t\"{\\\"ok\\\":true}\"\n" +
			"R\tsettings-probe\terror\t\"attempt to index a nil value\"\n" +
			"R\ttoggle-disable\tnotexecuted\t\"Operator: untick it\"\n" +
			"R\ttoggle-enable\tnotexecuted\t\"Operator: tick it\"\n" +
			"DONE\n"));

		QualificationReceipt[] receipts = [.. LiveSandboxSession.SpikeReceipts(RunId, "S0", transcript, SessionOutcome.Completed, true, [], [], [])];

		Assert.All(LiveSandboxSession.SpikeRequiredChecks, check => Assert.Equal(ReceiptStatus.Passed,
			Assert.Single(receipts, receipt => receipt.Check == check).Status));
		Assert.Equal(ReceiptStatus.NotExecuted, Assert.Single(receipts, static receipt => receipt.Check == "settings-probe").Status);
		QualificationReceipt[] toggles = [.. receipts.Where(static receipt => receipt.Check.StartsWith("toggle-", StringComparison.Ordinal))];
		Assert.All(toggles, static receipt => Assert.Equal(ReceiptStatus.NotExecuted, receipt.Status));
		Assert.Equal(["Operator: untick it", "Operator: tick it"], toggles.Select(static receipt => receipt.Observation));
		Assert.All(receipts, static receipt => Assert.Equal(("S0", "S0", "C3"), (receipt.Session, receipt.Scenario, receipt.Level)));
	}

	[Fact]
	public void MissingStepsAndADirtyWorkstationFail()
	{
		Transcript transcript = TranscriptParser.Parse(Encoding.UTF8.GetBytes("R\tstatus\tok\t\"not json\"\n"));

		QualificationReceipt[] receipts =
		[
			.. LiveSandboxSession.SpikeReceipts(RunId, "S0", transcript, SessionOutcome.TimedOut, false,
				["host executable SHA-256 A became B"], ["process 'gtutorial-x86_64' (id 7) is running"], ["speedhack-x86_64.dll"])
		];

		Assert.All(LiveSandboxSession.SpikeRequiredChecks, check => Assert.Equal(ReceiptStatus.Failed,
			Assert.Single(receipts, receipt => receipt.Check == check).Status));
		Assert.Equal("not reached", Assert.Single(receipts, static receipt => receipt.Check == "load-plugin").Observation);
		Assert.Equal("Ok: not json", Assert.Single(receipts, static receipt => receipt.Check == "status").Observation);
		Assert.Equal("speedhack-x86_64.dll", Assert.Single(receipts, static receipt => receipt.Check == "no-injected-module").Observation);
	}
}
