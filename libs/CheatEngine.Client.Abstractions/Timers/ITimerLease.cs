using CheatEngine.Client.Events;

namespace CheatEngine.Client.Timers;

/// <summary>Owns one Client timer registration and its bounded copied tick stream.</summary>
public interface ITimerLease : IEventStreamLease<TimerTick>
{
	/// <summary>Gets the request used to create this timer.</summary>
	public TimerRequest Request
	{
		get;
	}
}
