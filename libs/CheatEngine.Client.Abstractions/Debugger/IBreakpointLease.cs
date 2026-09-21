using CheatEngine.Client.Events;

namespace CheatEngine.Client.Debugger;

/// <summary>Owns one Client breakpoint and its bounded copied event stream.</summary>
public interface IBreakpointLease : IEventStreamLease<BreakpointEvent>
{
	/// <summary>Gets the request used to create this breakpoint.</summary>
	public BreakpointRequest Request
	{
		get;
	}

	/// <summary>Gets the target-selection epoch captured when the breakpoint was registered.</summary>
	public long SelectionEpoch
	{
		get;
	}
}
