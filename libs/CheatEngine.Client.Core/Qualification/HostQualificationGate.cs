using System.Collections.Immutable;

using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Runtime;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Core.Qualification;

/// <summary>The host qualification one recorded live run established for one Client capability.</summary>
/// <param name="Capability">The capability.</param>
/// <param name="PassedScenarios">The scenarios of the capability whose every receipt of the run passed.</param>
/// <param name="WaivedScenarios">The scenarios of the capability that carry a dated waiver instead of a pass.</param>
/// <param name="Architectures">The target architectures the run qualified the capability on.</param>
internal sealed record HostQualifiedCapability(
	ClientCapabilityId Capability,
	ImmutableArray<string> PassedScenarios,
	ImmutableArray<string> WaivedScenarios,
	ImmutableArray<CheatEngineArchitecture> Architectures);

/// <summary>
///     The evidence of one recorded live qualification run (<c>tests/CheatEngine.Client.Tests/LiveQualification/Evidence</c>),
///     embedded as data in <see cref="HostQualificationEvidence" />.
/// </summary>
/// <param name="RunId">The id of the recorded run.</param>
/// <param name="ClientVersion">The Client version the run qualified, without build metadata.</param>
/// <param name="SdkInformationalVersion">The exact CheatEngine.SDK identity the run used (<c>version+commit</c>).</param>
/// <param name="HostProfile">The host profile id of the run.</param>
/// <param name="CheatEngineVersion">The Cheat Engine file version of the run.</param>
/// <param name="Capabilities">The capabilities the run qualified.</param>
internal sealed record HostQualificationRecord(
	string RunId,
	string ClientVersion,
	string SdkInformationalVersion,
	string HostProfile,
	CheatEngineVersion CheatEngineVersion,
	ImmutableArray<HostQualifiedCapability> Capabilities);

/// <summary>The facts of the running host and target the qualification gate compares with the evidence.</summary>
/// <param name="ExactReviewedIdentity">Whether the loaded CheatEngine.SDK.Engine is exactly the reviewed package.</param>
/// <param name="SdkInformationalVersion">
///     The informational version of the loaded CheatEngine.SDK.Engine (<c>version+commit</c>), or
///     <see langword="null" />.
/// </param>
/// <param name="CheatEngineVersion">The observed Cheat Engine file version, or <see langword="null" />.</param>
/// <param name="CheatEngineBitness">
///     The width of Cheat Engine's own process, <see cref="PointerSize.Unknown" /> when it was not observed.
/// </param>
/// <param name="OperatingSystem">The observed host operating system.</param>
/// <param name="Backend">How Cheat Engine reaches the selected target.</param>
/// <param name="TargetArchitecture">The architecture of the selected target.</param>
/// <param name="ClientVersion">This Client's version without build metadata, or <see langword="null" />.</param>
internal readonly record struct HostQualificationContext(
	bool ExactReviewedIdentity,
	string? SdkInformationalVersion,
	CheatEngineVersion? CheatEngineVersion,
	PointerSize CheatEngineBitness,
	CheatEngineOperatingSystem OperatingSystem,
	TargetBackend Backend,
	CheatEngineArchitecture TargetArchitecture,
	string? ClientVersion);

/// <summary>
///     The qualification gate of a Client capability, derived from the host evidence this build embeds
///     (<see cref="HostQualificationEvidence" />), never from a claim in the source (audit A20-19).
/// </summary>
/// <remarks>
///     <para>
///         The gate is <see cref="ClientCapabilityEvidenceState.Satisfied" /> only when every condition holds: evidence
///         is embedded; the loaded CheatEngine.SDK.Engine is exactly the reviewed package
///         (<see cref="ConsumedSdkIdentity.ExactReviewedIdentity" />) and the evidence names that same identity; the
///         evidence records Cheat Engine 7.7.0.10621 and the host profile this Client supports
///         (<see cref="ConsumedSdkIdentity.SupportedHostProfileId" />); the observed host is Cheat Engine 7.7.0.10621,
///         64-bit, on Windows; the target is a local process whose architecture the run qualified for the capability;
///         this Client's version equals the version the evidence recorded; and every scenario the capability requires
///         (<see cref="ClientCapabilityCatalog" />) passed in the run, none of them waived. Otherwise the gate is
///         <see cref="ClientCapabilityEvidenceState.Unknown" />, with the first condition that does not hold as its
///         reason: a missing qualification is not evidence that the capability fails.
///     </para>
///     <para>
///         <c>Client.UnsafeLuaExecution</c> requires no scenario and is never qualified.
///     </para>
/// </remarks>
internal static class HostQualificationGate
{
	/// <summary>The only Cheat Engine file version a qualification covers.</summary>
	internal static readonly CheatEngineVersion QualifiedCheatEngineVersion = CheatEngineVersion.Ce77010621;

