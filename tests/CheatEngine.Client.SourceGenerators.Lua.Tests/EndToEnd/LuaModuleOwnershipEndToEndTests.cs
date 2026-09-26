using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Lua.Registration;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.Client.SourceGenerators.Lua.Tests.EndToEnd;

/// <summary>
///     C1 execution of the generated module and registrar around the CheatEngine.SDK registration leases (F12, Q16): the
///     module's public <c>Register</c>/<c>Unregister</c> and the generated registrar run unchanged; only the generated SDK
///     adapter is replaced by one that forwards each SDK call to a managed double of the SDK registration set. This is not
///     a Lua fixture (C2) and not a host observation (C3/C4).
/// </summary>
/// <remarks>
///     A function a script kept after disable is not tested here: that CheatEngine.SDK 2.0.0 closure (CRIT-07) runs
///     inside the SDK registration set, which these tests replace. Its evidence is the composition test (the module
///     publishes through <c>LuaRegistrationSet.Register</c>) and the Q16 host scenario.
/// </remarks>
[Trait("Qualification", "Q16")]
public sealed class LuaModuleOwnershipEndToEndTests
{
	private const string Status = "status";
	private const string Ping = "ping";
	private const string Marker = "marker";

	private static ModuleHarness Harness => ModuleHarness.Shared;

	[Fact]
	public void RegisterPublishesEveryExportAfterAPreflightThatWritesNothing()
	{
		(_, FakeLuaGlobals globals) = Registered();

		Assert.All(ModuleHarness.Exports, export => Assert.NotNull(globals[export]));
	}

	[Fact]
	public void UnregisterRemovesEveryExportTheModuleStillOwns()
	{
		(ILuaModule module, FakeLuaGlobals globals) = Registered();

		LuaModuleReleaseOutcome outcome = ModuleHarness.Unregister(module, globals);

		AssertOutcome(outcome, LeaseReleaseKind.Released, 3, 0, 0);
		Assert.True(outcome.IsComplete);
		Assert.All(ModuleHarness.Exports, export => Assert.Null(globals[export]));
		Assert.Equal(["read:status", "clear:status", "read:ping", "clear:ping", "read:marker", "clear:marker"],
			globals.Log);
	}

	[Fact]
	public void UnregisterLeavesAThirdPartyReplacementUntouched()
	{
		(ILuaModule module, FakeLuaGlobals globals) = Registered();
		FakeLuaValue thirdParty = new("third-party:ping");
		globals.AssignByThirdParty(Ping, thirdParty);

		LuaModuleReleaseOutcome outcome = ModuleHarness.Unregister(module, globals);

		AssertOutcome(outcome, LeaseReleaseKind.Released, 2, 1, 0);
		Assert.Same(thirdParty, globals[Ping]);
		Assert.Equal(0, globals.CountOf("clear:ping"));
		Assert.Null(globals[Status]);
		Assert.Null(globals[Marker]);
	}

	[Fact]
	public void UnregisterTreatsAWrappedFunctionAsAReplacement()
	{
		(ILuaModule module, FakeLuaGlobals globals) = Registered();
		// A wrapper that would call the module's function is still a different value: ownership is identity, not behaviour.
		FakeLuaValue wrapper = new("function(...) return plugin_ping(...) end");
		globals.AssignByThirdParty(Ping, wrapper);

		LuaModuleReleaseOutcome outcome = ModuleHarness.Unregister(module, globals);

		Assert.Equal(1, outcome.ReplacementCount);
		Assert.Same(wrapper, globals[Ping]);
		Assert.Equal(0, globals.CountOf("clear:ping"));
	}

	[Fact]
	public void UnregisterRemovesAValueAThirdPartyRestoredToTheModulesOwn()
	{
		(ILuaModule module, FakeLuaGlobals globals) = Registered();
		FakeLuaValue original = globals[Ping]!;
		globals.AssignByThirdParty(Ping, new FakeLuaValue("third-party:ping"));
		globals.AssignByThirdParty(Ping, original);

		LuaModuleReleaseOutcome outcome = ModuleHarness.Unregister(module, globals);

		AssertOutcome(outcome, LeaseReleaseKind.Released, 3, 0, 0);
		Assert.Null(globals[Ping]);
	}

