using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Results;

namespace CheatEngine.Client.Allocations;

/// <summary>Creates explicitly owned target-memory allocations.</summary>
/// <remarks>
///     <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add members
///     to it, so implement it only in a test double.
/// </remarks>
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