	/// <summary>Evaluates the qualification gate of one catalog row.</summary>
	/// <param name="entry">The capability's catalog row.</param>
	/// <param name="evidence">The embedded evidence, or <see langword="null" /> when this build embeds none.</param>
	/// <param name="context">The observed host and target facts.</param>
	/// <param name="noEvidenceReason">The reason when no evidence is embedded.</param>
	/// <returns>The gate.</returns>
	internal static ClientCapabilityEvidenceGate Evaluate(ClientCapabilityDescriptor entry, HostQualificationRecord? evidence,
		HostQualificationContext context, string noEvidenceReason)
	{
		ArgumentNullException.ThrowIfNull(entry);
		ArgumentException.ThrowIfNullOrWhiteSpace(noEvidenceReason);
		string capability = entry.Id.Value;
		if (entry.RequiredScenarios.IsDefaultOrEmpty)
		{
			return Unknown($"{capability} is never host-qualified: no live scenario covers it.");
		}

		if (evidence is null)
		{
			return Unknown(noEvidenceReason);
		}

		string loadedSdk = context.SdkInformationalVersion ?? "no informational version";
		if (!context.ExactReviewedIdentity)
		{
			return Unknown($"The loaded CheatEngine.SDK.Engine ({loadedSdk}) is not exactly the CheatEngine.SDK package " +
						   "this Client build reviewed.");
		}

		if (!string.Equals(context.SdkInformationalVersion, evidence.SdkInformationalVersion, StringComparison.Ordinal))
		{
			return Unknown($"Run {evidence.RunId} used CheatEngine.SDK {evidence.SdkInformationalVersion}, not the " +
						   $"loaded CheatEngine.SDK.Engine ({loadedSdk}).");
		}

		if (evidence.CheatEngineVersion != QualifiedCheatEngineVersion)
		{
			return Unknown($"Run {evidence.RunId} recorded Cheat Engine {evidence.CheatEngineVersion}; a qualification " +
						   $"covers Cheat Engine {QualifiedCheatEngineVersion} only.");
		}

		if (!string.Equals(evidence.HostProfile, ConsumedSdkIdentity.SupportedHostProfileId, StringComparison.Ordinal))
		{
			return Unknown($"Run {evidence.RunId} recorded the host profile {evidence.HostProfile}; this Client " +
						   $"supports {ConsumedSdkIdentity.SupportedHostProfileId} only.");
		}

		if (context.CheatEngineVersion != QualifiedCheatEngineVersion ||
			context.CheatEngineBitness != PointerSize.Bit64 ||
			context.OperatingSystem != CheatEngineOperatingSystem.Windows)
		{
			string bitness = context.CheatEngineBitness.IsKnown
				? $"{context.CheatEngineBitness.Bytes * 8}-bit"
				: "an unknown width";
			return Unknown($"The host is not Cheat Engine {QualifiedCheatEngineVersion} 64-bit on Windows (observed " +
						   $"{context.CheatEngineVersion?.ToString() ?? "an unknown version"}, {bitness}, " +
						   $"{context.OperatingSystem}).");
		}

		if (context.Backend != TargetBackend.LocalProcess)
		{
			return Unknown($"The selected target is reached through {context.Backend}; run {evidence.RunId} qualified local " +
						   "processes only.");
		}

		if (!string.Equals(context.ClientVersion, evidence.ClientVersion, StringComparison.Ordinal))
		{
			return Unknown($"This Client build ({context.ClientVersion ?? "no version"}) is not the Client " +
						   $"{evidence.ClientVersion} that run {evidence.RunId} qualified.");
		}

		HostQualifiedCapability? qualified = null;
		foreach (HostQualifiedCapability candidate in evidence.Capabilities)
		{
			if (candidate.Capability == entry.Id)
			{
				qualified = candidate;
				break;
			}
		}

		if (qualified is null)
		{
			return Unknown($"Run {evidence.RunId} recorded no qualification of {capability}.");
		}

		if (!qualified.Architectures.Contains(context.TargetArchitecture))
		{
			return Unknown($"Run {evidence.RunId} did not qualify {capability} on a {context.TargetArchitecture} target.");
		}

		foreach (string scenario in entry.RequiredScenarios)
		{
			if (qualified.WaivedScenarios.Contains(scenario))
			{
				return Unknown($"Scenario {scenario} of {capability} is waived in run {evidence.RunId}; a waiver never " +
							   "qualifies a capability.");
			}

			if (!qualified.PassedScenarios.Contains(scenario))
			{
				return Unknown($"Scenario {scenario} of {capability} did not pass in run {evidence.RunId}.");
			}
		}

		return new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Satisfied,
			$"Run {evidence.RunId} qualified {capability} for Client {evidence.ClientVersion} on Cheat Engine " +
			$"{QualifiedCheatEngineVersion} x64 ({evidence.HostProfile}) with CheatEngine.SDK {evidence.SdkInformationalVersion}.");
	}

	/// <summary>A version without its build metadata (<c>1.0.0+abc</c> is <c>1.0.0</c>), or <see langword="null" />.</summary>
	internal static string? WithoutMetadata(string? informationalVersion)
	{
		if (string.IsNullOrWhiteSpace(informationalVersion))
		{
			return null;
		}

		int plus = informationalVersion.IndexOf('+', StringComparison.Ordinal);
		return plus < 0 ? informationalVersion : informationalVersion[..plus];
	}

	private static ClientCapabilityEvidenceGate Unknown(string reason)
	{
		return new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Unknown, reason);
	}
}
