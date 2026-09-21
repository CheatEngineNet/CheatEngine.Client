using CheatEngine.Client.Runtime;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Core.Domains;

internal readonly record struct ProbeResult<T>(ClientCapabilityEvidenceGate Evidence, T? Value)
{
	internal bool HasValue => Evidence.State == ClientCapabilityEvidenceState.Satisfied;

	internal static ProbeResult<T> Available(T value)
	{
		return new ProbeResult<T>(new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Satisfied,
			"The runtime probe returned a value in its documented shape."), value);
	}

	internal static ProbeResult<T> MissingGlobal()
	{
		return new ProbeResult<T>(new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Missing,
			"The required Cheat Engine runtime global is not available in this host."), default);
	}

	internal static ProbeResult<T> MissingCapability()
	{
		return new ProbeResult<T>(new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Missing,
			"The required Cheat Engine runtime capability is not supported by this host."), default);
	}

	internal static ProbeResult<T> Unknown(string reason)
	{
		return new ProbeResult<T>(new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Unknown, reason),
			default);
	}

	internal static ProbeResult<T> Faulted(string reason)
	{
		return new ProbeResult<T>(new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Faulted, reason),
			default);
	}

	internal static ProbeResult<T> Malformed(string reason)
	{
		return new ProbeResult<T>(new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Malformed, reason),
			default);
	}

	internal RuntimeCapabilityAvailability ToAvailability(RuntimeCapabilityId capability)
	{
		RuntimeCapabilityAvailabilityState state = Evidence.State switch
		{
			ClientCapabilityEvidenceState.Satisfied => RuntimeCapabilityAvailabilityState.Available,
			ClientCapabilityEvidenceState.Missing => RuntimeCapabilityAvailabilityState.Unavailable,
			_ => RuntimeCapabilityAvailabilityState.Unknown
		};
		return new RuntimeCapabilityAvailability(capability, state, RuntimeCapabilityContract.Unknown);
	}
}
