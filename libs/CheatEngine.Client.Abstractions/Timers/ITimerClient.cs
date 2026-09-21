using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Events;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Timers;

/// <summary>Registers Client-owned recurring timers for the current activation.</summary>
public interface ITimerClient
{
	/// <summary>Tries to register a timer and its bounded copied tick stream.</summary>
	public bool TryRegister(
		TimerRequest request,
		TimerHandler handler,
		EventStreamOptions streamOptions,
		[NotNullWhen(true)] out ITimerLease? lease,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Registers a timer or throws when the capability is unavailable or registration fails.</summary>
	public ITimerLease Register(
		TimerRequest request,
		TimerHandler handler,
		EventStreamOptions streamOptions,
		CancellationToken cancellationToken = default);
}
