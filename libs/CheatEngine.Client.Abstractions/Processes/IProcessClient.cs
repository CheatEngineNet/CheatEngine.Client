using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Inspection;

namespace CheatEngine.Client.Processes;

/// <summary>Reads the process selected by the active Cheat Engine session.</summary>
public interface IProcessClient
{
	/// <summary>Tries to enumerate copied local-process metadata within an explicit materialization bound.</summary>
	public bool TryGetProcesses(ProcessEnumerationRequest request, out ProcessEnumerationResult result,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Enumerates copied local-process metadata within an explicit materialization bound.</summary>
	public ProcessEnumerationResult GetProcesses(ProcessEnumerationRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Tries to get a copied snapshot of the currently selected target process.</summary>
	public bool TryGetCurrent(out ProcessSnapshot snapshot, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Gets the selected process or throws when no target is attached.</summary>
	public ProcessSnapshot GetCurrent(CancellationToken cancellationToken = default);

	/// <summary>
	///     Re-reads Cheat Engine's selected target and advances the selection epoch when its PID or observed
	///     architecture changed.
	/// </summary>
	public bool TryRefresh(out ProcessSnapshot snapshot, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Refreshes the selected target or throws when no target is attached.</summary>
	public ProcessSnapshot Refresh(CancellationToken cancellationToken = default);

	/// <summary>Tries to attach Cheat Engine to an explicit process identifier and verifies the selected target.</summary>
	public bool TryAttach(TargetProcessId processId, out ProcessSnapshot snapshot,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Attaches to an explicit process identifier or throws when the host rejects it.</summary>
	public ProcessSnapshot Attach(TargetProcessId processId,
		CancellationToken cancellationToken = default);

	/// <summary>Tries to attach to the single local process whose executable name matches exactly.</summary>
	public bool TryAttachExactName(string processName, out ProcessSnapshot snapshot, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Attaches to the single exact-name match or throws when none or several exist.</summary>
	public ProcessSnapshot AttachExactName(string processName, CancellationToken cancellationToken = default);

	/// <summary>Tries to attach Cheat Engine to the foreground process when the host exposes a validated binding.</summary>
	public bool TryAttachForeground(out ProcessSnapshot snapshot, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Attaches Cheat Engine to the foreground process when the host exposes a validated binding.</summary>
	public ProcessSnapshot AttachForeground(CancellationToken cancellationToken = default);

	/// <summary>
	///     Tries to create and select an explicit process when the host exposes a validated create-and-attach binding.
	/// </summary>
	public bool TryCreate(ProcessStartRequest request, out ProcessSnapshot snapshot, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Creates and selects an explicit process when the host exposes a validated create-and-attach binding.</summary>
	public ProcessSnapshot Create(ProcessStartRequest request, CancellationToken cancellationToken = default);

	/// <summary>Tries to pause the selected target when the host exposes a validated pause binding.</summary>
	public bool TryPause(out ProcessSnapshot snapshot, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Pauses the selected target when the host exposes a validated pause binding.</summary>
	public ProcessSnapshot Pause(CancellationToken cancellationToken = default);

	/// <summary>Tries to resume the selected target when the host exposes a validated resume binding.</summary>
	public bool TryResumeExecution(out ProcessSnapshot snapshot, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Resumes the selected target when the host exposes a validated resume binding.</summary>
	public ProcessSnapshot ResumeExecution(CancellationToken cancellationToken = default);

	/// <summary>Tries to observe the selected target's pause state when the host exposes a validated binding.</summary>
	public bool TryGetPauseState(out ProcessPauseSnapshot snapshot, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Observes the selected target's pause state when the host exposes a validated binding.</summary>
	public ProcessPauseSnapshot GetPauseState(CancellationToken cancellationToken = default);
}
