namespace CheatEngine.Client.Extensions.DependencyInjection;

/// <summary>Internal bridge that gives Hosting its one, constrained shutdown-cleanup path.</summary>
/// <remarks>
///     It is intentionally not a public Client service. The bridge does not reopen ordinary work admission: Core accepts
///     only the already-active Cheat Engine main thread while the SDK lifecycle callback is still executing.
/// </remarks>
internal interface ICheatEngineClientActivationCleanup
{
	public IDisposable EnterCleanupScope();

	public void DrainOwnedResourcesForDisable();
}
