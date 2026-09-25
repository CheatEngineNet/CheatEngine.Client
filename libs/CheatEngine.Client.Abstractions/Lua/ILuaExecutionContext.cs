namespace CheatEngine.Client.Lua;

/// <summary>
///     Describes the short-lived, handle-free scope in which a typed Lua operation executes.
/// </summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add
///         members to it, so implement it only in a test double.
///     </para>
///     <para>
///         A context is valid only while the typed operation invocation that received it is running. It deliberately
///         exposes neither <c>LuaState</c>, <c>LuaRef</c>, CE objects nor raw Lua stack access. Operations should use
///         their SDK-generated <c>[LuaGlobal]</c> bindings while this context is active.
///     </para>
/// </remarks>
public interface ILuaExecutionContext
{
	/// <summary>Gets the activation epoch captured for this operation.</summary>
	public long Epoch
	{
		get;
	}

	/// <summary>Gets whether the context is still valid on the current Client execution boundary.</summary>
	public bool IsActive
	{
		get;
	}

	/// <summary>Throws when this context no longer belongs to the active plugin activation.</summary>
	/// <exception cref="CheatEngine.Client.Results.CheatEngineActivationExpiredException">
	///     The operation returned, its activation ended, or the context was observed outside its Client execution boundary.
	/// </exception>
	public void ThrowIfExpired();
}
