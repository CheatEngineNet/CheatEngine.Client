namespace CheatEngine.Client.Runtime;

/// <summary>One immutable capability prerequisite together with its explicit observation reason.</summary>
public readonly record struct ClientCapabilityEvidenceGate
{
	/// <summary>Creates a validated capability prerequisite observation.</summary>
	public ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState state, string reason)
	{
		if (!Enum.IsDefined(state))
		{
			throw new ArgumentOutOfRangeException(nameof(state), state,
				"The Client capability evidence state is not defined.");
		}

		ArgumentException.ThrowIfNullOrWhiteSpace(reason);
		State = state;
		Reason = reason;
	}

	/// <summary>Gets the independently observed prerequisite state.</summary>
	public ClientCapabilityEvidenceState State
	{
		get;
	}

	/// <summary>Gets the bounded reason for this prerequisite observation.</summary>
	public string Reason
	{
		get;
	}

	/// <summary>Gets whether the prerequisite has been established.</summary>
	public bool IsSatisfied => State == ClientCapabilityEvidenceState.Satisfied;
}
