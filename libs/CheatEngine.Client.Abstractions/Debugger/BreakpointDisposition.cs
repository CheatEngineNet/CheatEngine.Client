namespace CheatEngine.Client.Debugger;

/// <summary>Specifies the immediate continuation decision returned by a synchronous breakpoint handler.</summary>
public enum BreakpointDisposition
{
	/// <summary>Continues target execution after the synchronous handler returns.</summary>
	Continue,

	/// <summary>Leaves execution broken for the host's debugger workflow.</summary>
	Break
}
