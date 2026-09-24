namespace CheatEngine.Client.Runtime;

/// <summary>Identifies the evidence gate that supplies <see cref="ClientCapabilityEvidence.EffectiveReason" />.</summary>
public enum ClientCapabilityEvidenceReasonCode
{
	/// <summary>No gate supplies a reason: the evidence is the uninitialized default value.</summary>
	Unknown = 0,

	/// <summary>The operational Client adapter gate supplies the effective reason.</summary>
	Implementation = 1,

	/// <summary>The consumed package artifact gate supplies the effective reason.</summary>
	Package = 2,

	/// <summary>The Cheat Engine host observation gate supplies the effective reason.</summary>
	Host = 3,

	/// <summary>The live host-qualification gate supplies the effective reason.</summary>
	LiveQualification = 4,

	/// <summary>The Client policy gate supplies the effective reason.</summary>
	Policy = 5,

	/// <summary>The activation-lifetime gate supplies the effective reason.</summary>
	Lifetime = 6
}
