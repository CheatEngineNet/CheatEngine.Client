using System.Runtime.ExceptionServices;

namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>Owns client-created resources for one active plugin epoch and releases them in reverse creation order.</summary>
internal sealed class CoreResourceRegistry : IDisposable
{
	private readonly List<ResourceEntry> _resources = [];
	private bool _disposed;

	/// <summary>Releases every tracked resource even when an earlier cleanup fails.</summary>
	/// <exception cref="AggregateException">Several resources failed to release; the inner exceptions keep attempt order.</exception>
	/// <remarks>A single release failure is rethrown as the same instance with its original stack trace.</remarks>
	public void Dispose()
	{
		DisposeDetached(DetachAll());
	}

	/// <summary>Releases every tracked resource and appends each failure to <paramref name="failures" /> in attempt order.</summary>
	internal void DisposeCollecting(List<Exception> failures)
	{
		DisposeDetached(DetachAll(), failures);
	}

	internal T Track<T>(T resource)
		where T : class, IDisposable
	{
		return TrackCore(resource, null);
	}

	/// <summary>Tracks a resource that must be released when its target selection expires.</summary>
	internal T Track<T>(T resource, long targetSelectionEpoch)
		where T : class, IDisposable
	{
		return TrackCore(resource, targetSelectionEpoch);
	}

	/// <summary>Detaches and releases resources created for one target selection in reverse creation order.</summary>
	internal void DisposeTargetSelection(long targetSelectionEpoch)
	{
		DisposeDetached(DetachTargetSelection(targetSelectionEpoch));
	}

	/// <summary>Detaches every registration and marks the registry closed.</summary>
	internal IDisposable[] DetachAll()
	{
		lock (_resources)
		{
			if (_disposed)
			{
				return [];
			}

			_disposed = true;
			IDisposable[] resources = new IDisposable[_resources.Count];
			for (int index = 0; index < resources.Length; index++)
			{
				resources[index] = _resources[index].Resource;
			}

			_resources.Clear();
			return resources;
		}
	}

	/// <summary>Detaches the resources registered to one selection without closing the registry.</summary>
	internal IDisposable[] DetachTargetSelection(long targetSelectionEpoch)
	{
		lock (_resources)
		{
			if (_disposed)
			{
				return [];
			}

			List<IDisposable>? detached = null;
			for (int index = 0; index < _resources.Count; index++)
			{
				if (_resources[index].TargetSelectionEpoch != targetSelectionEpoch)
				{
					continue;
				}

				(detached ??= []).Add(_resources[index].Resource);
			}

			if (detached is null)
			{
				return [];
			}

			for (int index = _resources.Count - 1; index >= 0; index--)
			{
				if (_resources[index].TargetSelectionEpoch == targetSelectionEpoch)
				{
					_resources.RemoveAt(index);
				}
			}

			return [.. detached];
		}
	}

	/// <summary>
	///     Disposes an insertion-ordered resource snapshot in reverse order, attempts every release, and reports every
	///     failure (audit Q43).
	/// </summary>
	/// <exception cref="AggregateException">Several releases failed; the inner exceptions keep attempt order.</exception>
	/// <remarks>A single release failure is rethrown as the same instance with its original stack trace.</remarks>
	internal static void DisposeDetached(IReadOnlyList<IDisposable> resources)
	{
		List<Exception> failures = [];
		DisposeDetached(resources, failures);
		ThrowCleanupFailures(failures);
	}

	/// <summary>Disposes a snapshot in reverse order and appends each failure to <paramref name="failures" />.</summary>
	internal static void DisposeDetached(IReadOnlyList<IDisposable> resources, List<Exception> failures)
	{
		ArgumentNullException.ThrowIfNull(failures);
		for (int index = resources.Count - 1; index >= 0; index--)
		{
			try
			{
				resources[index].Dispose();
			}
			catch (Exception exception)
			{
				failures.Add(exception);
			}
		}
	}

	/// <summary>Throws nothing, the single failure unchanged, or one aggregate of every failure in attempt order.</summary>
	/// <remarks>
	///     https://learn.microsoft.com/dotnet/standard/exceptions/best-practices-for-exceptions#capture-exceptions-to-rethrow-later
	/// </remarks>
	internal static void ThrowCleanupFailures(List<Exception> failures)
	{
		ArgumentNullException.ThrowIfNull(failures);
		if (failures.Count == 0)
		{
			return;
		}

		if (failures.Count == 1)
		{
			ExceptionDispatchInfo.Capture(failures[0]).Throw();
		}

		throw new AggregateException("Client resource cleanup encountered several failures.", failures);
	}

	private T TrackCore<T>(T resource, long? targetSelectionEpoch)
		where T : class, IDisposable
	{
		ArgumentNullException.ThrowIfNull(resource);

		lock (_resources)
		{
			ThrowIfDisposed();
			if (!ContainsReference(resource))
			{
				_resources.Add(new ResourceEntry(resource, targetSelectionEpoch));
			}
		}

		return resource;
	}

	internal bool Untrack(IDisposable resource)
	{
		ArgumentNullException.ThrowIfNull(resource);

		lock (_resources)
		{
			for (int index = _resources.Count - 1; index >= 0; index--)
			{
				if (!ReferenceEquals(_resources[index].Resource, resource))
				{
					continue;
				}

				_resources.RemoveAt(index);
				return true;
			}

			return false;
		}
	}

	private bool ContainsReference(IDisposable resource)
	{
		for (int index = 0; index < _resources.Count; index++)
		{
			if (ReferenceEquals(_resources[index].Resource, resource))
			{
				return true;
			}
		}

		return false;
	}

	private void ThrowIfDisposed()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(CoreResourceRegistry),
				"Resources cannot be registered after the Cheat Engine client lifecycle has stopped.");
		}
	}

	private readonly record struct ResourceEntry(IDisposable Resource, long? TargetSelectionEpoch);
}
