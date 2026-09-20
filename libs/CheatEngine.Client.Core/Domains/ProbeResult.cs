using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Core.Domains;

internal readonly record struct ProbeResult<T>(
	RuntimeCapabilityId Capability,
	RuntimeCapabilityAvailabilityState State,
	T? Value)
{
	internal static ProbeResult<T> Available(RuntimeCapabilityId capability, T value)
	{
		return new ProbeResult<T>(capability, RuntimeCapabilityAvailabilityState.Available, value);
	}

	internal static ProbeResult<T> Unavailable(RuntimeCapabilityId capability)
	{
		return new ProbeResult<T>(capability, RuntimeCapabilityAvailabilityState.Unavailable, default);
	}

	internal static ProbeResult<T> Unknown(RuntimeCapabilityId capability)
	{
		return new ProbeResult<T>(capability, RuntimeCapabilityAvailabilityState.Unknown, default);
	}

	internal RuntimeCapabilityAvailability ToAvailability()
	{
		return new RuntimeCapabilityAvailability(Capability, State, RuntimeCapabilityContract.Unknown);
	}
}
