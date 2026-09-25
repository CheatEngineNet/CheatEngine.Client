namespace CheatEngine.Client.Runtime;

/// <summary>One immutable capability prerequisite together with its explicit observation reason.</summary>
public readonly record struct ClientCapabilityEvidenceGate
{
	private readonly string? _reason;

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
		_reason = reason;
	}

	/// <summary>Gets the independently observed prerequisite state.</summary>
	public ClientCapabilityEvidenceState State
	{
		get;
	}

	/// <summary>Gets the bounded reason for this prerequisite observation.</summary>
	/// <remarks><see cref="string.Empty" /> for the <see langword="default" /> value.</remarks>
	public string Reason => _reason ?? string.Empty;

	/// <summary>Gets whether the prerequisite has been established.</summary>
	public bool IsSatisfied => State == ClientCapabilityEvidenceState.Satisfied;

	/// <summary>Gets whether this value is the uninitialized <see langword="default" />, which has no reason.</summary>
	internal bool IsDefault => _reason is null;
}
