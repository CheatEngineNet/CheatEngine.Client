namespace CheatEngine.Client.Core.Domains;

/// <summary>Internal port for CE runtime globals, so the domain is testable without mocking generated SDK statics.</summary>
/// <remarks>
///     Every member is a read-only observation (audit ADR-09a, Q45): a runtime probe never loads a driver, executes code
///     remotely, selects a target or changes a host setting.
/// </remarks>
internal interface IRuntimeProbe : ITargetArchitectureProbe
{
	/// <summary>Reads the coarse Cheat Engine version number.</summary>
	public double GetCheatEngineVersion();

	/// <summary>Reads Cheat Engine's host system-architecture code.</summary>
	public int GetSystemArchitecture();

	/// <summary>Reads Cheat Engine's target ABI code.</summary>
	public int GetTargetAbi();
}
