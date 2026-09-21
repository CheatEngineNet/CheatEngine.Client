using CheatEngine.SDK.Engine.Inspection;

namespace CheatEngine.Client.Processes;

/// <summary>A copied pause-state observation tied to one selected-target epoch.</summary>
public readonly record struct ProcessPauseSnapshot
{
	/// <summary>Creates a target pause-state observation.</summary>
	public ProcessPauseSnapshot(TargetProcessId processId, ProcessPauseState state, long selectionEpoch)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(selectionEpoch);
		ProcessId = processId;
		State = state;
		SelectionEpoch = selectionEpoch;
	}

	/// <summary>Gets the selected process identifier observed with this state.</summary>
	public TargetProcessId ProcessId
	{
		get;
	}

	/// <summary>Gets the observed pause state.</summary>
	public ProcessPauseState State
	{
		get;
	}

	/// <summary>Gets the target-selection epoch associated with the observation.</summary>
	public long SelectionEpoch
	{
		get;
	}
}
