using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

using SymbolRegistrationLease = CheatEngine.Client.Core.Domains.SymbolRegistrationLease;

namespace CheatEngine.Client.Core.Tests.Domains;

/// <summary>
///     The Client symbol lease over a fake CheatEngine.SDK release handle: every SDK release kind reaches the lease as its
///     mapped outcome, the activation-local name reservation follows the lease's end, and Dispose never throws.
/// </summary>
public sealed class SymbolRegistrationLeaseTests : IDisposable
{
	private const string Name = "fixture-symbol";

	private readonly ControlledCoreLifetimeContext _context = new();
	private readonly CoreLifetime _lifetime;
	private readonly SdkMainThreadDispatcher _dispatcher;
	private readonly List<string> _releasedNames = [];

	public SymbolRegistrationLeaseTests()
	{
		_lifetime = new CoreLifetime(_context);
		_dispatcher = new SdkMainThreadDispatcher(_lifetime, new InlineMainThreadInvoker());
	}

	public static TheoryData<SymbolRegistrationReleaseKind> EverySdkReleaseKind =>
		[.. Enum.GetValues<SymbolRegistrationReleaseKind>()];

	public void Dispose()
	{
		_context.Dispose();
	}

	[Theory]
	[Trait("Qualification", "Q16.b")]
	[MemberData(nameof(EverySdkReleaseKind))]
	public void EverySdkReleaseKindReachesTheLeaseAsItsMappedOutcome(SymbolRegistrationReleaseKind kind)
	{
		ScriptedHandle handle = new(kind);
		SymbolRegistrationLease lease = CreateLease(handle);
		LeaseReleaseOutcome expected = SdkReleaseOutcomes.FromSymbolRegistration(kind);

		LeaseReleaseOutcome outcome = lease.Release();

		Assert.Equal(expected, outcome);
		Assert.Equal(expected, lease.LastReleaseOutcome);
		Assert.Equal(!expected.IsRetryable, lease.IsReleased);
		string[] expectedReleasedNames = expected.IsRetryable ? [] : [Name];
		Assert.Equal(expectedReleasedNames, _releasedNames);
		Assert.Equal(1, handle.Calls);
	}

