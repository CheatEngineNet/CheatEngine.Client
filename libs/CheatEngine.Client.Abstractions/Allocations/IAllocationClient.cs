using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Results;

namespace CheatEngine.Client.Allocations;

/// <summary>Allocates memory in Cheat Engine's selected target, each allocation owned by a lease.</summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add members
///         to it, so implement it only in a test double.
///     </para>
///     <para>
///         <b>Experimental (<c>CECLIENT5002</c>).</b> The allocation API can change in a minor release until its live
///         scenarios pass; see the Abstractions README.
///     </para>
///     <para>
///         An allocation runs Cheat Engine's <c>allocateMemory</c> on Cheat Engine's main thread through CheatEngine.SDK's
///         allocator, for a target whose identity it could establish; it is refused with
///         <see cref="CheatEngineFailureKind.TargetIdentityUnavailable" /> otherwise, before any Cheat Engine call. When
///         Cheat Engine allocated but no lease can be published, the Client frees the allocation once: a free that is not
///         confirmed reports <see cref="CheatEngineHostEffect.CleanupUnconfirmed" /> and the failure message carries the
///         address, so the memory can be recovered by other means.
///     </para>
/// </remarks>
[Experimental(ClientExperimentalDiagnostics.Allocations, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
public interface IAllocationClient
{
	/// <summary>Tries to allocate memory in Cheat Engine's selected target.</summary>
	/// <param name="request">The allocation.</param>
	/// <param name="lease">The lease of the allocation when the method returns <see langword="true" />; release it when done.</param>
	/// <param name="failure">The classified failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">Observed before Cheat Engine allocates, and after.</param>
	/// <returns><see langword="true" /> when the memory was allocated and its lease published.</returns>
	/// <remarks>
	///     A cancellation observed after Cheat Engine allocated frees the allocation at once and publishes nothing.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which has no size, or a tampered one;
	///     it is thrown before the activation check and before any Cheat Engine call.
	/// </exception>
	public bool TryAllocate(
		AllocationRequest request,
		[NotNullWhen(true)] out ITargetMemoryLease? lease,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Allocates memory in Cheat Engine's selected target, or throws the failure.</summary>
	/// <param name="request">The allocation.</param>
	/// <param name="cancellationToken">Observed before Cheat Engine allocates, and after.</param>
	/// <returns>The lease of the allocation; release it when done.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="request" /> is the <see langword="default" /> request, which has no size, or a tampered one;
	///     it is thrown before the activation check and before any Cheat Engine call.
	/// </exception>
	public ITargetMemoryLease Allocate(
		AllocationRequest request,
		CancellationToken cancellationToken = default);
}
