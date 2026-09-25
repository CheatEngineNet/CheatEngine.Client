namespace CheatEngine.Client.Runtime;

/// <summary>
///     Immutable, independently sourced prerequisites for one Client capability. A satisfied policy never establishes
///     package, host, qualification, or activation-lifetime evidence.
/// </summary>
public readonly record struct ClientCapabilityEvidence
{
	/// <summary>Creates validated evidence for one Client capability.</summary>
	public ClientCapabilityEvidence(
		ClientCapabilityEvidenceGate implementation,
		ClientCapabilityEvidenceGate package,
		ClientCapabilityEvidenceGate host,
		ClientCapabilityEvidenceGate liveQualification,
		ClientCapabilityEvidenceGate policy,
		ClientCapabilityEvidenceGate lifetime)
	{
		Validate(implementation, nameof(implementation));
		Validate(package, nameof(package));
		Validate(host, nameof(host));
		Validate(liveQualification, nameof(liveQualification));
		Validate(policy, nameof(policy));
		Validate(lifetime, nameof(lifetime));

		Implementation = implementation;
		Package = package;
		Host = host;
		LiveQualification = liveQualification;
		Policy = policy;
		Lifetime = lifetime;
	}

	/// <summary>Gets whether an operational Client adapter is delivered for this capability.</summary>
	public ClientCapabilityEvidenceGate Implementation
	{
		get;
	}

	/// <summary>Gets whether the required primitive is established in the consumed package artifact.</summary>
	public ClientCapabilityEvidenceGate Package
	{
		get;
	}

	/// <summary>Gets the independent observation of the required Cheat Engine host primitive.</summary>
	public ClientCapabilityEvidenceGate Host
	{
		get;
	}

	/// <summary>Gets whether the required host profile has passed its explicit live qualification gate.</summary>
	public ClientCapabilityEvidenceGate LiveQualification
	{
		get;
	}

	/// <summary>Gets whether Client policy permits this capability for the current activation.</summary>
	public ClientCapabilityEvidenceGate Policy
	{
		get;
	}

	/// <summary>Gets whether the activation lifetime required by this capability is current.</summary>
	public ClientCapabilityEvidenceGate Lifetime
	{
		get;
	}

	/// <summary>
	///     Gets the availability the gates establish: <see cref="ClientCapabilityAvailabilityState.Unavailable" /> when a
	///     gate is <see cref="ClientCapabilityEvidenceState.Missing" />,
	///     <see cref="ClientCapabilityAvailabilityState.Available" /> when every gate is
	///     <see cref="ClientCapabilityEvidenceState.Satisfied" />, otherwise
	///     <see cref="ClientCapabilityAvailabilityState.Unknown" />.
	/// </summary>
	public ClientCapabilityAvailabilityState AvailabilityState
	{
		get
		{
			if (HasState(ClientCapabilityEvidenceState.Missing))
			{
				return ClientCapabilityAvailabilityState.Unavailable;
			}

			return AllSatisfied
				? ClientCapabilityAvailabilityState.Available
				: ClientCapabilityAvailabilityState.Unknown;
		}
	}

	/// <summary>
	///     Gets the reason for the highest-priority missing, faulted, malformed, or unknown prerequisite; empty for the
	///     uninitialized default value.
	/// </summary>
	public string EffectiveReason => EffectiveReasonCode == ClientCapabilityEvidenceReasonCode.Unknown
		? string.Empty
		: GetGate(EffectiveReasonCode).Reason;

	/// <summary>
	///     Gets the stable code for the evidence gate that supplies <see cref="EffectiveReason" />, or
	///     <see cref="ClientCapabilityEvidenceReasonCode.Unknown" /> for the uninitialized default value. Its state remains
	///     available through the corresponding evidence-gate property.
	/// </summary>
	public ClientCapabilityEvidenceReasonCode EffectiveReasonCode => GetEffectiveReasonCode();

	private bool AllSatisfied =>
		Implementation.IsSatisfied && Package.IsSatisfied && Host.IsSatisfied && LiveQualification.IsSatisfied &&
		Policy.IsSatisfied && Lifetime.IsSatisfied;

	private ClientCapabilityEvidenceReasonCode GetEffectiveReasonCode()
	{
		// The constructor requires a reason for every gate, so a gate without one is the uninitialized default value.
		if (Implementation.IsDefault)
		{
			return ClientCapabilityEvidenceReasonCode.Unknown;
		}

		if (Lifetime.State == ClientCapabilityEvidenceState.Missing)
		{
			return ClientCapabilityEvidenceReasonCode.Lifetime;
		}

		if (Policy.State == ClientCapabilityEvidenceState.Missing)
		{
			return ClientCapabilityEvidenceReasonCode.Policy;
		}

		if (Implementation.State == ClientCapabilityEvidenceState.Missing)
		{
			return ClientCapabilityEvidenceReasonCode.Implementation;
		}

		if (Package.State == ClientCapabilityEvidenceState.Missing)
		{
			return ClientCapabilityEvidenceReasonCode.Package;
		}

		if (Host.State == ClientCapabilityEvidenceState.Missing)
		{
			return ClientCapabilityEvidenceReasonCode.Host;
		}

		if (LiveQualification.State == ClientCapabilityEvidenceState.Missing)
		{
			return ClientCapabilityEvidenceReasonCode.LiveQualification;
		}

		if (Host.State is ClientCapabilityEvidenceState.Faulted or ClientCapabilityEvidenceState.Malformed)
		{
			return ClientCapabilityEvidenceReasonCode.Host;
		}

		if (Package.State is ClientCapabilityEvidenceState.Faulted or ClientCapabilityEvidenceState.Malformed)
		{
			return ClientCapabilityEvidenceReasonCode.Package;
		}

		if (LiveQualification.State is ClientCapabilityEvidenceState.Faulted or ClientCapabilityEvidenceState.Malformed)
		{
			return ClientCapabilityEvidenceReasonCode.LiveQualification;
		}

		if (Implementation.State is ClientCapabilityEvidenceState.Faulted or ClientCapabilityEvidenceState.Malformed)
		{
			return ClientCapabilityEvidenceReasonCode.Implementation;
		}

		if (Policy.State is ClientCapabilityEvidenceState.Faulted or ClientCapabilityEvidenceState.Malformed)
		{
			return ClientCapabilityEvidenceReasonCode.Policy;
		}

		if (Lifetime.State is ClientCapabilityEvidenceState.Faulted or ClientCapabilityEvidenceState.Malformed)
		{
			return ClientCapabilityEvidenceReasonCode.Lifetime;
		}

		if (Host.State == ClientCapabilityEvidenceState.Unknown)
		{
			return ClientCapabilityEvidenceReasonCode.Host;
		}

		if (Package.State == ClientCapabilityEvidenceState.Unknown)
		{
			return ClientCapabilityEvidenceReasonCode.Package;
		}

		if (LiveQualification.State == ClientCapabilityEvidenceState.Unknown)
		{
			return ClientCapabilityEvidenceReasonCode.LiveQualification;
		}

		if (Implementation.State == ClientCapabilityEvidenceState.Unknown)
		{
			return ClientCapabilityEvidenceReasonCode.Implementation;
		}

		if (Policy.State == ClientCapabilityEvidenceState.Unknown)
		{
			return ClientCapabilityEvidenceReasonCode.Policy;
		}

		return ClientCapabilityEvidenceReasonCode.Lifetime;
	}

	private ClientCapabilityEvidenceGate GetGate(ClientCapabilityEvidenceReasonCode reasonCode)
	{
		return reasonCode switch
		{
			ClientCapabilityEvidenceReasonCode.Implementation => Implementation,
			ClientCapabilityEvidenceReasonCode.Package => Package,
			ClientCapabilityEvidenceReasonCode.Host => Host,
			ClientCapabilityEvidenceReasonCode.LiveQualification => LiveQualification,
			ClientCapabilityEvidenceReasonCode.Policy => Policy,
			ClientCapabilityEvidenceReasonCode.Lifetime => Lifetime,
			_ => throw new ArgumentOutOfRangeException(nameof(reasonCode), reasonCode,
				"The Client capability evidence reason code is not defined.")
		};
	}

	private bool HasState(ClientCapabilityEvidenceState state)
	{
		return Implementation.State == state || Package.State == state || Host.State == state ||
			   LiveQualification.State == state || Policy.State == state || Lifetime.State == state;
	}

	private static void Validate(ClientCapabilityEvidenceGate gate, string parameterName)
	{
		if (!Enum.IsDefined(gate.State))
		{
			throw new ArgumentOutOfRangeException(parameterName, gate.State,
				"The Client capability evidence state is not defined.");
		}

		ArgumentException.ThrowIfNullOrWhiteSpace(gate.Reason);
	}
}
