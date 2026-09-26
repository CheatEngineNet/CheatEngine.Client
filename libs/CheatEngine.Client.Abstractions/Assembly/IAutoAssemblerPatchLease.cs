using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Results;

namespace CheatEngine.Client.Assembly;

/// <summary>Owns one Auto Assembler patch the Client applied for the current activation and target selection.</summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add
///         members to it, so implement it only in a test double.
///     </para>
///     <para>
///         <b>Experimental (<c>CECLIENT5004</c>).</b> The Auto Assembler API can change in a minor release until its
///         live scenarios pass; see the Abstractions README.
///     </para>
///     <para>
///         The lease holds the only disable information Cheat Engine returned for the patch. The first release attempt
///         that reaches Cheat Engine consumes it: CheatEngine.SDK validates that the target the patch was applied to is
///         still selected, then runs the script's <c>[DISABLE]</c> section once with that information. A release on
///         another target is refused (<see cref="LeaseReleaseKind.RefusedTargetChanged" />) without selecting a process;
///         a release after Cheat Engine's Lua runtime detached or replaced its Lua state, or one that could not begin
///         the disable, is refused (<see cref="LeaseReleaseKind.RefusedRuntimeChanged" />); and a disable that Cheat
///         Engine did not confirm is <see cref="LeaseReleaseKind.CleanupUnconfirmed" />. In each case the attempt ended
///         the lease, the patch may remain in the target, and
///         <see cref="ICheatEngineLease.RequiresManualRecovery" /> becomes <see langword="true" />: no later release can
///         disable it. Before any release, <see cref="CanDisable" /> is the early signal: it is <see langword="false" />
///         once Cheat Engine's Lua runtime detached or replaced its Lua state, while
///         <see cref="ICheatEngineLease.RequiresManualRecovery" /> stays <see langword="false" /> until a release
///         reports what it left.
///     </para>
///     <para>
///         <b>Release the lease before selecting another process.</b> The Client observes a selection change only after
///         Cheat Engine already targets the new process, and then ends the lease with
///         <see cref="LeaseReleaseKind.RefusedTargetChanged" />: the disable information is consumed, the patch
///         stays in the previous process, and selecting that process again cannot disable it.
///     </para>
///     <para>
///         The Client never rebuilds a <c>[DISABLE]</c> section, and it does not expose Cheat Engine's disable
///         information (allocations, registered symbols): that table stays the only disable authority.
///     </para>
/// </remarks>
[Experimental(ClientExperimentalDiagnostics.AutoAssemblerPatches, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
public interface IAutoAssemblerPatchLease : ICheatEngineLease
{
	/// <summary>Gets the Client diagnostic name supplied with the script, if any.</summary>
	public string? Name
	{
		get;
	}

	/// <summary>Gets the target-selection epoch of the process CheatEngine.SDK applied the patch in.</summary>
	public long SelectionEpoch
	{
		get;
	}

	/// <summary>
	///     Gets whether a release can still run the <c>[DISABLE]</c> section: the lease holds Cheat Engine's disable
	///     information in the current Lua state.
	/// </summary>
	/// <remarks>
	///     <see langword="false" /> once a release attempt consumed the information: read
	///     <see cref="ICheatEngineLease.LastReleaseOutcome" /> to know whether that attempt disabled the patch. Before any
	///     release, <see langword="false" /> means that Cheat Engine's Lua state was detached or replaced: a release that
	///     reaches CheatEngine.SDK cannot run <c>[DISABLE]</c>, is refused
	///     (<see cref="LeaseReleaseKind.RefusedRuntimeChanged" />) and leaves the patch in the target.
	/// </remarks>
	public bool CanDisable
	{
		get;
	}

	/// <summary>
	///     Gets whether Cheat Engine applied the script while the selected target changed: the target observed right after
	///     the activation no longer matched the one observed before it.
	/// </summary>
	/// <remarks>
	///     The patch stays bound to the target observed before the activation, so its release is refused while another
	///     process is selected. Treat its effect on either process as uncertain.
	/// </remarks>
	public bool AppliedAfterTargetChange
	{
		get;
	}

	/// <summary>Gets Cheat Engine's bounded, unparsed compilation warnings for the activation, if it returned any.</summary>
	/// <remarks>User data: the text can contain script source and file paths.</remarks>
	public string? HostWarnings
	{
		get;
	}

	/// <summary>Gets whether <see cref="HostWarnings" /> was cut at the Client's host-text bound.</summary>
	public bool HostWarningsTruncated
	{
		get;
	}

}
