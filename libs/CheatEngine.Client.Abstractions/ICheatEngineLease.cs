using CheatEngine.Client.Results;

namespace CheatEngine.Client;

/// <summary>Owns one Cheat Engine resource that the Client created for the current activation.</summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add
///         members to it, so implement it only in a test double.
///     </para>
///     <para>
///         <see cref="Release" /> releases the resource on Cheat Engine's main thread and returns what happened as a
///         <see cref="LeaseReleaseOutcome" />; it does not throw for a cleanup that failed, was refused, or could not
///         begin. <see cref="IDisposable.Dispose" /> performs the same release, <b>never throws</b>, and discards the
///         outcome, which <see cref="LastReleaseOutcome" /> still reports.
///     </para>
///     <para>
///         Releasing is idempotent. Once an attempt ends the lease (<see cref="IsReleased" />), a later
///         <see cref="Release" /> or <see cref="IDisposable.Dispose" /> makes no Cheat Engine call and returns the
///         outcome that ended the lease (<see cref="LastReleaseOutcome" />), unchanged, so a refused or unconfirmed
///         release is never reported as complete by a second call. A retryable outcome
///         (<see cref="LeaseReleaseOutcome.IsRetryable" />) keeps the lease active: a later release tries again, and the
///         activation cleanup tries again before the plugin is disabled.
///     </para>
///     <para>
///         The activation owns every lease it created. A lease that was never released is released when the plugin is
///         disabled. A target-bound lease (an allocation, a value-scan session, an Auto Assembler patch) also ends when
///         the Client observes that Cheat Engine selected another process, but that release frees nothing: it reaches
///         CheatEngine.SDK after Cheat Engine already targets the new process, so CheatEngine.SDK refuses it before any
///         Cheat Engine call (<see cref="LeaseReleaseKind.RefusedTargetChanged" /> or another refusal, which requires
///         manual recovery) and no later release can free the resource: an allocation or a patch stays in the previous
///         process, and the scanner and found list of a session stay in Cheat Engine. Release a target-bound lease
///         before selecting another process. A release that is still incomplete when the plugin is disabled (a
///         retryable outcome that failed again, a refusal, an unconfirmed or partial cleanup) is reported in the
///         aggregated deactivation failure, not thrown to the code that released the lease.
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
	///     Once the lease has ended, this value is the outcome of the attempt that ended it; a repeated release returns it
	///     and does not replace it.
	/// </remarks>
	public LeaseReleaseOutcome? LastReleaseOutcome
	{
		get;
	}

	/// <summary>Releases the resource on Cheat Engine's main thread and returns what happened.</summary>
	/// <returns>
	///     The outcome of this attempt; when an earlier attempt already ended the lease, the outcome of that attempt
	///     (<see cref="LastReleaseOutcome" />), without any Cheat Engine call.
	/// </returns>
	/// <remarks>
	///     The method can be called from any thread: the release itself runs on Cheat Engine's main thread. When the
	///     work cannot be dispatched (for example while the plugin is being disabled, from a thread other than the main
	///     thread), the outcome is <see cref="LeaseReleaseKind.CleanupUnavailable" /> and the lease stays active.
	/// </remarks>
	public LeaseReleaseOutcome Release();
}
