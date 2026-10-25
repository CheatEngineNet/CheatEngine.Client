namespace CheatEngine.Client.Modules;

/// <summary>Releases resources owned by a Client module as its activation ends.</summary>
/// <remarks>
///     Hosting invokes this after every module's <see cref="ICheatEngineClientModule.OnDisabling" /> callback and before
///     it drains Client-owned leases. It runs inside the activation's synchronous cleanup scope, after
///     <see cref="ICheatEngineClient.Stopping" /> was cancelled. Implementations should release only resources they own,
///     use the Client dispatcher for main-thread work, and must not create new resources. The Client Lua facade remains
///     unavailable during this stage.
/// </remarks>
public interface ICheatEngineClientResourceCleanup
{
	/// <summary>Releases this module's owned resources before the activation drains Client-owned leases.</summary>
	/// <param name="client">The Client activation that is stopping.</param>
	public void OnReleasingResources(ICheatEngineClient client);
}
