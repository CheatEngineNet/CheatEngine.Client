namespace CheatEngine.Client.Core.Domains;

/// <summary>Internal port for CE runtime globals, so the domain is testable without mocking generated SDK statics.</summary>
internal interface IRuntimeProbe
{
	public double GetCheatEngineVersion();
	public int GetSystemArchitecture();
	public int GetTargetAbi();
	public long GetOpenedProcessId();
	public bool TargetIs64Bit();
}
