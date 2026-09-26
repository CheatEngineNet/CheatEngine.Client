using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Inspection;

namespace CheatEngine.Client.Processes;

/// <summary>Reads and changes the process selected by the active Cheat Engine session.</summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add
///         members to it, so implement it only in a test double.
///     </para>
///     <para>
///         Cheat Engine's selected target is ambient: a session that holds a process identifier does not stop the user,
///         another plugin or a script from selecting another process. The checks of this client (CheatEngine.SDK's
///         PID-bracketed observation, the process incarnation, the selection epoch) reduce that risk; they are not
///         transactions. Like CheatEngine.SDK, this client cannot see a selection that changed and changed back between
///         two observations (A-B-A): only a later observation of another PID or incarnation advances the epoch.
///     </para>
///     <para>
///         Every observation is a read-only CheatEngine.SDK 2.0.0 operation that reads the selected process identifier
///         before and after the target facts, and reads none of them when no target is selected: with no target opened,
///         Cheat Engine reports the same ISA family, width and pointer size as an x64 target. The ISA is the SDK's
///         derivation from Cheat Engine's x86 and ARM family facts together with its 64-bit fact, never from the 64-bit
///         fact alone; the process width is the observed bitness. A file opened as a process has no process identity.
///     </para>
///     <para>
///         For a local process the selection identity also includes its incarnation: the PID and the creation time that
///         CheatEngine.SDK observed together with the local backend. When the same PID denotes another process (a
///         different creation time), the selection epoch advances. A CEServer target or a target whose backend is not
///         established has no local incarnation and never receives local operating-system metadata.
///     </para>
///     <para>
///         Every member but <see cref="TryGetLocalProcesses" /> and <see cref="GetLocalProcesses" /> checks the
///         activation after its arguments: an ended activation throws
///         <see cref="CheatEngineActivationExpiredException" /> and a stopping one
///         <see cref="CheatEngineInvalidStateException" />. A deactivation callback can still read the current
///         process on Cheat Engine's main thread (see <see cref="ICheatEngineClient" />), unless the observation finds
///         a changed target selection, which cannot advance while the activation stops; it cannot attach. A
///         <c>Try</c> member returns every other failure; the throwing member with the same inputs throws it through
///         <see cref="CheatEngineFailure.Throw(CancellationToken)" />.
///     </para>
/// </remarks>
public interface IProcessClient
{
	/// <summary>Tries to get a copied snapshot of the currently selected target process.</summary>
	/// <param name="snapshot">The copied selected process on success; otherwise the default value.</param>
	/// <param name="failure">The classified failure; the default value on success.</param>
	/// <param name="cancellationToken">
	///     Observed before the observation is dispatched to Cheat Engine's main thread.
	/// </param>
	/// <returns><see langword="true" /> when Cheat Engine's selected target was observed and copied.</returns>
	/// <remarks>
	///     Returns <see cref="CheatEngineFailureKind.TargetNotAttached" /> only when Cheat Engine has no selected target.
	///     Every other status that establishes no target keeps its own kind, with
	///     <see cref="CheatEngineHostEffect.Completed" />: <see cref="CheatEngineFailureKind.TargetChanged" /> when the
	///     selected target changed while it was observed, <see cref="CheatEngineFailureKind.TargetIdentityUnavailable" />
	///     for a file opened as a process, and <see cref="CheatEngineFailureKind.CapabilityUnavailable" />,
	///     <see cref="CheatEngineFailureKind.LuaError" /> or <see cref="CheatEngineFailureKind.InvalidHostResult" /> for
	///     an absent, raising or malformed selection read. Another target fact that raises or is malformed leaves that
	///     fact unknown. Local operating-system metadata is optional enrichment; its absence leaves the Cheat Engine
	///     target snapshot valid with null name and executable path. An SDK fault of a Cheat Engine or local-catalog
	///     call is returned as a classified failure and never crosses this method. Invalid arguments and Client
	///     lifecycle exceptions (<see cref="CheatEngineClientException" />) are thrown.
	/// </remarks>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping (outside a deactivation callback, or inside one when the observation finds a
	///     changed target selection).
	/// </exception>
	public bool TryGetCurrentProcess(out ProcessSnapshot snapshot, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Gets the selected process or throws when no target is attached.</summary>
	/// <param name="cancellationToken">
	///     Observed before the observation is dispatched to Cheat Engine's main thread.
	/// </param>
	/// <returns>The copied selected process.</returns>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping (outside a deactivation callback, or inside one when the observation finds a
	///     changed target selection), or the observation failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The call observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">
	///     The observation failed with any other failure kind, <see cref="CheatEngineFailureKind.TargetNotAttached" />
	///     included.
	/// </exception>
	public ProcessSnapshot GetCurrentProcess(CancellationToken cancellationToken = default);

	/// <summary>
	///     Re-reads Cheat Engine's selected target and advances the selection epoch when its PID or its local
	///     incarnation changed, or when its observed backend, ISA or process width changed from one known value to
	///     another.
	/// </summary>
	/// <param name="snapshot">The copied selected process on success; otherwise the default value.</param>
	/// <param name="failure">The classified failure; the default value on success.</param>
	/// <param name="cancellationToken">
	///     Observed before the observation is dispatched to Cheat Engine's main thread.
	/// </param>
	/// <returns><see langword="true" /> when Cheat Engine's selected target was observed and copied.</returns>
	/// <remarks>
	///     Returns <see cref="CheatEngineFailureKind.TargetNotAttached" /> and invalidates an observed selection when
	///     Cheat Engine reports no selected target; a file opened as a process also invalidates it and returns
	///     <see cref="CheatEngineFailureKind.TargetIdentityUnavailable" />. A fact that is transiently unknown for the
	///     same PID neither advances the selection epoch nor replaces the last value known for that selection, so a
	///     probe failure does not invalidate target-bound leases and does not weaken the selection identity to the PID
	///     alone. Local metadata is optional enrichment and does not establish liveness or target identity. This is an
	///     observation, not an atomic process-lifetime guarantee; exceptions follow
	///     <see cref="TryGetCurrentProcess" />.
	/// </remarks>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping (outside a deactivation callback, or inside one when the observation finds a
	///     changed target selection).
	/// </exception>
	public bool TryRefresh(out ProcessSnapshot snapshot, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Refreshes the selected target or throws when no target is attached.</summary>
	/// <param name="cancellationToken">
	///     Observed before the observation is dispatched to Cheat Engine's main thread.
	/// </param>
	/// <returns>The copied selected process.</returns>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping (outside a deactivation callback, or inside one when the observation finds a
	///     changed target selection), or the observation failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The call observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">
	///     The observation failed with any other failure kind, <see cref="CheatEngineFailureKind.TargetNotAttached" />
	///     included.
	/// </exception>
	public ProcessSnapshot Refresh(CancellationToken cancellationToken = default);

	/// <summary>Tries to attach Cheat Engine to an explicit process identifier and verifies the selected target.</summary>
	/// <param name="processId">The positive identifier of the process to attach to.</param>
	/// <param name="snapshot">The copied process selected after the attach on success; otherwise the default.</param>
	/// <param name="failure">The classified failure; the default value on success.</param>
	/// <param name="cancellationToken">Observed before the attach is dispatched to Cheat Engine's main thread.</param>
	/// <returns><see langword="true" /> when Cheat Engine selected the requested process and it was observed.</returns>
	/// <remarks>
	///     <para>
	///         The attach is CheatEngine.SDK's <c>SelectAndObserve</c>: Cheat Engine's selection call, then the selected
	///         process identifier read again; a normal return of the call is not success by itself. A refused attach keeps
	///         the SDK status as its kind, with <see cref="CheatEngineHostEffect.Unknown" />:
	///         <see cref="CheatEngineFailureKind.OperationRejected" /> when Cheat Engine did not confirm the requested
	///         process, <see cref="CheatEngineFailureKind.TargetNotAttached" /> when it then reported no target,
	///         <see cref="CheatEngineFailureKind.TargetIdentityUnavailable" /> for a file opened as a process,
	///         <see cref="CheatEngineFailureKind.TargetChanged" /> when the selection changed again before it was observed,
	///         and <see cref="CheatEngineFailureKind.CapabilityUnavailable" />,
	///         <see cref="CheatEngineFailureKind.LuaError" /> or <see cref="CheatEngineFailureKind.InvalidHostResult" /> for
	///         an absent, raising or malformed global. The selection is observed again after a refusal, so the selection
	///         epoch follows whatever Cheat Engine now selects.
	///     </para>
	///     <para>
	///         Attaching resets Cheat Engine's configured pointer size to the target default, so an attach silently undoes
	///         an earlier pointer-size override. A fault of the attach call is returned with
	///         <see cref="CheatEngineHostEffect.Unknown" />; exceptions otherwise follow
	///         <see cref="TryGetCurrentProcess" />.
	///     </para>
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="processId" /> is not positive.</exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">The activation is stopping.</exception>
	public bool TryAttach(TargetProcessId processId, out ProcessSnapshot snapshot,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Attaches to an explicit process identifier or throws when the host rejects it.</summary>
	/// <param name="processId">The positive identifier of the process to attach to.</param>
	/// <param name="cancellationToken">Observed before the attach is dispatched to Cheat Engine's main thread.</param>
	/// <returns>The copied process selected after the attach.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="processId" /> is not positive.</exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, or the attach failed with <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The attach observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">The attach failed with any other failure kind.</exception>
	public ProcessSnapshot Attach(TargetProcessId processId,
		CancellationToken cancellationToken = default);

	/// <summary>
	///     Tries to attach to the single locally discovered process whose executable name matches exactly.
	/// </summary>
	/// <param name="processName">The executable file name, with or without <c>.exe</c> and without a directory.</param>
	/// <param name="snapshot">The copied process selected after the attach on success; otherwise the default.</param>
	/// <param name="failure">
	///     The classified failure, <see cref="CheatEngineFailureKind.NotFound" /> when no local process matches and
	///     <see cref="CheatEngineFailureKind.AmbiguousMatch" /> when several do; the default value on success.
	/// </param>
	/// <param name="cancellationToken">
	///     Observed before the local catalog is searched, and before the attach is dispatched to Cheat Engine's main
	///     thread.
	/// </param>
	/// <returns><see langword="true" /> when Cheat Engine selected the single match and it was observed.</returns>
	/// <remarks>
	///     Activation admission occurs before local discovery; caller cancellation is then observed before catalog access.
	///     A local match is only an attach candidate. Cheat Engine's selected target is verified before returning.
	/// </remarks>
	/// <exception cref="ArgumentNullException"><paramref name="processName" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentException">
	///     <paramref name="processName" /> is empty, white space, includes a directory path or is only <c>.exe</c>.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">The activation is stopping.</exception>
	public bool TryAttachExactName(string processName, out ProcessSnapshot snapshot, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Attaches to the single exact-name match or throws when none or several exist.</summary>
	/// <param name="processName">The executable file name, with or without <c>.exe</c> and without a directory.</param>
	/// <param name="cancellationToken">
	///     Observed before the local catalog is searched, and before the attach is dispatched to Cheat Engine's main
	///     thread.
	/// </param>
	/// <returns>The copied process selected after the attach.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="processName" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentException">
	///     <paramref name="processName" /> is empty, white space, includes a directory path or is only <c>.exe</c>.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, or the attach failed with <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The attach observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">
	///     The attach failed with any other failure kind, <see cref="CheatEngineFailureKind.NotFound" /> and
	///     <see cref="CheatEngineFailureKind.AmbiguousMatch" /> included.
	/// </exception>
	public ProcessSnapshot AttachExactName(string processName, CancellationToken cancellationToken = default);

	/// <summary>Tries to enumerate copied local-process metadata within an explicit materialization bound.</summary>
	/// <param name="request">The bound on the number of copied processes and an optional name filter.</param>
	/// <param name="result">The copied processes, ordered by identifier, on success; otherwise the default.</param>
	/// <param name="failure">The classified failure; the default value on success.</param>
	/// <param name="cancellationToken">
	///     Observed before the catalog is read and between the Client's materialization steps.
	/// </param>
	/// <returns><see langword="true" /> when the local catalog was read.</returns>
	/// <remarks>
	///     This is an offline diagnostic of the local operating-system process catalog. It neither dispatches to Cheat
	///     Engine nor observes, selects or proves a Cheat Engine target, needs no current activation, and its values
	///     remain ordinary managed snapshots after the plugin is disabled. The catalog never describes a CEServer target
	///     or a file opened as a process, and a local identifier equal to a Cheat Engine target identifier is not evidence
	///     that both name the same process. Request validation occurs first; cancellation is then observed before catalog
	///     access and between Client-managed materialization steps and reported as
	///     <see cref="CheatEngineFailureKind.Cancelled" /> with <see cref="CheatEngineHostEffect.NotStarted" />. A
	///     catalog that cannot be read is <see cref="CheatEngineFailureKind.OperationRejected" />.
	/// </remarks>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is the <see langword="default" /> request, whose bound is zero
	///     (<see cref="ArgumentOutOfRangeException" />), or a tampered one with an empty name filter.
	/// </exception>
	public bool TryGetLocalProcesses(LocalProcessEnumerationRequest request, out LocalProcessEnumerationResult result,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Enumerates copied local-process metadata within an explicit materialization bound.</summary>
	/// <param name="request">The bound on the number of copied processes and an optional name filter.</param>
	/// <param name="cancellationToken">
	///     Observed before the catalog is read and between the Client's materialization steps.
	/// </param>
	/// <returns>The copied processes, ordered by identifier.</returns>
	/// <remarks>Needs no activation, like <see cref="TryGetLocalProcesses" />.</remarks>
	/// <exception cref="ArgumentException">
	///     <paramref name="request" /> is the <see langword="default" /> request, whose bound is zero
	///     (<see cref="ArgumentOutOfRangeException" />), or a tampered one with an empty name filter.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The enumeration observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">
	///     The enumeration failed with any other failure kind: the local catalog could not be read.
	/// </exception>
	public LocalProcessEnumerationResult GetLocalProcesses(LocalProcessEnumerationRequest request,
		CancellationToken cancellationToken = default);
}
