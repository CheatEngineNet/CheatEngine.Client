namespace CheatEngine.Client.Processes;

/// <summary>Describes whether a selected process is paused.</summary>
public enum ProcessPauseState
{
	/// <summary>The host could not establish the state.</summary>
	Unknown = 0,

	/// <summary>The target is executing.</summary>
	Running = 1,

	/// <summary>The target is paused.</summary>
	Paused = 2
}
