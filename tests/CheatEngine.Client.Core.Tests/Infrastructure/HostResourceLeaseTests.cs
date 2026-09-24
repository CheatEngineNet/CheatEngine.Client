using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Core.Tests.Infrastructure;

public sealed class HostResourceLeaseTests : IDisposable
{
	private const string Operation = "Test.Release";

	private static readonly LeaseReleaseOutcome Released = new(LeaseReleaseKind.Released,
		CheatEngineHostEffect.Completed);

	private static readonly LeaseReleaseOutcome Unavailable = new(LeaseReleaseKind.CleanupUnavailable,
		CheatEngineHostEffect.NotStarted);

	private static readonly LeaseReleaseOutcome Unconfirmed = new(LeaseReleaseKind.CleanupUnconfirmed,
		CheatEngineHostEffect.Started);

	private static readonly LeaseReleaseOutcome TargetChanged = new(LeaseReleaseKind.RefusedTargetChanged,
		CheatEngineHostEffect.NotStarted);

	private readonly ControlledCoreLifetimeContext _context = new();
	private readonly RecordingDiagnostics _diagnostics = new();
	private readonly MarkingMainThreadInvoker _invoker = new();
	private readonly CoreLifetime _lifetime;
	private readonly SdkMainThreadDispatcher _dispatcher;

	public HostResourceLeaseTests()
	{
		_diagnostics.Invoker = _invoker;
		_lifetime = new CoreLifetime(_context, _diagnostics);
		_dispatcher = new SdkMainThreadDispatcher(_lifetime, _invoker);
	}

	public void Dispose()
	{
		_context.Dispose();
	}

	[Fact]
	public void ReleaseRunsOnTheMainThreadThroughTheDispatcher()
	{
		ScriptedLease lease = CreateLease(Released);

		LeaseReleaseOutcome outcome = lease.Release();

		Assert.Equal(Released, outcome);
		Assert.Equal([true], lease.RanOnMainThread);
		Assert.Equal(1, _invoker.Calls);
		Assert.True(lease.IsReleased);
		Assert.Equal(Released, lease.LastReleaseOutcome);
	}

