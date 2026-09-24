namespace CheatEngine.Client.Runtime;

/// <summary>An immutable observation of a Client-owned high-level capability and its evidence.</summary>
public readonly record struct ClientCapabilityAvailability
{
	/// <summary>Creates an availability projection from independently sourced prerequisite evidence.</summary>
	/// <param name="capability">The stable Client-owned capability identifier.</param>
	/// <param name="evidence">The implementation, package, host, qualification, policy and lifetime gates.</param>
	/// <exception cref="ArgumentException">
	///     <paramref name="capability" /> is empty, or <paramref name="evidence" /> is the uninitialized default value.
	/// </exception>
	public ClientCapabilityAvailability(ClientCapabilityId capability, ClientCapabilityEvidence evidence)
	{
		if (capability.IsEmpty)
		{
			throw new ArgumentException("A Client capability identifier is required.", nameof(capability));
		}

		if (evidence.EffectiveReasonCode == ClientCapabilityEvidenceReasonCode.Unknown)
		{
			throw new ArgumentException("Initialized Client capability evidence is required.", nameof(evidence));
		}

		Capability = capability;
		Evidence = evidence;
		State = evidence.AvailabilityState;
		Reason = evidence.EffectiveReason;
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

	/// <summary>Gets the separately observed implementation, package, host, qualification, policy, and lifetime gates.</summary>
	public ClientCapabilityEvidence Evidence
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
