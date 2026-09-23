using CheatEngine.Client.Results;

namespace CheatEngine.Client.Processes;

/// <summary>Reads bounded, copied metadata from the local operating-system process catalog.</summary>
/// <remarks>
///     This is an offline diagnostic contract. It neither dispatches to Cheat Engine nor observes, selects, or proves
///     a Cheat Engine target. Its values remain ordinary managed snapshots after a plugin activation ends. The catalog is
///     the local operating system's: it never describes a CEServer target or a file opened as a process, and a local
///     identifier equal to a Cheat Engine target identifier is not evidence that both name the same process. For each
///     operation, request validation occurs first, then cancellation is observed before catalog access and between
///     Client-managed materialization steps.
/// </remarks>
public interface ILocalProcessDiagnostics
{
	/// <summary>Tries to enumerate copied local-process metadata within an explicit materialization bound.</summary>
	/// <remarks>Cancellation is observed before catalog access and between Client-managed materialization steps.</remarks>
	public bool TryGetProcesses(ProcessEnumerationRequest request, out ProcessEnumerationResult result,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Enumerates copied local-process metadata within an explicit materialization bound.</summary>
	public ProcessEnumerationResult GetProcesses(ProcessEnumerationRequest request,
		CancellationToken cancellationToken = default);
}
