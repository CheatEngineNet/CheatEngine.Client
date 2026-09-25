namespace CheatEngine.Client.Core.Qualification;

/// <summary>
///     The host qualification evidence this Client build embeds: data only, read by <see cref="HostQualificationGate" />.
/// </summary>
/// <remarks>
///     <para>
///         It is empty until a live qualification run is recorded: the commit that records a run's redacted evidence
///         under <c>tests/CheatEngine.Client.Tests/LiveQualification/Evidence</c> fills it with that run's id, the Client
///         version, the exact CheatEngine.SDK identity, the host profile and, per capability, the passed and waived
///         scenarios and the qualified target architectures. Nothing else writes it.
///     </para>
///     <para>
///         This file is excluded from the qualified source digest (<c>QualifiedSourceDigest</c>), so recording the evidence
///         does not change the digest of the sources the run qualified.
///     </para>
/// </remarks>
internal static class HostQualificationEvidence
{
	/// <summary>Gets the recorded evidence, or <see langword="null" /> while no run is recorded.</summary>
	internal static HostQualificationRecord? Recorded => null;
}