	[Fact]
	public void ReleaseIsIdempotentAndKeepsTheOutcomeThatEndedTheLease()
	{
		ScriptedLease lease = CreateLease(Released);

		_ = lease.Release();
		LeaseReleaseOutcome repeated = lease.Release();
		lease.Dispose();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.AlreadyReleased, CheatEngineHostEffect.NotStarted),
			repeated);
		Assert.Equal(1, lease.Calls);
		Assert.Equal(1, _invoker.Calls);
		Assert.Equal(Released, lease.LastReleaseOutcome);
	}

	[Fact]
	public void ANewLeaseHasNoOutcomeAndIsNotReleased()
	{
		ScriptedLease lease = CreateLease(Released);

		Assert.False(lease.IsReleased);
		Assert.Null(lease.LastReleaseOutcome);
		Assert.Equal(0, lease.Calls);
	}

	[Fact]
	public void DisposeNeverThrowsWhenTheReleaseFaultsAndTheFaultIsNeverRetried()
	{
		ScriptedLease lease = CreateLease(Released);
		lease.Fault = new InvalidOperationException("SDK fault during destroy");

		lease.Dispose();
		LeaseReleaseOutcome repeated = lease.Release();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnconfirmed, CheatEngineHostEffect.Unknown),
			lease.LastReleaseOutcome);
		Assert.True(lease.IsReleased);
		Assert.Equal(LeaseReleaseKind.AlreadyReleased, repeated.Kind);
		Assert.Equal(1, lease.Calls);
	}

	[Fact]
	public void DisposeNeverThrowsWhenDispatchIsRefusedAndTheLeaseStaysActive()
	{
		ScriptedLease lease = CreateLease(Released);
		_context.Stop();

		lease.Dispose();

		Assert.Equal(Unavailable, lease.LastReleaseOutcome);
		Assert.False(lease.IsReleased);
		Assert.Equal(0, lease.Calls);
		Assert.Equal(0, _invoker.Calls);
	}

	[Fact]
	public void ARetryableOutcomeKeepsTheLeaseActiveAndALaterReleaseRetries()
	{
		ScriptedLease lease = CreateLease(Unavailable, Released);

		LeaseReleaseOutcome first = lease.Release();
		bool releasedAfterFirst = lease.IsReleased;
		LeaseReleaseOutcome second = lease.Release();

		Assert.Equal(Unavailable, first);
		Assert.False(releasedAfterFirst);
		Assert.Equal(Released, second);
		Assert.True(lease.IsReleased);
		Assert.Equal(2, lease.Calls);
	}

	[Fact]
	[Trait("Qualification", "Q43")]
	public void DeactivationRetriesRetryableLeasesAndReportsEveryIncompleteOutcomeInOneAggregate()
	{
		ScriptedLease unconfirmed = CreateLease(Unconfirmed);
		ScriptedLease unavailable = CreateLease(Unavailable, Unavailable);
		ScriptedLease forgotten = CreateLease(Released);
		ScriptedLease released = CreateLease(Released);
		ScriptedLease refused = CreateLease(TargetChanged);
		unconfirmed.Register(_lifetime);
		unavailable.Register(_lifetime);
		forgotten.Register(_lifetime);
		released.Register(_lifetime);
		refused.Register(_lifetime);

		_ = unconfirmed.Release();
		_ = unavailable.Release();
		released.Dispose();
		AggregateException report = Drain<AggregateException>();

		// The drain runs in reverse creation order; complete leases are never reported.
		CheatEngineFailure[] failures =
		[
			.. report.InnerExceptions.Select(static exception =>
				Assert.IsType<CheatEngineOperationException>(exception).Failure)
		];
		Assert.Equal(
			[
				CheatEngineFailureKind.TargetChanged, CheatEngineFailureKind.CapabilityUnavailable,
				CheatEngineFailureKind.IndeterminateHostResult
			],
			failures.Select(static failure => failure.Kind));
		Assert.All(failures, static failure =>
		{
			Assert.Equal(Operation, failure.Operation);
			Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, failure.HostEffect);
			Assert.Null(failure.Exception);
		});
		Assert.Equal(
			"The lease release ended with CleanupUnavailable (host effect: NotStarted); the resource may remain in " +
			"Cheat Engine or in the target.", failures[1].Message);
		Assert.Equal(1, unconfirmed.Calls);
		Assert.Equal(2, unavailable.Calls);
		Assert.Equal(1, forgotten.Calls);
		Assert.Equal(1, released.Calls);
		Assert.Equal(1, refused.Calls);
		Assert.Equal(Released, forgotten.LastReleaseOutcome);
	}

	[Fact]
	[Trait("Qualification", "Q43")]
	public void ATargetChangeReleasesATargetBoundLeaseWithoutThrowingAndTheDeactivationReportsIt()
	{
		ScriptedLease lease = CreateLease(TargetChanged);
		lease.Register(_lifetime, _lifetime.TargetSelection.Epoch);

		long next = _lifetime.TargetSelection.Advance("Test.SelectTarget");

		Assert.Equal(1, next);
		Assert.True(lease.IsReleased);
		Assert.Equal(TargetChanged, lease.LastReleaseOutcome);
		CheatEngineOperationException report = Drain<CheatEngineOperationException>();
		Assert.Equal(CheatEngineFailureKind.TargetChanged, report.Failure.Kind);
		Assert.Equal(1, lease.Calls);
	}

	[Fact]
	public void ACompleteTargetBoundReleaseLeavesBothRegistries()
	{
		ScriptedLease lease = CreateLease(Released);
		lease.Register(_lifetime, _lifetime.TargetSelection.Epoch);

		_ = lease.Release();
		_ = _lifetime.TargetSelection.Advance("Test.SelectTarget");
		_context.Stop();
		using (_lifetime.EnterCleanupScope())
		{
			_lifetime.DrainOwnedResourcesForDisable();
		}

		Assert.Equal(1, lease.Calls);
		Assert.Equal(Released, lease.LastReleaseOutcome);
	}

	[Fact]
	public void RegistrationForAnExpiredTargetSelectionFailsAndTracksNothing()
	{
		ScriptedLease lease = CreateLease(Released);
		long staleEpoch = _lifetime.TargetSelection.Epoch;
		_ = _lifetime.TargetSelection.Advance("Test.SelectTarget");

		CheatEngineClientLifecycleException exception =
			Assert.Throws<CheatEngineClientLifecycleException>(() => lease.Register(_lifetime, staleEpoch));
		_context.Stop();
		using (_lifetime.EnterCleanupScope())
		{
			_lifetime.DrainOwnedResourcesForDisable();
		}

		Assert.Equal("TargetSelection.Track", exception.Failure.Operation);
		Assert.Equal(0, lease.Calls);
	}

	[Fact]
	public void RegisteringALeaseTwiceIsRejected()
	{
		ScriptedLease lease = CreateLease(Released);
		lease.Register(_lifetime);

		Assert.Throws<InvalidOperationException>(() => lease.Register(_lifetime));
	}

	[Fact]
	[Trait("Qualification", "Q43")]
	public void DisposingTheActivationWithoutACleanupScopeReportsTheLeaseItCouldNotRelease()
	{
		ScriptedLease lease = CreateLease(Released);
		lease.Register(_lifetime);

		CheatEngineOperationException report = Assert.Throws<CheatEngineOperationException>(_lifetime.Dispose);

		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, report.Failure.Kind);
		Assert.Equal(Unavailable, lease.LastReleaseOutcome);
		Assert.Equal(0, lease.Calls);
	}

	[Fact]
	[Trait("Qualification", "Q46")]
	public void EveryAttemptIsLoggedWithTheOperationKindAndEffectOnly()
	{
		ScriptedLease lease = CreateLease(Unavailable, Released);

		_ = lease.Release();
		_ = lease.Release();
		lease.Dispose();

		Assert.Equal(
			[
				(Operation, LeaseReleaseKind.CleanupUnavailable, CheatEngineHostEffect.NotStarted),
				(Operation, LeaseReleaseKind.Released, CheatEngineHostEffect.Completed),
				(Operation, LeaseReleaseKind.AlreadyReleased, CheatEngineHostEffect.NotStarted)
			],
			_diagnostics.Releases);
		Assert.False(_diagnostics.LoggedInsideCallback);
	}

	private ScriptedLease CreateLease(params LeaseReleaseOutcome[] outcomes)
	{
		return new ScriptedLease(_dispatcher, _diagnostics, _invoker, outcomes);
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

	/// <summary>Returns scripted outcomes in order and records whether each release ran inside the main-thread invoker.</summary>
	private sealed class ScriptedLease(
		ICheatEngineDispatcher dispatcher,
		ICoreDiagnostics diagnostics,
		MarkingMainThreadInvoker invoker,
		LeaseReleaseOutcome[] outcomes) : HostResourceLease(HostResourceLeaseTests.Operation, dispatcher, diagnostics)
	{
		private int _next;

		internal int Calls
		{
			get;
			private set;
		}

		internal List<bool> RanOnMainThread
		{
			get;
		} = [];

		internal Exception? Fault
		{
			get;
			set;
		}

		protected override LeaseReleaseOutcome ReleaseOnMainThread()
		{
			Calls++;
			RanOnMainThread.Add(invoker.IsInvoking);
			if (Fault is not null)
			{
				throw Fault;
			}

			LeaseReleaseOutcome outcome = outcomes[Math.Min(_next, outcomes.Length - 1)];
			_next++;
			return outcome;
		}
	}

	/// <summary>Runs callbacks inline and marks the time spent inside them as the main thread.</summary>
	private sealed class MarkingMainThreadInvoker : IMainThreadInvoker
	{
		internal int Calls
		{
			get;
			private set;
		}

		internal bool IsInvoking
		{
			get;
			private set;
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
			IsInvoking = true;
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
				IsInvoking = false;
			}
		}
	}

	/// <summary>Records the lease events and whether one was emitted inside a dispatched callback.</summary>
	private sealed class RecordingDiagnostics : ICoreDiagnostics
	{
		internal MarkingMainThreadInvoker? Invoker
		{
			get;
			set;
		}

		internal List<(string Operation, LeaseReleaseKind Kind, CheatEngineHostEffect HostEffect)> Releases
		{
			get;
		} = [];

		internal bool LoggedInsideCallback
		{
			get;
			private set;
		}

		public void LeaseReleased(string operation, LeaseReleaseKind kind, CheatEngineHostEffect hostEffect)
		{
			LoggedInsideCallback |= Invoker?.IsInvoking == true;
			Releases.Add((operation, kind, hostEffect));
		}

		public void RuntimeSnapshotCaptured(long activationEpoch, CheatEngineArchitecture targetArchitecture,
			int processPointerBytes, int configuredPointerBytes, bool pointerSizeMismatch)
		{
		}

		public void CapabilityRefused(string capability, string operation, ClientCapabilityEvidenceReasonCode gate,
			ClientCapabilityEvidenceState gateState)
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

		public void SymbolLeaseReleased(SymbolLeaseReleaseKind kind)
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
	}
}
