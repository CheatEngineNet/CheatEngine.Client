using CheatEngine.Client.Results;
using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Hosting.Context;

namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>Captures one enabled plugin context and owns all client-created resources for its epoch.</summary>
internal sealed class CoreLifetime : IDisposable
{
	private readonly ICoreLifetimeContext _context;
	private readonly CoreResourceRegistry _resources = new();
	private int _cleanupScopeDepth;
	private int _disposed;
	private int _resourcesDrained;

	private CoreLifetime(PluginContext context, ICoreDiagnostics? diagnostics)
		: this(new PluginContextAdapter(context), diagnostics)
	{
	}

	/// <summary>Creates an activation lifetime over a context, with an optional diagnostics sink.</summary>
	/// <param name="context">The captured plugin context.</param>
	/// <param name="diagnostics">
	///     The Core diagnostics sink of this activation; every emit is guarded so a throwing sink never changes a result.
	/// </param>
	internal CoreLifetime(ICoreLifetimeContext context, ICoreDiagnostics? diagnostics = null)
	{
		_context = context ?? throw new ArgumentNullException(nameof(context));
		TargetSelection = new TargetSelectionLifetime(ThrowIfInactive);
		Diagnostics = GuardedCoreDiagnostics.Wrap(diagnostics);
	}

	/// <summary>Gets the guarded diagnostics sink of this activation.</summary>
	internal ICoreDiagnostics Diagnostics
	{
		get;
	}

	internal long Epoch => _context.Epoch;

	internal CancellationToken Stopping => _context.Stopping;

	/// <summary>Gets the per-activation target-selection lifetime; this is deliberately independent from <see cref="Epoch" />.</summary>
	internal TargetSelectionLifetime TargetSelection
	{
		get;
	}

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

	/// <summary>
	///     Releases target-selection resources, then activation resources, attempting every release and reporting every
	///     failure: one failure is rethrown unchanged, several are aggregated in attempt order (audit Q43).
	/// </summary>
	/// <remarks>
	///     Client leases (<see cref="HostResourceLease" />) never throw from <see cref="IDisposable.Dispose" />: they stay
	///     registered with the activation while their release is retryable or incomplete, and the activation drain
	///     retries them once more and turns every incomplete outcome into one failure of this report.
	/// </remarks>
	private void DisposeOwnedResources()
	{
		List<Exception> failures = [];
		try
		{
			TargetSelection.DisposeCollecting(failures, ReportCleanupFailure);
		}
		catch (Exception exception)
		{
			failures.Add(exception);
		}

		try
		{
			_resources.DisposeCollecting(failures, ReportCleanupFailure, reportOutcomes: true);
		}
		catch (Exception exception)
		{
			failures.Add(exception);
		}

		CoreResourceRegistry.ThrowCleanupFailures(failures);
	}

	/// <summary>Reports one failed release with the resource and exception type names only (A24-16).</summary>
	private void ReportCleanupFailure(IDisposable resource, Exception exception)
	{
		Diagnostics.CoreResourceCleanupFailed(resource.GetType().Name, exception.GetType().FullName ??
																	   exception.GetType().Name);
	}

	internal static CoreLifetime Capture()
	{
		return Capture(null);
	}

	/// <summary>Captures the enabled plugin context with the diagnostics sink of this activation.</summary>
	internal static CoreLifetime Capture(ICoreDiagnostics? diagnostics)
	{
		PluginContext? context = PluginHost.Context;
		if (context is null || !context.IsCurrent)
		{
			throw new CheatEngineClientLifecycleException(
				"Client.Activate",
				"Cheat Engine has not enabled a plugin context for this client scope.");
		}

		return new CoreLifetime(context, diagnostics);
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

	private sealed class PluginContextAdapter(PluginContext context) : ICoreLifetimeContext
	{
		private readonly PluginContext _context = context ?? throw new ArgumentNullException(nameof(context));

		public long Epoch => _context.Epoch;

		public CancellationToken Stopping => _context.ShutdownToken;

		public bool IsCurrent => _context.IsCurrent;

		public bool IsMainThread => _context.IsMainThread;
	}
}
