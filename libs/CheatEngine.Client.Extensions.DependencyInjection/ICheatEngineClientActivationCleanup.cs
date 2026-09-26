namespace CheatEngine.Client.Extensions.DependencyInjection;

/// <summary>Internal bridge that gives Hosting its one, constrained shutdown-cleanup path.</summary>
/// <remarks>
///     It is intentionally not a public Client service. The bridge does not reopen ordinary work admission: Core accepts
///     only the already-active Cheat Engine main thread while the SDK lifecycle callback is still executing.
/// </remarks>
internal interface ICheatEngineClientActivationCleanup
{
	/// <summary>
	///     Gets whether CheatEngine.SDK detected that Cheat Engine replaced its Lua state outside the plugin's control
	///     during this activation (A8).
	/// </summary>
	/// <remarks>
	///     The SDK's own fact, sticky until the next enable: a lock-free read on any thread that never dispatches and
	///     never admits Lua work. Once the SDK detected the reset it refuses every Lua admission, the runtime snapshot's
	///     included, so this read is the only place the fact can still be observed during cleanup.
	/// </remarks>
	public bool ExternalLuaStateResetDetected
	{
		get;
	}

	public IDisposable EnterCleanupScope();

	public void DrainOwnedResourcesForDisable();
}
