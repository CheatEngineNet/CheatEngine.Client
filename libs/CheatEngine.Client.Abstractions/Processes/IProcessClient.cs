using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Inspection;

namespace CheatEngine.Client.Processes;

/// <summary>Reads and changes the process selected by the active Cheat Engine session.</summary>
/// <remarks>
///     <para>
///         Cheat Engine's selected target is ambient: a session that holds a process identifier does not stop the user,
///         another plugin or a script from selecting another process. The checks of this client (PID-bracketed
///         observation, selection epoch) reduce that risk; they are not transactions.
///     </para>
///     <para>
///         Every observation reads the opened process identifier first: with no target opened, Cheat Engine reports the
///         same ISA family, width and pointer size as an x64 target. The ISA is derived from Cheat Engine's x86 and ARM
///         family facts together with its 64-bit fact, never from the 64-bit fact alone; the process width is stored as
///         observed. This client reads no target-identity evidence, so it detects neither identifier reuse by another
///         process nor a CEServer or file-as-process backend.
///     </para>
/// </remarks>
public interface IProcessClient
{
	/// <summary>Tries to get a copied snapshot of the currently selected target process.</summary>
	/// <remarks>
	///     Returns <see cref="CheatEngineFailureKind.TargetNotAttached" /> only when Cheat Engine has no selected target,
	///     and <see cref="CheatEngineFailureKind.IndeterminateHostResult" /> when the selected target changed while it was
	///     observed or the closing read of the opened process identifier failed (the facts cannot be attributed to one
	///     target). Local operating-system metadata is optional enrichment; its absence leaves the Cheat Engine target
	///     snapshot valid with null name and executable path. An SDK exception raised by a target-fact probe leaves that
	///     fact unknown; any other fault of a Cheat Engine or local-catalog call is returned as a classified failure and
	///     never crosses this method. Invalid arguments and Client lifecycle exceptions
	///     (<see cref="CheatEngineClientException" />) are thrown.
	/// </remarks>
	public bool TryGetCurrent(out ProcessSnapshot snapshot, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Gets the selected process or throws when no target is attached.</summary>
	public ProcessSnapshot GetCurrent(CancellationToken cancellationToken = default);

	/// <summary>
	///     Re-reads Cheat Engine's selected target and advances the selection epoch when its PID changed, or when its
	///     observed ISA or process width changed from one known value to another.
	/// </summary>
	/// <remarks>
	///     Returns <see cref="CheatEngineFailureKind.TargetNotAttached" /> and invalidates an observed selection only
	///     when Cheat Engine reports no selected target. A fact that is transiently unknown for the same PID neither
	///     advances the selection epoch nor replaces the last value known for that selection, so a probe failure does not
	///     invalidate target-bound leases and does not weaken the selection identity to the PID alone. Local metadata is
	///     optional enrichment and does not establish liveness or target identity. This is an observation, not an atomic
	///     process-lifetime guarantee; exceptions follow <see cref="TryGetCurrent" />.
	/// </remarks>
	public bool TryRefresh(out ProcessSnapshot snapshot, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Refreshes the selected target or throws when no target is attached.</summary>
	public ProcessSnapshot Refresh(CancellationToken cancellationToken = default);

	/// <summary>Tries to attach Cheat Engine to an explicit process identifier and verifies the selected target.</summary>
	/// <remarks>
	///     Attaching resets Cheat Engine's configured pointer size to the target default, so an attach silently undoes an
	///     earlier pointer-size override. A fault of Cheat Engine's attach call is returned with
	///     <see cref="CheatEngineHostEffect.Unknown" />; exceptions otherwise follow <see cref="TryGetCurrent" />.
	/// </remarks>
	public bool TryAttach(TargetProcessId processId, out ProcessSnapshot snapshot,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Attaches to an explicit process identifier or throws when the host rejects it.</summary>
	public ProcessSnapshot Attach(TargetProcessId processId,
		CancellationToken cancellationToken = default);

	/// <summary>Tries to attach to the single locally discovered process whose executable name matches exactly.</summary>
	/// <remarks>
	///     Activation admission occurs before local discovery; caller cancellation is then observed before catalog access.
	///     A local match is only an attach candidate. Cheat Engine's selected target is verified before returning.
	/// </remarks>
	public bool TryAttachExactName(string processName, out ProcessSnapshot snapshot, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Attaches to the single exact-name match or throws when none or several exist.</summary>
	public ProcessSnapshot AttachExactName(string processName, CancellationToken cancellationToken = default);
}
