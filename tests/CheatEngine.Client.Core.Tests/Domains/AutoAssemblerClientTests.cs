#pragma warning disable CECLIENT5004 // These tests exercise the experimental Auto Assembler client.

using CheatEngine.Client.Assembly;
using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains.Assembly;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Assembly;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Tests.Domains;

/// <summary>
///     The opt-in Auto Assembler client over a fake port (plan L17): the policy refusal without the opt-in (Q44), the
///     activation and release of a patch lease (Q35), every mapped outcome, and the lease rules (one disable attempt,
///     refusal on a changed target, a <c>Dispose</c> that never throws).
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

	public AutoAssemblerClientTests()
	{
		_diagnostics.Invoker = _invoker;
		_lifetime = new CoreLifetime(_context, _diagnostics);
		_dispatcher = new SdkMainThreadDispatcher(_lifetime, _invoker);
		_port = new FakePort(_invoker);
	}

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
		Assert.True(lease.IsEnabled);
		Assert.False(lease.IsReleased);
		Assert.False(lease.RequiresManualRecovery);
		Assert.Empty(_diagnostics.TargetChangeWarnings);

		LeaseReleaseOutcome outcome = lease.Release();
		LeaseReleaseOutcome repeated = lease.Release();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed), outcome);
		Assert.Equal(LeaseReleaseKind.AlreadyReleased, repeated.Kind);
		Assert.Equal(1, _port.Owner!.ReleaseCalls);
		Assert.Equal([true], _port.Owner.ReleasedOnMainThread);
		Assert.True(lease.IsReleased);
		Assert.False(lease.IsEnabled);
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
	[InlineData(AutoAssemblerApplyOutcomeKind.Unknown, CheatEngineFailureKind.Unknown, CheatEngineHostEffect.Unknown)]
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
	public void AnOwnerNextToAFailureIsReleasedAndAnAppliedOutcomeWithoutAnOwnerIsInvalid()
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
		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, ownerless.Kind);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, ownerless.HostEffect);
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

	[Theory]
	[Trait("Qualification", "Q35")]
	[InlineData(TargetReleaseStatus.Released, CheatEngineHostEffect.Completed)]
	[InlineData(TargetReleaseStatus.RefusedTargetChanged, CheatEngineHostEffect.CleanupUnconfirmed)]
	public void ALeaseThatCannotBeRegisteredIsReleasedAtOnce(TargetReleaseStatus release,
		CheatEngineHostEffect expectedEffect)
	{
		_port.Owner!.ReleaseStatus = release;
		_port.DuringApply = () => _lifetime.TargetSelection.Advance("Processes.Attach");
		AutoAssemblerClient client = CreateClient();

		bool applied = client.TryApplyPatch(new AutoAssemblerScript(Script), out IAutoAssemblerPatchLease? lease,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(applied);
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.TargetChanged, failure.Kind);
		Assert.Equal(expectedEffect, failure.HostEffect);
		Assert.IsType<CheatEngineClientLifecycleException>(failure.Exception);
		Assert.Contains(release.ToString(), failure.Message, StringComparison.Ordinal);
		Assert.Equal(1, _port.Owner.ReleaseCalls);
		Assert.Equal([true], _port.Owner.ReleasedOnMainThread);
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
		Assert.False(lease.IsEnabled);
		Assert.Equal(LeaseReleaseKind.AlreadyReleased, repeated.Kind);
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
		Assert.Equal(LeaseReleaseKind.AlreadyReleased, second.Kind);
		Assert.Equal(first, lease.LastReleaseOutcome);
		Assert.True(lease.IsReleased);
		Assert.True(lease.RequiresManualRecovery);
		Assert.False(lease.IsEnabled);
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
	[InlineData(AutoAssemblerCheckOutcomeKind.Unknown, CheatEngineFailureKind.Unknown, CheatEngineHostEffect.Unknown)]
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
		return new AutoAssemblerClient(_dispatcher, new CoreClientPolicy([], false, enabled), _lifetime, _port);
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

	/// <summary>Consumes its disable information on the first release, like CheatEngine.SDK's patch owner.</summary>
	private sealed class FakeOwner(TrackingInvoker invoker) : IAutoAssemblerPatchOwner
	{
		public bool IsEnabled => !IsConsumed;

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
