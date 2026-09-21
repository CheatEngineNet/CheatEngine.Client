using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Events;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Debugger;

/// <summary>Registers Client-owned breakpoints with immediate handler decisions and bounded copied event streams.</summary>
public interface IDebuggerClient
{
	/// <summary>Tries to register one breakpoint for the current activation and selection.</summary>
	public bool TryRegisterBreakpoint(
		BreakpointRequest request,
		BreakpointHandler handler,
		EventStreamOptions streamOptions,
		[NotNullWhen(true)] out IBreakpointLease? lease,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Registers one breakpoint or throws when the capability is unavailable or registration fails.</summary>
	public IBreakpointLease RegisterBreakpoint(
		BreakpointRequest request,
		BreakpointHandler handler,
		EventStreamOptions streamOptions,
		CancellationToken cancellationToken = default);
}
