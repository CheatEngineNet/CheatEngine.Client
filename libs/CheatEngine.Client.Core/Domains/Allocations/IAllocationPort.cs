using CheatEngine.SDK.Engine.Allocation;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Domains.Allocations;

/// <summary>Internal boundary that allocates target memory through CheatEngine.SDK; Core tests replace it with doubles.</summary>
/// <remarks>Every member runs on Cheat Engine's main thread, inside a dispatched callback.</remarks>
internal interface IAllocationPort
{
	/// <summary>Allocates memory in Cheat Engine's selected target (<c>TargetMemoryAllocator.TryAllocate</c>).</summary>
	/// <param name="request">The SDK request, built and validated by the Client.</param>
	/// <param name="region">The owner of the allocation, only when CheatEngine.SDK published one.</param>
	/// <returns>The factual copy of the SDK outcome.</returns>
	public AllocationAttempt TryAllocate(in TargetAllocationRequest request, out IAllocatedRegionHandle? region);
}

/// <summary>Internal view of one CheatEngine.SDK <c>AllocatedRegion</c>: its target binding and its release only.</summary>
/// <remarks>Every member runs on Cheat Engine's main thread, inside a dispatched callback.</remarks>
internal interface IAllocatedRegionHandle
{
	/// <summary>Gets the process incarnation that CheatEngine.SDK bound the allocation to (<c>TargetIncarnation</c>).</summary>
	public TargetProcessIncarnation TargetIncarnation
	{
		get;
	}

	/// <summary>
	///     Frees the allocation in the process incarnation it was made in (<c>ReleaseWithTargetOutcome</c>); never throws.
	///     CheatEngine.SDK refuses, without any Cheat Engine call, when another target or another Lua runtime is current.
	/// </summary>
	/// <returns>
	///     The status of the one release attempt; a later call returns the same status without any Cheat Engine call.
	/// </returns>
	public TargetReleaseStatus Release();
}

/// <summary>The copied facts of one <c>TargetMemoryAllocator.TryAllocate</c> call.</summary>
/// <param name="Kind">The category of the <c>allocateMemory</c> call.</param>
/// <param name="Effect">How far the allocation went.</param>
/// <param name="Address">The address Cheat Engine returned, including when no owner was published; otherwise zero.</param>
/// <param name="Compensation">
///     The status of the one release CheatEngine.SDK attempted because Cheat Engine allocated but no owner could be
///     published; <see langword="null" /> when no compensation was needed.
/// </param>
internal readonly record struct AllocationAttempt(
	TargetMemoryOperationOutcomeKind Kind,
	EngineEffectState Effect,
	Address Address,
	TargetReleaseStatus? Compensation);
