using CheatEngine.Client.Allocations;
using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains.Allocations;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Allocation;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Tests.Domains.Allocations;

/// <summary>
///     The allocation lifecycle against a scripted CheatEngine.SDK allocator (plan L16): a published lease and its one
///     release, a target change or a reused PID that the SDK refuses without freeing anything in the new target (Q30), a
///     runtime change, an unconfirmed or unavailable release, the compensation of an allocation that got no owner,
///     cancellation before and after Cheat Engine allocated, and a Dispose that never throws.
/// </summary>
public sealed class AllocationClientTests : IDisposable
{
	private readonly AllocationClient _client;
	private readonly ControlledCoreLifetimeContext _context = new();
	private readonly CoreLifetime _lifetime;
	private readonly FakeAllocationPort _port = new();

	public AllocationClientTests()
	{
		_lifetime = new CoreLifetime(_context);
		_client = new AllocationClient(new SdkMainThreadDispatcher(_lifetime, new InlineMainThreadInvoker()), _port);
	}

	private FakeAllocatedRegion Region => _port.Region;

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	public void Dispose()
	{
		_context.Dispose();
	}

	[Fact]
	[Trait("Qualification", "Q30.a")]
	public void AnAllocationIsPublishedAsALeaseAndReleasedOnce()
	{
		ITargetMemoryLease lease = _client.Allocate(new AllocationRequest(4096), Token);

		Assert.Equal(FakeAllocationPort.AllocatedAddress, lease.Address);
		Assert.Equal(4096, lease.Size);
		Assert.Equal(AllocationProtection.ReadWrite, lease.Protection);
		Assert.Equal(_lifetime.TargetSelection.Epoch, lease.SelectionEpoch);
		Assert.False(lease.IsReleased);
		Assert.False(lease.RequiresManualRecovery);
		Assert.Null(lease.LastReleaseOutcome);

		LeaseReleaseOutcome released = lease.Release();
		LeaseReleaseOutcome again = lease.Release();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed), released);
		Assert.Equal(LeaseReleaseKind.AlreadyReleased, again.Kind);
		Assert.True(lease.IsReleased);
		Assert.False(lease.RequiresManualRecovery);
		Assert.Equal(released, lease.LastReleaseOutcome);
		Assert.Equal(1, Region.ReleaseCalls);
		Assert.Equal(1, Region.Deallocations);
		// The copied facts stay readable after the release.
		Assert.Equal(FakeAllocationPort.AllocatedAddress, lease.Address);
	}

	[Fact]
	[Trait("Qualification", "Q30.a")]
	public void EveryRequestPassesAnExplicitProtectionAndItsPreferredAddress()
	{
		Address preferred = new(0x1_4000_0000);

		ITargetMemoryLease readWrite = _client.Allocate(new AllocationRequest(16), Token);
		TargetAllocationRequest first = _port.LastRequest.GetValueOrDefault();
		ITargetMemoryLease executable = _client.Allocate(
			new AllocationRequest(32, AllocationProtection.ExecuteReadWrite, preferred), Token);
		TargetAllocationRequest second = _port.LastRequest.GetValueOrDefault();

		Assert.Equal(16, first.Size.Value);
		Assert.Equal(MemoryProtection.ReadWrite, first.Protection);
		Assert.Null(first.PreferredBaseAddress);
		Assert.Equal(32, second.Size.Value);
		Assert.Equal(MemoryProtection.ExecuteReadWrite, second.Protection);
		Assert.Equal(preferred, second.PreferredBaseAddress);
		Assert.Equal(AllocationProtection.ReadWrite, readWrite.Protection);
		Assert.Equal(AllocationProtection.ExecuteReadWrite, executable.Protection);
	}

	[Fact]
	[Trait("Qualification", "Q30.a")]
	public void AnAllocationThatIsNeverReleasedIsReleasedBeforeTheActivationEnds()
	{
		ITargetMemoryLease lease = _client.Allocate(new AllocationRequest(64), Token);

		_context.Stop();
		using (_lifetime.EnterCleanupScope())
		{
			_lifetime.DrainOwnedResourcesForDisable();
		}

		Assert.True(lease.IsReleased);
		Assert.Equal(1, Region.Deallocations);
		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed),
			lease.LastReleaseOutcome);
	}

	[Theory]
	[Trait("Qualification", "Q30.a")]
	[Trait("Qualification", "Q30.b")]
	[InlineData(TargetReleaseStatus.RefusedTargetChanged)]
	[InlineData(TargetReleaseStatus.RefusedProcessReused)]
	public void ATargetChangeReleasesTheLeaseAndTheSdkRefusalFreesNothingInTheNewTarget(TargetReleaseStatus refusal)
	{
		ITargetMemoryLease lease = _client.Allocate(new AllocationRequest(64), Token);
		Region.ReleaseStatus = refusal;

		_ = _lifetime.TargetSelection.Advance("Processes.Attach");
		int releaseCalls = Region.ReleaseCalls;
		LeaseReleaseOutcome again = lease.Release();
		CheatEngineOperationException reported = DrainExpectingOneReport();

		Assert.True(lease.IsReleased);
		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.RefusedTargetChanged, CheatEngineHostEffect.NotStarted),
			lease.LastReleaseOutcome);
		Assert.True(lease.RequiresManualRecovery);
		Assert.Equal(1, releaseCalls);
		Assert.Equal(0, Region.Deallocations);
		Assert.Equal(1, _port.Allocations);
		Assert.Equal(LeaseReleaseKind.AlreadyReleased, again.Kind);
		Assert.Equal(1, Region.ReleaseCalls);
		// The refused allocation stays in the deactivation report (Q43), under the kind of the refusal.
		Assert.Equal(TargetMemoryLease.ReleaseOperation, reported.Failure.Operation);
		Assert.Equal(CheatEngineFailureKind.TargetChanged, reported.Failure.Kind);
	}

	[Fact]
	[Trait("Qualification", "Q30.a")]
	public void ARuntimeChangeRefusesTheReleaseWhichRequiresManualRecovery()
	{
		ITargetMemoryLease lease = _client.Allocate(new AllocationRequest(64), Token);
		Region.ReleaseStatus = TargetReleaseStatus.RefusedRuntimeChanged;

		LeaseReleaseOutcome released = lease.Release();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.RefusedRuntimeChanged, CheatEngineHostEffect.NotStarted),
			released);
		Assert.True(lease.IsReleased);
		Assert.True(lease.RequiresManualRecovery);
		Assert.Equal(0, Region.Deallocations);
	}

	[Fact]
	[Trait("Qualification", "Q30.a")]
	public void AnUnconfirmedReleaseRequiresManualRecoveryAndIsNeverRetried()
	{
		ITargetMemoryLease lease = _client.Allocate(new AllocationRequest(64), Token);
		Region.ReleaseStatus = TargetReleaseStatus.UnconfirmedAfterInvocation;

		LeaseReleaseOutcome released = lease.Release();
		LeaseReleaseOutcome again = lease.Release();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnconfirmed, CheatEngineHostEffect.Started),
			released);
		Assert.True(lease.IsReleased);
		Assert.True(lease.RequiresManualRecovery);
		Assert.Equal(LeaseReleaseKind.AlreadyReleased, again.Kind);
		Assert.Equal(1, Region.ReleaseCalls);
		Assert.Equal(1, Region.Deallocations);
	}

	[Fact]
	[Trait("Qualification", "Q43")]
	public void AReleaseThatCannotBeginStaysRetryableAndIsReportedAtDeactivation()
	{
		ITargetMemoryLease lease = _client.Allocate(new AllocationRequest(64), Token);
		// CheatEngine.SDK consumes the owner when the runtime detached before deAlloc could begin.
		Region.ReleaseStatus = TargetReleaseStatus.NotInvoked;

		LeaseReleaseOutcome released = lease.Release();
		bool releasedAfterFirstAttempt = lease.IsReleased;
		CheatEngineOperationException reported = DrainExpectingOneReport();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnavailable, CheatEngineHostEffect.NotStarted),
			released);
		Assert.False(releasedAfterFirstAttempt);
		Assert.False(lease.RequiresManualRecovery);
		// The deactivation cleanup retried through the consumed owner, which reports the same status without a Cheat Engine
		// call, and the lease stays in the report.
		Assert.True(Region.ReleaseCalls > 1);
		Assert.Equal(0, Region.Deallocations);
		Assert.Equal(TargetMemoryLease.ReleaseOperation, reported.Failure.Operation);
		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, reported.Failure.Kind);
	}

	[Fact]
	[Trait("Qualification", "Q43")]
	public void AReleaseWhileThePluginIsStoppingRunsInTheDeactivationCleanup()
	{
		ITargetMemoryLease lease = _client.Allocate(new AllocationRequest(64), Token);

		_context.Stop();
		LeaseReleaseOutcome refused = lease.Release();
		int callsWhileStopping = Region.ReleaseCalls;
		using (_lifetime.EnterCleanupScope())
		{
			_lifetime.DrainOwnedResourcesForDisable();
		}

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnavailable, CheatEngineHostEffect.NotStarted),
			refused);
		Assert.Equal(0, callsWhileStopping);
		Assert.True(lease.IsReleased);
		Assert.Equal(1, Region.Deallocations);
	}

	[Fact]
	public void DisposeNeverThrowsWhenTheReleaseFaults()
	{
		ITargetMemoryLease lease = _client.Allocate(new AllocationRequest(64), Token);
		Region.ReleaseFault = new InvalidOperationException("deAlloc raised");

		lease.Dispose();
		lease.Dispose();

		Assert.True(lease.IsReleased);
		Assert.True(lease.RequiresManualRecovery);
		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnconfirmed, CheatEngineHostEffect.Unknown),
			lease.LastReleaseOutcome);
		Assert.Equal(1, Region.ReleaseCalls);
	}

	[Theory]
	[Trait("Qualification", "Q30.a")]
	[InlineData(TargetMemoryOperationOutcomeKind.ExpectedFailure, EngineEffectState.NotApplied,
		CheatEngineFailureKind.OperationRejected, CheatEngineHostEffect.NotApplied)]
	[InlineData(TargetMemoryOperationOutcomeKind.TargetIdentityUnavailable, EngineEffectState.NotStarted,
		CheatEngineFailureKind.TargetIdentityUnavailable, CheatEngineHostEffect.NotStarted)]
	[InlineData(TargetMemoryOperationOutcomeKind.TargetIdentityMismatch, EngineEffectState.NotStarted,
		CheatEngineFailureKind.TargetChanged, CheatEngineHostEffect.NotStarted)]
	[InlineData(TargetMemoryOperationOutcomeKind.GlobalUnavailable, EngineEffectState.NotStarted,
		CheatEngineFailureKind.CapabilityUnavailable, CheatEngineHostEffect.NotStarted)]
	[InlineData(TargetMemoryOperationOutcomeKind.ProtectedLuaFailure, EngineEffectState.Unknown,
		CheatEngineFailureKind.LuaError, CheatEngineHostEffect.Unknown)]
	[InlineData(TargetMemoryOperationOutcomeKind.MarshallingFailure, EngineEffectState.Unknown,
		CheatEngineFailureKind.InvalidHostResult, CheatEngineHostEffect.Unknown)]
	public void ARefusedAllocationPublishesNoLease(TargetMemoryOperationOutcomeKind outcome, EngineEffectState effect,
		CheatEngineFailureKind kind, CheatEngineHostEffect hostEffect)
	{
		_port.Refuse(outcome, effect);

		bool allocated = _client.TryAllocate(new AllocationRequest(64), out ITargetMemoryLease? lease,
			out CheatEngineFailure failure, Token);

		Assert.False(allocated);
		Assert.Null(lease);
		Assert.Equal(kind, failure.Kind);
		Assert.Equal(hostEffect, failure.HostEffect);
		Assert.Equal(AllocationClient.AllocateOperation, failure.Operation);
		Assert.Equal(1, _port.Allocations);
		Assert.Equal(0, Region.ReleaseCalls);
		Assert.Throws<CheatEngineOperationException>(() => _client.Allocate(new AllocationRequest(64), Token));
	}

	[Fact]
	[Trait("Qualification", "Q30.a")]
	public void AnAllocationThatGotNoOwnerAndWasReleasedReportsNothingRemains()
	{
		_port.Refuse(TargetMemoryOperationOutcomeKind.Succeeded, EngineEffectState.Applied,
			FakeAllocationPort.AllocatedAddress, TargetReleaseStatus.Released);

		bool allocated = _client.TryAllocate(new AllocationRequest(16), out ITargetMemoryLease? lease,
			out CheatEngineFailure failure, Token);

		Assert.False(allocated);
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.IndeterminateHostResult, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
	}

	[Theory]
	[Trait("Qualification", "Q30.a")]
	[InlineData(TargetReleaseStatus.UnconfirmedAfterInvocation, CheatEngineFailureKind.IndeterminateHostResult)]
	[InlineData(TargetReleaseStatus.RefusedRuntimeChanged, CheatEngineFailureKind.RuntimeChanged)]
	[InlineData(TargetReleaseStatus.RefusedIdentityUnavailable, CheatEngineFailureKind.TargetIdentityUnavailable)]
	public void AnUnconfirmedCompensationCarriesTheAddressForManualRecovery(TargetReleaseStatus compensation,
		CheatEngineFailureKind kind)
	{
		_port.Refuse(TargetMemoryOperationOutcomeKind.Succeeded, EngineEffectState.Applied,
			FakeAllocationPort.AllocatedAddress, compensation);

		bool allocated = _client.TryAllocate(new AllocationRequest(16), out ITargetMemoryLease? lease,
			out CheatEngineFailure failure, Token);

		Assert.False(allocated);
		Assert.Null(lease);
		Assert.Equal(kind, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, failure.HostEffect);
		Assert.Contains("16 bytes at 0x7FF000001000", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void CancellationBeforeTheAllocationReachesNoAllocator()
	{
		bool allocated = _client.TryAllocate(new AllocationRequest(64), out ITargetMemoryLease? lease,
			out CheatEngineFailure failure, new CancellationToken(true));

		Assert.False(allocated);
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(0, _port.Allocations);
		Assert.Throws<CheatEngineOperationCanceledException>(() =>
			_client.Allocate(new AllocationRequest(64), new CancellationToken(true)));
	}

	[Fact]
	public void CancellationObservedAfterTheAllocationReleasesItAndPublishesNothing()
	{
		using CancellationTokenSource cancellation = new();
		_port.DuringAllocate = cancellation.Cancel;

		bool allocated = _client.TryAllocate(new AllocationRequest(64), out ITargetMemoryLease? lease,
			out CheatEngineFailure failure, cancellation.Token);

		Assert.False(allocated);
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Equal(1, Region.Deallocations);
	}

	[Fact]
	public void ALateCancellationWhoseReleaseIsRefusedCarriesTheAddress()
	{
		using CancellationTokenSource cancellation = new();
		_port.DuringAllocate = cancellation.Cancel;
		Region.ReleaseStatus = TargetReleaseStatus.RefusedTargetChanged;

		bool allocated = _client.TryAllocate(new AllocationRequest(64), out _, out CheatEngineFailure failure,
			cancellation.Token);

		Assert.False(allocated);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, failure.HostEffect);
		Assert.Contains("64 bytes at 0x7FF000001000", failure.Message, StringComparison.Ordinal);
		Assert.Equal(0, Region.Deallocations);
	}

	[Fact]
	public void AnAllocatorFaultIsTranslatedWithAnUnknownEffect()
	{
		EngineLuaException fault = new("TargetMemoryAllocate", LuaStatus.RuntimeError, "allocateMemory raised");
		_port.Fault = fault;

		bool allocated = _client.TryAllocate(new AllocationRequest(64), out ITargetMemoryLease? lease,
			out CheatEngineFailure failure, Token);

		Assert.False(allocated);
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.LuaError, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Unknown, failure.HostEffect);
		Assert.Same(fault, failure.Exception);
	}

	[Fact]
	public void TheDefaultRequestIsRefusedBeforeDispatch()
	{
		bool allocated = _client.TryAllocate(default, out ITargetMemoryLease? lease, out CheatEngineFailure failure,
			Token);

		Assert.False(allocated);
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(0, _port.Allocations);
		Assert.Throws<CheatEngineOperationException>(() => _client.Allocate(default, Token));
	}

	[Fact]
	[Trait("Qualification", "Q30.a")]
	public void ATargetChangeDuringTheAllocationReleasesItAndPublishesNoLease()
	{
		_port.DuringAllocate = () => _ = _lifetime.TargetSelection.Advance("Processes.Attach");
		Region.ReleaseStatus = TargetReleaseStatus.RefusedTargetChanged;

		Assert.Throws<CheatEngineClientLifecycleException>(() =>
			_client.TryAllocate(new AllocationRequest(64), out _, out _, Token));

		Assert.Equal(1, Region.ReleaseCalls);
		Assert.Equal(0, Region.Deallocations);
	}

	private CheatEngineOperationException DrainExpectingOneReport()
	{
		_context.Stop();
		AggregateException? report = null;
		using (_lifetime.EnterCleanupScope())
		{
			try
			{
				_lifetime.DrainOwnedResourcesForDisable();
			}
			catch (CheatEngineOperationException single)
			{
				report = new AggregateException(single);
			}
			catch (AggregateException several)
			{
				report = several;
			}
		}

		Assert.NotNull(report);
		return Assert.IsType<CheatEngineOperationException>(Assert.Single(report.InnerExceptions));
	}
}
