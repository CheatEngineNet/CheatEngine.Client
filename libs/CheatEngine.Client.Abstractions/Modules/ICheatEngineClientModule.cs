namespace CheatEngine.Client.Modules;

/// <summary>A scoped feature that participates in the Cheat Engine client lifecycle.</summary>
/// <remarks>
///     <b>Implementable.</b> Applications implement this interface and the Client calls it. Its members are frozen for
///     the 1.x line.
/// </remarks>
public interface ICheatEngineClientModule
{
	/// <summary>Runs after the client scope and Lua runtime are active.</summary>
	/// <param name="client">The client of the activation.</param>
	public void OnEnabled(ICheatEngineClient client);

	/// <summary>Runs before the client scope is disposed and the Lua runtime is detached.</summary>
	/// <param name="client">The client of the activation that is stopping.</param>
	/// <remarks>
	///     When the plugin disables, it runs on Cheat Engine's main thread after
	///     <see cref="ICheatEngineClient.Stopping" /> was cancelled and before the activation releases what it owns.
	///     A call it makes on that thread can still read and change existing state and release leases, but cannot
	///     create a lease, attach, run Lua, instructions or Auto Assembler scripts, or load or save a table: see the
	///     deactivation callbacks of <see cref="ICheatEngineClient" />.
	/// </remarks>
	public void OnDisabling(ICheatEngineClient client);
}
