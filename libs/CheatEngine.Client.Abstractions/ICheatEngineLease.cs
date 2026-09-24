using CheatEngine.Client.Results;

namespace CheatEngine.Client;

/// <summary>Owns one Cheat Engine resource that the Client created for the current activation.</summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client creates and implements leases; do not implement this interface. Members can be
///         added in a minor release.
///     </para>
///     <para>
///         <see cref="Release" /> releases the resource on Cheat Engine's main thread and returns what happened as a
///         <see cref="LeaseReleaseOutcome" />; it does not throw for a cleanup that failed, was refused, or could not
///         begin. <see cref="IDisposable.Dispose" /> performs the same release, <b>never throws</b>, and discards the
///         outcome, which <see cref="LastReleaseOutcome" /> still reports.
///     </para>
///     <para>
///         Releasing is idempotent. Once an attempt ends the lease (<see cref="IsReleased" />), a later
///         <see cref="Release" /> or <see cref="IDisposable.Dispose" /> makes no Cheat Engine call and returns
///         <see cref="LeaseReleaseKind.AlreadyReleased" />. A retryable outcome
///         (<see cref="LeaseReleaseOutcome.IsRetryable" />) keeps the lease active: a later release tries again, and the
///         activation cleanup tries again before the plugin is disabled.
///     </para>
///     <para>
///         The activation owns every lease it created. A lease that was never released is released when the plugin is
///         disabled, and a target-bound lease is also released when Cheat Engine selects another process. A release that
///         remains incomplete at that point (a retryable outcome that failed again, a refusal, an unconfirmed or partial
///         cleanup) is reported in the aggregated deactivation failure, not thrown to the code that released the lease.
///     </para>
/// </remarks>
public interface ICheatEngineLease : IDisposable
{
	/// <summary>
	///     Gets whether the lease has ended: an attempt returned an outcome that is not retryable, so no later attempt
	///     will be made.
	/// </summary>
	/// <remarks>
	///     An ended lease is not necessarily clean: read <see cref="LastReleaseOutcome" /> to know whether the resource was
	///     released (<see cref="LeaseReleaseOutcome.IsComplete" />) or may remain
	///     (<see cref="LeaseReleaseOutcome.RequiresManualRecovery" />).
	/// </remarks>
	public bool IsReleased
	{
		get;
	}

	/// <summary>
	///     Gets the outcome of the last release attempt, or <see langword="null" /> when the lease has not been released
	///     yet.
	/// </summary>
	/// <remarks>
	///     A repeated release of an ended lease returns <see cref="LeaseReleaseKind.AlreadyReleased" /> but does not
	///     replace this value, which keeps the outcome of the attempt that ended the lease.
	/// </remarks>
	public LeaseReleaseOutcome? LastReleaseOutcome
	{
		get;
	}

	/// <summary>Releases the resource on Cheat Engine's main thread and returns what happened.</summary>
	/// <returns>
	///     The outcome of this attempt; <see cref="LeaseReleaseKind.AlreadyReleased" /> when an earlier attempt already
	///     ended the lease.
	/// </returns>
	/// <remarks>
	///     The method can be called from any thread: the release itself runs on Cheat Engine's main thread. When the
	///     work cannot be dispatched (for example while the plugin is being disabled, from a thread other than the main
	///     thread), the outcome is <see cref="LeaseReleaseKind.CleanupUnavailable" /> and the lease stays active.
	/// </remarks>
	public LeaseReleaseOutcome Release();
}
