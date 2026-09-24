using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>The one base of every Client lease: main-thread release, idempotence, registration, and reporting.</summary>
/// <remarks>
///     <para>
///         A derived lease implements only <see cref="ReleaseOnMainThread" />, which runs on Cheat Engine's main thread
///         through the activation dispatcher (<c>SdkMainThreadDispatcher</c> in production) and maps the SDK release
///         status with <see cref="SdkReleaseOutcomes" />. This base serializes the attempts, records the outcome, and
///         never lets an exception escape <see cref="Release" /> or <see cref="Dispose" />: an exception from the derived
///         release is <see cref="LeaseReleaseKind.CleanupUnconfirmed" /> with an <see cref="CheatEngineHostEffect.Unknown" />
///         effect (a call may have begun, so it is never retried), and work that cannot be dispatched is
///         <see cref="LeaseReleaseKind.CleanupUnavailable" /> with <see cref="CheatEngineHostEffect.NotStarted" />.
///     </para>
///     <para>
///         <see cref="Register" /> tracks the lease in the activation registry and, for a target-bound lease, in the
///         target selection as well. A complete outcome unregisters it. A retryable outcome keeps it registered, so the
///         activation cleanup retries it before the plugin is disabled; an outcome that requires manual recovery keeps
///         it registered too, so the aggregated deactivation report (audit Q43) carries it. A target change disposes a
///         target-bound lease without throwing to the caller that selected the new target.
///     </para>
///     <para>
///         Every attempt is logged after the dispatched work returned, with its operation name, kind and effect only
///         (audit Q46).
///     </para>
/// </remarks>
internal abstract class HostResourceLease : ICheatEngineLease, IOutcomeReportingResource
{
	private static readonly LeaseReleaseOutcome AlreadyReleasedOutcome =
		new(LeaseReleaseKind.AlreadyReleased, CheatEngineHostEffect.NotStarted);

	private static readonly LeaseReleaseOutcome UnavailableOutcome =
		new(LeaseReleaseKind.CleanupUnavailable, CheatEngineHostEffect.NotStarted);

	private static readonly LeaseReleaseOutcome UnexpectedFaultOutcome =
		new(LeaseReleaseKind.CleanupUnconfirmed, CheatEngineHostEffect.Unknown);

	private readonly ICoreDiagnostics _diagnostics;
	private readonly ICheatEngineDispatcher _dispatcher;
	private readonly Lock _gate = new();
	private LeaseReleaseOutcome? _lastOutcome;
	private CoreLifetime? _lifetime;
	private int _released;
	private bool _targetBound;

