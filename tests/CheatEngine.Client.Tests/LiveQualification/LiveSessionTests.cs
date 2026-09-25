using System.Runtime.Versioning;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>Runs one session of <see cref="SessionPlans" /> and asserts what a session itself must guarantee.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveSessions
{
	/// <summary>
	///     Runs <paramref name="plan" /> in the collection's run: the workstation is left as it was, the driver ran to its
	///     end, and no check failed. A NotExecuted check is recorded as such and does not fail the fact: whether the
	///     release can go ahead is decided on the recorded summary, never by skipping or inventing a pass.
	/// </summary>
	internal static async Task RunAsync(LiveQualificationFixture fixture, QualificationSessionPlan plan)
	{
		LiveQualificationInputs inputs = fixture.RequireAuthorization();

		LiveSessionResult result = await LiveSandboxSession.RunSessionAsync(plan, inputs, fixture.Feed,
			CheatEngineRegistryGuard.ForWorkstation(), fixture.Run(inputs), TestContext.Current.CancellationToken);

		foreach (QualificationReceipt receipt in result.Receipts)
		{
			TestContext.Current.TestOutputHelper?.WriteLine(
				$"receipt[{receipt.Scenario}/{receipt.Check}] {receipt.Status}: {receipt.Observation}");
		}

		TestContext.Current.TestOutputHelper?.WriteLine($"run directory: {result.Layout.RunDirectory}");
		Assert.True(result.UserStateRestored, "The Cheat Engine user state was not restored and verified.");
		Assert.Empty(result.InstallationChanges);
		Assert.Empty(result.LeftoverProcesses);
		Assert.Equal(SessionOutcome.Completed, result.Outcome);
		Assert.All(result.Receipts, static receipt => Assert.True(receipt.Status != ReceiptStatus.Failed,
			$"{receipt.Scenario}/{receipt.Check}: {receipt.Observation}"));
	}
}

/// <summary>S1: the x64 core on gtutorial-x86_64.</summary>
[Collection(LiveQualificationSerialGroup.Name)]
[Trait("Category", "LiveQualification")]
[Trait("Session", "S1")]
[Trait("Qualification", "Q05")]
[Trait("Qualification", "Q16.b")]
[Trait("Qualification", "Q19")]
[Trait("Qualification", "Q20")]
[Trait("Qualification", "Q21")]
[Trait("Qualification", "Q25")]
[Trait("Qualification", "Q26")]
[Trait("Qualification", "Q27")]
[Trait("Qualification", "Q28")]
[Trait("Qualification", "Q29")]
[Trait("Qualification", "Q30.a")]
[Trait("Qualification", "Q31")]
[Trait("Qualification", "Q32")]
[Trait("Qualification", "Q33")]
[Trait("Qualification", "Q34")]
[Trait("Qualification", "Q35")]
[Trait("Qualification", "Q40")]
[Trait("Qualification", "Q45")]
[Trait("Qualification", "Q46")]
[SupportedOSPlatform("windows")]
public sealed class LiveSessionS1Tests(LiveQualificationFixture fixture)
{
	[Fact]
	public Task X64CoreSessionRecordsItsReceiptsAsync()
	{
		return LiveSessions.RunAsync(fixture, SessionPlans.S1);
	}
}

/// <summary>S2: lifecycle and faults, without the Auto Assembler opt-in.</summary>
[Collection(LiveQualificationSerialGroup.Name)]
[Trait("Category", "LiveQualification")]
[Trait("Session", "S2")]
[Trait("Qualification", "Q05")]
[Trait("Qualification", "Q06")]
[Trait("Qualification", "Q16")]
[Trait("Qualification", "Q43")]
[Trait("Qualification", "Q44")]
[Trait("Qualification", "Q46")]
[SupportedOSPlatform("windows")]
public sealed class LiveSessionS2Tests(LiveQualificationFixture fixture)
{
	[Fact]
	public Task LifecycleSessionRecordsItsReceiptsAsync()
	{
		return LiveSessions.RunAsync(fixture, SessionPlans.S2);
	}
}

/// <summary>S3: target identity on two gtutorial-x86_64 instances and a file opened as a process.</summary>
[Collection(LiveQualificationSerialGroup.Name)]
[Trait("Category", "LiveQualification")]
[Trait("Session", "S3")]
[Trait("Qualification", "Q26")]
[Trait("Qualification", "Q28")]
[Trait("Qualification", "Q30.a")]
[Trait("Qualification", "Q30.b")]
[Trait("Qualification", "Q32")]
[Trait("Qualification", "Q35")]
[SupportedOSPlatform("windows")]
public sealed class LiveSessionS3Tests(LiveQualificationFixture fixture)
{
	[Fact]
	public Task TargetIdentitySessionRecordsItsReceiptsAsync()
	{
		return LiveSessions.RunAsync(fixture, SessionPlans.S3);
	}
}

/// <summary>S4: the x86 target gtutorial-i386.</summary>
[Collection(LiveQualificationSerialGroup.Name)]
[Trait("Category", "LiveQualification")]
[Trait("Session", "S4")]
[Trait("Qualification", "Q21")]
[Trait("Qualification", "Q28")]
[Trait("Qualification", "Q32")]
[SupportedOSPlatform("windows")]
public sealed class LiveSessionS4Tests(LiveQualificationFixture fixture)
{
	[Fact]
	public Task X86SessionRecordsItsReceiptsAsync()
	{
		return LiveSessions.RunAsync(fixture, SessionPlans.S4);
	}
}

/// <summary>S5: coexistence, in both load orders.</summary>
[Collection(LiveQualificationSerialGroup.Name)]
[Trait("Category", "LiveQualification")]
[Trait("Session", "S5")]
[Trait("Qualification", "Q09")]
[Trait("Qualification", "Q10")]
[Trait("Qualification", "Q16")]
[SupportedOSPlatform("windows")]
public sealed class LiveSessionS5Tests(LiveQualificationFixture fixture)
{
	[Fact]
	public Task PluginANeighbourPluginBOrderRecordsItsReceiptsAsync()
	{
		return LiveSessions.RunAsync(fixture, SessionPlans.S5a);
	}

	[Fact]
	public Task NeighbourPluginAPluginBOrderRecordsItsReceiptsAsync()
	{
		return LiveSessions.RunAsync(fixture, SessionPlans.S5b);
	}
}

/// <summary>S6: the template, instantiated from the packed Templates package.</summary>
[Collection(LiveQualificationSerialGroup.Name)]
[Trait("Category", "LiveQualification")]
[Trait("Session", "S6")]
[Trait("Qualification", "Q40")]
[SupportedOSPlatform("windows")]
public sealed class LiveSessionS6Tests(LiveQualificationFixture fixture)
{
	[Fact]
	public Task TemplateSessionRecordsItsReceiptsAsync()
	{
		return LiveSessions.RunAsync(fixture, SessionPlans.S6);
	}
}
