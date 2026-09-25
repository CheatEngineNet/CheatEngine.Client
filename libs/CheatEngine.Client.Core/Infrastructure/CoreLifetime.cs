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
		ThrowIfActivationExpired("Client.EnterCleanupScope");
		if (!_context.IsMainThread)
		{
			throw ClientExceptions.InvalidState("Client.EnterCleanupScope",
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
			throw ClientExceptions.InvalidState("Client.DrainResources",
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
			throw ClientExceptions.InvalidState(
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

	/// <summary>Rejects new work once the activation ends or starts stopping, the cleanup scope included.</summary>
	/// <remarks>
	///     Creating a lease-owned resource is admitted only while the activation is active, never from the cleanup scope:
	///     every operation that creates a lease (a symbol registration, a value-scan session, an allocation, an Auto
	///     Assembler patch) calls this before it dispatches and again in its dispatched callback, before
	///     CheatEngine.SDK creates anything that no lease could own. A Lua module registration calls it before it
	///     dispatches and after the registration, and the activation tracks its lease before the dispatch, so the
	///     cleanup scope drains a module that registered while the activation began stopping. Operations on an
	///     existing resource use <see cref="ThrowIfDispatchRefused" />, so the cleanup scope can still release it.
	/// </remarks>
	/// <param name="operation">The public operation name.</param>
	internal void ThrowIfInactive(string operation)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		ThrowIfActivationExpired(operation);

		if (Stopping.IsCancellationRequested)
		{
			throw ClientExceptions.InvalidState(operation,
				"The Cheat Engine plugin lifecycle is stopping and no new client work is admitted.");
		}
	}

	/// <summary>
	///     Throws when dispatch is refused: the activation ended, or it is stopping and the caller is not the current
	///     main-thread cleanup scope.
	/// </summary>
	internal void ThrowIfDispatchRefused(string operation)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		ThrowIfActivationExpired(operation);

		if (!Stopping.IsCancellationRequested || IsInCleanupScopeOnMainThread)
		{
			return;
		}

		throw ClientExceptions.InvalidState(operation,
			"The Cheat Engine plugin lifecycle is stopping and no new client work is admitted.");
	}

	/// <summary>Throws the activation-expired exception once the captured activation is no longer current.</summary>
	private void ThrowIfActivationExpired(string operation)
	{
		if (!IsActivationCurrent)
		{
			throw ClientExceptions.ActivationExpired(operation,
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