	[Fact]
	public void UnregisterCountsAnExportThatIsAlreadyAbsentAsAReplacementAndWritesNothing()
	{
		(ILuaModule module, FakeLuaGlobals globals) = Registered();
		globals.AssignByThirdParty(Marker, null);

		LuaModuleReleaseOutcome outcome = ModuleHarness.Unregister(module, globals);

		// CheatEngine.SDK compares nil with the installed value like any other value: it is not the module's any more.
		AssertOutcome(outcome, LeaseReleaseKind.Released, 2, 1, 0);
		Assert.Equal(0, globals.CountOf("clear:marker"));
	}

	[Theory]
	[InlineData(FakeLuaType.TableValue)]
	[InlineData(FakeLuaType.NumberValue)]
	[InlineData(FakeLuaType.StringValue)]
	[InlineData(FakeLuaType.UserdataValue)]
	[InlineData(FakeLuaType.BooleanValue)]
	[InlineData(FakeLuaType.FunctionValue)]
	public void ThirdPartyValuesOfAnyLuaTypeAreCountedAsReplacements(FakeLuaType type)
	{
		// The double compares by object identity: this proves that the Client code keeps any value the SDK reports as
		// replaced, not how lua_rawequal compares strings, numbers or light C functions (that needs a Lua state, C2).
		(ILuaModule module, FakeLuaGlobals globals) = Registered();
		FakeLuaValue thirdParty = new("third-party:" + type, type);
		globals.AssignByThirdParty(Status, thirdParty);

		LuaModuleReleaseOutcome outcome = ModuleHarness.Unregister(module, globals);

		Assert.Equal(1, outcome.ReplacementCount);
		Assert.Same(thirdParty, globals[Status]);
	}

	[Fact]
	public void UnregisterAfterALuaStateReplacementWritesNothingAndReportsRefusedRuntimeChanged()
	{
		(ILuaModule module, FakeLuaGlobals globals) = Registered();
		globals.ReplaceLuaState();
		FakeLuaValue newStateValue = new("new-state:status");
		globals.AssignByThirdParty(Status, newStateValue);

		LuaModuleReleaseOutcome outcome = ModuleHarness.Unregister(module, globals);

		AssertOutcome(outcome, LeaseReleaseKind.RefusedRuntimeChanged, 0, 0, 3);
		Assert.False(outcome.IsComplete);
		Assert.Empty(globals.Log);
		Assert.Same(newStateValue, globals[Status]);
	}

	[Fact]
	public void UnregisterAfterAReattachWritesNothingAndLeavesTheEarlierGlobalsInPlace()
	{
		(ILuaModule module, FakeLuaGlobals globals) = Registered();
		globals.Reattach();

		LuaModuleReleaseOutcome outcome = ModuleHarness.Unregister(module, globals);

		// The earlier attachment's functions may remain: this is why the kind requires manual recovery, not ExternallyRemoved.
		AssertOutcome(outcome, LeaseReleaseKind.RefusedRuntimeChanged, 0, 0, 3);
		Assert.Empty(globals.Log);
		Assert.All(ModuleHarness.Exports, export => Assert.NotNull(globals[export]));
	}

	[Theory]
	[InlineData(LuaAdmissionStatus.Detached)]
	[InlineData(LuaAdmissionStatus.ExternalStateReset)]
	public void UnregisterWithoutTheLuaUniverseConsumesTheRegistrationAsStaleWithoutLua(LuaAdmissionStatus admission)
	{
		(ILuaModule module, FakeLuaGlobals globals) = Registered();
		globals.Admission = admission;

		LuaModuleReleaseOutcome outcome = ModuleHarness.Unregister(module, globals);
		LuaModuleReleaseOutcome retry = ModuleHarness.Unregister(module, globals);

		AssertOutcome(outcome, LeaseReleaseKind.RefusedRuntimeChanged, 0, 0, 3);
		Assert.Equal(LeaseReleaseKind.AlreadyReleased, retry.Kind);
		Assert.Equal(1, globals.StaleReleaseCount);
		Assert.Empty(globals.Log);
	}

