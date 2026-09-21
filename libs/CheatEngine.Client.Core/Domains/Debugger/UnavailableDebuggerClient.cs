using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Domains.Events;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Debugger;
using CheatEngine.Client.Events;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Domains.Debugger;

/// <summary>Preserves copied breakpoint semantics until debugger callback ownership and reactivation pass a live gate.</summary>
internal sealed class UnavailableDebuggerClient : IDebuggerClient
{
	private readonly CoreLifetime? _lifetime;

	internal UnavailableDebuggerClient(CoreLifetime? lifetime = null)
	{
		_lifetime = lifetime;
	}

	public bool TryRegisterBreakpoint(BreakpointRequest request, BreakpointHandler handler,
		EventStreamOptions streamOptions,
		[NotNullWhen(true)] out IBreakpointLease? lease, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(handler);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(streamOptions.Capacity);
		lease = null;
		failure = UnavailableCapabilityFailure.Create(_lifetime, "Debugger breakpoints", "Debugger.RegisterBreakpoint",
			cancellationToken);
		return false;
	}

	public IBreakpointLease RegisterBreakpoint(BreakpointRequest request, BreakpointHandler handler,
		EventStreamOptions streamOptions, CancellationToken cancellationToken = default)
	{
		_ = TryRegisterBreakpoint(request, handler, streamOptions, out _, out CheatEngineFailure failure,
			cancellationToken);
		return UnavailableCapabilityFailure.Throw<IBreakpointLease>(failure);
	}
}
