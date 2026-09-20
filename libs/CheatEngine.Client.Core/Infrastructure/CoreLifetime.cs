using CheatEngine.Client.Results;
using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Hosting.Context;

namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>Captures one enabled plugin context and owns all client-created resources for its epoch.</summary>
internal sealed class CoreLifetime : IDisposable
{
	private readonly PluginContext _context;
	private readonly CoreResourceRegistry _resources = new();
	private int _cleanupScopeDepth;
	private int _disposed;
	private int _resourcesDrained;

	private CoreLifetime(PluginContext context)
	{
		_context = context;
		TargetSelection = new TargetSelectionLifetime(ThrowIfInactive);
	}

	internal long Epoch => _context.Epoch;

	internal CancellationToken Stopping => _context.ShutdownToken;

	/// <summary>Gets the per-activation target-selection lifetime; this is deliberately independent from <see cref="Epoch" />.</summary>
	internal TargetSelectionLifetime TargetSelection { get; }

	/// <summary>Gets whether the captured SDK activation context itself is still current, regardless of admission closure.</summary>
	internal bool IsActivationCurrent => Volatile.Read(ref _disposed) == 0 && _context.IsCurrent;

	internal bool IsCurrent =>
		IsActivationCurrent && !Stopping.IsCancellationRequested;

	/// <summary>
	///     Gets whether synchronous dispatch is currently legal. During disable, only code running inside the explicit
	///     main-thread cleanup scope may dispatch; no worker admission is reopened.
	/// </summary>
	internal bool CanDispatch => IsActivationCurrent &&
	                             (!Stopping.IsCancellationRequested || IsInCleanupScopeOnMainThread);

	private bool IsInCleanupScopeOnMainThread => Volatile.Read(ref _cleanupScopeDepth) != 0 &&
	                                             IsActivationCurrent &&
	                                             _context.IsMainThread;

	/// <summary>Releases client-owned resources while the hosting plugin still owns SDK detach sequencing.</summary>
	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		if (Interlocked.Exchange(ref _resourcesDrained, 1) != 0)
		{
			return;
		}

		DisposeOwnedResources();
	}

	/// <summary>
	///     Opens the hosting-only cleanup path on Cheat Engine's main thread. It permits direct, synchronous cleanup
	///     dispatch after SDK shutdown cancellation, but never permits a worker to enter the SDK work-admission gate.
	/// </summary>
	internal IDisposable EnterCleanupScope()
	{
		ThrowIfActivationCurrent("Client.EnterCleanupScope");
		if (!_context.IsMainThread)
		{
			throw new CheatEngineClientLifecycleException("Client.EnterCleanupScope",
				"Cheat Engine cleanup must run on the plugin main thread.");
		}

		int cleanupScopeDepth = Interlocked.Increment(ref _cleanupScopeDepth);
		if (cleanupScopeDepth <= 0)
		{
			Interlocked.Decrement(ref _cleanupScopeDepth);
			throw new InvalidOperationException("The Cheat Engine cleanup scope depth overflowed.");
		}

		return new CleanupScope(this);
	}

	/// <summary>Drains client-owned CE resources while the SDK context remains attached and the cleanup scope is active.</summary>
	internal void DrainOwnedResourcesForDisable()
	{
		if (!IsInCleanupScopeOnMainThread)
		{
			throw new CheatEngineClientLifecycleException("Client.DrainResources",
				"Client-owned Cheat Engine resources can only be drained by the active main-thread cleanup scope.");
		}

		if (Interlocked.Exchange(ref _resourcesDrained, 1) != 0)
		{
			return;
		}

		DisposeOwnedResources();
	}

	private void DisposeOwnedResources()
	{
		Exception? firstFailure = null;
		try
		{
			TargetSelection.Dispose();
		}
		catch (Exception exception)
		{
			firstFailure = exception;
		}

		try
		{
			_resources.Dispose();
		}
		catch (Exception exception)
		{
			firstFailure ??= exception;
		}

		if (firstFailure is not null)
		{
			throw firstFailure;
		}
	}

	internal static CoreLifetime Capture()
	{
		PluginContext? context = PluginHost.Context;
		if (context is null || !context.IsCurrent)
		{
			throw new CheatEngineClientLifecycleException(
				"Client.Activate",
				"Cheat Engine has not enabled a plugin context for this client scope.");
		}

		return new CoreLifetime(context);
	}

	internal T Track<T>(T resource)
		where T : class, IDisposable
	{
		ThrowIfInactive("Client.TrackResource");
		return _resources.Track(resource);
	}

	internal bool Untrack(IDisposable resource)
	{
		return _resources.Untrack(resource);
	}

	internal void ThrowIfInactive(string operation)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		ThrowIfActivationCurrent(operation);

		if (Stopping.IsCancellationRequested)
		{
			throw new CheatEngineClientLifecycleException(operation,
				"The Cheat Engine plugin lifecycle is stopping and no new client work is admitted.");
		}
	}

	/// <summary>Rejects ordinary dispatch once admission closes, except for the current main-thread cleanup scope.</summary>
	internal void ThrowIfDispatchAllowed(string operation)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		ThrowIfActivationCurrent(operation);

		if (!Stopping.IsCancellationRequested || IsInCleanupScopeOnMainThread)
		{
			return;
		}

		throw new CheatEngineClientLifecycleException(operation,
			"The Cheat Engine plugin lifecycle is stopping and no new client work is admitted.");
	}

	private void ThrowIfActivationCurrent(string operation)
	{
		if (!IsActivationCurrent)
		{
			throw new CheatEngineActivationExpiredException(operation,
				"The Cheat Engine plugin lifecycle changed, so this client epoch is stale.");
		}
	}

	private void ExitCleanupScope()
	{
		if (Interlocked.Decrement(ref _cleanupScopeDepth) >= 0)
		{
			return;
		}

		Interlocked.Exchange(ref _cleanupScopeDepth, 0);
		throw new InvalidOperationException("The Cheat Engine cleanup scope was released more than once.");
	}

	private sealed class CleanupScope(CoreLifetime lifetime) : IDisposable
	{
		private CoreLifetime? _lifetime = lifetime;

		public void Dispose()
		{
			CoreLifetime? owner = Interlocked.Exchange(ref _lifetime, null);
			owner?.ExitCleanupScope();
		}
	}
}
