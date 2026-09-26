using System.Runtime.Versioning;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>
///     S0, the spike of the live qualification (runner specification): build the qualification harness from the packed
///     packages, load it into a sandboxed Cheat Engine 7.7 on gtutorial-x86_64, call status, runtime and capabilities(1),
///     inspect the settings form read-only, close Cheat Engine, and prove that the user state is restored, the source
///     installation untouched and no process left. Its receipts are never committed; its facts go to the README.
/// </summary>
/// <remarks>
///     A live fact: never run in CI (both legs exclude <c>Category=LiveQualification</c>), never skipped. Without the
///     opt-in of <see cref="LiveQualificationOptIn" /> it fails at once with the instructions.
/// </remarks>
[Collection(LiveQualificationSerialGroup.Name)]
[Trait("Category", "LiveQualification")]
[Trait("Session", "S0")]
[SupportedOSPlatform("windows")]
public sealed class LiveSandboxSpikeTests(LiveQualificationFixture fixture)
{
	[Fact]
	public async Task SandboxedHarnessAnswersAndTheWorkstationIsLeftAsItWasAsync()
	{
		LiveQualificationInputs inputs = fixture.RequireAuthorization();

		LiveSessionResult result = await LiveSandboxSession.RunSpikeAsync(inputs, fixture.Feed, CheatEngineRegistryGuard.ForWorkstation(),
			TestContext.Current.CancellationToken);

		foreach (QualificationReceipt receipt in result.Receipts)
		{
			TestContext.Current.TestOutputHelper?.WriteLine($"receipt[{receipt.Check}] {receipt.Status}: {receipt.Observation}");
		}

		TestContext.Current.TestOutputHelper?.WriteLine($"run directory: {result.Layout.RunDirectory}");
		Assert.True(result.UserStateRestored, "The Cheat Engine user state was not restored and verified.");
		Assert.Empty(result.InstallationChanges);
		Assert.Empty(result.LeftoverProcesses);
		Assert.Equal(SessionOutcome.Completed, result.Outcome);
		Assert.All(result.Receipts, static receipt => Assert.True(receipt.Status != ReceiptStatus.Failed, $"{receipt.Check}: {receipt.Observation}"));
		Assert.All(LiveSandboxSession.SpikeRequiredChecks, check => Assert.Equal(ReceiptStatus.Passed,
			Assert.Single(result.Receipts, receipt => receipt.Check == check).Status));
	}
}