	[Fact]
	[Trait("Qualification", "Q16.b")]
	public void ASupersededLeaseEndsCompleteWithoutASecondSdkCall()
	{
		ScriptedHandle handle = new(SymbolRegistrationReleaseKind.Superseded);
		SymbolRegistrationLease lease = CreateLease(handle);

		LeaseReleaseOutcome outcome = lease.Release();
		LeaseReleaseOutcome repeated = lease.Release();
		lease.Dispose();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.Superseded, CheatEngineHostEffect.NotStarted), outcome);
		Assert.True(outcome.IsComplete);
		Assert.Equal(outcome, repeated);
		Assert.Equal(outcome, lease.LastReleaseOutcome);
		Assert.Equal(1, handle.Calls);
		Assert.Equal([Name], _releasedNames);
	}

	[Fact]
	[Trait("Qualification", "Q16.b")]
	[Trait("Qualification", "Q43")]
	public void AStaleRuntimeIsARefusedRuntimeChangeThatTheDeactivationReports()
	{
		// The SDK makes no call into a replaced Lua runtime, so the name may remain: manual recovery, never a retry.
		ScriptedHandle handle = new(SymbolRegistrationReleaseKind.StaleRuntime);
		SymbolRegistrationLease lease = CreateLease(handle);
		lease.Register(_lifetime);

		LeaseReleaseOutcome outcome = lease.Release();
		CheatEngineOperationException report = Drain<CheatEngineOperationException>();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.RefusedRuntimeChanged, CheatEngineHostEffect.NotStarted),
			outcome);
		Assert.True(outcome.RequiresManualRecovery);
		Assert.True(lease.IsReleased);
		Assert.Equal(CheatEngineFailureKind.RuntimeChanged, report.Failure.Kind);
		Assert.Equal(SymbolRegistrationLease.ReleaseOperation, report.Failure.Operation);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, report.Failure.HostEffect);
		Assert.Equal(1, handle.Calls);
	}

	[Fact]
	[Trait("Qualification", "Q16.b")]
	[Trait("Qualification", "Q43")]
	public void AnIndeterminateCleanupIsUnconfirmedNeverRetriedAndReportedAtDeactivation()
	{
		ScriptedHandle handle = new(SymbolRegistrationReleaseKind.CleanupIndeterminate);
		SymbolRegistrationLease lease = CreateLease(handle);
		lease.Register(_lifetime);

		LeaseReleaseOutcome outcome = lease.Release();
		LeaseReleaseOutcome repeated = lease.Release();
		CheatEngineOperationException report = Drain<CheatEngineOperationException>();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnconfirmed, CheatEngineHostEffect.Started),
			outcome);
		Assert.Equal(outcome, repeated);
		Assert.Equal(CheatEngineFailureKind.IndeterminateHostResult, report.Failure.Kind);
		Assert.Equal(1, handle.Calls);
		Assert.Equal([Name], _releasedNames);
	}

	[Fact]
	[Trait("Qualification", "Q43")]
	public void AnUnavailableCleanupKeepsTheLeaseAndItsReservationUntilARetryReleasesIt()
	{
		ScriptedHandle handle = new(SymbolRegistrationReleaseKind.CleanupUnavailable,
			SymbolRegistrationReleaseKind.Released);
		SymbolRegistrationLease lease = CreateLease(handle);
		lease.Register(_lifetime);

		LeaseReleaseOutcome first = lease.Release();
		bool releasedAfterFirst = lease.IsReleased;
		int reservationsAfterFirst = _releasedNames.Count;
		_context.Stop();
		using (_lifetime.EnterCleanupScope())
		{
			_lifetime.DrainOwnedResourcesForDisable();
		}

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnavailable, CheatEngineHostEffect.NotStarted),
			first);
		Assert.False(releasedAfterFirst);
		Assert.Equal(0, reservationsAfterFirst);
		Assert.True(lease.IsReleased);
		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed),
			lease.LastReleaseOutcome);
		Assert.Equal(2, handle.Calls);
		Assert.Equal([Name], _releasedNames);
	}

	[Fact]
	[Trait("Qualification", "Q16.b")]
	public void DisposeNeverThrowsWhenTheSdkReleaseFaultsAndTheFaultIsNeverRetried()
	{
		ScriptedHandle handle = new(SymbolRegistrationReleaseKind.Released)
		{
			Fault = new InvalidOperationException("the SDK release faulted")
		};
		SymbolRegistrationLease lease = CreateLease(handle);

		lease.Dispose();
		LeaseReleaseOutcome repeated = lease.Release();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnconfirmed, CheatEngineHostEffect.Unknown),
			lease.LastReleaseOutcome);
		Assert.True(lease.IsReleased);
		Assert.Equal(lease.LastReleaseOutcome, repeated);
		Assert.Equal(1, handle.Calls);
		Assert.Equal([Name], _releasedNames);
	}

	[Fact]
	[Trait("Qualification", "Q16.b")]
	public void DisposeNeverThrowsWhenDispatchIsRefusedAndTheLeaseKeepsItsReservation()
	{
		ScriptedHandle handle = new(SymbolRegistrationReleaseKind.Released);
		SymbolRegistrationLease lease = CreateLease(handle);
		_context.Stop();

		lease.Dispose();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnavailable, CheatEngineHostEffect.NotStarted),
			lease.LastReleaseOutcome);
		Assert.False(lease.IsReleased);
		Assert.Equal(0, handle.Calls);
		Assert.Empty(_releasedNames);
	}

	[Fact]
	public void TheLeaseExposesTheRegisteredNameAndAddressAndRejectsMissingCollaborators()
	{
		SymbolRegistration registration = new(Name, new Address(0x401000));
		ScriptedHandle handle = new(SymbolRegistrationReleaseKind.Released);

		SymbolRegistrationLease lease = new(registration, handle, _dispatcher, _releasedNames.Add);

		Assert.Equal(Name, lease.Name);
		Assert.Equal(new Address(0x401000), lease.Address);
		Assert.Null(lease.LastReleaseOutcome);
		Assert.Equal("handle", Assert.Throws<ArgumentNullException>(() =>
			new SymbolRegistrationLease(registration, null!, _dispatcher, _releasedNames.Add)).ParamName);
		Assert.Equal("releaseName", Assert.Throws<ArgumentNullException>(() =>
			new SymbolRegistrationLease(registration, handle, _dispatcher, null!)).ParamName);
	}

	private SymbolRegistrationLease CreateLease(ScriptedHandle handle)
	{
		return new SymbolRegistrationLease(new SymbolRegistration(Name, new Address(0x401000)), handle, _dispatcher,
			_releasedNames.Add);
	}

	private TException Drain<TException>()
		where TException : Exception
	{
		_context.Stop();
		using (_lifetime.EnterCleanupScope())
		{
			return Assert.Throws<TException>(_lifetime.DrainOwnedResourcesForDisable);
		}
	}

	/// <summary>A fake CheatEngine.SDK release handle that returns scripted kinds in order and counts its calls.</summary>
	private sealed class ScriptedHandle(params SymbolRegistrationReleaseKind[] kinds) : ISymbolRegistrationHandle
	{
		internal int Calls
		{
			get;
			private set;
		}

		internal Exception? Fault
		{
			get;
			init;
		}

		public SymbolRegistrationReleaseKind Release()
		{
			Calls++;
			if (Fault is not null)
			{
				throw Fault;
			}

			return kinds[Math.Min(Calls - 1, kinds.Length - 1)];
		}
	}
}
