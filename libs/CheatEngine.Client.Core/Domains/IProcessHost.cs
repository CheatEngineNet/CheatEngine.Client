namespace CheatEngine.Client.Core.Domains;

/// <summary>Internal adapter boundary for CE process globals and local managed process metadata.</summary>
/// <remarks>
///     The target facts come from <see cref="ITargetArchitectureProbe" /> (read-only observations). Local metadata comes
///     from the base class library: it describes local processes only and never a CEServer or file-as-process target.
/// </remarks>
internal interface IProcessHost : ITargetArchitectureProbe
{
	/// <summary>Asks Cheat Engine to open (attach to) a process; this also resets Cheat Engine's configured pointer size.</summary>
	public void OpenProcess(long processId);

	public bool TryGetLocalProcess(int processId, out LocalProcessInfo process);

	public IReadOnlyList<LocalProcessInfo> GetLocalProcesses();

	public IReadOnlyList<LocalProcessInfo> FindProcessesByExactName(string processName);
}
