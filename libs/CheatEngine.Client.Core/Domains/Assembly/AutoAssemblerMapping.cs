#pragma warning disable CECLIENT5004 // Core implements the experimental Auto Assembler surface it serves.

using CheatEngine.Client.Assembly;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Assembly;
using CheatEngine.SDK.Engine.Targets;

namespace CheatEngine.Client.Core.Domains.Assembly;

/// <summary>
///     Maps every CheatEngine.SDK 2.0.0 Auto Assembler outcome category to the Client result vocabulary, without reading
///     Cheat Engine's text.
/// </summary>
/// <remarks>
///     <para>Activation (<see cref="AutoAssemblerApplyOutcomeKind" />):</para>
///     <list type="bullet">
///         <item><description><c>Applied</c>: success, the patch lease is returned.</description></item>
///         <item>
///             <description>
///                 <c>AppliedTargetChanged</c>: success, the lease reports <c>AppliedAfterTargetChange</c> and the
///                 Client logs a warning; the patch stays bound to the target observed before the activation.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <c>Rejected</c>: <see cref="CheatEngineFailureKind.OperationRejected" /> with the SDK's effect,
///                 <see cref="CheatEngineHostEffect.Unknown" />: a rejection does not prove that nothing changed.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <c>GlobalUnavailable</c>: <see cref="CheatEngineFailureKind.CapabilityUnavailable" />,
///                 <see cref="CheatEngineHostEffect.NotStarted" />.
///             </description>
///         </item>
///         <item>
///             <description><c>ProtectedLuaFailure</c>: <see cref="CheatEngineFailureKind.LuaError" />, unknown effect.</description>
///         </item>
///         <item>
///             <description><c>InvalidResult</c>: <see cref="CheatEngineFailureKind.InvalidHostResult" />, unknown effect.</description>
///         </item>
///         <item>
///             <description>
///                 <c>TargetIdentityUnavailable</c>: <see cref="CheatEngineFailureKind.TargetIdentityUnavailable" />,
///                 <see cref="CheatEngineHostEffect.NotStarted" />.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <c>HandoffFailed</c>: <see cref="CheatEngineFailureKind.BindingError" /> (the category of the SDK's
///                 <c>EngineResourceHandoffException</c>) with <see cref="CheatEngineHostEffect.CleanupUnconfirmed" />,
///                 whatever the compensation reported: no rollback stronger than Cheat Engine's own disable is promised.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <c>Unknown</c> and any value this Client version does not know:
///                 <see cref="CheatEngineFailureKind.Unknown" /> with an unknown effect, never a success.
///             </description>
///         </item>
///     </list>
///     <para>
///         Syntax check (<see cref="AutoAssemblerCheckOutcomeKind" />): <c>Accepted</c> and <c>Rejected</c> are verdicts
///         (the check succeeded); <c>GlobalUnavailable</c> is <see cref="CheatEngineFailureKind.CapabilityUnavailable" />
///         with <see cref="CheatEngineHostEffect.NotStarted" />; <c>ProtectedLuaFailure</c> is
///         <see cref="CheatEngineFailureKind.LuaError" />, <c>InvalidResult</c>
///         <see cref="CheatEngineFailureKind.InvalidHostResult" />, and <c>Unknown</c> or an unknown value
///         <see cref="CheatEngineFailureKind.Unknown" />, each with an unknown effect.
///     </para>
///     <para>
///         The host effect of an activation comes from the SDK's own effect state through
///         <see cref="HostEffectMapping" />. The mapping-totality tests fail when the consumed SDK adds a category.
///     </para>
/// </remarks>
internal static class AutoAssemblerMapping
{
	private const string TruncatedSuffix = " [truncated]";

