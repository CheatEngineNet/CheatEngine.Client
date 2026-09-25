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
///         The lease holds the only disable information Cheat Engine returned for the patch. The first release attempt
///         that reaches Cheat Engine consumes it: CheatEngine.SDK validates that the target the patch was applied to is
///         still selected, then runs the script's <c>[DISABLE]</c> section once with that information. A release on
///         another target is refused (<see cref="LeaseReleaseKind.RefusedTargetChanged" />) without selecting a process;
///         a release after Cheat Engine's Lua runtime detached or replaced its Lua state, or one that could not begin
///         the disable, is refused (<see cref="LeaseReleaseKind.RefusedRuntimeChanged" />); and a disable that Cheat
///         Engine did not confirm is <see cref="LeaseReleaseKind.CleanupUnconfirmed" />. In each case the attempt ended
///         the lease, the patch may remain in the target, and <see cref="RequiresManualRecovery" /> becomes
///         <see langword="true" />: no later release can disable it.
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
	///     Gets whether the lease still holds Cheat Engine's disable information in the current Lua state, so a release
	///     can attempt the disable.
	/// </summary>
	/// <remarks>
	///     <see langword="false" /> once a release attempt consumed the information, or after Cheat Engine's Lua state was
	///     detached or replaced. It does not say whether the patch is still present in the target.
	/// </remarks>
	public bool IsEnabled
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

	/// <summary>
	///     Gets whether the patch may remain in the target although this lease can no longer disable it: a release attempt
	///     consumed Cheat Engine's disable information without a confirmed disable.
	/// </summary>
	/// <remarks>
	///     This covers every outcome whose <see cref="LeaseReleaseOutcome.RequiresManualRecovery" /> is
	///     <see langword="true" />. A <see cref="LeaseReleaseKind.CleanupUnavailable" /> outcome means that the release
	///     could not be dispatched to Cheat Engine: the disable information was not consumed, and a later release can
	///     still run <c>[DISABLE]</c>.
	/// </remarks>
	public bool RequiresManualRecovery
	{
		get;
	}
}