	/// <summary>Creates a lease that releases through <paramref name="dispatcher" />.</summary>
	/// <param name="operation">The stable operation name of the release, for example <c>Allocations.Release</c>.</param>
	/// <param name="dispatcher">The activation dispatcher that runs the release on Cheat Engine's main thread.</param>
	/// <param name="diagnostics">The activation diagnostics; nothing is logged when omitted.</param>
	protected HostResourceLease(string operation, ICheatEngineDispatcher dispatcher, ICoreDiagnostics? diagnostics)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		Operation = operation;
		_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		_diagnostics = GuardedCoreDiagnostics.Wrap(diagnostics);
	}

	/// <summary>Gets the stable operation name of the release, the only text its logs and reports carry.</summary>
	internal string Operation
	{
		get;
	}

	public bool IsReleased => Volatile.Read(ref _released) != 0;

	public LeaseReleaseOutcome? LastReleaseOutcome
	{
		get
		{
			lock (_gate)
			{
				return _lastOutcome;
			}
		}
	}

	public LeaseReleaseOutcome Release()
	{
		LeaseReleaseOutcome outcome = Attempt();
		_diagnostics.LeaseReleased(Operation, outcome.Kind, outcome.HostEffect);
		return outcome;
	}

	/// <summary>Releases the lease like <see cref="Release" /> and discards the outcome; it never throws.</summary>
	public void Dispose()
	{
		try
		{
			_ = Release();
		}
		catch (Exception)
		{
			// Deliberately ignored: Dispose never throws (the outcome stays in LastReleaseOutcome).
		}
	}

	Exception? IOutcomeReportingResource.ReleaseForDeactivation()
	{
		try
		{
			LeaseReleaseOutcome outcome = IsReleased ? (LastReleaseOutcome ?? AlreadyReleasedOutcome) : Release();
			return outcome.IsComplete ? null : CreateReport(outcome);
		}
		catch (Exception exception)
		{
			return exception;
		}
	}

	/// <summary>Registers the lease with the activation that owns it, and with its target selection when bound to one.</summary>
	/// <param name="lifetime">The owning activation.</param>
	/// <param name="targetSelectionEpoch">
	///     The target-selection epoch the resource belongs to, or <see langword="null" /> for a resource that is not bound
	///     to the selected target.
	/// </param>
	/// <exception cref="CheatEngineClientException">
	///     The activation is no longer active, or the target selection already changed; the lease is not registered.
	/// </exception>
	internal void Register(CoreLifetime lifetime, long? targetSelectionEpoch = null)
	{
		ArgumentNullException.ThrowIfNull(lifetime);
		lock (_gate)
		{
			if (_lifetime is not null)
			{
				throw new InvalidOperationException("A Client lease is registered with its activation only once.");
			}

			_lifetime = lifetime;
			_targetBound = targetSelectionEpoch.HasValue;
		}

		lifetime.Track(this);
		if (targetSelectionEpoch is not { } epoch)
		{
			return;
		}

		try
		{
			_ = lifetime.TargetSelection.Track(this, epoch);
		}
		catch (Exception)
		{
			_ = lifetime.Untrack(this);
			throw;
		}
	}

	/// <summary>
	///     Releases the resource. It runs on Cheat Engine's main thread, one attempt at a time, and never after the lease
	///     ended.
	/// </summary>
	/// <returns>The outcome, usually mapped from the SDK release status with <see cref="SdkReleaseOutcomes" />.</returns>
	/// <remarks>An exception is recorded as an unconfirmed cleanup with an unknown effect and never retried.</remarks>
	protected abstract LeaseReleaseOutcome ReleaseOnMainThread();

	/// <summary>Maps an incomplete outcome to the failure kind that the deactivation report carries.</summary>
	private static CheatEngineFailureKind GetReportKind(LeaseReleaseKind kind)
	{
		return kind switch
		{
			LeaseReleaseKind.RefusedNoTarget => CheatEngineFailureKind.TargetNotAttached,
			LeaseReleaseKind.RefusedTargetChanged => CheatEngineFailureKind.TargetChanged,
			LeaseReleaseKind.RefusedTargetIdentityUnavailable => CheatEngineFailureKind.TargetIdentityUnavailable,
			LeaseReleaseKind.RefusedRuntimeChanged => CheatEngineFailureKind.RuntimeChanged,
			LeaseReleaseKind.CleanupUnavailable => CheatEngineFailureKind.CapabilityUnavailable,
			LeaseReleaseKind.CleanupUnconfirmed or LeaseReleaseKind.PartiallyReleased =>
				CheatEngineFailureKind.IndeterminateHostResult,
			_ => CheatEngineFailureKind.Unknown
		};
	}

	private LeaseReleaseOutcome Attempt()
	{
		if (IsReleased)
		{
			return AlreadyReleasedOutcome;
		}

		bool ran = false;
		LeaseReleaseOutcome dispatched = default;
		try
		{
			_ = _dispatcher.TryInvoke(() =>
			{
				dispatched = ReleaseUnderGate();
				ran = true;
				return true;
			}, out bool _, out CheatEngineFailure _);
		}
		catch (Exception)
		{
			// Dispatch was refused (activation stopping or ended, or no main-thread admission): nothing ran unless the
			// callback already recorded its outcome below.
		}

		return ran ? dispatched : RecordUnavailable();
	}

	private LeaseReleaseOutcome ReleaseUnderGate()
	{
		lock (_gate)
		{
			if (IsReleased)
			{
				return AlreadyReleasedOutcome;
			}

			LeaseReleaseOutcome outcome;
			try
			{
				outcome = ReleaseOnMainThread();
			}
			catch (Exception)
			{
				outcome = UnexpectedFaultOutcome;
			}

			return Record(outcome);
		}
	}

	private LeaseReleaseOutcome RecordUnavailable()
	{
		lock (_gate)
		{
			return IsReleased ? AlreadyReleasedOutcome : Record(UnavailableOutcome);
		}
	}

	/// <summary>Records an attempt's outcome; the caller holds the gate.</summary>
	private LeaseReleaseOutcome Record(LeaseReleaseOutcome outcome)
	{
		_lastOutcome = outcome;
		if (!outcome.IsRetryable)
		{
			Volatile.Write(ref _released, 1);
		}

		if (outcome.IsComplete && _lifetime is { } lifetime)
		{
			_ = lifetime.Untrack(this);
			if (_targetBound)
			{
				_ = lifetime.TargetSelection.Untrack(this);
			}
		}

		return outcome;
	}

	private CheatEngineOperationException CreateReport(LeaseReleaseOutcome outcome)
	{
		return new CheatEngineOperationException(new CheatEngineFailure(GetReportKind(outcome.Kind), Operation,
			$"The lease release ended with {outcome.Kind} (host effect: {outcome.HostEffect}); the resource may " +
			"remain in Cheat Engine or in the target.", null, CheatEngineHostEffect.CleanupUnconfirmed));
	}
}
