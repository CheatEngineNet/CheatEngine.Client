using CheatEngine.Client.Core.Domains.Allocations;
using CheatEngine.SDK.Engine.Allocation;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Tests.TestSupport;

/// <summary>A scripted allocator: returns <see cref="Attempt" />, and <see cref="Region" /> when it publishes an owner.</summary>
internal sealed class FakeAllocationPort : IAllocationPort
{
	/// <summary>The address of a successful scripted allocation.</summary>
	internal static readonly Address AllocatedAddress = new(0x7FF0_0000_1000);

	internal AllocationAttempt Attempt
	{
		get;
		set;
	} = new(TargetMemoryOperationOutcomeKind.Succeeded, EngineEffectState.Applied, AllocatedAddress, null);

	/// <summary>Whether the allocator publishes <see cref="Region" />, as CheatEngine.SDK does only with an owner.</summary>
	internal bool PublishesOwner
	{
		get;
		set;
	} = true;

	internal Exception? Fault
	{
		get;
		set;
	}

	internal Action? DuringAllocate
	{
		get;
		set;
	}

	/// <summary>The owner the next allocation publishes; replace it to publish another allocation.</summary>
	internal FakeAllocatedRegion Region
	{
		get;
		set;
	} = new();

	internal int Allocations
	{
		get;
		private set;
	}

	internal TargetAllocationRequest? LastRequest
	{
		get;
		private set;
	}

	/// <summary>Scripts an allocation that published no owner.</summary>
	internal void Refuse(TargetMemoryOperationOutcomeKind kind, EngineEffectState effect, Address address = default,
		TargetReleaseStatus? compensation = null)
	{
		PublishesOwner = false;
		Attempt = new AllocationAttempt(kind, effect, address, compensation);
	}

	public AllocationAttempt TryAllocate(in TargetAllocationRequest request, out IAllocatedRegionHandle? region)
	{
		Allocations++;
		LastRequest = request;
		DuringAllocate?.Invoke();
		if (Fault is { } fault)
		{
			throw fault;
		}

		region = PublishesOwner ? Region : null;
		return Attempt;
	}
}

/// <summary>
///     Emulates the CheatEngine.SDK 2.0.0 <c>AllocatedRegion</c> release the Client relies on: the one attempt consumes
///     the owner, a refusal makes no Cheat Engine call, and a later call reports the first attempt again without any call.
/// </summary>
internal sealed class FakeAllocatedRegion : IAllocatedRegionHandle
{
	private TargetReleaseStatus? _consumed;

	/// <summary>The incarnation the SDK bound the allocation to, the default selected target's unless replaced.</summary>
	public TargetProcessIncarnation TargetIncarnation
	{
		get;
		set;
	} = FakeSelectedTarget.FirstIncarnation;

	/// <summary>The status of the one release attempt.</summary>
	internal TargetReleaseStatus ReleaseStatus
	{
		get;
		set;
	} = TargetReleaseStatus.Released;

	internal Exception? ReleaseFault
	{
		get;
		set;
	}

	/// <summary>Gets the number of release requests the Client made.</summary>
	internal int ReleaseCalls
	{
		get;
		private set;
	}

	/// <summary>Gets the number of <c>deAlloc</c> calls: only a release that reached Cheat Engine makes one.</summary>
	internal int Deallocations
	{
		get;
		private set;
	}

	public TargetReleaseStatus Release()
	{
		ReleaseCalls++;
		if (_consumed is { } consumed)
		{
			return consumed;
		}

		if (ReleaseFault is { } fault)
		{
			_consumed = TargetReleaseStatus.UnconfirmedAfterInvocation;
			throw fault;
		}

		if (ReleaseStatus is TargetReleaseStatus.Released or TargetReleaseStatus.UnconfirmedAfterInvocation)
		{
			Deallocations++;
		}

		_consumed = ReleaseStatus;
		return ReleaseStatus;
	}
}