	/// <summary>Maps an activation outcome; <see langword="null" /> when Cheat Engine applied the script.</summary>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="facts">The copied SDK outcome.</param>
	/// <returns>The failure, or <see langword="null" /> for <c>Applied</c> and <c>AppliedTargetChanged</c>.</returns>
	internal static CheatEngineFailure? ToApplyFailure(string operation, AutoAssemblerApplyFacts facts)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		CheatEngineHostEffect effect = HostEffectMapping.FromSdk(facts.Effect);
		return facts.Kind switch
		{
			AutoAssemblerApplyOutcomeKind.Applied or AutoAssemblerApplyOutcomeKind.AppliedTargetChanged => null,
			AutoAssemblerApplyOutcomeKind.Rejected => new CheatEngineFailure(CheatEngineFailureKind.OperationRejected,
				operation,
				WithHostText("Cheat Engine rejected the Auto Assembler script; part of its effects may have been applied.",
					facts.HostText, facts.HostTextTruncated),
				null, effect),
			AutoAssemblerApplyOutcomeKind.GlobalUnavailable => new CheatEngineFailure(
				CheatEngineFailureKind.CapabilityUnavailable, operation,
				"Cheat Engine's autoAssemble function is unavailable; the script was not applied.", null, effect),
			AutoAssemblerApplyOutcomeKind.ProtectedLuaFailure => new CheatEngineFailure(CheatEngineFailureKind.LuaError,
				operation,
				$"The protected Lua call of autoAssemble failed with status '{facts.LuaStatus}'; part of the script may " +
				"have been applied.", null, effect),
			AutoAssemblerApplyOutcomeKind.InvalidResult => new CheatEngineFailure(
				CheatEngineFailureKind.InvalidHostResult, operation,
				"Cheat Engine returned an Auto Assembler result outside the documented shapes; the script may have been " +
				"applied without disable information.", null, effect),
			AutoAssemblerApplyOutcomeKind.TargetIdentityUnavailable => new CheatEngineFailure(
				CheatEngineFailureKind.TargetIdentityUnavailable, operation,
				"The selected target could not be qualified as a process incarnation; the script was not applied.", null,
				effect),
			AutoAssemblerApplyOutcomeKind.HandoffFailed => new CheatEngineFailure(CheatEngineFailureKind.BindingError,
				operation,
				"Cheat Engine applied the script, but CheatEngine.SDK could not hand its disable information to an " +
				$"owner; its one compensating disable ended as {DescribeCompensation(facts.Compensation)}. The patch may " +
				"remain in the target.", null, CheatEngineHostEffect.CleanupUnconfirmed),
			_ => new CheatEngineFailure(CheatEngineFailureKind.Unknown, operation,
				"CheatEngine.SDK reported no recognized Auto Assembler outcome; the script may have been applied.", null,
				CheatEngineHostEffect.Unknown)
		};
	}

	/// <summary>Maps a syntax-check outcome to a verdict or a failure.</summary>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="facts">The copied SDK outcome.</param>
	/// <param name="result">The verdict for <c>Accepted</c> and <c>Rejected</c>; otherwise the default value.</param>
	/// <param name="failure">The failure when the check produced no verdict; otherwise the default value.</param>
	/// <returns><see langword="true" /> when Cheat Engine returned a verdict.</returns>
	internal static bool TryMapCheck(string operation, AutoAssemblerCheckFacts facts,
		out AutoAssemblerCheckResult result, out CheatEngineFailure failure)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		result = default;
		failure = default;
		switch (facts.Kind)
		{
			case AutoAssemblerCheckOutcomeKind.Accepted:
				result = new AutoAssemblerCheckResult(true, null, false);
				return true;
			case AutoAssemblerCheckOutcomeKind.Rejected:
				result = new AutoAssemblerCheckResult(false, facts.HostText,
					facts.HostText is not null && facts.HostTextTruncated);
				return true;
			case AutoAssemblerCheckOutcomeKind.GlobalUnavailable:
				failure = new CheatEngineFailure(CheatEngineFailureKind.CapabilityUnavailable, operation,
					"Cheat Engine's autoAssembleCheck function is unavailable; the script was not checked.", null,
					CheatEngineHostEffect.NotStarted);
				return false;
			case AutoAssemblerCheckOutcomeKind.ProtectedLuaFailure:
				failure = new CheatEngineFailure(CheatEngineFailureKind.LuaError, operation,
					$"The protected Lua call of autoAssembleCheck failed with status '{facts.LuaStatus}'.", null,
					CheatEngineHostEffect.Unknown);
				return false;
			case AutoAssemblerCheckOutcomeKind.InvalidResult:
				failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, operation,
					"Cheat Engine returned an Auto Assembler check result outside the documented shape.", null,
					CheatEngineHostEffect.Unknown);
				return false;
			default:
				failure = new CheatEngineFailure(CheatEngineFailureKind.Unknown, operation,
					"CheatEngine.SDK reported no recognized Auto Assembler check outcome.", null,
					CheatEngineHostEffect.Unknown);
				return false;
		}
	}

	private static string WithHostText(string message, string? hostText, bool truncated)
	{
		return hostText is null
			? message
			: message + " Cheat Engine reported: " + hostText + (truncated ? TruncatedSuffix : string.Empty);
	}

	private static string DescribeCompensation(TargetReleaseStatus? compensation)
	{
		return compensation is { } status ? status.ToString() : "an unreported status";
	}
}
