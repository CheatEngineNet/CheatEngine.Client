namespace CheatEngine.Client.Debugger;

/// <summary>Specifies the target-memory access that triggers a breakpoint.</summary>
public enum BreakpointKind
{
	/// <summary>Breaks when execution reaches the target address.</summary>
	Execute,

	/// <summary>Breaks when the target address is written.</summary>
	Write,

	/// <summary>Breaks when the target address is read or written.</summary>
	Access
}