	[Theory]
	[InlineData(LuaAdmissionStatus.TransitionInProgress)]
	[InlineData(LuaAdmissionStatus.ThreadNotAdmitted)]
	[InlineData(LuaAdmissionStatus.NoStateForThread)]
	[InlineData(LuaAdmissionStatus.Unknown)]
	public void UnregisterWithoutAdmissionKeepsTheRegistrationForALaterAttempt(LuaAdmissionStatus admission)
	{
		(ILuaModule module, FakeLuaGlobals globals) = Registered();
		globals.Admission = admission;

		LuaModuleReleaseOutcome refused = ModuleHarness.Unregister(module, globals);
		globals.Admission = LuaAdmissionStatus.Admitted;
		LuaModuleReleaseOutcome retried = ModuleHarness.Unregister(module, globals);

		AssertOutcome(refused, LeaseReleaseKind.CleanupUnavailable, 0, 0, 3);
		Assert.False(refused.IsComplete);
		AssertOutcome(retried, LeaseReleaseKind.Released, 3, 0, 0);
	}

	[Fact]
	public void UnregisterReportsIndependentFailuresAsAPartialReleaseThatIsNeverRetried()
	{
		(ILuaModule module, FakeLuaGlobals globals) = Registered();
		globals.FailReads.Add(Status);
		globals.FailClears.Add(Ping);

		LuaModuleReleaseOutcome outcome = ModuleHarness.Unregister(module, globals);
		globals.ClearLog();
		LuaModuleReleaseOutcome retry = ModuleHarness.Unregister(module, globals);

		Assert.Equal(LeaseReleaseKind.PartiallyReleased, outcome.Kind);
		Assert.Equal([Status, Ping], outcome.FailedExports);
		Assert.Equal(1, outcome.RemovedCount);
		Assert.Equal(2, outcome.RemainingCount);
		Assert.False(outcome.IsComplete);
		Assert.Null(globals[Marker]);
		Assert.NotNull(globals[Status]);
		Assert.NotNull(globals[Ping]);
		Assert.Equal(LeaseReleaseKind.AlreadyReleased, retry.Kind);
		Assert.Empty(globals.Log);
	}

	[Theory]
	[InlineData(LuaRegistrationReleaseKind.NotAttempted)]
	[InlineData(LuaRegistrationReleaseKind.PartiallyReleased)]
	[InlineData((LuaRegistrationReleaseKind) 99)]
	public void AReleaseOutsideTheSdkShapeIsAnUnconfirmedCleanupThatIsNeverRetried(LuaRegistrationReleaseKind reported)
	{
		(ILuaModule module, FakeLuaGlobals globals) = Registered();
		// A kind a later CheatEngine.SDK adds, the SDK's pre-release value, or a partial release that names no global.
		globals.NextRelease = new FakeRelease(reported, 1, 0, 0, 0, []);

		LuaModuleReleaseOutcome outcome = ModuleHarness.Unregister(module, globals);
		LuaModuleReleaseOutcome retry = ModuleHarness.Unregister(module, globals);

		// The lease was consumed before the release ran: the outcome requires manual recovery and is never retryable, so
		// the Client lease ends and the activation cleanup reports it instead of reading the retry as a clean release.
		AssertOutcome(outcome, LeaseReleaseKind.CleanupUnconfirmed, 1, 0, 2);
		Assert.False(outcome.IsComplete);
		LeaseReleaseOutcome lease = new(outcome.Kind, CheatEngineHostEffect.Started);
		Assert.True(lease.RequiresManualRecovery);
		Assert.False(lease.IsRetryable);
		Assert.Equal(LeaseReleaseKind.AlreadyReleased, retry.Kind);
		Assert.Equal(2, globals.AdmittedOperationCount);
	}

