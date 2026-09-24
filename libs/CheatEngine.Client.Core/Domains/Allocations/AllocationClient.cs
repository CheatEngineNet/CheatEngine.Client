using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Allocations;
using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Allocation;

namespace CheatEngine.Client.Core.Domains.Allocations;

/// <summary>Allocates target memory through CheatEngine.SDK's allocator, on Cheat Engine's main thread.</summary>
/// <remarks>
///     <para>
///         A published allocation is registered with the activation and with the target selection it was made in, in the
///         same main-thread callback that made it, so no path leaves it without an owner: a registration that fails, and
///         a cancellation observed after the allocation, release it at once.
///     </para>
///     <para>
///         The target selection is the one of the process incarnation that CheatEngine.SDK bound the allocation to
///         (<see cref="ITargetSelectionBinder" />), not the last selection the Client observed: a process selected in Cheat
///         Engine's own window since then advances the epoch before the lease is registered, so the next observation never
///         releases an allocation whose own process is still selected.
///     </para>
/// </remarks>
internal sealed class AllocationClient : IAllocationClient
{
	/// <summary>The public operation name of an allocation.</summary>
	internal const string AllocateOperation = "Allocations.Allocate";

	private readonly SdkMainThreadDispatcher _dispatcher;
	private readonly IAllocationPort _port;
	private readonly ITargetSelectionBinder _selection;

	/// <summary>Creates the allocation client of an activation.</summary>
	/// <param name="dispatcher">The activation dispatcher.</param>
	/// <param name="selection">The owner of the observed target selection, the activation's process client.</param>
	/// <param name="port">The allocator; CheatEngine.SDK's when omitted.</param>
	internal AllocationClient(SdkMainThreadDispatcher dispatcher, ITargetSelectionBinder selection,
		IAllocationPort? port = null)
	{
		_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		_selection = selection ?? throw new ArgumentNullException(nameof(selection));
		_port = port ?? SdkAllocationPort.Instance;
	}

	public bool TryAllocate(AllocationRequest request, [NotNullWhen(true)] out ITargetMemoryLease? lease,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		lease = null;
		// An ended or stopping activation throws before a refusal or a cancellation is reported, never the reverse.
		_dispatcher.Lifetime.ThrowIfDispatchAllowed(AllocateOperation);
		if (!AllocationMapping.TryCreateRequest(request, AllocateOperation, out TargetAllocationRequest sdkRequest,
				out failure))
		{
			return false;
		}

		if (cancellationToken.IsCancellationRequested)
		{
			failure = CancellationMapping.BeforeNativeCall(AllocateOperation);
			return false;
		}

		if (!_dispatcher.TryInvoke(() => AllocateOnMainThread(request, sdkRequest, cancellationToken),
				out AllocateOutcome outcome, out failure, cancellationToken))
		{
			return false;
		}

		_selection.ReportBinding(outcome.Binding, AllocateOperation);
		lease = outcome.Lease;
		failure = outcome.Failure;
		return lease is not null;
	}

	public ITargetMemoryLease Allocate(AllocationRequest request, CancellationToken cancellationToken = default)
	{
		if (TryAllocate(request, out ITargetMemoryLease? lease, out CheatEngineFailure failure, cancellationToken))
		{
			return lease;
		}

		failure.Throw(cancellationToken);
		throw new UnreachableException();
	}

	private AllocateOutcome AllocateOnMainThread(AllocationRequest request, TargetAllocationRequest sdkRequest,
		CancellationToken cancellationToken)
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return new AllocateOutcome(null, CancellationMapping.BeforeNativeCall(AllocateOperation));
		}

		CoreLifetime lifetime = _dispatcher.Lifetime;
		AllocationAttempt attempt;
		IAllocatedRegionHandle? region;
		try
		{
			attempt = _port.TryAllocate(in sdkRequest, out region);
		}
		catch (Exception fault) when (SdkBoundary.IsSdkFault(fault))
		{
			// The SDK reports every expected result as an outcome; a fault means that how far the call got is not known.
			return new AllocateOutcome(null,
				SdkBoundary.Translate(AllocateOperation, fault, CheatEngineHostEffect.Unknown, lifetime));
		}

		if (region is null)
		{
			return new AllocateOutcome(null,
				AllocationMapping.FromUnpublishedAllocation(attempt, request.Size, AllocateOperation));
		}

		if (cancellationToken.IsCancellationRequested)
		{
			// Nothing is published after a late cancellation: the new allocation is released at once.
			LeaseReleaseOutcome released = SdkReleaseOutcomes.FromTarget(region.Release());
			return new AllocateOutcome(null,
				AllocationMapping.CancelledAfterAllocation(released, attempt.Address, request.Size, AllocateOperation));
		}

		TargetSelectionBinding binding;
		TargetMemoryLease lease;
		try
		{
			binding = _selection.BindOwner(region.TargetIncarnation, AllocateOperation);
			lease = new TargetMemoryLease(_dispatcher, region, attempt.Address, request, binding.SelectionEpoch);
			lease.Register(lifetime, binding.SelectionEpoch);
		}
		catch (Exception)
		{
			// The activation or the target selection ended while the allocation was made: release it here, on the main
			// thread, since no registry will.
			_ = region.Release();
			throw;
		}

		return new AllocateOutcome(lease, default)
		{
			Binding = binding
		};
	}

	private readonly record struct AllocateOutcome(TargetMemoryLease? Lease, CheatEngineFailure Failure)
	{
		/// <summary>Gets the selection binding of a published lease, reported after the callback returned.</summary>
		internal TargetSelectionBinding Binding
		{
			get;
			init;
		}
	}
}
