#pragma warning disable CECLIENT5004 // These tests exercise the experimental Auto Assembler client.

using CheatEngine.Client.Assembly;
using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Domains.Assembly;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Assembly;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Tests.Domains;

/// <summary>
///     The opt-in Auto Assembler client over a fake port (plan L17): the policy refusal without the opt-in (Q44), the
///     activation and release of a patch lease (Q35), every mapped outcome, and the lease rules (one disable attempt,
///     refusal on a changed target, a <c>Dispose</c> that never throws). A patch applied in a process selected in Cheat
///     Engine's own window stays with that process, and a registration the activation or the selection refused reports
///     what the release of the patch left.
/// </summary>
public sealed class AutoAssemblerClientTests : IDisposable
{
	private const string Script = "[ENABLE]\r\nalloc(probe,4)\r\n[DISABLE]\r\ndealloc(probe)";

	private readonly ControlledCoreLifetimeContext _context = new();
	private readonly RecordingDiagnostics _diagnostics = new();
	private readonly SdkMainThreadDispatcher _dispatcher;
	private readonly TrackingInvoker _invoker = new();
	private readonly CoreLifetime _lifetime;
	private readonly FakePort _port;
	private readonly ProcessClient _processes;
	private readonly FakeSelectedTarget _target = new();

	public AutoAssemblerClientTests()
	{
		_diagnostics.Invoker = _invoker;
		_lifetime = new CoreLifetime(_context, _diagnostics);
		_dispatcher = new SdkMainThreadDispatcher(_lifetime, _invoker);
		_port = new FakePort(_invoker);
		_processes = FakeSelectedTarget.CreateProcessClient(_dispatcher, _target);
	}

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	public void Dispose()
	{
		_context.Dispose();
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void WithoutTheOptInEveryCallIsRefusedBeforeAnyCheatEngineCall()
	{
		AutoAssemblerClient client = CreateClient(enabled: false);
		CancellationToken token = TestContext.Current.CancellationToken;

		bool applied = client.TryApplyPatch(new AutoAssemblerScript(Script), out IAutoAssemblerPatchLease? lease,
			out CheatEngineFailure applyFailure, token);
		bool checkedScript = client.TryCheck(new AutoAssemblerScript(Script), out AutoAssemblerCheckResult result,
			out CheatEngineFailure checkFailure, token);

		Assert.False(applied);
		Assert.Null(lease);
		Assert.False(checkedScript);
		Assert.Equal(default, result);
		AssertPolicyRefusal(applyFailure, AutoAssemblerClient.ApplyOperation);
		AssertPolicyRefusal(checkFailure, AutoAssemblerClient.CheckOperation);
		Assert.Equal(0, _port.ApplyCalls);
		Assert.Equal(0, _port.CheckCalls);
		Assert.Equal(0, _invoker.Calls);
		Assert.Equal(
			[
				(ClientCapabilityId.AutoAssemblerPatches.Value, AutoAssemblerClient.ApplyOperation),
				(ClientCapabilityId.AutoAssemblerPatches.Value, AutoAssemblerClient.CheckOperation)
			],
			_diagnostics.Refusals);
		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable,
			Assert.Throws<CheatEngineOperationException>(() => client.ApplyPatch(new AutoAssemblerScript(Script), token))
				.Failure.Kind);
		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable,
			Assert.Throws<CheatEngineOperationException>(() => client.Check(new AutoAssemblerScript(Script), token))
				.Failure.Kind);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void ThePolicyEnablesAutoAssemblerPatchesOnlyThroughItsOwnOptIn()
	{
		Assert.False(CoreClientPolicy.SafeDefaults.EnableAutoAssemblerPatches);
		Assert.False(new CoreClientPolicy([], true).EnableAutoAssemblerPatches);
		Assert.True(new CoreClientPolicy([], false, enableAutoAssemblerPatches: true).EnableAutoAssemblerPatches);
		Assert.False(new CoreClientPolicy([], false, enableAutoAssemblerPatches: true).EnableUnsafeLuaExecution);
	}

	[Fact]
	public void CancellationIsObservedBeforeThePolicyGateAndBeforeDispatch()
	{
		AutoAssemblerClient refused = CreateClient(enabled: false);
		AutoAssemblerClient enabled = CreateClient();
		CancellationToken cancelled = new(true);

		Assert.False(refused.TryApplyPatch(new AutoAssemblerScript(Script), out _, out CheatEngineFailure failure,
			cancelled));
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Empty(_diagnostics.Refusals);
		CheatEngineOperationCanceledException exception = Assert.Throws<CheatEngineOperationCanceledException>(() =>
			enabled.ApplyPatch(new AutoAssemblerScript(Script), cancelled));
		Assert.Equal(CheatEngineHostEffect.NotStarted, exception.Failure.HostEffect);
		Assert.Throws<CheatEngineOperationCanceledException>(() =>
			enabled.Check(new AutoAssemblerScript(Script), cancelled));
		Assert.Equal(0, _invoker.Calls);
		Assert.Equal(0, _port.ApplyCalls);
	}

	[Fact]
	public void TheDefaultScriptAndAnEndedActivationAreRejectedBeforeAnyWork()
	{
		AutoAssemblerClient client = CreateClient();
		CancellationToken token = TestContext.Current.CancellationToken;

		Assert.Throws<ArgumentException>(() => client.TryApplyPatch(default, out _, out _, token));
		Assert.Throws<ArgumentException>(() => client.TryCheck(default, out _, out _, token));
		_context.IsCurrent = false;
		Assert.Throws<CheatEngineActivationExpiredException>(() =>
			client.TryApplyPatch(new AutoAssemblerScript(Script), out _, out _, token));
		Assert.Equal(0, _invoker.Calls);
	}

