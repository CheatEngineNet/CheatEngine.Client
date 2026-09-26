using LivePlugin.Qualification.Harness;

namespace CheatEngine.Client.LivePlugin.Qualification.Tests;

/// <summary>
///     The fault switch of the rollback (Q06) and cleanup (Q43) scenarios: the file the SDK runner writes next to the
///     plugin selects one stage, only when the gate authorized the run; anything else leaves the plugin unfaulted.
/// </summary>
public sealed class QualificationFaultInjectionTests
{
	private const string PluginDirectory = "bundle/ClientQualification";
	private const string SwitchPath = PluginDirectory + "/" + QualificationFaultSwitch.FileName;

	[Fact]
	public void FaultFileIsIgnoredWhenAuthorizationIsDenied()
	{
		FakeQualificationEnvironment environment = new();
		environment.SetVariable(QualificationAuthorization.AcknowledgementVariable, null);
		environment.SetFile(SwitchPath, Switch("ModuleOnEnabled"));
		AuthorizationDecision denied = QualificationAuthorization.Evaluate(environment);

		FaultDecision decision = QualificationFaultSwitch.Read(PluginDirectory, denied, environment);

		Assert.Equal(FaultStage.None, decision.Stage);
		Assert.Equal(FaultSwitchReason.IgnoredUnauthorized, decision.Reason);
		Assert.False(decision.FaultsEnable);
	}

	[Theory]
	[InlineData("{ \"schema\": \"cheatengine-client-qualification-fault/v0\", \"throwIn\": \"ModuleOnEnabled\" }")]
	[InlineData("{ \"schema\": \"ce77-live-probe-fault-v1\", \"throwIn\": \"OnEnable\" }")]
	[InlineData("{ \"schema\": \"ce77-live-probe-fault-v1\", \"throwIn\": \"99\" }")]
	[InlineData("{ \"schema\": \"ce77-live-probe-fault-v1\", \"throwIn\": 2 }")]
	[InlineData("{ \"schema\": 1, \"throwIn\": \"ModuleOnEnabled\" }")]
	[InlineData("not json")]
	public void FaultFileWithAnUnknownSchemaIsIgnoredAndReported(string text)
	{
		FakeQualificationEnvironment environment = new();
		environment.SetFile(SwitchPath, text);

		FaultDecision decision = QualificationFaultSwitch.Read(PluginDirectory,
			QualificationAuthorization.Evaluate(environment), environment);

		Assert.Equal(FaultStage.None, decision.Stage);
		Assert.Equal(FaultSwitchReason.IgnoredInvalid, decision.Reason);
	}

	[Theory]
	[InlineData("Configure", false, false, false)]
	[InlineData("ModuleOnEnabled", true, false, false)]
	[InlineData("ModuleOnDisabling", false, true, false)]
	[InlineData("ResourceCleanup", false, false, true)]
	[InlineData("ModuleOnDisablingAndResourceCleanup", false, true, true)]
	[InlineData("None", false, false, false)]
	public void FaultFileSelectsExactlyTheRequestedStage(string stage, bool enable, bool disabling, bool cleanup)
	{
		FakeQualificationEnvironment environment = new();
		environment.SetFile(SwitchPath, Switch(stage));

		FaultDecision decision = QualificationFaultSwitch.Read(PluginDirectory,
			QualificationAuthorization.Evaluate(environment), environment);

		Assert.Equal(Enum.Parse<FaultStage>(stage), decision.Stage);
		Assert.Equal(FaultSwitchReason.Selected, decision.Reason);
		Assert.Equal(enable, decision.FaultsEnable);
		Assert.Equal(disabling, decision.FaultsDisabling);
		Assert.Equal(cleanup, decision.FaultsResourceCleanup);
	}

	[Fact]
	public void AbsentFaultFileMeansNoFault()
	{
		FakeQualificationEnvironment environment = new();

		FaultDecision decision = QualificationFaultSwitch.Read(PluginDirectory,
			QualificationAuthorization.Evaluate(environment), environment);
		FaultDecision noDirectory = QualificationFaultSwitch.Read(null,
			QualificationAuthorization.Evaluate(environment), environment);

		Assert.Same(FaultDecision.NoFault, decision);
		Assert.Equal(FaultSwitchReason.Absent, decision.Reason);
		Assert.Equal(FaultStage.None, noDirectory.Stage);
	}

	// The document the SDK runner writes for a scenario with "faultStage" (Invoke-LocalQualification.ps1, stage 7).
	private static string Switch(string stage)
	{
		return "{\"schema\":\"" + QualificationFaultSwitch.Schema + "\",\"throwIn\":\"" + stage + "\"}";
	}
}
