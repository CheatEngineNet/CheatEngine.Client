using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Results;

namespace CheatEngine.Client.Scanning;

/// <summary>One value scan over Cheat Engine's scanner: a first scan, next scans, and bounded reads of the results.</summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add
///         members to it, so implement it only in a test double.
///     </para>
///     <para>
///         <b>Experimental (<c>CECLIENT5001</c>).</b> The value-scan API can change in a minor release until its live
///         scenarios pass; see the Abstractions README.
///     </para>
///     <para>
///         <b>Sequence.</b> A session accepts a first scan in <see cref="ValueScanSessionState.Created" />, then next
///         scans, counts and reads in <see cref="ValueScanSessionState.ResultsReady" />; <see cref="TryReset" /> returns
///         it to <see cref="ValueScanSessionState.Created" />. An operation that the state does not accept is refused with
///         <see cref="CheatEngineFailureKind.InvalidState" /> and <see cref="CheatEngineHostEffect.NotStarted" />. Every
///         operation runs on Cheat Engine's main thread through the activation dispatcher; a first or next scan starts
///         Cheat Engine's scan and waits for it in the same call, so Cheat Engine's main thread is busy until the scan
///         ends.
///     </para>
///     <para>
///         <b>Re-entrancy.</b> Cheat Engine runs queued main-thread work while it waits for a scan. A call to this session
///         made from such work is refused with <see cref="CheatEngineFailureKind.InvalidState" />; a release requested
///         from it runs once, when the scan call has returned.
///     </para>
///     <para>
///         <b>Cancellation.</b> The token is observed before each Cheat Engine call and never interrupts one. A scan
///         cancelled after Cheat Engine started it and before the Client waits for it reports
///         <see cref="CheatEngineFailureKind.Cancelled" /> with <see cref="CheatEngineHostEffect.Started" />: the session
///         stays <see cref="ValueScanSessionState.Scanning" /> and accepts only its release, which asks Cheat Engine to
///         stop the scan and waits for it for up to five seconds. A cancellation observed after Cheat Engine finished
///         reports <see cref="CheatEngineHostEffect.Completed" /> and publishes nothing.
///     </para>
///     <para>
///         <b>Failures.</b> When a scan fails after Cheat Engine began it, the failure message ends with Cheat Engine's own
///         error text, bounded to 1024 bytes, when it reported one; classify the failure by its kind, never by that text.
///         An operation after the session's target or Lua runtime changed is refused with
///         <see cref="CheatEngineFailureKind.TargetChanged" />, <see cref="CheatEngineFailureKind.TargetIdentityUnavailable" />
///         or <see cref="CheatEngineFailureKind.RuntimeChanged" />; only the release remains. After the release, an
///         operation fails with <see cref="CheatEngineFailureKind.InvalidState" />, unless such a change refused the
///         release (<see cref="LeaseReleaseKind.RefusedTargetChanged" />,
///         <see cref="LeaseReleaseKind.RefusedTargetIdentityUnavailable" /> or
///         <see cref="LeaseReleaseKind.RefusedRuntimeChanged" />): it then keeps failing with the kind of that change,
///         which a throwing form throws as <see cref="CheatEngineOperationException" />.
///     </para>
///     <para>
///         <b>Release.</b> <see cref="ICheatEngineLease.Release" /> destroys the found list, then the scanner, on Cheat
///         Engine's main thread, and reports the worse of the two outcomes; a scan that may still run is asked to stop
///         first, and a stop that Cheat Engine did not confirm is <see cref="LeaseReleaseKind.CleanupUnconfirmed" />.
///         CheatEngine.SDK never destroys them through another target: after a target change it refuses the release
///         before any Cheat Engine call (<see cref="LeaseReleaseKind.RefusedTargetChanged" /> or
///         <see cref="LeaseReleaseKind.RefusedTargetIdentityUnavailable" />), which requires manual recovery. After a
///         change of the Lua runtime, or when the target was never checked, CheatEngine.SDK consumes the session without
///         any Cheat Engine call and reports a release that could not begin: the outcome is
///         <see cref="LeaseReleaseKind.CleanupUnavailable" />, <see cref="ICheatEngineLease.IsReleased" /> stays
///         <see langword="false" /> while <see cref="State" /> is <see cref="ValueScanSessionState.Closed" />, and the
///         lease stays registered so that the deactivation report carries it. Retrying that release destroys nothing,
///         since CheatEngine.SDK no longer owns the objects.
///     </para>
/// </remarks>
[Experimental(ClientExperimentalDiagnostics.ValueScans, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
public interface IValueScanSession : ICheatEngineLease
{
	/// <summary>Gets the target-selection epoch of the process the session was created in.</summary>
	public long SelectionEpoch
	{
		get;
	}

	/// <summary>Gets the session state that Cheat Engine's scan session reported after the last operation.</summary>
	public ValueScanSessionState State
	{
		get;
	}

	/// <summary>
	///     Gets why the session is <see cref="ValueScanSessionState.Invalidated" />, or
	///     <see cref="ValueScanInvalidationKind.None" />.
	/// </summary>
	public ValueScanInvalidationKind Invalidation
	{
		get;
	}

	/// <summary>Tries to run a first scan and wait for its results.</summary>
	/// <param name="request">The first scan.</param>
	/// <param name="failure">The classified failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">Observed before the scan starts, before the wait, and after it.</param>
	/// <returns><see langword="true" /> when the results are ready.</returns>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> did not come from a <see cref="ValueScanFirstRequest" /> factory: the
	///     <see langword="default" /> request, or a tampered one (an <see cref="ArgumentOutOfRangeException" /> for a
	///     comparison, a value type, a range or an option its factories would refuse). It is thrown before the
	///     activation check and before any Cheat Engine call.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback.
	/// </exception>
	public bool TryFirstScan(ValueScanFirstRequest request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Runs a first scan and waits for its results, or throws the failure.</summary>
	/// <param name="request">The first scan.</param>
	/// <param name="cancellationToken">Observed before the scan starts, before the wait, and after it.</param>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> did not come from a <see cref="ValueScanFirstRequest" /> factory: the
	///     <see langword="default" /> request, or a tampered one (an <see cref="ArgumentOutOfRangeException" /> for a
	///     comparison, a value type, a range or an option its factories would refuse). It is thrown before the
	///     activation check and before any Cheat Engine call.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback, or the scan failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />: a state the session does not accept, a re-entrant call,
	///     or a released session, unless a target or Lua runtime change refused its release (see the remarks).
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The scan observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">
	///     The scan failed with any other failure kind.
	/// </exception>
	public void FirstScan(ValueScanFirstRequest request, CancellationToken cancellationToken = default);

	/// <summary>Tries to run a next scan over the current results and wait for its results.</summary>
	/// <param name="request">The next scan.</param>
	/// <param name="failure">The classified failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">Observed before the scan starts, before the wait, and after it.</param>
	/// <returns><see langword="true" /> when the new results are ready.</returns>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> did not come from a <see cref="ValueScanNextRequest" /> factory: the
	///     <see langword="default" /> request, or a tampered one (an <see cref="ArgumentOutOfRangeException" /> for a
	///     comparison or a value type that is not a defined value). It is thrown before the activation check and
	///     before any Cheat Engine call; a value of another type than the session's first scan is refused with
	///     <see cref="CheatEngineFailureKind.OperationRejected" /> instead.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback.
	/// </exception>
	public bool TryNextScan(ValueScanNextRequest request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Runs a next scan over the current results and waits for its results, or throws the failure.</summary>
	/// <param name="request">The next scan.</param>
	/// <param name="cancellationToken">Observed before the scan starts, before the wait, and after it.</param>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> did not come from a <see cref="ValueScanNextRequest" /> factory: the
	///     <see langword="default" /> request, or a tampered one (an <see cref="ArgumentOutOfRangeException" /> for a
	///     comparison or a value type that is not a defined value). It is thrown before the activation check and
	///     before any Cheat Engine call; a value of another type than the session's first scan is refused with
	///     <see cref="CheatEngineFailureKind.OperationRejected" /> instead.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback, or the scan failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />: a state the session does not accept, a re-entrant call,
	///     or a released session, unless a target or Lua runtime change refused its release (see the remarks).
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The scan observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">
	///     The scan failed with any other failure kind.
	/// </exception>
	public void NextScan(ValueScanNextRequest request, CancellationToken cancellationToken = default);

	/// <summary>Tries to clear the results so that the session accepts a new first scan.</summary>
	/// <param name="failure">The classified failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">Observed before Cheat Engine clears the results, and after.</param>
	/// <returns><see langword="true" /> when the session is <see cref="ValueScanSessionState.Created" />.</returns>
	/// <remarks>
	///     A reset recovers an <see cref="ValueScanSessionState.Invalidated" /> session while its target and Lua runtime
	///     are unchanged; it is refused while Cheat Engine may still be scanning.
	/// </remarks>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback.
	/// </exception>
	public bool TryReset(out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Clears the results so that the session accepts a new first scan, or throws the failure.</summary>
	/// <param name="cancellationToken">Observed before Cheat Engine clears the results, and after.</param>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback, or the reset failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />: a state the session does not accept, a re-entrant call,
	///     or a released session, unless a target or Lua runtime change refused its release (see the remarks).
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The reset observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">
	///     The reset failed with any other failure kind.
	/// </exception>
	public void Reset(CancellationToken cancellationToken = default);

	/// <summary>Tries to read the number of current results.</summary>
	/// <param name="resultCount">The number of results when the method returns <see langword="true" />.</param>
	/// <param name="failure">The classified failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">Observed before Cheat Engine is asked, and after.</param>
	/// <returns><see langword="true" /> when the count was read.</returns>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback.
	/// </exception>
	public bool TryGetResultCount(out ulong resultCount, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Reads the number of current results, or throws the failure.</summary>
	/// <param name="cancellationToken">Observed before Cheat Engine is asked, and after.</param>
	/// <returns>The number of results.</returns>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback, or the count failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />: a state the session does not accept, a re-entrant call,
	///     or a released session, unless a target or Lua runtime change refused its release (see the remarks).
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The count observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">
	///     The count failed with any other failure kind.
	/// </exception>
	public ulong GetResultCount(CancellationToken cancellationToken = default);

	/// <summary>Tries to copy one bounded page of the current results.</summary>
	/// <param name="request">The first index and the maximum number of results to copy.</param>
	/// <param name="page">The copied page when the method returns <see langword="true" />.</param>
	/// <param name="failure">The classified failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">Observed before the copy and between the copied results.</param>
	/// <returns>
	///     <see langword="true" /> when the page was copied; a scan without results reads as an empty page. A start index
	///     at or beyond a non-zero result count is refused with <see cref="CheatEngineFailureKind.OperationRejected" />.
	/// </returns>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which allows no result, or a tampered
	///     one: it is thrown before the activation check and before any Cheat Engine call.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback.
	/// </exception>
	public bool TryRead(ValueScanReadRequest request, out ValueScanPage page, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Copies one bounded page of the current results, or throws the failure.</summary>
	/// <param name="request">The first index and the maximum number of results to copy.</param>
	/// <param name="cancellationToken">Observed before the copy and between the copied results.</param>
	/// <returns>The copied page.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which allows no result, or a tampered
	///     one: it is thrown before the activation check and before any Cheat Engine call.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback, or the read failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />: a state the session does not accept, a re-entrant call,
	///     or a released session, unless a target or Lua runtime change refused its release (see the remarks).
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The read observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">
	///     The read failed with any other failure kind.
	/// </exception>
	public ValueScanPage Read(ValueScanReadRequest request, CancellationToken cancellationToken = default);
}
