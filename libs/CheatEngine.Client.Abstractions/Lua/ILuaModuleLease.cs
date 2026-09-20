namespace CheatEngine.Client.Lua;

/// <summary>Owns one explicit Lua module registration in a single Cheat Engine client epoch.</summary>
/// <remarks>
///     Disposing the lease unregisters its module synchronously on Cheat Engine's main thread. Forgotten leases are
///     released in LIFO order while the activation cleanup scope still permits SDK dispatch. Disposing a lease more
///     than once has no effect. The lease never exposes a Lua state, Lua reference, or native Lua handle.
/// </remarks>
public interface ILuaModuleLease : IDisposable
{
	/// <summary>Gets the Client activation epoch that owns this registration.</summary>
	public long Epoch
	{
		get;
	}

	/// <summary>Gets whether this lease has already completed its one release attempt.</summary>
	public bool IsReleased
	{
		get;
	}
}
