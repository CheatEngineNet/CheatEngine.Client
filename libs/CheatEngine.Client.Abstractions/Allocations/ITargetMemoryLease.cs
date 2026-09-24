using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Allocations;

/// <summary>Owns one allocation that the Client made in a target process.</summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add
///         members to it, so implement it only in a test double.
///     </para>
///     <para>
///         <b>Experimental (<c>CECLIENT5002</c>).</b> The allocation API can change in a minor release until its live
///         scenarios pass; see the Abstractions README.
///     </para>
///     <para>
///         <b>Release.</b> <see cref="ICheatEngineLease.Release" /> frees the allocation on Cheat Engine's main thread,
///         and only in the process, and the process incarnation, that it was made in: when Cheat Engine has selected
///         another process, or when the same process identifier now names another process, CheatEngine.SDK refuses
///         before any Cheat Engine call (<see cref="LeaseReleaseKind.RefusedTargetChanged" />) and the Client never
///         selects the original process again to free it. The allocation is also refused after a change of the Lua
///         runtime that made it (<see cref="LeaseReleaseKind.RefusedRuntimeChanged" />), and a free that Cheat Engine did
///         not confirm is <see cref="LeaseReleaseKind.CleanupUnconfirmed" />; it is never retried. In each of these cases
///         the memory may remain in the target: <see cref="RequiresManualRecovery" /> is <see langword="true" />, and
///         <see cref="Address" /> and <see cref="Size" /> stay readable.
///     </para>
///     <para>
///         The lease belongs to the activation and to its target selection: selecting another process releases it
///         (which is then refused, as above), and so does disabling the plugin. Do not cache or pool allocations across
///         selections.
///     </para>
/// </remarks>
[Experimental(ClientExperimentalDiagnostics.Allocations, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
public interface ITargetMemoryLease : ICheatEngineLease
{
	/// <summary>Gets the target address that Cheat Engine allocated; it stays readable after the release.</summary>
	public Address Address
	{
		get;
	}

	/// <summary>Gets the number of bytes that was requested, which the release passes back to Cheat Engine.</summary>
	public long Size
	{
		get;
	}

	/// <summary>Gets the page protection the allocation was made with.</summary>
	public AllocationProtection Protection
	{
		get;
	}

	/// <summary>Gets the target-selection epoch of the process the allocation was made in.</summary>
	public long SelectionEpoch
	{
		get;
	}

	/// <summary>
	///     Gets whether the release ended without freeing the allocation, which may remain in the target until the
	///     application frees it by other means or the process ends: the value of
	///     <see cref="LeaseReleaseOutcome.RequiresManualRecovery" /> of <see cref="ICheatEngineLease.LastReleaseOutcome" />,
	///     and <see langword="false" /> before any release.
	/// </summary>
	public bool RequiresManualRecovery
	{
		get;
	}
}
