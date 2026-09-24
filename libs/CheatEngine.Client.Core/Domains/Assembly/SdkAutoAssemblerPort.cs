using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Assembly;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.Client.Core.Domains.Assembly;

/// <summary>
///     Production Auto Assembler port: CheatEngine.SDK 2.0.0 <c>AutoAssemblerPatcher.TryApplyWithOutcome</c> and
///     <c>AutoAssemblerPatcher.TryCheck</c>, behind the Client's Lua admission.
/// </summary>
/// <remarks>
///     <para>
///         This type is the only Client code that calls <c>AutoAssemblerPatcher</c> and the only code that holds an
///         <c>AutoAssemblerPatch</c>. The patch is handed to its Client owner through <see cref="OwnershipHandoff" />, so
///         a failure between the activation and the publication of the owner releases the patch exactly once.
///     </para>
///     <para>
///         No hosted test has a Cheat Engine process or Lua state, so its lines are excluded from the coverage metric;
///         <c>TryContractTests</c> proves that a detached runtime is refused before any Cheat Engine call.
///     </para>
/// </remarks>
internal sealed class SdkAutoAssemblerPort : IAutoAssemblerPort
{
	private SdkAutoAssemblerPort()
	{
	}

	/// <summary>Gets the stateless production port.</summary>
	internal static SdkAutoAssemblerPort Instance
	{
		get;
	} = new();

	public bool TryApply(string operation, string script, AutoAssemblerOptions options,
		out AutoAssemblerApplyFacts facts, out IAutoAssemblerPatchOwner? patch, out CheatEngineFailure admissionFailure)
	{
		facts = default;
		patch = null;
		if (!LuaAdmission.TryAcquire(operation, out LuaRuntimeOperation admitted, out admissionFailure))
		{
			return false;
		}

		using LuaRuntimeOperation admission = admitted;
		AutoAssemblerApplyOutcome outcome =
			AutoAssemblerPatcher.TryApplyWithOutcome(script, options, out AutoAssemblerPatch? applied);
		facts = new AutoAssemblerApplyFacts(outcome.Kind, outcome.Effect, outcome.LuaStatus, outcome.HostText,
			outcome.HostTextTruncated, outcome.HostWarnings, outcome.HostWarningsTruncated,
			outcome.Compensation?.Status);
		if (applied is not null)
		{
			patch = OwnershipHandoff.Adopt(applied, static owner => new SdkPatchOwner(owner),
				static owner => SdkReleaseOutcomes.FromTarget(owner.ReleaseWithTargetOutcome().Status));
		}

		return true;
	}

	public bool TryCheck(string operation, string script, AutoAssemblerOptions options,
		out AutoAssemblerCheckFacts facts, out CheatEngineFailure admissionFailure)
	{
		facts = default;
		if (!LuaAdmission.TryAcquire(operation, out LuaRuntimeOperation admitted, out admissionFailure))
		{
			return false;
		}

		using LuaRuntimeOperation admission = admitted;
		AutoAssemblerCheckOutcome outcome = AutoAssemblerPatcher.TryCheck(script, true, options);
		facts = new AutoAssemblerCheckFacts(outcome.Kind, outcome.LuaStatus, outcome.HostText,
			outcome.HostTextTruncated);
		return true;
	}

	/// <summary>The Client view of CheatEngine.SDK's <c>AutoAssemblerPatch</c>.</summary>
	/// <remarks>
	///     <see cref="Release" /> is <c>ReleaseWithTargetOutcome</c>: it consumes the disable information, validates the
	///     captured target and runs <c>[DISABLE]</c> once. It never throws for a consumed owner here, because the lease
	///     reads <see cref="IsConsumed" /> first under its release gate.
	/// </remarks>
	private sealed class SdkPatchOwner(AutoAssemblerPatch patch) : IAutoAssemblerPatchOwner
	{
		public bool IsEnabled => patch.IsEnabled;

		public bool IsConsumed => patch.IsDisposed;

		public bool RequiresManualRecovery => patch.RequiresManualRecovery;

		public TargetReleaseStatus LastReleaseStatus => patch.LastReleaseOutcome.Status;

		public TargetReleaseStatus Release()
		{
			return patch.ReleaseWithTargetOutcome().Status;
		}
	}
}
