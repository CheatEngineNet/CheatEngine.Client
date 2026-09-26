using CheatEngine.SDK.Engine.Allocation;
using CheatEngine.SDK.Engine.Targets;

namespace CheatEngine.Client.Core.Domains.Allocations;

/// <summary>Production adapter over <c>TargetMemoryAllocator</c> and <c>AllocatedRegion</c> of CheatEngine.SDK 2.0.0.</summary>
/// <remarks>
///     <para>
///         Allocation goes through <see cref="TargetMemoryAllocator.TryAllocate" />, the SDK path that binds an allocation
///         to the Lua runtime and the qualified target incarnation that made it, and that makes the one compensation
///         attempt when Cheat Engine allocated but no owner could be published. The release goes through
///         <see cref="AllocatedRegion.ReleaseWithTargetOutcome" />, which refuses another target or runtime without any
///         Cheat Engine call and never selects a target.
///     </para>
///     <para>
///         Only a hosted Cheat Engine can reach this adapter: it is excluded from the coverage metric, and Core tests
///         exercise the allocation domain through <see cref="IAllocationPort" /> doubles.
///     </para>
/// </remarks>
internal sealed class SdkAllocationPort : IAllocationPort
{
	private readonly TargetMemoryAllocator _allocator = new();

	private SdkAllocationPort()
	{
	}

	/// <summary>Gets the production adapter.</summary>
	internal static SdkAllocationPort Instance
	{
		get;
	} = new();

	public AllocationAttempt TryAllocate(in TargetAllocationRequest request, out IAllocatedRegionHandle? region)
	{
		TargetAllocationAcquireOutcome outcome = _allocator.TryAllocate(request, out AllocatedRegion? created);
		TargetReleaseStatus? compensation = outcome.Compensation?.Status;
		if (outcome.HasOwner && created is not null)
		{
			region = new SdkAllocatedRegionHandle(created);
			return new AllocationAttempt(outcome.Allocation.Operation.Kind, outcome.Effect, outcome.Allocation.Address,
				compensation);
		}

		// The SDK publishes a region only with an owner. A region that a contract break publishes anyway is released here,
		// once, and its release is reported as the compensation, rather than leaving the allocation without an owner.
		if (created is not null)
		{
			TargetReleaseStatus released = created.ReleaseWithTargetOutcome().Status;
			compensation ??= released;
		}

		region = null;
		return new AllocationAttempt(outcome.Allocation.Operation.Kind, outcome.Effect, outcome.Allocation.Address,
			compensation);
	}

	private sealed class SdkAllocatedRegionHandle(AllocatedRegion region) : IAllocatedRegionHandle
	{
		private readonly AllocatedRegion _region = region ?? throw new ArgumentNullException(nameof(region));

		public TargetProcessIncarnation TargetIncarnation => _region.TargetIncarnation;

		public TargetReleaseStatus Release()
		{
			// The SDK consumes the owner on its one attempt: a later call reports that attempt again, without any call.
			if (_region.IsDisposed)
			{
				return _region.LastReleaseOutcome.Status;
			}

			try
			{
				return _region.ReleaseWithTargetOutcome().Status;
			}
			catch (ObjectDisposedException)
			{
				return _region.LastReleaseOutcome.Status;
			}
		}
	}
}
