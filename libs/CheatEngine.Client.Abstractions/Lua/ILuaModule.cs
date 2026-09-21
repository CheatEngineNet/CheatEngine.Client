namespace CheatEngine.Client.Lua;

/// <summary>Explicitly registers one application-owned set of Lua exports for the current Client activation.</summary>
/// <remarks>
///     The Client invokes both methods synchronously on Cheat Engine's main thread and owns the resulting registration
///     lease. An implementation can call its SDK-generated <c>RegisterLuaFunctions</c> and
///     <c>UnregisterLuaFunctions</c> methods internally, including acquisition of the SDK state required by those
///     generated methods. No SDK Lua state, reference, or raw stack access crosses this Client contract.
/// </remarks>
public interface ILuaModule
{
	/// <summary>Registers this module's exports for the current Cheat Engine activation.</summary>
	/// <remarks>
	///     Throw when the generated SDK registration reports a non-success status so
	///     <see cref="ILuaClient.TryRegisterModule" /> can return the mapped failure.
	///     <para>
	///         A manual module that implements only this interface remains a compatible advanced escape hatch. Because it
	///         does not declare its identity or exports, it cannot participate in the Client's activation-wide global
	///         collision guarantee. Implement <see cref="IDescribedLuaModule" /> for ordinary application modules.
	///     </para>
	/// </remarks>
	public void Register();

	/// <summary>Unregisters this module's exports for the current Cheat Engine activation.</summary>
	/// <remarks>
	///     This is called at most once by the Client, either by disposing the returned lease or during activation
	///     cleanup. Implementations should make their own cleanup safe if an SDK operation reports a failure.
	/// </remarks>
	public void Unregister();
}
