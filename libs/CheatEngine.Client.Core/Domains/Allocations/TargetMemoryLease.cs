using CheatEngine.Client.Allocations;
using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Domains.Allocations;

/// <summary>One target allocation: a lease over one CheatEngine.SDK <c>AllocatedRegion</c>.</summary>
/// <remarks>
///     <para>
///         The lease is a <see cref="HostResourceLease" /> registered with the activation and with the target selection it
///         was made in. Its release runs <c>ReleaseWithTargetOutcome</c> on Cheat Engine's main thread, when the
///         application releases it, when Cheat Engine selects another process, or before CheatEngine.SDK detaches at
///         deactivation. The SDK frees the allocation only in the process incarnation and the Lua runtime that made it;
///         otherwise it refuses without any Cheat Engine call, and the lease never selects a target to free it.
///     </para>
///     <para>
///         The address, size and protection are copied when the allocation is made, so they stay readable after the
///         release, which the manual recovery of a refused or unconfirmed release needs.
///     </para>
/// </remarks>
internal sealed class TargetMemoryLease : HostResourceLease, ITargetMemoryLease
{
	/// <summary>The public operation name of the release.</summary>
	internal const string ReleaseOperation = "Allocations.Release";

	private readonly IAllocatedRegionHandle _region;

	/// <summary>Creates the lease of an allocation that CheatEngine.SDK published; the caller registers it.</summary>
	/// <param name="dispatcher">The activation dispatcher.</param>
	/// <param name="region">The SDK owner, which this lease owns from now on.</param>
	/// <param name="address">The allocated address.</param>
	/// <param name="request">The request the allocation was made for.</param>
	/// <param name="selectionEpoch">The target-selection epoch the allocation was made in.</param>
	internal TargetMemoryLease(SdkMainThreadDispatcher dispatcher, IAllocatedRegionHandle region, Address address,
		AllocationRequest request, long selectionEpoch)
		: base(ReleaseOperation, dispatcher, dispatcher?.Lifetime.Diagnostics)
	{
		_region = region ?? throw new ArgumentNullException(nameof(region));
		Address = address;
		Size = request.Size;
		Protection = request.Protection;
		SelectionEpoch = selectionEpoch;
	}

	public Address Address
	{
		get;
	}

	public long Size
	{
		get;
	}

	public AllocationProtection Protection
	{
		get;
	}

	public long SelectionEpoch
	{
		get;
	}

	public bool RequiresManualRecovery => LastReleaseOutcome is { RequiresManualRecovery: true };

	protected override LeaseReleaseOutcome ReleaseOnMainThread()
	{
		return SdkReleaseOutcomes.FromTarget(_region.Release());
	}
}
