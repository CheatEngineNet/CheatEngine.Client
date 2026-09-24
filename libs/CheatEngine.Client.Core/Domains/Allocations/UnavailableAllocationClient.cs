using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Allocations;
using CheatEngine.Client.Core.Domains.Events;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;

namespace CheatEngine.Client.Core.Domains.Allocations;

/// <summary>Preserves the allocation contract while its SDK ownership factory awaits live-host validation.</summary>
internal sealed class UnavailableAllocationClient : IAllocationClient
{
	private readonly CoreLifetime? _lifetime;

	internal UnavailableAllocationClient(CoreLifetime? lifetime = null)
	{
		_lifetime = lifetime;
	}

	public bool TryAllocate(TargetAllocationRequest request, [NotNullWhen(true)] out ITargetMemoryLease? lease,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		lease = null;
		failure = UnavailableCapabilityFailure.Create(_lifetime, ClientCapabilityId.Allocations, "Target allocations",
			"Allocations.Allocate",
			cancellationToken);
		return false;
	}

	public ITargetMemoryLease Allocate(TargetAllocationRequest request, CancellationToken cancellationToken = default)
	{
		_ = TryAllocate(request, out _, out CheatEngineFailure failure, cancellationToken);
		return UnavailableCapabilityFailure.Throw<ITargetMemoryLease>(failure, cancellationToken);
	}
}
