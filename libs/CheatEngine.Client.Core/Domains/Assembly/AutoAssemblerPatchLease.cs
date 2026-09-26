using CheatEngine.Client.Assembly;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Targets;

namespace CheatEngine.Client.Core.Domains.Assembly;

/// <summary>The Client lease of one applied Auto Assembler patch, bound to the target selection it was applied in.</summary>
/// <remarks>
///     <para>
///         The release runs on Cheat Engine's main thread through <see cref="HostResourceLease" /> and calls the SDK
///         owner's <c>ReleaseWithTargetOutcome</c> once: the SDK validates the captured target, then runs
///         <c>[DISABLE]</c> with the disable information Cheat Engine returned. The Client never rebuilds a
///         <c>[DISABLE]</c> section. The status is mapped with <see cref="AutoAssemblerMapping.ToReleaseOutcome" />.
///     </para>
///     <para>
///         The SDK consumes the disable information on the first attempt that reaches it, whatever the result, so every
///         outcome of that attempt ends the lease: <see cref="LeaseReleaseKind.Released" />, or a kind that requires
///         manual recovery. An attempt that could not begin the disable (<c>NotInvoked</c>) is
///         <see cref="LeaseReleaseKind.RefusedRuntimeChanged" />, never the retryable
///         <see cref="LeaseReleaseKind.CleanupUnavailable" />, because nothing is left to retry. Only a status this Client
///         version does not recognize stays retryable; a later attempt then makes no Cheat Engine call and reports the
///         recorded status again.
///     </para>
/// </remarks>
internal sealed class AutoAssemblerPatchLease : HostResourceLease, IAutoAssemblerPatchLease
{
	/// <summary>The stable operation name of the release.</summary>
	internal const string ReleaseOperation = "AutoAssembler.Release";

	private readonly IAutoAssemblerPatchOwner _patch;

	/// <summary>Creates the lease that owns an applied patch; register it with <see cref="HostResourceLease.Register" />.</summary>
	/// <param name="patch">The sole owner of the patch's disable information.</param>
	/// <param name="name">The Client diagnostic name of the script.</param>
	/// <param name="selectionEpoch">
	///     The target-selection epoch of the process CheatEngine.SDK bound the patch to
	///     (<see cref="ITargetSelectionBinder" />).
	/// </param>
	/// <param name="appliedAfterTargetChange">Whether the SDK observed a target change right after the activation.</param>
	/// <param name="hostWarnings">Cheat Engine's bounded compilation warnings.</param>
	/// <param name="hostWarningsTruncated">Whether <paramref name="hostWarnings" /> was cut at the bound.</param>
	/// <param name="dispatcher">The activation dispatcher that runs the release on Cheat Engine's main thread.</param>
	/// <param name="diagnostics">The activation diagnostics.</param>
	internal AutoAssemblerPatchLease(IAutoAssemblerPatchOwner patch, string? name, long selectionEpoch,
		bool appliedAfterTargetChange, string? hostWarnings, bool hostWarningsTruncated,
		ICheatEngineDispatcher dispatcher, ICoreDiagnostics? diagnostics)
		: base(ReleaseOperation, dispatcher, diagnostics)
	{
		_patch = patch ?? throw new ArgumentNullException(nameof(patch));
		Name = name;
		SelectionEpoch = selectionEpoch;
		AppliedAfterTargetChange = appliedAfterTargetChange;
		HostWarnings = hostWarnings;
		HostWarningsTruncated = hostWarnings is not null && hostWarningsTruncated;
	}

	public string? Name
	{
		get;
	}

	public long SelectionEpoch
	{
		get;
	}

	public bool CanDisable => !IsReleased && _patch.IsEnabled;

	public bool AppliedAfterTargetChange
	{
		get;
	}

	public string? HostWarnings
	{
		get;
	}

	public bool HostWarningsTruncated
	{
		get;
	}

	/// <summary>
	///     Gets CheatEngine.SDK's own flag, which only a release attempt sets: the attempt consumed the disable
	///     information without a confirmed disable. It adds to the recorded outcome only for a status this Client version
	///     does not recognize, whose outcome stays retryable; a replaced Lua state alone clears <see cref="CanDisable" />
	///     and leaves the flag unset until a release.
	/// </summary>
	protected override bool OwnerRequiresManualRecovery => _patch.RequiresManualRecovery;

	protected override LeaseReleaseOutcome ReleaseOnMainThread()
	{
		// One disable attempt per patch: a consumed owner reports the status of the attempt that consumed it.
		TargetReleaseStatus status = _patch.IsConsumed ? _patch.LastReleaseStatus : _patch.Release();
		return AutoAssemblerMapping.ToReleaseOutcome(status);
	}
}
