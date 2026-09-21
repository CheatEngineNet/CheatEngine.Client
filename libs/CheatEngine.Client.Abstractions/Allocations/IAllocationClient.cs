using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Results;

namespace CheatEngine.Client.Allocations;

/// <summary>Creates explicitly owned target-memory allocations.</summary>
public interface IAllocationClient
{
	/// <summary>Tries to allocate bounded target memory for the current selection.</summary>
	public bool TryAllocate(
		TargetAllocationRequest request,
		[NotNullWhen(true)] out ITargetMemoryLease? lease,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Allocates bounded target memory or throws when the host rejects the request.</summary>
	public ITargetMemoryLease Allocate(
		TargetAllocationRequest request,
		CancellationToken cancellationToken = default);
}
