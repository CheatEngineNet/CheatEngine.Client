using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Inspection;

namespace CheatEngine.Client.Processes;

/// <summary>Reads the process selected by the active Cheat Engine session.</summary>
public interface IProcessClient
{
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
}
