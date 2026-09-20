using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Internal adapter boundary for CE process globals and local managed process metadata.</summary>
internal interface IProcessHost
{
	public long GetOpenedProcessId();
	public void OpenProcess(long processId);
	public bool TryGetLocalProcess(int processId, out LocalProcessInfo process);
	public IReadOnlyList<LocalProcessInfo> FindProcessesByExactName(string processName);
	public CheatEngineArchitecture GetTargetArchitecture();
}