	[Fact]
	public void UnregisterWithoutARegistrationReportsAlreadyReleasedWithoutLua()
	{
		ILuaModule module = Harness.CreateModule();
		FakeLuaGlobals globals = ModuleHarness.CreateGlobals();

		LuaModuleReleaseOutcome outcome = ModuleHarness.Unregister(module, globals);

		Assert.Equal(LeaseReleaseKind.AlreadyReleased, outcome.Kind);
		Assert.Equal(ModuleHarness.ModuleName, outcome.ModuleName);
		Assert.True(outcome.IsComplete);
		Assert.Empty(globals.Log);
	}

	[Fact]
	public void RegisterRefusesAnOccupiedExportBeforeAnyWrite()
	{
		ILuaModule module = Harness.CreateModule();
		FakeLuaGlobals globals = ModuleHarness.CreateGlobals();
		FakeLuaValue existing = new("third-party:marker");
		globals.AssignByThirdParty(Marker, existing);

		CheatEngineFailure failure = RegisterFailure(module, globals);

		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotApplied, failure.HostEffect);
		Assert.Equal("Lua.RegisterModule", failure.Operation);
		Assert.Equal("Lua global 'marker' is already defined and cannot be replaced by Client module 'plugin'.",
			failure.Message);
		Assert.Equal(["read:status", "read:ping", "read:marker"], globals.Log);
		Assert.Same(existing, globals[Marker]);
		Assert.Null(globals[Status]);
		Assert.Equal(LeaseReleaseKind.AlreadyReleased, ModuleHarness.Unregister(module, globals).Kind);
	}

	[Fact]
	public void RegisterReportsAPreflightReadFailureBeforeAnyWrite()
	{
		ILuaModule module = Harness.CreateModule();
		FakeLuaGlobals globals = ModuleHarness.CreateGlobals();
		globals.FailReads.Add(Ping);

		CheatEngineFailure failure = RegisterFailure(module, globals);

		Assert.Equal(CheatEngineFailureKind.LuaError, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotApplied, failure.HostEffect);
		Assert.Contains("could not read Lua global 'ping'", failure.Message, StringComparison.Ordinal);
		Assert.Contains("LUA_ERRRUN", failure.Message, StringComparison.Ordinal);
		Assert.Equal(["read:status", "read-failed:ping"], globals.Log);
	}

	[Fact]
	public void RegisterRollsBackWhatItPublishedWhenPublicationFails()
	{
		ILuaModule module = Harness.CreateModule();
		FakeLuaGlobals globals = ModuleHarness.CreateGlobals();
		globals.FailPublications.Add(Ping);

		CheatEngineFailure failure = RegisterFailure(module, globals);

		Assert.Equal(CheatEngineFailureKind.LuaError, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotApplied, failure.HostEffect);
		Assert.StartsWith("Client Lua module 'plugin' could not publish Lua global 'ping' (LUA_ERRRUN). The rollback " +
						  "reported Released (removed 1, replaced 1, remaining 0).", failure.Message, StringComparison.Ordinal);
		Assert.Equal(
			["read:status", "read:ping", "read:marker", "write:status", "write-failed:ping", "read:status", "clear:status",
				"read:ping"],
			globals.Log);
		Assert.All(ModuleHarness.Exports, export => Assert.Null(globals[export]));
		Assert.Equal(LeaseReleaseKind.AlreadyReleased, ModuleHarness.Unregister(module, globals).Kind);
	}

	[Fact]
	public void RegisterReportsARollbackCompletedByTheResidualReleaseAsNotApplied()
	{
		ILuaModule module = Harness.CreateModule();
		FakeLuaGlobals globals = ModuleHarness.CreateGlobals();
		globals.FailPublications.Add(Marker);
		globals.FailClearsOnce.Add(Ping);

		CheatEngineFailure failure = RegisterFailure(module, globals);

		// The SDK compensation could not clear ping and kept it in a residual lease; the generated registrar released that
		// residual lease once more, inside the same admitted operation, and it succeeded.
		Assert.Equal(CheatEngineFailureKind.LuaError, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotApplied, failure.HostEffect);
		Assert.Contains("The rollback reported PartiallyReleased (removed 1, replaced 1, remaining 1, failed ping).",
			failure.Message, StringComparison.Ordinal);
		Assert.Contains("The release of what the rollback left reported Released (removed 1, replaced 0, remaining 0).",
			failure.Message, StringComparison.Ordinal);
		Assert.All(ModuleHarness.Exports, export => Assert.Null(globals[export]));
	}

	[Fact]
	public void RegisterReportsARollbackThatLeftAGlobalAsAnUnconfirmedCleanup()
	{
		ILuaModule module = Harness.CreateModule();
		FakeLuaGlobals globals = ModuleHarness.CreateGlobals();
		globals.FailPublications.Add(Marker);
		globals.FailClears.Add(Ping);

		CheatEngineFailure failure = RegisterFailure(module, globals);

		Assert.Equal(CheatEngineFailureKind.LuaError, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, failure.HostEffect);
		Assert.Contains("The release of what the rollback left reported PartiallyReleased (removed 0, replaced 0, " +
						"remaining 1, failed ping).", failure.Message, StringComparison.Ordinal);
		Assert.NotNull(globals[Ping]);
		Assert.Null(globals[Status]);
		Assert.Equal(LeaseReleaseKind.AlreadyReleased, ModuleHarness.Unregister(module, globals).Kind);
	}

	[Theory]
	[InlineData(LuaRegistrationResultKind.Succeeded, CheatEngineHostEffect.CleanupUnconfirmed)]
	[InlineData(LuaRegistrationResultKind.Unspecified, CheatEngineHostEffect.Unknown)]
	[InlineData((LuaRegistrationResultKind) 99, CheatEngineHostEffect.Unknown)]
	public void RegisterFailsClosedOnAResultWithoutALease(LuaRegistrationResultKind reported,
		CheatEngineHostEffect expectedEffect)
	{
		ILuaModule module = Harness.CreateModule();
		FakeLuaGlobals globals = ModuleHarness.CreateGlobals();
		globals.NextPublication = new FakePublication(reported, null, null, null,
			new FakeRelease(LuaRegistrationReleaseKind.NotAttempted, 0, 0, 0, 0, []));

		CheatEngineFailure failure = RegisterFailure(module, globals);

		// A success without its lease may have published globals that no lease can remove.
		Assert.Equal(CheatEngineFailureKind.IndeterminateHostResult, failure.Kind);
		Assert.Equal(expectedEffect, failure.HostEffect);
		Assert.Equal("Lua.RegisterModule", failure.Operation);
		Assert.Equal(0, globals.OpenOperationCount);
		Assert.Equal(LeaseReleaseKind.AlreadyReleased, ModuleHarness.Unregister(module, globals).Kind);
	}

	[Theory]
	[InlineData(LuaAdmissionStatus.Detached, CheatEngineFailureKind.ActivationExpired)]
	[InlineData(LuaAdmissionStatus.TransitionInProgress, CheatEngineFailureKind.ActivationExpired)]
	[InlineData(LuaAdmissionStatus.ExternalStateReset, CheatEngineFailureKind.RuntimeChanged)]
	[InlineData(LuaAdmissionStatus.ThreadNotAdmitted, CheatEngineFailureKind.InvalidState)]
	[InlineData(LuaAdmissionStatus.NoStateForThread, CheatEngineFailureKind.InvalidState)]
	[InlineData(LuaAdmissionStatus.Unknown, CheatEngineFailureKind.IndeterminateHostResult)]
	[InlineData((LuaAdmissionStatus) 99, CheatEngineFailureKind.IndeterminateHostResult)]
	public void RegisterWithoutAdmissionIsRefusedBeforeAnyLuaCall(LuaAdmissionStatus admission,
		CheatEngineFailureKind expected)
	{
		ILuaModule module = Harness.CreateModule();
		FakeLuaGlobals globals = ModuleHarness.CreateGlobals();
		globals.Admission = admission;

		CheatEngineFailure failure = RegisterFailure(module, globals);

		Assert.Equal(expected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Contains(admission.ToString(), failure.Message, StringComparison.Ordinal);
		Assert.Empty(globals.Log);
	}

	[Fact]
	public void RegisterAgainReleasesTheEarlierRegistrationFirst()
	{
		(ILuaModule module, FakeLuaGlobals globals) = Registered();
		globals.ReplaceLuaState();
		globals.ClearLog();

		ModuleHarness.Register(module, globals);
		LuaModuleReleaseOutcome outcome = ModuleHarness.Unregister(module, globals);

		// The earlier lease is stale in the new state: it is forgotten without a Lua operation, then the exports are
		// published again and released by the new lease.
		Assert.Equal(
			["read:status", "read:ping", "read:marker", "write:status", "write:ping", "write:marker", "read:status",
				"clear:status", "read:ping", "clear:ping", "read:marker", "clear:marker"],
			globals.Log);
		AssertOutcome(outcome, LeaseReleaseKind.Released, 3, 0, 0);
	}

	[Fact]
	public void RegisterAgainPublishesNothingWhenTheEarlierReleaseLeftAGlobal()
	{
		(ILuaModule module, FakeLuaGlobals globals) = Registered();
		FakeLuaValue kept = globals[Ping]!;
		globals.FailClears.Add(Ping);

		CheatEngineFailure failure = RegisterFailure(module, globals);

		// The module's own leftover function is not reported as a third party's global: the partial release is the failure.
		Assert.Equal(CheatEngineFailureKind.LuaError, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, failure.HostEffect);
		Assert.Equal("Client Lua module 'plugin' could not release its earlier registration before registering again; " +
					 "nothing was published. The release reported PartiallyReleased (removed 2, replaced 0, remaining 1, " +
					 "failed ping).", failure.Message);
		Assert.Equal(0, globals.CountOf("write"));
		Assert.Same(kept, globals[Ping]);
		Assert.Equal(0, globals.OpenOperationCount);
		// The earlier lease was consumed by that release: nothing is left for Unregister to retry.
		Assert.Equal(LeaseReleaseKind.AlreadyReleased, ModuleHarness.Unregister(module, globals).Kind);
	}

	[Theory]
	[InlineData(LuaRegistrationReleaseKind.NotAttempted)]
	[InlineData((LuaRegistrationReleaseKind) 99)]
	public void RegisterAgainPublishesNothingWhenTheEarlierReleaseIsNotUnderstood(LuaRegistrationReleaseKind reported)
	{
		(ILuaModule module, FakeLuaGlobals globals) = Registered();
		globals.NextRelease = new FakeRelease(reported, 0, 0, 0, 3, []);

		CheatEngineFailure failure = RegisterFailure(module, globals);

		// A release CheatEngine.SDK reports outside its documented shape is not confirmed: nothing is published.
		Assert.Equal(CheatEngineFailureKind.IndeterminateHostResult, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, failure.HostEffect);
		Assert.Equal(0, globals.CountOf("write"));
		Assert.Equal(0, globals.OpenOperationCount);
		Assert.Equal(LeaseReleaseKind.AlreadyReleased, ModuleHarness.Unregister(module, globals).Kind);
	}

	[Fact]
	public void RegisterAgainAfterAReattachNamesTheStaleReleaseInTheCollision()
	{
		(ILuaModule module, FakeLuaGlobals globals) = Registered();
		globals.Reattach();

		CheatEngineFailure failure = RegisterFailure(module, globals);

		// The earlier attachment's functions stay in the same Lua state: the collision is with the module's own globals.
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotApplied, failure.HostEffect);
		Assert.Equal("Lua global 'status' is already defined and cannot be replaced by Client module 'plugin'. The " +
					 "release of the module's earlier registration reported Stale (removed 0, replaced 0, remaining 3).",
			failure.Message);
		Assert.Equal(["read:status"], globals.Log);
	}

	[Fact]
	public void ARefusedRegistrationKeepsTheRegistrationTheModuleAlreadyOwned()
	{
		(ILuaModule module, FakeLuaGlobals globals) = Registered();
		globals.Admission = LuaAdmissionStatus.TransitionInProgress;

		Assert.Equal(CheatEngineFailureKind.ActivationExpired, RegisterFailure(module, globals).Kind);
		globals.Admission = LuaAdmissionStatus.Admitted;
		LuaModuleReleaseOutcome outcome = ModuleHarness.Unregister(module, globals);

		AssertOutcome(outcome, LeaseReleaseKind.Released, 3, 0, 0);
	}

	[Fact]
	public void RegisterAndUnregisterEachRunInOneAdmittedOperationThatTheyEnd()
	{
		(ILuaModule module, FakeLuaGlobals globals) = Registered();

		LuaModuleReleaseOutcome outcome = ModuleHarness.Unregister(module, globals);
		globals.Admission = LuaAdmissionStatus.Detached;
		_ = RegisterFailure(Harness.CreateModule(), globals);

		Assert.Equal(LeaseReleaseKind.Released, outcome.Kind);
		// The registration, then the release; the refused registration held no operation.
		Assert.Equal(2, globals.AdmittedOperationCount);
		Assert.Equal(0, globals.OpenOperationCount);
		// The module handed the registrar its bindings' registration with the RejectExisting policy, once.
		Assert.Equal([LuaRegistrationCollisionPolicy.RejectExisting], globals.BindingsRegistrations);
	}

	[Fact]
	public void AFailedPublicationEndsItsOperationAfterReleasingTheResidualLease()
	{
		ILuaModule module = Harness.CreateModule();
		FakeLuaGlobals globals = ModuleHarness.CreateGlobals();
		globals.FailPublications.Add(Marker);
		globals.FailClearsOnce.Add(Ping);

		_ = RegisterFailure(module, globals);

		// The rollback, the residual release and the publication share one admitted operation, ended before the throw.
		Assert.Equal(1, globals.AdmittedOperationCount);
		Assert.Equal(0, globals.OpenOperationCount);
		Assert.Equal(["read:ping", "clear:ping"], globals.Log.TakeLast(2));
	}

	private static (ILuaModule Module, FakeLuaGlobals Globals) Registered()
	{
		ILuaModule module = Harness.CreateModule();
		FakeLuaGlobals globals = ModuleHarness.CreateGlobals();
		ModuleHarness.Register(module, globals);
		Assert.Equal(
			["read:status", "read:ping", "read:marker", "write:status", "write:ping", "write:marker"],
			globals.Log);
		Assert.Equal(0, globals.OpenOperationCount);
		globals.ClearLog();
		return (module, globals);
	}

	private static CheatEngineFailure RegisterFailure(ILuaModule module, FakeLuaGlobals globals)
	{
		// The exception type follows the failure kind (CheatEngineFailure.ToException); the failure is the contract.
		CheatEngineClientException exception =
			Assert.ThrowsAny<CheatEngineClientException>(() => ModuleHarness.Register(module, globals));
		return exception.Failure;
	}

	private static void AssertOutcome(LuaModuleReleaseOutcome outcome, LeaseReleaseKind kind, int removed,
		int replaced, int remaining)
	{
		Assert.Equal(ModuleHarness.ModuleName, outcome.ModuleName);
		Assert.Equal(kind, outcome.Kind);
		Assert.Equal(removed, outcome.RemovedCount);
		Assert.Equal(0, outcome.RestoredCount);
		Assert.Equal(replaced, outcome.ReplacementCount);
		Assert.Equal(remaining, outcome.RemainingCount);
		Assert.Empty(outcome.FailedExports);
	}
}
