namespace CheatEngine.Client.Core.Domains;

/// <summary>Internal adapter boundary for local managed process metadata.</summary>
/// <remarks>
///     The target facts come from <see cref="IRuntimeObservationPort" /> and the attach call from
///     <see cref="IProcessSelectionPort" /> (CheatEngine.SDK operations). Local metadata comes from the base class
///     library: it describes local processes only and never a CEServer or file-as-process target.
/// </remarks>
internal interface IProcessHost
{
	public bool TryGetLocalProcess(int processId, out LocalProcessInfo process);

	public IReadOnlyList<LocalProcessInfo> GetLocalProcesses();

	public IReadOnlyList<LocalProcessInfo> FindProcessesByExactName(string processName);
}
