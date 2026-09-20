namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>Owns client-created resources for one active plugin epoch and releases them in reverse creation order.</summary>
internal sealed class CoreResourceRegistry : IDisposable
{
	private readonly List<ResourceEntry> _resources = [];
	private bool _disposed;

	/// <summary>Releases every tracked resource even when an earlier cleanup fails.</summary>
	public void Dispose()
	{
		DisposeDetached(DetachAll());
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

	/// <summary>Disposes an insertion-ordered resource snapshot in reverse order, preserving the first fault.</summary>
	internal static void DisposeDetached(IReadOnlyList<IDisposable> resources)
	{
		Exception? firstFailure = null;
		for (int index = resources.Count - 1; index >= 0; index--)
		{
			try
			{
				resources[index].Dispose();
			}
			catch (Exception exception)
			{
				firstFailure ??= exception;
			}
		}

		if (firstFailure is not null)
		{
			throw firstFailure;
		}
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
