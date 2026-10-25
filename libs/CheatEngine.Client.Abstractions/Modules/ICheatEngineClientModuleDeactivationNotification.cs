namespace CheatEngine.Client.Modules;

/// <summary>Finalizes session-long module state after module shutdown callbacks complete.</summary>
/// <remarks>
///     Hosting invokes this after every module's <see cref="ICheatEngineClientModule.OnDisabling" /> callback and before
///     it releases module-owned or Client-owned resources. It runs inside the activation's synchronous cleanup scope,
///     after <see cref="ICheatEngineClient.Stopping" /> was cancelled. Implementations may finalize persistent
///     presentation or state that already exists for this activation, but must not create resources, attach, or use the
///     ordinary Client Lua facade. Failures are isolated from notification peers and later resource cleanup.
/// </remarks>
public interface ICheatEngineClientModuleDeactivationNotification
{
	/// <summary>Finalizes persistent module state for the stopping activation.</summary>
	/// <param name="client">The Client activation that is stopping.</param>
	public void OnDeactivated(ICheatEngineClient client);
}
