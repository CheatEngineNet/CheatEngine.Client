namespace CheatEngine.Client.Runtime;

/// <summary>Identifies the evidence gate that supplies <see cref="ClientCapabilityEvidence.EffectiveReason"/>.</summary>
public enum ClientCapabilityEvidenceReasonCode : byte
{
	/// <summary>The operational Client adapter gate supplies the effective reason.</summary>
	Implementation = 0,

	/// <summary>The consumed package artifact gate supplies the effective reason.</summary>
	Package = 1,

	/// <summary>The Cheat Engine host observation gate supplies the effective reason.</summary>
	Host = 2,

	/// <summary>The live host-qualification gate supplies the effective reason.</summary>
	LiveQualification = 3,

	/// <summary>The Client policy gate supplies the effective reason.</summary>
	Policy = 4,

	/// <summary>The activation-lifetime gate supplies the effective reason.</summary>
	Lifetime = 5
}