	[Fact]
	[Trait("Qualification", "Q35")]
	public void AnAppliedPatchIsATargetBoundLeaseThatDisablesOnceOnTheMainThread()
	{
		_port.ApplyFacts = Facts(AutoAssemblerApplyOutcomeKind.Applied, warnings: "unused label", warningsTruncated: true);
		AutoAssemblerClient client = CreateClient();

		bool applied = client.TryApplyPatch(new AutoAssemblerScript(Script, "probe.patch"),
			out IAutoAssemblerPatchLease? lease, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(applied);
		Assert.Equal(default, failure);
		Assert.NotNull(lease);
		Assert.Equal([Script], _port.Scripts);
		Assert.Same(AutoAssemblerClient.Options, _port.LastOptions);
		Assert.True(_port.LastOptions!.CaptureHostText);
		Assert.Equal(AutoAssemblerClient.HostTextByteLimit, _port.LastOptions.MaxHostTextBytes);
		Assert.Equal(AutoAssemblerOptions.MinDisableInfoEntries, _port.LastOptions.MaxDisableInfoEntries);
		Assert.Equal(AutoAssemblerOptions.MinDisableInfoNameBytes, _port.LastOptions.MaxDisableInfoNameBytes);
		Assert.True(_port.RanOnMainThread);
		Assert.Equal("probe.patch", lease.Name);
		Assert.Equal(0, lease.SelectionEpoch);
		Assert.False(lease.AppliedAfterTargetChange);
		Assert.Equal("unused label", lease.HostWarnings);
		Assert.True(lease.HostWarningsTruncated);
		Assert.True(lease.CanDisable);
		Assert.False(lease.IsReleased);
		Assert.False(lease.RequiresManualRecovery);
		Assert.Empty(_diagnostics.TargetChangeWarnings);

		LeaseReleaseOutcome outcome = lease.Release();
		LeaseReleaseOutcome repeated = lease.Release();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed), outcome);
		Assert.Equal(outcome, repeated);
		Assert.Equal(1, _port.Owner!.ReleaseCalls);
		Assert.Equal([true], _port.Owner.ReleasedOnMainThread);
		Assert.True(lease.IsReleased);
		Assert.False(lease.CanDisable);
		Assert.False(lease.RequiresManualRecovery);
		Assert.Equal(outcome, lease.LastReleaseOutcome);
	}

	[Fact]
	[Trait("Qualification", "Q35")]
	public void AnActivationAppliedWhileTheTargetChangedIsReturnedWithAWarning()
	{
		_port.ApplyFacts = Facts(AutoAssemblerApplyOutcomeKind.AppliedTargetChanged);
		AutoAssemblerClient client = CreateClient();

		IAutoAssemblerPatchLease lease =
			client.ApplyPatch(new AutoAssemblerScript(Script), TestContext.Current.CancellationToken);

		Assert.True(lease.AppliedAfterTargetChange);
		Assert.Equal([(AutoAssemblerClient.ApplyOperation, 0L, false)], _diagnostics.TargetChangeWarnings);
	}

	[Theory]
	[Trait("Qualification", "Q35")]
	[InlineData(AutoAssemblerApplyOutcomeKind.Rejected, CheatEngineFailureKind.OperationRejected,
		CheatEngineHostEffect.Unknown)]
	[InlineData(AutoAssemblerApplyOutcomeKind.GlobalUnavailable, CheatEngineFailureKind.CapabilityUnavailable,
		CheatEngineHostEffect.NotStarted)]
	[InlineData(AutoAssemblerApplyOutcomeKind.ProtectedLuaFailure, CheatEngineFailureKind.LuaError,
		CheatEngineHostEffect.Unknown)]
	[InlineData(AutoAssemblerApplyOutcomeKind.InvalidResult, CheatEngineFailureKind.InvalidHostResult,
		CheatEngineHostEffect.Unknown)]
	[InlineData(AutoAssemblerApplyOutcomeKind.TargetIdentityUnavailable,
		CheatEngineFailureKind.TargetIdentityUnavailable, CheatEngineHostEffect.NotStarted)]
	[InlineData(AutoAssemblerApplyOutcomeKind.HandoffFailed, CheatEngineFailureKind.BindingError,
		CheatEngineHostEffect.CleanupUnconfirmed)]
	[InlineData(AutoAssemblerApplyOutcomeKind.Unknown, CheatEngineFailureKind.IndeterminateHostResult,
		CheatEngineHostEffect.Unknown)]
	public void AFailedActivationIsMappedWithoutALease(AutoAssemblerApplyOutcomeKind kind,
		CheatEngineFailureKind expectedKind, CheatEngineHostEffect expectedEffect)
	{
		_port.ApplyFacts = Facts(kind);
		_port.Owner = null;
		AutoAssemblerClient client = CreateClient();

		bool applied = client.TryApplyPatch(new AutoAssemblerScript(Script), out IAutoAssemblerPatchLease? lease,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(applied);
		Assert.Null(lease);
		Assert.Equal(expectedKind, failure.Kind);
		Assert.Equal(expectedEffect, failure.HostEffect);
		Assert.Equal(AutoAssemblerClient.ApplyOperation, failure.Operation);
		Assert.Null(failure.Exception);
		Assert.Empty(_diagnostics.TargetChangeWarnings);
	}

	[Fact]
	public void ARejectionCarriesCheatEnginesBoundedTextAndAHandoffFailureItsCompensation()
	{
		_port.Owner = null;
		AutoAssemblerClient client = CreateClient();
		CancellationToken token = TestContext.Current.CancellationToken;

		_port.ApplyFacts = Facts(AutoAssemblerApplyOutcomeKind.Rejected, "Error in line 2", hostTextTruncated: true);
		Assert.False(client.TryApplyPatch(new AutoAssemblerScript(Script), out _, out CheatEngineFailure rejected,
			token));
		_port.ApplyFacts = Facts(AutoAssemblerApplyOutcomeKind.HandoffFailed,
			compensation: TargetReleaseStatus.Released);
		Assert.False(client.TryApplyPatch(new AutoAssemblerScript(Script), out _, out CheatEngineFailure handoff, token));
		_port.ApplyFacts = Facts(AutoAssemblerApplyOutcomeKind.ProtectedLuaFailure);
		Assert.False(client.TryApplyPatch(new AutoAssemblerScript(Script), out _, out CheatEngineFailure lua, token));

		Assert.EndsWith("Cheat Engine reported: Error in line 2 [truncated]", rejected.Message, StringComparison.Ordinal);
		Assert.Contains("ended as Released", handoff.Message, StringComparison.Ordinal);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, handoff.HostEffect);
		Assert.Contains(LuaStatus.RuntimeError.ToString(), lua.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void AnOwnerNextToAFailureIsReleasedAndAnAppliedOutcomeWithoutAnOwnerIsIndeterminate()
	{
		AutoAssemblerClient client = CreateClient();
		CancellationToken token = TestContext.Current.CancellationToken;
		FakeOwner stray = new(_invoker);
		_port.Owner = stray;
		_port.ApplyFacts = Facts(AutoAssemblerApplyOutcomeKind.Rejected);

		Assert.False(client.TryApplyPatch(new AutoAssemblerScript(Script), out _, out CheatEngineFailure rejected,
			token));
		_port.Owner = null;
		_port.ApplyFacts = Facts(AutoAssemblerApplyOutcomeKind.Applied);
		Assert.False(client.TryApplyPatch(new AutoAssemblerScript(Script), out _, out CheatEngineFailure ownerless,
			token));

		Assert.Equal(CheatEngineFailureKind.OperationRejected, rejected.Kind);
		Assert.Equal(1, stray.ReleaseCalls);
		Assert.Equal(CheatEngineFailureKind.IndeterminateHostResult, ownerless.Kind);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, ownerless.HostEffect);
	}

	[Fact]
	public void AnOwnerNextToAFailureWhoseReleaseIsNotCompleteLeavesTheCleanupUnconfirmed()
	{
		AutoAssemblerClient client = CreateClient();
		FakeOwner stray = new(_invoker)
		{
			ReleaseStatus = TargetReleaseStatus.UnconfirmedAfterInvocation
		};
		_port.Owner = stray;
		_port.ApplyFacts = Facts(AutoAssemblerApplyOutcomeKind.Rejected);

		Assert.False(client.TryApplyPatch(new AutoAssemblerScript(Script), out _, out CheatEngineFailure rejected,
			TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.OperationRejected, rejected.Kind);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, rejected.HostEffect);
		Assert.Equal(1, stray.ReleaseCalls);
	}

	[Fact]
	public void ARefusedLuaAdmissionAndAnSdkFaultAreReturnedAsFailures()
	{
		AutoAssemblerClient client = CreateClient();
		CancellationToken token = TestContext.Current.CancellationToken;
		CheatEngineFailure refusal = new(CheatEngineFailureKind.ActivationExpired, AutoAssemblerClient.ApplyOperation,
			"Detached.", null, CheatEngineHostEffect.NotStarted);
		_port.Admission = refusal;

		Assert.False(client.TryApplyPatch(new AutoAssemblerScript(Script), out _, out CheatEngineFailure refused,
			token));
		Assert.False(client.TryCheck(new AutoAssemblerScript(Script), out _, out CheatEngineFailure refusedCheck,
			token));
		_port.Admission = null;
		EngineGlobalUnavailableException fault = new("AutoAssemblerApply");
		_port.Fault = fault;
		Assert.False(client.TryApplyPatch(new AutoAssemblerScript(Script), out _, out CheatEngineFailure faulted,
			token));

		Assert.Equal(refusal, refused);
		Assert.Equal(CheatEngineFailureKind.ActivationExpired, refusedCheck.Kind);
		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, faulted.Kind);
		Assert.Equal(CheatEngineHostEffect.Unknown, faulted.HostEffect);
		Assert.Same(fault, faulted.Exception);
	}

	/// <summary>
	///     A patch the port could not publish, whose one disable was not confirmed, is reported like the AOB result list:
	///     the publication fault keeps its classification and the unconfirmed release makes it CleanupUnconfirmed.
	/// </summary>
	[Theory]
	[Trait("Qualification", "Q35")]
	[InlineData(TargetReleaseStatus.UnconfirmedAfterInvocation, "CleanupUnconfirmed")]
	[InlineData(TargetReleaseStatus.NotInvoked, "RefusedRuntimeChanged")]
	public void APatchWhosePublicationFailedAndWhoseDisableWasNotConfirmedIsCleanupUnconfirmed(
		TargetReleaseStatus disable, string expectedKind)
	{
		InvalidOperationException publishFailure = new("the patch owner could not be published");
		AutoAssemblerClient client = CreateClient();
		// The handoff releases the unpublished patch with the production port's own mapping.
		_port.DuringApply = () => _ = OwnershipHandoff.Adopt<object, object>(new object(), _ => throw publishFailure,
			_ => SdkAutoAssemblerPort.MapUnpublishedRelease(disable));

		Assert.False(client.TryApplyPatch(new AutoAssemblerScript(Script), out IAutoAssemblerPatchLease? lease,
			out CheatEngineFailure failure, Token));

		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, failure.HostEffect);
		Assert.Equal(AutoAssemblerClient.ApplyOperation, failure.Operation);
		Assert.Same(publishFailure, failure.Exception);
		Assert.EndsWith($"The applied Auto Assembler patch release was not confirmed ({expectedKind}).",
			failure.Message, StringComparison.Ordinal);
		Assert.Equal(0, _port.Owner!.ReleaseCalls);
	}

	/// <summary>
	///     The production port releases a patch it could not publish with the lease's mapping, not the shared one: a
	///     disable that could not begin is the terminal RefusedRuntimeChanged, since CheatEngine.SDK consumed the disable
	///     information, never the retryable CleanupUnavailable.
	/// </summary>
	[Fact]
	public void ThePortMapsTheDisableOfAnUnpublishedPatchLikeTheLease()
	{
		foreach (TargetReleaseStatus status in Enum.GetValues<TargetReleaseStatus>())
		{
			Assert.Equal(AutoAssemblerMapping.ToReleaseOutcome(status),
				SdkAutoAssemblerPort.MapUnpublishedRelease(status));
		}

		LeaseReleaseOutcome notInvoked = SdkAutoAssemblerPort.MapUnpublishedRelease(TargetReleaseStatus.NotInvoked);
		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.RefusedRuntimeChanged, CheatEngineHostEffect.NotStarted),
			notInvoked);
		Assert.NotEqual(SdkReleaseOutcomes.FromTarget(TargetReleaseStatus.NotInvoked), notInvoked);
	}

	[Theory]
	[Trait("Qualification", "Q35")]
	[InlineData(TargetReleaseStatus.Released, CheatEngineHostEffect.Completed, "which was released at once")]
	[InlineData(TargetReleaseStatus.RefusedTargetChanged, CheatEngineHostEffect.CleanupUnconfirmed,
		"ended with RefusedTargetChanged")]
	public void ALeaseThatCannotBeRegisteredIsReleasedAtOnce(TargetReleaseStatus release,
		CheatEngineHostEffect expectedEffect, string expected)
	{
		_port.Owner!.ReleaseStatus = release;
		AutoAssemblerClient client = new(_dispatcher, new CoreClientPolicy([], false, true), _lifetime,
			new StaleSelectionBinder(), _port);

		bool applied = client.TryApplyPatch(new AutoAssemblerScript(Script), out IAutoAssemblerPatchLease? lease,
			out CheatEngineFailure failure, Token);

		Assert.False(applied);
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.TargetChanged, failure.Kind);
		Assert.Equal(expectedEffect, failure.HostEffect);
		Assert.IsType<CheatEngineInvalidStateException>(failure.Exception);
		Assert.Contains(expected, failure.Message, StringComparison.Ordinal);
		Assert.Equal(1, _port.Owner.ReleaseCalls);
		Assert.Equal([true], _port.Owner.ReleasedOnMainThread);
		Assert.Throws<CheatEngineOperationException>(() => client.ApplyPatch(new AutoAssemblerScript(Script), Token));
	}

	[Fact]
	[Trait("Qualification", "Q43")]
	public void AnActivationDuringTheDeactivationCleanupIsRefusedBeforeCheatEngineApplies()
	{
		AutoAssemblerClient client = CreateClient();
		_context.Stop();
		using (_lifetime.EnterCleanupScope())
		{
			Assert.Throws<CheatEngineInvalidStateException>(() =>
				client.TryApplyPatch(new AutoAssemblerScript(Script), out _, out _, Token));
		}

		// Like every lease-creating operation, an activation is admitted with ThrowIfInactive, which makes no exception for
		// a cleanup scope: the admission refuses it before anything is dispatched, and nothing is applied or released.
		Assert.Equal(0, _invoker.Calls);
		Assert.Equal(0, _port.ApplyCalls);
		Assert.Equal(0, _port.Owner!.ReleaseCalls);
	}

	[Theory]
	[Trait("Qualification", "Q43")]
	[InlineData(false, typeof(CheatEngineInvalidStateException))]
	[InlineData(true, typeof(CheatEngineActivationExpiredException))]
	public void AnActivationThatStopsOrEndsAfterItsAdmissionIsRefusedBeforeCheatEngineApplies(bool ends,
		Type expected)
	{
		AutoAssemblerClient client = CreateClient();
		// The admission and the dispatch let the activation through; it stops or ends before the callback runs on
		// Cheat Engine's main thread, so only the callback's own check can refuse it.
		_invoker.BeforeCallback = () =>
		{
			if (ends)
			{
				_context.IsCurrent = false;
			}
			else
			{
				_context.Stop();
			}
		};

		CheatEngineClientException refused = Assert.ThrowsAny<CheatEngineClientException>(() =>
			client.TryApplyPatch(new AutoAssemblerScript(Script), out _, out _, Token));

		Assert.IsType(expected, refused);
		Assert.Equal(AutoAssemblerClient.ApplyOperation, refused.Failure.Operation);
		Assert.Equal(1, _invoker.Calls);
		Assert.Equal(0, _port.ApplyCalls);
		Assert.Equal(0, _port.Owner!.ReleaseCalls);
	}

	[Theory]
	[Trait("Qualification", "Q43")]
	[InlineData(TargetReleaseStatus.Released, "which was released at once")]
	[InlineData(TargetReleaseStatus.UnconfirmedAfterInvocation, "ended with CleanupUnconfirmed")]
	public void AnActivationStoppingDuringTheApplyThrowsWhatTheReleaseLeft(TargetReleaseStatus release,
		string expected)
	{
		_port.Owner!.ReleaseStatus = release;
		_port.DuringApply = _context.Stop;
		AutoAssemblerClient client = CreateClient();

		CheatEngineInvalidStateException stopping = Assert.Throws<CheatEngineInvalidStateException>(() =>
			client.TryApplyPatch(new AutoAssemblerScript(Script), out _, out _, Token));

		Assert.Contains(expected, stopping.Message, StringComparison.Ordinal);
		Assert.Equal(AutoAssemblerClient.ApplyOperation, stopping.Failure.Operation);
		Assert.Equal(1, _port.Owner.ReleaseCalls);
		Assert.Equal([true], _port.Owner.ReleasedOnMainThread);
	}

	[Fact]
	[Trait("Qualification", "Q35")]
	public void ATargetChangeObservedDuringTheApplyKeysTheLeaseToTheProcessItWasAppliedIn()
	{
		_ = _processes.GetCurrentProcess(Token);
		_port.Owner!.ReleaseStatus = TargetReleaseStatus.RefusedTargetChanged;
		// CheatEngine.SDK bound the patch to process 42; Cheat Engine then selects process 43, which the Client
		// observes before the lease is registered.
		_port.DuringApply = () =>
		{
			_target.Select(FakeSelectedTarget.OtherProcessIncarnation);
			_ = _processes.GetCurrentProcess(Token);
		};
		AutoAssemblerClient client = CreateClient();

		IAutoAssemblerPatchLease lease = client.ApplyPatch(new AutoAssemblerScript(Script), Token);
		bool releasedWhenPublished = lease.IsReleased;
		ProcessSnapshot observed = _processes.GetCurrentProcess(Token);

		Assert.False(releasedWhenPublished);
		// The next observation of process 43 ends the lease of process 42: CheatEngine.SDK refuses the disable there.
		Assert.True(observed.SelectionEpoch > lease.SelectionEpoch);
		Assert.True(lease.IsReleased);
		Assert.Equal(LeaseReleaseKind.RefusedTargetChanged, lease.LastReleaseOutcome?.Kind);
		Assert.True(lease.RequiresManualRecovery);
		Assert.Equal(1, _port.Owner.ReleaseCalls);
	}

	[Fact]
	[Trait("Qualification", "Q35")]
	public void APatchAppliedInAProcessSelectedInCheatEngineStaysWithThatProcess()
	{
		long observedEpoch = _processes.GetCurrentProcess(Token).SelectionEpoch;
		AutoAssemblerClient client = CreateClient();
		FakeOwner first = _port.Owner!;
		first.ReleaseStatus = TargetReleaseStatus.RefusedTargetChanged;
		IAutoAssemblerPatchLease inFirst = client.ApplyPatch(new AutoAssemblerScript(Script), Token);
		// Cheat Engine's own window selects another process: no Client call observes it.
		_target.Select(FakeSelectedTarget.OtherProcessIncarnation);
		FakeOwner second = new(_invoker)
		{
			TargetIncarnation = FakeSelectedTarget.OtherProcessIncarnation
		};
		_port.Owner = second;

		IAutoAssemblerPatchLease inSecond = client.ApplyPatch(new AutoAssemblerScript(Script), Token);
		long secondEpoch = inSecond.SelectionEpoch;
		ProcessSnapshot observed = _processes.GetCurrentProcess(Token);

		// The first patch's process is no longer selected: its lease ended when the second patch was bound, and
		// CheatEngine.SDK refused to disable it in the new target.
		Assert.Equal(observedEpoch, inFirst.SelectionEpoch);
		Assert.True(inFirst.IsReleased);
		Assert.Equal(LeaseReleaseKind.RefusedTargetChanged, inFirst.LastReleaseOutcome?.Kind);
		Assert.Equal(1, first.ReleaseCalls);
		// The second patch belongs to the selection the next observation finds, which releases nothing.
		Assert.True(secondEpoch > observedEpoch);
		Assert.Equal(secondEpoch, observed.SelectionEpoch);
		Assert.Equal(new TargetProcessId(43), observed.Id);
		Assert.False(inSecond.IsReleased);
		Assert.True(inSecond.CanDisable);
		Assert.Equal(0, second.ReleaseCalls);
	}

	[Fact]
	[Trait("Qualification", "Q35")]
	public void APatchInTheObservedProcessKeepsTheObservedSelection()
	{
		long observedEpoch = _processes.GetCurrentProcess(Token).SelectionEpoch;
		AutoAssemblerClient client = CreateClient();

		IAutoAssemblerPatchLease lease = client.ApplyPatch(new AutoAssemblerScript(Script), Token);
		ProcessSnapshot observed = _processes.GetCurrentProcess(Token);

		Assert.Equal(observedEpoch, lease.SelectionEpoch);
		Assert.Equal(observedEpoch, observed.SelectionEpoch);
		Assert.False(lease.IsReleased);
		Assert.Equal(0, _port.Owner!.ReleaseCalls);
	}

	[Fact]
	[Trait("Qualification", "Q35")]
	public void SelectingAnotherTargetEndsTheLeaseWithoutDisablingThePatch()
	{
		AutoAssemblerClient client = CreateClient();
		IAutoAssemblerPatchLease lease =
			client.ApplyPatch(new AutoAssemblerScript(Script), TestContext.Current.CancellationToken);
		// The Client observes the change after Cheat Engine already targets the new process, so CheatEngine.SDK refuses
		// the disable there and consumes the disable information: the patch stays in the previous process.
		_port.Owner!.ReleaseStatus = TargetReleaseStatus.RefusedTargetChanged;

		_ = _lifetime.TargetSelection.Advance("Processes.Attach");
		LeaseReleaseOutcome repeated = lease.Release();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.RefusedTargetChanged, CheatEngineHostEffect.NotStarted),
			lease.LastReleaseOutcome);
		Assert.True(lease.IsReleased);
		Assert.True(lease.RequiresManualRecovery);
		Assert.False(lease.CanDisable);
		Assert.Equal(lease.LastReleaseOutcome, repeated);
		Assert.Equal(1, _port.Owner.ReleaseCalls);
	}

	[Theory]
	[InlineData(TargetReleaseStatus.RefusedRuntimeChanged, LeaseReleaseKind.RefusedRuntimeChanged)]
	[InlineData(TargetReleaseStatus.NotInvoked, LeaseReleaseKind.RefusedRuntimeChanged)]
	[InlineData(TargetReleaseStatus.RefusedIdentityUnavailable, LeaseReleaseKind.RefusedTargetIdentityUnavailable)]
	[InlineData(TargetReleaseStatus.UnconfirmedAfterInvocation, LeaseReleaseKind.CleanupUnconfirmed)]
	public void AReleaseThatConsumedTheDisableInformationWithoutConfirmationRequiresManualRecovery(
		TargetReleaseStatus status, LeaseReleaseKind expected)
	{
		AutoAssemblerClient client = CreateClient();
		IAutoAssemblerPatchLease lease =
			client.ApplyPatch(new AutoAssemblerScript(Script), TestContext.Current.CancellationToken);
		_port.Owner!.ReleaseStatus = status;

		LeaseReleaseOutcome outcome = lease.Release();

		Assert.Equal(expected, outcome.Kind);
		Assert.True(outcome.RequiresManualRecovery);
		Assert.True(lease.RequiresManualRecovery);
		Assert.True(lease.IsReleased);
	}

	/// <summary>
	///     CheatEngine.SDK 2.0.0 sets its manual-recovery flag only inside a release attempt. After Cheat Engine replaced
	///     its Lua state, the owner reports that it cannot disable and no flag: <c>CanDisable</c> is the early signal, and
	///     only the refused release makes the lease require manual recovery.
	/// </summary>
	[Fact]
	public void ALostDisableInformationClearsCanDisableAndOnlyItsRefusedReleaseRequiresManualRecovery()
	{
		AutoAssemblerClient client = CreateClient();
		IAutoAssemblerPatchLease lease = client.ApplyPatch(new AutoAssemblerScript(Script), Token);
		_port.Owner!.LostDisableInformation = true;
		_port.Owner.ReleaseStatus = TargetReleaseStatus.RefusedRuntimeChanged;

		bool canDisableBefore = lease.CanDisable;
		bool manualRecoveryBefore = lease.RequiresManualRecovery;
		LeaseReleaseOutcome outcome = lease.Release();

		Assert.False(canDisableBefore);
		Assert.False(manualRecoveryBefore);
		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.RefusedRuntimeChanged, CheatEngineHostEffect.NotStarted),
			outcome);
		Assert.True(lease.IsReleased);
		Assert.True(lease.RequiresManualRecovery);
		Assert.Equal(1, _port.Owner.ReleaseCalls);
	}

	/// <summary>
	///     A release status this Client version does not recognize keeps the lease active, but CheatEngine.SDK consumed
	///     the disable information and set its own flag: the lease requires manual recovery, and a later release reports
	///     the recorded status again without another disable.
	/// </summary>
	[Fact]
	public void AnUnrecognizedReleaseStatusKeepsTheLeaseActiveButRequiresManualRecovery()
	{
		AutoAssemblerClient client = CreateClient();
		IAutoAssemblerPatchLease lease = client.ApplyPatch(new AutoAssemblerScript(Script), Token);
		_port.Owner!.ReleaseStatus = (TargetReleaseStatus) 99;

		LeaseReleaseOutcome first = lease.Release();
		LeaseReleaseOutcome second = lease.Release();

		Assert.True(first.IsRetryable);
		Assert.False(first.RequiresManualRecovery);
		Assert.Equal(first, second);
		Assert.False(lease.IsReleased);
		Assert.False(lease.CanDisable);
		Assert.True(lease.RequiresManualRecovery);
		Assert.Equal(1, _port.Owner.ReleaseCalls);
	}

	[Fact]
	public void DisposeNeverThrowsWhenTheReleaseFaultsAndTheDisableIsNeverRetried()
	{
		AutoAssemblerClient client = CreateClient();
		IAutoAssemblerPatchLease lease =
			client.ApplyPatch(new AutoAssemblerScript(Script), TestContext.Current.CancellationToken);
		_port.Owner!.ReleaseFault = new InvalidOperationException("detached during [DISABLE]");

		lease.Dispose();
		lease.Dispose();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnconfirmed, CheatEngineHostEffect.Unknown),
			lease.LastReleaseOutcome);
		Assert.True(lease.RequiresManualRecovery);
		Assert.Equal(1, _port.Owner.ReleaseCalls);
	}

	[Fact]
	public void ADisableThatCouldNotBeginEndsTheLeaseAndIsReportedForManualRecovery()
	{
		AutoAssemblerClient client = CreateClient();
		IAutoAssemblerPatchLease lease =
			client.ApplyPatch(new AutoAssemblerScript(Script), TestContext.Current.CancellationToken);
		_port.Owner!.ReleaseStatus = TargetReleaseStatus.NotInvoked;

		LeaseReleaseOutcome first = lease.Release();
		LeaseReleaseOutcome second = lease.Release();
		Exception? report = ((IOutcomeReportingResource) lease).ReleaseForDeactivation();

		// CheatEngine.SDK consumed the disable information: nothing is left to retry, so the lease ended.
		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.RefusedRuntimeChanged, CheatEngineHostEffect.NotStarted),
			first);
		Assert.False(first.IsRetryable);
		Assert.True(first.RequiresManualRecovery);
		Assert.Equal(first, second);
		Assert.Equal(first, lease.LastReleaseOutcome);
		Assert.True(lease.IsReleased);
		Assert.True(lease.RequiresManualRecovery);
		Assert.False(lease.CanDisable);
		Assert.Equal(CheatEngineFailureKind.RuntimeChanged,
			Assert.IsType<CheatEngineOperationException>(report).Failure.Kind);
		Assert.Equal(1, _port.Owner.ReleaseCalls);
	}

	[Fact]
	public void AReleaseThatCouldNotBeDispatchedKeepsTheDisableInformationForALaterRelease()
	{
		AutoAssemblerClient client = CreateClient();
		IAutoAssemblerPatchLease lease =
			client.ApplyPatch(new AutoAssemblerScript(Script), TestContext.Current.CancellationToken);
		_invoker.Refuse = true;

		LeaseReleaseOutcome refused = lease.Release();
		_invoker.Refuse = false;
		LeaseReleaseOutcome released = lease.Release();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnavailable, CheatEngineHostEffect.NotStarted),
			refused);
		Assert.Equal(LeaseReleaseKind.Released, released.Kind);
		Assert.False(lease.RequiresManualRecovery);
		Assert.Equal(1, _port.Owner!.ReleaseCalls);
	}

	[Fact]
	public void ACheckReturnsCheatEnginesVerdictWithoutALease()
	{
		AutoAssemblerClient client = CreateClient();
		CancellationToken token = TestContext.Current.CancellationToken;
		_port.CheckFacts = new AutoAssemblerCheckFacts(AutoAssemblerCheckOutcomeKind.Accepted, LuaStatus.Ok, null, false);

		AutoAssemblerCheckResult accepted = client.Check(new AutoAssemblerScript(Script), token);
		_port.CheckFacts = new AutoAssemblerCheckFacts(AutoAssemblerCheckOutcomeKind.Rejected, LuaStatus.Ok,
			"Unknown instruction", true);
		bool checkedScript = client.TryCheck(new AutoAssemblerScript(Script), out AutoAssemblerCheckResult rejected,
			out CheatEngineFailure failure, token);

		Assert.Equal(new AutoAssemblerCheckResult(true, null, false), accepted);
		Assert.True(checkedScript);
		Assert.Equal(default, failure);
		Assert.False(rejected.IsAccepted);
		Assert.Equal("Unknown instruction", rejected.HostMessages);
		Assert.True(rejected.HostMessagesTruncated);
		Assert.Equal("Rejected", rejected.ToString());
		Assert.Equal(2, _port.CheckCalls);
		Assert.Equal(0, _port.ApplyCalls);
		Assert.Same(AutoAssemblerClient.Options, _port.LastOptions);
	}

	[Theory]
	[InlineData(AutoAssemblerCheckOutcomeKind.GlobalUnavailable, CheatEngineFailureKind.CapabilityUnavailable,
		CheatEngineHostEffect.NotStarted)]
	[InlineData(AutoAssemblerCheckOutcomeKind.ProtectedLuaFailure, CheatEngineFailureKind.LuaError,
		CheatEngineHostEffect.Unknown)]
	[InlineData(AutoAssemblerCheckOutcomeKind.InvalidResult, CheatEngineFailureKind.InvalidHostResult,
		CheatEngineHostEffect.Unknown)]
	[InlineData(AutoAssemblerCheckOutcomeKind.Unknown, CheatEngineFailureKind.IndeterminateHostResult,
		CheatEngineHostEffect.Unknown)]
	public void ACheckWithoutAVerdictIsAFailure(AutoAssemblerCheckOutcomeKind kind,
		CheatEngineFailureKind expectedKind, CheatEngineHostEffect expectedEffect)
	{
		AutoAssemblerClient client = CreateClient();
		_port.CheckFacts = new AutoAssemblerCheckFacts(kind, LuaStatus.RuntimeError, null, false);

		bool checkedScript = client.TryCheck(new AutoAssemblerScript(Script), out AutoAssemblerCheckResult result,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(checkedScript);
		Assert.Equal(default, result);
		Assert.Equal(expectedKind, failure.Kind);
		Assert.Equal(expectedEffect, failure.HostEffect);
		Assert.Equal(AutoAssemblerClient.CheckOperation, failure.Operation);
		Assert.Equal(expectedKind, Assert.Throws<CheatEngineOperationException>(() =>
			client.Check(new AutoAssemblerScript(Script), TestContext.Current.CancellationToken)).Failure.Kind);
	}

	private static void AssertPolicyRefusal(CheatEngineFailure failure, string operation)
	{
		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(operation, failure.Operation);
		Assert.Contains("EnableAutoAssemblerPatches()", failure.Message, StringComparison.Ordinal);
	}

	/// <summary>Builds the facts CheatEngine.SDK reports, with its documented effect rule for each kind.</summary>
	private static AutoAssemblerApplyFacts Facts(AutoAssemblerApplyOutcomeKind kind, string? hostText = null,
		bool hostTextTruncated = false, string? warnings = null, bool warningsTruncated = false,
		TargetReleaseStatus? compensation = null)
	{
		EngineEffectState effect = kind switch
		{
			AutoAssemblerApplyOutcomeKind.Applied => EngineEffectState.Applied,
			AutoAssemblerApplyOutcomeKind.GlobalUnavailable or AutoAssemblerApplyOutcomeKind.TargetIdentityUnavailable =>
				EngineEffectState.NotStarted,
			_ => EngineEffectState.Unknown
		};
		LuaStatus status = kind == AutoAssemblerApplyOutcomeKind.ProtectedLuaFailure
			? LuaStatus.RuntimeError
			: LuaStatus.Ok;
		return new AutoAssemblerApplyFacts(kind, effect, status, hostText, hostTextTruncated, warnings,
			warningsTruncated, compensation);
	}

	private AutoAssemblerClient CreateClient(bool enabled = true)
	{
		return new AutoAssemblerClient(_dispatcher, new CoreClientPolicy([], false, enabled), _lifetime, _processes,
			_port);
	}

	/// <summary>Runs callbacks inline and marks the time spent inside them as Cheat Engine's main thread.</summary>
	private sealed class TrackingInvoker : IMainThreadInvoker
	{
		private int _depth;

		internal int Calls
		{
			get;
			private set;
		}

		internal bool IsInvoking => _depth > 0;

		/// <summary>Gets or sets whether the invoker refuses the work without running it, like a closed dispatch.</summary>
		internal bool Refuse
		{
			get;
			set;
		}

		/// <summary>Gets or sets work that runs once the dispatch was admitted, before the callback.</summary>
		internal Action? BeforeCallback
		{
			get;
			set;
		}

		public Exception? Invoke(Action callback)
		{
			return Invoke<bool>(() =>
			{
				callback();
				return true;
			}).Exception;
		}

		public MainThreadInvocationResult<T> Invoke<T>(Func<T> callback)
		{
			Calls++;
			if (Refuse)
			{
				return new MainThreadInvocationResult<T>(default!,
					new InvalidOperationException("Cheat Engine's main thread refused the work."));
			}

			_depth++;
			try
			{
				BeforeCallback?.Invoke();
				return new MainThreadInvocationResult<T>(callback(), null);
			}
			catch (Exception exception)
			{
				return new MainThreadInvocationResult<T>(default!, exception);
			}
			finally
			{
				_depth--;
			}
		}
	}

	/// <summary>A scripted Auto Assembler port; it publishes <see cref="Owner" /> for an applied outcome.</summary>
	private sealed class FakePort(TrackingInvoker invoker) : IAutoAssemblerPort
	{
		internal CheatEngineFailure? Admission
		{
			get;
			set;
		}

		internal int ApplyCalls
		{
			get;
			private set;
		}

		internal AutoAssemblerApplyFacts ApplyFacts
		{
			get;
			set;
		} = Facts(AutoAssemblerApplyOutcomeKind.Applied);

		internal int CheckCalls
		{
			get;
			private set;
		}

		internal AutoAssemblerCheckFacts CheckFacts
		{
			get;
			set;
		}

		internal Action? DuringApply
		{
			get;
			set;
		}

		internal Exception? Fault
		{
			get;
			set;
		}

		internal AutoAssemblerOptions? LastOptions
		{
			get;
			private set;
		}

		internal FakeOwner? Owner
		{
			get;
			set;
		} = new(invoker);

		internal bool RanOnMainThread
		{
			get;
			private set;
		}

		internal List<string> Scripts
		{
			get;
		} = [];

		public bool TryApply(string operation, string script, AutoAssemblerOptions options,
			out AutoAssemblerApplyFacts facts, out IAutoAssemblerPatchOwner? patch,
			out CheatEngineFailure admissionFailure)
		{
			ApplyCalls++;
			Record(script, options);
			facts = default;
			patch = null;
			if (Fault is { } fault)
			{
				throw fault;
			}

			if (Admission is { } refusal)
			{
				admissionFailure = refusal;
				return false;
			}

			DuringApply?.Invoke();
			facts = ApplyFacts;
			patch = Owner;
			admissionFailure = default;
			return true;
		}

		public bool TryCheck(string operation, string script, AutoAssemblerOptions options,
			out AutoAssemblerCheckFacts facts, out CheatEngineFailure admissionFailure)
		{
			CheckCalls++;
			Record(script, options);
			facts = default;
			if (Admission is { } refusal)
			{
				admissionFailure = refusal;
				return false;
			}

			facts = CheckFacts;
			admissionFailure = default;
			return true;
		}

		private void Record(string script, AutoAssemblerOptions options)
		{
			Scripts.Add(script);
			LastOptions = options;
			RanOnMainThread = invoker.IsInvoking;
		}
	}

	/// <summary>
	///     Consumes its disable information on the first release, like CheatEngine.SDK's patch owner, which sets its
	///     manual-recovery flag only there.
	/// </summary>
	private sealed class FakeOwner(TrackingInvoker invoker) : IAutoAssemblerPatchOwner
	{
		public TargetProcessIncarnation TargetIncarnation
		{
			get;
			init;
		} = FakeSelectedTarget.FirstIncarnation;

		public bool IsEnabled => !IsConsumed && !LostDisableInformation;

		public bool IsConsumed
		{
			get;
			private set;
		}

		public bool RequiresManualRecovery
		{
			get;
			private set;
		}

		/// <summary>Gets or sets whether Cheat Engine's Lua state was detached or replaced since the activation.</summary>
		internal bool LostDisableInformation
		{
			get;
			set;
		}

		public TargetReleaseStatus LastReleaseStatus
		{
			get;
			private set;
		}

		internal int ReleaseCalls
		{
			get;
			private set;
		}

		internal List<bool> ReleasedOnMainThread
		{
			get;
		} = [];

		internal Exception? ReleaseFault
		{
			get;
			set;
		}

		internal TargetReleaseStatus ReleaseStatus
		{
			get;
			set;
		} = TargetReleaseStatus.Released;

		public TargetReleaseStatus Release()
		{
			ReleaseCalls++;
			ReleasedOnMainThread.Add(invoker.IsInvoking);
			IsConsumed = true;
			if (ReleaseFault is { } fault)
			{
				RequiresManualRecovery = true;
				throw fault;
			}

			LastReleaseStatus = ReleaseStatus;
			RequiresManualRecovery = ReleaseStatus != TargetReleaseStatus.Released;
			return ReleaseStatus;
		}
	}

	/// <summary>A binder that keys every owner to an epoch the selection already left.</summary>
	private sealed class StaleSelectionBinder : ITargetSelectionBinder
	{
		public TargetSelectionBinding BindOwner(TargetProcessIncarnation incarnation, string operation)
		{
			return new TargetSelectionBinding(-1, null);
		}

		public void ReportBinding(TargetSelectionBinding binding, string operation)
		{
		}
	}

	/// <summary>Records the capability refusals, the lease releases and the Auto Assembler warnings.</summary>
	private sealed class RecordingDiagnostics : ICoreDiagnostics
	{
		internal TrackingInvoker? Invoker
		{
			get;
			set;
		}

		internal List<(string Capability, string Operation)> Refusals
		{
			get;
		} = [];

		internal List<(string Operation, long SelectionEpoch, bool InsideCallback)> TargetChangeWarnings
		{
			get;
		} = [];

		public void AutoAssemblerPatchAppliedAfterTargetChange(string operation, long selectionEpoch)
		{
			TargetChangeWarnings.Add((operation, selectionEpoch, Invoker?.IsInvoking == true));
		}

		public void CapabilityRefused(string capability, string operation, ClientCapabilityEvidenceReasonCode gate,
			ClientCapabilityEvidenceState gateState)
		{
			Assert.Equal(ClientCapabilityEvidenceReasonCode.Policy, gate);
			Assert.Equal(ClientCapabilityEvidenceState.Missing, gateState);
			Refusals.Add((capability, operation));
		}

		public void RuntimeSnapshotCaptured(long activationEpoch, CheatEngineArchitecture targetArchitecture,
			int processPointerBytes, int configuredPointerBytes, bool pointerSizeMismatch)
		{
		}

		public void TargetSelectionAdvanced(long activationEpoch, long selectionEpoch, string operation, string reason)
		{
		}

		public void PointerWidthMismatchRefused(string operation, int processPointerBytes, int configuredPointerBytes)
		{
		}

		public void MemoryBatchCompleted(string operation, int requested, int completed, string effectState)
		{
		}

		public void TableGenerationAdvanced(long activationEpoch, long tableGeneration)
		{
		}

		public void StaleRecordIdentifierRefused(string operation, long tableGeneration)
		{
		}

		public void RecordActivationNotApplied(string operation, bool requestedState, string status)
		{
		}

		public void SymbolRegistrationRejected(string operation, string reason)
		{
		}

		public void PatternScanCompleted(PatternScanScope scope, long hostResultCount, int materializedCount,
			bool truncated, long hostScanMilliseconds, long copyMilliseconds)
		{
		}

		public void LuaOperationCompleted(string operation, string outcome, long elapsedMilliseconds, int scriptLength)
		{
		}

		public void CoreResourceCleanupFailed(string componentType, string exceptionType)
		{
		}

		public void LeaseReleased(string operation, LeaseReleaseKind kind, CheatEngineHostEffect hostEffect)
		{
		}
	}
}
