namespace CheatEngine.Client.Debugger;

/// <summary>Handles one copied breakpoint event without awaiting on a Cheat Engine callback thread.</summary>
public delegate BreakpointDisposition BreakpointHandler(BreakpointEvent breakpointEvent);
