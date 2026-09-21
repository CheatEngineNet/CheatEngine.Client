using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Domains.Events;
using CheatEngine.Client.Events;
using CheatEngine.Client.Results;
using CheatEngine.Client.Timers;

namespace CheatEngine.Client.Core.Domains.Timers;

/// <summary>Preserves recurring timer semantics until timer callback cleanup passes the live-host gate.</summary>
internal sealed class UnavailableTimerClient : ITimerClient
{
	public bool TryRegister(TimerRequest request, TimerHandler handler, EventStreamOptions streamOptions,
		[NotNullWhen(true)] out ITimerLease? lease, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(handler);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(streamOptions.Capacity);
		lease = null;
		failure = UnavailableCapabilityFailure.Create("Timers", "Timers.Register", cancellationToken);
		return false;
	}

	public ITimerLease Register(TimerRequest request, TimerHandler handler, EventStreamOptions streamOptions,
		CancellationToken cancellationToken = default)
	{
		_ = TryRegister(request, handler, streamOptions, out _, out CheatEngineFailure failure, cancellationToken);
		return UnavailableCapabilityFailure.Throw<ITimerLease>(failure);
	}
}
