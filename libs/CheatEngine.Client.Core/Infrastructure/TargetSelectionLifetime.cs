using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>
///     Owns the independently advancing target-selection epoch for one plugin activation. It deliberately does not
///     change the SDK activation epoch: selecting another process invalidates target-bound leases, not the client.
/// </summary>
internal sealed class TargetSelectionLifetime(Action<string> activationGuard) : IDisposable
{
	private readonly Action<string> _activationGuard =
		activationGuard ?? throw new ArgumentNullException(nameof(activationGuard));

	private readonly Lock _gate = new();
	private readonly CoreResourceRegistry _resources = new();
	private bool _disposed;
	private long _epoch;

	/// <summary>Gets the current target-selection epoch, starting at zero for each activation.</summary>
	internal long Epoch => Volatile.Read(ref _epoch);

	/// <summary>Releases every target-bound resource that remains at activation shutdown.</summary>
	/// <exception cref="AggregateException">Several resources failed to release; the inner exceptions keep attempt order.</exception>
	public void Dispose()
	{
		List<Exception> failures = [];
		DisposeCollecting(failures);
		CoreResourceRegistry.ThrowCleanupFailures(failures);
	}

	/// <summary>Releases every remaining target-bound resource and appends each failure in attempt order.</summary>
	internal void DisposeCollecting(List<Exception> failures, Action<IDisposable, Exception>? onFailure = null)
	{
		IDisposable[] resources;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			resources = _resources.DetachAll();
		}

		CoreResourceRegistry.DisposeDetached(resources, failures, onFailure);
	}

	/// <summary>
	///     Advances the target selection after a PID or architecture change and releases the old selection's
	///     client-owned leases in reverse creation order.
	/// </summary>
	internal long Advance(string operation)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);

		IDisposable[] staleResources;
		long nextEpoch;
		lock (_gate)
		{
			_activationGuard(operation);
			ThrowIfDisposed();
			long staleEpoch = _epoch;
			nextEpoch = checked(staleEpoch + 1);
			Volatile.Write(ref _epoch, nextEpoch);
			staleResources = _resources.DetachTargetSelection(staleEpoch);
		}

		CoreResourceRegistry.DisposeDetached(staleResources);
		return nextEpoch;
	}

	/// <summary>Throws when a target-bound lease belongs to an earlier target selection.</summary>
	internal void ThrowIfExpired(long capturedEpoch, string operation)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);

		lock (_gate)
		{
			_activationGuard(operation);
			ThrowIfDisposed();
			if (capturedEpoch == _epoch)
			{
				return;
			}

			throw new CheatEngineClientLifecycleException(operation,
				"The target process selection changed, so this resource is no longer valid.");
		}
	}

	/// <summary>Registers a disposable resource against the current target selection.</summary>
	internal T Track<T>(T resource, long capturedEpoch)
		where T : class, IDisposable
	{
		ArgumentNullException.ThrowIfNull(resource);

		lock (_gate)
		{
			ThrowIfExpiredCore(capturedEpoch, "TargetSelection.Track");
			return _resources.Track(resource, capturedEpoch);
		}
	}

	/// <summary>Removes a resource after its owner released it explicitly.</summary>
	internal bool Untrack(IDisposable resource)
	{
		ArgumentNullException.ThrowIfNull(resource);

		lock (_gate)
		{
			return _resources.Untrack(resource);
		}
	}

	private void ThrowIfExpiredCore(long capturedEpoch, string operation)
	{
		_activationGuard(operation);
		ThrowIfDisposed();
		if (capturedEpoch == _epoch)
		{
			return;
		}

		throw new CheatEngineClientLifecycleException(operation,
			"The target process selection changed, so this resource is no longer valid.");
	}

	private void ThrowIfDisposed()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(TargetSelectionLifetime),
				"Target-bound resources cannot be registered after the Cheat Engine client lifecycle has stopped.");
		}
	}
}
