using System.Runtime.InteropServices;

namespace CheatEngine.Client.Runtime;

/// <summary>An immutable observation of a Client-owned high-level capability and its evidence.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct ClientCapabilityAvailability
{
	/// <summary>Creates an observation after validating its stable public shape.</summary>
	/// <param name="capability">The stable Client-owned capability identifier.</param>
	/// <param name="state">The observed availability state.</param>
	/// <param name="reason">The explicit probe, policy, or gate reason behind the state.</param>
	public ClientCapabilityAvailability(
		ClientCapabilityId capability,
		ClientCapabilityAvailabilityState state,
		string reason)
	{
		if (capability.IsEmpty)
		{
			throw new ArgumentException("A Client capability identifier is required.", nameof(capability));
		}

		if (!Enum.IsDefined(state))
		{
			throw new ArgumentOutOfRangeException(nameof(state), state, "The Client capability state is not defined.");
		}

		ArgumentException.ThrowIfNullOrWhiteSpace(reason);

		Capability = capability;
		State = state;
		Reason = reason;
	}

	/// <summary>Gets the stable Client-owned capability identifier.</summary>
	public ClientCapabilityId Capability
	{
		get;
	}

	/// <summary>Gets the observed availability state.</summary>
	public ClientCapabilityAvailabilityState State
	{
		get;
	}

	/// <summary>Gets the explicit probe, policy, or gate reason behind the state.</summary>
	public string Reason
	{
		get;
	}

	/// <summary>Gets whether this capability was explicitly established as available.</summary>
	public bool IsAvailable => State == ClientCapabilityAvailabilityState.Available;

	/// <summary>Gets whether this capability was explicitly established as available or unavailable.</summary>
	public bool IsKnown => State != ClientCapabilityAvailabilityState.Unknown;
}
